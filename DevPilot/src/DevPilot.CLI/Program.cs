using DevPilot.AI;
using DevPilot.AI.Generation;
using DevPilot.Contracts;
using DevPilot.Core;
using DevPilot.Indexer;
using DevPilot.LocalService;
using DevPilot.RAG;
using DevPilot.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Diagnostics;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "DEVPILOT_");

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services
    .AddDevPilotCore(builder.Configuration)
    .AddDevPilotStorage()
    .AddDevPilotIndexer()
    .AddDevPilotAi()
    .AddDevPilotRag()
    .AddDevPilotLocalService();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DevPilot.CLI");

var rootCommand = new RootCommand("DevPilot for Windows local-first developer assistant.");

var indexCommand = new Command("index", "Index local repository metadata and source chunks.");
var indexPathArgument = new Argument<string>("path", "Repository path to index.");
indexCommand.AddArgument(indexPathArgument);
indexCommand.SetHandler(async (string? path) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            logger.LogError("Repository path is required.");
            Environment.ExitCode = 1;
            return;
        }

        if (!Directory.Exists(path))
        {
            logger.LogError("Repository path does not exist: {Path}", path);
            Environment.ExitCode = 1;
            return;
        }

        await InitializeStorageAsync(host.Services).ConfigureAwait(false);
        var indexingService = host.Services.GetRequiredService<IRepositoryIndexingService>();
        await indexingService.IndexAsync(path).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Index command failed.");
        Environment.ExitCode = 1;
    }
}, indexPathArgument);

var searchCommand = new Command("search", "Run a semantic code search placeholder.");
var searchQueryArgument = new Argument<string>("query", "Natural language search query.");
var topKOption = new Option<int>(
    aliases: ["--top", "-k"],
    getDefaultValue: () => 5,
    description: "Number of semantic matches to return.");
searchCommand.AddArgument(searchQueryArgument);
searchCommand.AddOption(topKOption);
searchCommand.SetHandler(async (string query, int topK) =>
{
    try
    {
        await InitializeStorageAsync(host.Services).ConfigureAwait(false);
        var searchService = host.Services.GetRequiredService<ISemanticSearchService>();
        var result = await searchService.SearchAsync(new SearchRequest(query, topK)).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("Top Results:");
        Console.WriteLine();

        if (result.Matches.Count == 0)
        {
            Console.WriteLine("No semantic matches found. Run `devpilot index <path>` first.");
            return;
        }

        foreach (var match in result.Matches)
        {
            var chunk = match.Chunk;
            var title = string.IsNullOrWhiteSpace(chunk.SymbolName)
                ? chunk.FilePath
                : $"{chunk.SymbolName} ({chunk.ChunkType})";

            Console.WriteLine($"{match.Rank}. {title}");
            Console.WriteLine($"   Score: {chunk.RelevanceScore:0.000}");
            Console.WriteLine($"   File: {chunk.FilePath}");
            Console.WriteLine($"   Lines: {chunk.StartLine}-{chunk.EndLine}");
            Console.WriteLine($"   Preview: {chunk.ContentPreview}");
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Search command failed.");
        Environment.ExitCode = 1;
    }
}, searchQueryArgument, topKOption);

var debugSearchCommand = new Command("debug-search", "Inspect semantic retrieval rankings and timings.");
var debugSearchQueryArgument = new Argument<string>("query", "Natural language search query.");
var debugSearchTopKOption = new Option<int>(
    aliases: ["--top", "-k"],
    getDefaultValue: () => 5,
    description: "Number of semantic matches to inspect.");
debugSearchCommand.AddArgument(debugSearchQueryArgument);
debugSearchCommand.AddOption(debugSearchTopKOption);
debugSearchCommand.SetHandler(async (string query, int topK) =>
{
    try
    {
        await InitializeStorageAsync(host.Services).ConfigureAwait(false);
        var diagnostics = host.Services.GetRequiredService<IRetrievalDiagnosticsService>();
        var result = await diagnostics.InspectAsync(new SearchRequest(query, topK)).ConfigureAwait(false);

        Console.WriteLine("[INFO] Query embedding generated");
        Console.WriteLine($"[INFO] Dimensions: {result.QueryEmbeddingDimensions}");
        Console.WriteLine($"[INFO] Query embedding time: {result.QueryEmbeddingDuration.TotalMilliseconds:0} ms");
        Console.WriteLine($"[INFO] Retrieval time: {result.RetrievalDuration.TotalMilliseconds:0} ms");
        Console.WriteLine("[INFO] Top semantic matches:");
        Console.WriteLine();

        foreach (var match in result.Matches)
        {
            var title = string.IsNullOrWhiteSpace(match.SymbolName)
                ? match.FilePath
                : $"{match.SymbolName} ({match.ChunkType})";
            Console.WriteLine($"{match.Rank}. {title}");
            Console.WriteLine($"Similarity: {match.Similarity:0.000}");
            Console.WriteLine($"File: {match.FilePath}");
            Console.WriteLine($"ChunkId: {match.ChunkId}");
            Console.WriteLine($"Lines: {match.StartLine}-{match.EndLine}");
            Console.WriteLine($"TokenEstimate: {match.TokenEstimate}");
            Console.WriteLine($"Preview: {match.Preview}");
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Debug search command failed.");
        Environment.ExitCode = 1;
    }
}, debugSearchQueryArgument, debugSearchTopKOption);

var askCommand = new Command("ask", "Ask a local code assistant placeholder.");
var askPromptArgument = new Argument<string>("prompt", "Question for the future local assistant.");
var askTopKOption = new Option<int>(
    aliases: ["--top", "-k"],
    getDefaultValue: () => 5,
    description: "Number of retrieved chunks to use as context.");
askCommand.AddArgument(askPromptArgument);
askCommand.AddOption(askTopKOption);
askCommand.SetHandler(async (string prompt, int topK) =>
{
    try
    {
        await InitializeStorageAsync(host.Services).ConfigureAwait(false);
        var ragPipeline = host.Services.GetRequiredService<IRagPipeline>();
        var response = await ragPipeline.AskAsync(new RagRequest(prompt, null, topK)).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("Answer:");
        Console.WriteLine(response.Answer);

        Console.WriteLine("Referenced Files:");
        foreach (var reference in response.ReferencedContext
            .GroupBy(context => context.FilePath)
            .OrderBy(group => group.Key))
        {
            Console.WriteLine($"* {reference.Key}");
        }

        Console.WriteLine();
        Console.WriteLine($"Retrieved Chunks: {response.RetrievedContextCount}");
        Console.WriteLine($"Inference Time: {response.InferenceDuration.TotalMilliseconds:0} ms");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Ask command failed.");
        Environment.ExitCode = 1;
    }
}, askPromptArgument, askTopKOption);

var inspectPromptCommand = new Command("inspect-prompt", "Render the grounded prompt without running inference.");
var inspectPromptArgument = new Argument<string>("prompt", "Question to inspect.");
var inspectPromptTopKOption = new Option<int>(
    aliases: ["--top", "-k"],
    getDefaultValue: () => 5,
    description: "Number of retrieved chunks to include.");
inspectPromptCommand.AddArgument(inspectPromptArgument);
inspectPromptCommand.AddOption(inspectPromptTopKOption);
inspectPromptCommand.SetHandler(async (string prompt, int topK) =>
{
    try
    {
        await InitializeStorageAsync(host.Services).ConfigureAwait(false);
        var diagnostics = host.Services.GetRequiredService<IPromptDiagnosticsService>();
        var result = await diagnostics.InspectAsync(new RagRequest(prompt, null, topK)).ConfigureAwait(false);

        Console.WriteLine($"[INFO] Retrieved chunks: {result.RetrievedChunkCount}");
        Console.WriteLine($"[INFO] Estimated prompt tokens: {result.EstimatedPromptTokens}");
        Console.WriteLine($"[INFO] Retrieval time: {result.RetrievalDuration.TotalMilliseconds:0} ms");
        Console.WriteLine($"[INFO] Prompt build time: {result.PromptBuildDuration.TotalMilliseconds:0} ms");
        Console.WriteLine();
        Console.WriteLine("Retrieved Context:");
        foreach (var chunk in result.RetrievedChunks)
        {
            Console.WriteLine($"- {chunk.Rank}. {chunk.FilePath}:{chunk.StartLine}-{chunk.EndLine} ({chunk.Similarity:0.000}, tokens {chunk.TokenEstimate})");
        }

        Console.WriteLine();
        Console.WriteLine("Constructed Prompt:");
        Console.WriteLine();
        Console.WriteLine(result.Prompt.Text);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Inspect prompt command failed.");
        Environment.ExitCode = 1;
    }
}, inspectPromptArgument, inspectPromptTopKOption);

var inspectChunksCommand = new Command("inspect-chunks", "Inspect indexed chunk boundaries and retrieval readiness.");
var inspectChunksFileOption = new Option<string?>(
    aliases: ["--file", "-f"],
    description: "Filter chunks by file path or name.");
inspectChunksCommand.AddOption(inspectChunksFileOption);
inspectChunksCommand.SetHandler(async (string? file) =>
{
    try
    {
        await InitializeStorageAsync(host.Services).ConfigureAwait(false);
        var inspector = host.Services.GetRequiredService<IChunkInspectionService>();
        var chunks = await inspector.InspectAsync(file).ConfigureAwait(false);

        Console.WriteLine($"[INFO] Chunks: {chunks.Count}");
        Console.WriteLine();
        foreach (var chunk in chunks)
        {
            Console.WriteLine($"{chunk.FilePath}:{chunk.StartLine}-{chunk.EndLine}");
            Console.WriteLine($"ChunkId: {chunk.ChunkId}");
            Console.WriteLine($"Type: {chunk.ChunkType}");
            Console.WriteLine($"Symbol: {chunk.SymbolName ?? "(none)"}");
            Console.WriteLine($"Characters: {chunk.CharacterCount}");
            Console.WriteLine($"TokenEstimate: {chunk.TokenEstimate}");
            Console.WriteLine($"ChunkHash: {chunk.ChunkHash}");
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Inspect chunks command failed.");
        Environment.ExitCode = 1;
    }
}, inspectChunksFileOption);

var validateRuntimeCommand = new Command("validate-runtime", "Validate local ONNX models, tokenizer behavior, providers, and streaming.");
var validatePromptArgument = new Argument<string>(
    "prompt",
    getDefaultValue: () => "Validate local inference runtime",
    description: "Probe prompt used for tokenizer, profiling, and streaming validation.");
validateRuntimeCommand.AddArgument(validatePromptArgument);
validateRuntimeCommand.SetHandler(async (string prompt) =>
{
    try
    {
        await InitializeStorageAsync(host.Services).ConfigureAwait(false);
        var validator = host.Services.GetRequiredService<IModelValidationService>();
        var streaming = host.Services.GetRequiredService<IStreamingValidationService>();
        var report = await validator.ValidateAsync(prompt).ConfigureAwait(false);
        var stream = await streaming.ValidateAsync(prompt).ConfigureAwait(false);

        Console.WriteLine("[INFO] Runtime capability report");
        Console.WriteLine($"Selected provider: {report.Runtime.SelectedProvider}");
        Console.WriteLine($"Hardware acceleration: {report.Runtime.HardwareAccelerationEnabled}");
        Console.WriteLine($"CPU: {report.Runtime.Hardware.CpuDescription}");
        Console.WriteLine($"OS: {report.Runtime.Hardware.OperatingSystem}");
        Console.WriteLine($"Process memory: {report.Runtime.Hardware.CurrentProcessMemoryBytes / 1024 / 1024} MB");
        Console.WriteLine();

        foreach (var provider in report.Runtime.Providers)
        {
            Console.WriteLine($"Provider {provider.Provider}: available={provider.IsAvailable}, selected={provider.IsSelected}");
            Console.WriteLine($"  {provider.Detail}");
        }

        Console.WriteLine();
        PrintModel(report.EmbeddingModel);
        PrintModel(report.LlmModel);
        Console.WriteLine($"Tokenizer compatible: {report.Tokenizer.IsCompatible}");
        Console.WriteLine($"Tokenizer active tokens: {report.Tokenizer.ActiveTokens}/{report.Tokenizer.RequestedMaxTokens}");
        Console.WriteLine($"Tokenizer truncated: {report.Tokenizer.WasTruncated}");
        foreach (var issue in report.Tokenizer.Issues)
        {
            Console.WriteLine($"  [{issue.Severity}] {issue.Code}: {issue.Message}");
        }

        Console.WriteLine();
        Console.WriteLine($"Embedding latency: {report.Profile.EmbeddingDuration.TotalMilliseconds:0} ms");
        Console.WriteLine($"Retrieval latency: {report.Profile.RetrievalDuration.TotalMilliseconds:0} ms");
        Console.WriteLine($"Prompt build latency: {report.Profile.PromptBuildDuration.TotalMilliseconds:0} ms");
        Console.WriteLine($"Inference latency: {report.Profile.InferenceDuration.TotalMilliseconds:0} ms");
        Console.WriteLine($"Memory delta: {(report.Profile.MemoryAfterBytes - report.Profile.MemoryBeforeBytes) / 1024 / 1024} MB");
        Console.WriteLine();
        Console.WriteLine($"Streaming tokens: {report.Profile.StreamingTokens}");
        Console.WriteLine($"Streaming cancellation observed: {(stream.CancellationObserved ? "Passed" : "Failed")}");
        Console.WriteLine($"Streaming validation tokens received: {stream.TokensReceived}");
        foreach (var issue in stream.Issues)
        {
            Console.WriteLine($"  [{issue.Severity}] {issue.Code}: {issue.Message}");
        }
        Console.WriteLine($"Streaming partial: {report.Profile.StreamingPartial}");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Runtime validation command failed.");
        Environment.ExitCode = 1;
    }
}, validatePromptArgument);

// 1. benchmark-runtime command
var benchmarkRuntimeCommand = new Command("benchmark-runtime", "Measure standard token generation speed and throughput.");
var benchmarkRuntimePromptOption = new Option<string>("--prompt", () => "Write a quicksort implementation in C#", "Prompt to run generation on.");
var benchmarkRuntimeLimitOption = new Option<int>("--limit", () => 64, "Number of tokens to generate.");
benchmarkRuntimeCommand.AddOption(benchmarkRuntimePromptOption);
benchmarkRuntimeCommand.AddOption(benchmarkRuntimeLimitOption);
benchmarkRuntimeCommand.SetHandler(async (string prompt, int limit) =>
{
    try
    {
        var llmOptions = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<LLMSettings>>();
        llmOptions.Value.DiagnosticsLevel = "Benchmark";

        var llm = host.Services.GetRequiredService<ILLMService>();
        var request = new InferenceRequest(
            prompt,
            null,
            limit,
            0.7,
            0.9,
            Array.Empty<RetrievedContext>(),
            new GenerationParameters { MaxTokens = limit });

        Console.WriteLine("[INFO] Running benchmark-runtime...");
        var stopwatch = Stopwatch.StartNew();
        var result = await llm.GenerateAsync(request).ConfigureAwait(false);
        stopwatch.Stop();

        Console.WriteLine();
        Console.WriteLine("================ BENCHMARK RUNTIME REPORT ================");
        Console.WriteLine($"Total Duration: {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine($"Generated Answer Length: {result.Answer.Length} characters");
        Console.WriteLine($"Inference Timing (measured by service): {result.Duration.TotalMilliseconds:0.0} ms");
        Console.WriteLine($"Estimated Throughput: {result.OutputTokenCount / result.Duration.TotalSeconds:0.0} tokens/sec");
        Console.WriteLine("==========================================================");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "benchmark-runtime failed.");
    }
}, benchmarkRuntimePromptOption, benchmarkRuntimeLimitOption);

// 2. benchmark-streaming command
var benchmarkStreamingCommand = new Command("benchmark-streaming", "Benchmark real-time streaming response chunk sizes and arrival latency.");
var benchmarkStreamingPromptOption = new Option<string>("--prompt", () => "Explain clean architecture in 3 sentences.", "Prompt to stream.");
benchmarkStreamingCommand.AddOption(benchmarkStreamingPromptOption);
benchmarkStreamingCommand.SetHandler(async (string prompt) =>
{
    try
    {
        var llm = host.Services.GetRequiredService<ILLMService>();
        var request = new InferenceRequest(
            prompt,
            null,
            128,
            0.7,
            0.9,
            Array.Empty<RetrievedContext>(),
            new GenerationParameters { MaxTokens = 128 });

        Console.WriteLine("[INFO] Running benchmark-streaming...");
        var stopwatch = Stopwatch.StartNew();
        var chunks = new List<string>();
        var chunkTimes = new List<double>();

        var stepStopwatch = Stopwatch.StartNew();
        await foreach (var chunk in llm.StreamAsync(request).ConfigureAwait(false))
        {
            chunkTimes.Add(stepStopwatch.Elapsed.TotalMilliseconds);
            chunks.Add(chunk);
            Console.Write(chunk);
            stepStopwatch.Restart();
        }
        stopwatch.Stop();

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("================ BENCHMARK STREAMING REPORT ================");
        Console.WriteLine($"Total Stream Time: {stopwatch.ElapsedMilliseconds:0.0} ms");
        Console.WriteLine($"Total Chunks Emitted: {chunks.Count}");
        if (chunks.Count > 0)
        {
            Console.WriteLine($"Avg Chunk Length: {chunks.Average(c => c.Length):0.0} chars");
            Console.WriteLine($"Max Chunk Length: {chunks.Max(c => c.Length)} chars");
            Console.WriteLine($"Avg Inter-chunk Latency: {chunkTimes.Average():0.0} ms");
        }
        Console.WriteLine("============================================================");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "benchmark-streaming failed.");
    }
}, benchmarkStreamingPromptOption);

// 3. benchmark-rag command
var benchmarkRagCommand = new Command("benchmark-rag", "Benchmark retrieval ranking, Jaccard overlap reranking, and chunk deduplication latency.");
var benchmarkRagQueryOption = new Option<string>("--query", () => "jwt authentication controller", "Search query for RAG pipeline.");
benchmarkRagCommand.AddOption(benchmarkRagQueryOption);
benchmarkRagCommand.SetHandler(async (string query) =>
{
    try
    {
        await InitializeStorageAsync(host.Services).ConfigureAwait(false);
        var searchService = host.Services.GetRequiredService<ISemanticSearchService>();

        Console.WriteLine("[INFO] Running benchmark-rag...");
        var sw = Stopwatch.StartNew();
        var searchResult = await searchService.SearchAsync(new SearchRequest(query, 15)).ConfigureAwait(false);
        sw.Stop();

        Console.WriteLine($"[INFO] Semantic retrieval generated {searchResult.Matches.Count} candidates in {sw.ElapsedMilliseconds} ms.");

        var rawChunks = searchResult.Matches.Select(m => m.Chunk).ToList();
        
        sw.Restart();
        var optimized = RagOptimizer.Optimize(query, rawChunks, 5, 12000, 2000);
        sw.Stop();

        Console.WriteLine($"[INFO] RagOptimizer complete in {sw.Elapsed.TotalMilliseconds:0.00} ms.");
        Console.WriteLine();
        Console.WriteLine("================ BENCHMARK RAG & RETRIEVAL REPORT ================");
        Console.WriteLine($"Original candidate count: {rawChunks.Count}");
        Console.WriteLine($"Optimized/deduplicated count: {optimized.Count}");
        Console.WriteLine($"Total Optimization Latency: {sw.Elapsed.TotalMilliseconds:0.00} ms");
        Console.WriteLine("Top reranked results:");
        for (var i = 0; i < optimized.Count; i++)
        {
            var item = optimized[i];
            Console.WriteLine($"  {i + 1}. {item.FilePath}:{item.StartLine}-{item.EndLine} (Score: {item.RelevanceScore:0.000}, Length: {item.Content.Length} chars)");
        }
        Console.WriteLine("==================================================================");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "benchmark-rag failed.");
    }
}, benchmarkRagQueryOption);

// 4. benchmark-provider command
var benchmarkProviderCommand = new Command("benchmark-provider", "Benchmark and print provider status capabilities.");
benchmarkProviderCommand.SetHandler(() =>
{
    try
    {
        var selector = host.Services.GetRequiredService<IExecutionProviderSelector>();
        var active = selector.SelectProvider();
        var statuses = selector.GetProviderStatuses();

        Console.WriteLine();
        Console.WriteLine("================ BENCHMARK PROVIDER REPORT ================");
        Console.WriteLine($"Active Target Provider: {active}");
        Console.WriteLine("All Supported Hardware Providers Status:");
        foreach (var status in statuses)
        {
            Console.WriteLine($"  * Provider: {status.Provider}");
            Console.WriteLine($"    Available: {status.IsAvailable}");
            Console.WriteLine($"    Selected: {status.IsSelected}");
            Console.WriteLine($"    Detail: {status.Detail}");
        }
        Console.WriteLine("===========================================================");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "benchmark-provider failed.");
    }
});

// 5. benchmark-kvcache command
var benchmarkKvcacheCommand = new Command("benchmark-kvcache", "Validate KV Cache device binding latency advantages between prefill and decode.");
var benchmarkKvcachePromptOption = new Option<string>("--prompt", () => "Explain asynchronous programming in .NET using async and await with an exhaustive code sample and analysis.", "Prompt to run.");
benchmarkKvcacheCommand.AddOption(benchmarkKvcachePromptOption);
benchmarkKvcacheCommand.SetHandler(async (string prompt) =>
{
    try
    {
        var llmOptions = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<LLMSettings>>();
        llmOptions.Value.DiagnosticsLevel = "Benchmark";

        var llm = host.Services.GetRequiredService<ILLMService>();
        var request = new InferenceRequest(
            prompt,
            null,
            32,
            0.7,
            0.9,
            Array.Empty<RetrievedContext>(),
            new GenerationParameters { MaxTokens = 32 });

        Console.WriteLine("[INFO] Running benchmark-kvcache...");
        
        var sw = Stopwatch.StartNew();
        var result = await llm.GenerateAsync(request).ConfigureAwait(false);
        sw.Stop();

        Console.WriteLine();
        Console.WriteLine("================ BENCHMARK KV-CACHE REPORT ================");
        Console.WriteLine($"Total Duration: {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"Process Peak Working Set Memory: {Environment.WorkingSet / 1024 / 1024} MB");
        Console.WriteLine("===========================================================");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "benchmark-kvcache failed.");
    }
}, benchmarkKvcachePromptOption);

var serviceCommand = new Command("service", "Start the local REST web API service.");
serviceCommand.SetHandler(async () =>
{
    try
    {
        await InitializeStorageAsync(host.Services).ConfigureAwait(false);
        logger.LogInformation("Starting DevPilot local REST web service...");
        await host.RunAsync().ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Service command failed.");
        Environment.ExitCode = 1;
    }
});

rootCommand.AddCommand(indexCommand);
rootCommand.AddCommand(searchCommand);
rootCommand.AddCommand(debugSearchCommand);
rootCommand.AddCommand(askCommand);
rootCommand.AddCommand(inspectPromptCommand);
rootCommand.AddCommand(inspectChunksCommand);
rootCommand.AddCommand(validateRuntimeCommand);
rootCommand.AddCommand(benchmarkRuntimeCommand);
rootCommand.AddCommand(benchmarkStreamingCommand);
rootCommand.AddCommand(benchmarkRagCommand);
rootCommand.AddCommand(benchmarkProviderCommand);
rootCommand.AddCommand(benchmarkKvcacheCommand);
rootCommand.AddCommand(serviceCommand);

logger.LogInformation("DevPilot CLI starting.");
return await rootCommand.InvokeAsync(args).ConfigureAwait(false);

static async Task InitializeStorageAsync(IServiceProvider services)
{
    var initializer = services.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync().ConfigureAwait(false);
}

static void PrintModel(ModelValidationResult result)
{
    Console.WriteLine($"Model: {result.ModelName}");
    Console.WriteLine($"  Path: {result.ModelPath}");
    Console.WriteLine($"  Exists: {result.Exists}");
    Console.WriteLine($"  Loaded: {result.Loaded}");
    Console.WriteLine($"  Compatible: {result.IsCompatible}");
    Console.WriteLine($"  Provider: {result.ExecutionProvider}");
    Console.WriteLine($"  Load time: {result.LoadDuration.TotalMilliseconds:0} ms");
    Console.WriteLine($"  Inputs: {string.Join(", ", result.InputNames)}");
    Console.WriteLine($"  Outputs: {string.Join(", ", result.OutputNames)}");
    foreach (var issue in result.Issues)
    {
        Console.WriteLine($"  [{issue.Severity}] {issue.Code}: {issue.Message}");
    }
}
