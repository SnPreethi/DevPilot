using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevPilot.Contracts;
using DevPilot.Core;
using DevPilot.RAG;
using DevPilot.Patching;
using DevPilot.Core.Diagnostics;
using DevPilot.Core.Execution;
using DevPilot.Core.Modernization;

namespace DevPilot.LocalService;

public sealed class DevPilotWorker : BackgroundService
{
    private readonly ILogger<DevPilotWorker> _logger;
    private readonly IServiceProvider _parentServiceProvider;

    public DevPilotWorker(ILogger<DevPilotWorker> logger, IServiceProvider parentServiceProvider)
    {
        _logger = logger;
        _parentServiceProvider = parentServiceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DevPilot local service starting...");

        var builder = WebApplication.CreateBuilder();

        // Bind Kestrel to listen only locally
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(5071);
        });

        // Add CORS support so VS Code webview can access endpoints
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            });
        });

        var app = builder.Build();

        app.UseCors();

        // 0. GET /
        app.MapGet("/", () => Results.Ok(new { status = "DevPilot Local REST Web Service Online", version = "1.0.0" }));

        // 1. GET /runtime-status
        app.MapGet("/runtime-status", () =>
        {
            var capabilityService = _parentServiceProvider.GetRequiredService<IRuntimeCapabilityService>();
            var capabilityReport = capabilityService.GetCapabilities();

            // We can also retrieve the LLM & embedding load status from the actual services
            var modelManager = _parentServiceProvider.GetRequiredService<IModelManager>();
            var activeProvider = capabilityReport.SelectedProvider;
            var llmDescriptor = modelManager.Resolve(activeProvider);

            var llmService = _parentServiceProvider.GetRequiredService<ILLMService>() as DevPilot.AI.OnnxLLMService;
            var embeddingModel = _parentServiceProvider.GetService<DevPilot.AI.OnnxEmbeddingModel>();

            var response = new
            {
                selectedProvider = capabilityReport.SelectedProvider.ToString(),
                hardwareSummary = $"CPU: {capabilityReport.Hardware.CpuDescription}, RAM: {ToMegabytes(capabilityReport.Hardware.TotalMemoryBytes)} MB, OS: {capabilityReport.Hardware.OperatingSystem}",
                memoryStatus = $"Process: {ToMegabytes(capabilityReport.Hardware.CurrentProcessMemoryBytes)} MB",
                gpuDetected = capabilityReport.Hardware.GpuDetected,
                directMLAvailable = capabilityReport.Hardware.DirectMLAvailable,
                providers = capabilityReport.Providers.Select(p => new
                {
                    provider = p.Provider.ToString(),
                    isAvailable = p.IsAvailable,
                    isSelected = p.IsSelected,
                    detail = p.Detail
                }).ToList(),
                llmModel = new
                {
                    modelId = llmDescriptor.Name,
                    modelPath = llmDescriptor.ModelPath,
                    exists = File.Exists(llmDescriptor.ModelPath),
                    isLoaded = llmService?.IsLoaded ?? false,
                    loadDurationMs = llmService?.LoadDuration.TotalMilliseconds ?? 0
                },
                embeddingModel = new
                {
                    isLoaded = embeddingModel?.IsLoaded ?? false,
                    modelPath = embeddingModel?.ModelPath ?? "",
                    exists = string.IsNullOrEmpty(embeddingModel?.ModelPath) ? false : File.Exists(embeddingModel.ModelPath),
                    loadDurationMs = embeddingModel?.LoadDuration.TotalMilliseconds ?? 0
                }
            };

            return Results.Json(response);
        });

        // 2. POST /search
        app.MapPost("/search", async ([FromBody] SearchRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.BadRequest(new { error = "Query is required." });
            }

            var semanticSearchService = _parentServiceProvider.GetRequiredService<ISemanticSearchService>();
            var results = await semanticSearchService.SearchAsync(
                new SearchRequest(request.Query, request.MaxResults > 0 ? request.MaxResults : 5, request.RepositoryId),
                cancellationToken);

            var matches = results.Matches.Select(m => new
            {
                rank = m.Rank,
                chunkId = m.Chunk.ChunkId,
                filePath = m.Chunk.FilePath,
                symbolName = m.Chunk.SymbolName,
                startLine = m.Chunk.StartLine,
                endLine = m.Chunk.EndLine,
                similarity = m.Chunk.RelevanceScore,
                preview = m.Chunk.ContentPreview
            }).ToList();

            return Results.Ok(new { query = request.Query, matches });
        });

        // 3. POST /chat
        app.MapPost("/chat", async (HttpContext httpContext, [FromBody] ChatRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                httpContext.Response.StatusCode = 400;
                await httpContext.Response.WriteAsync("Prompt is required.", cancellationToken);
                return;
            }

            var promptBuilder = _parentServiceProvider.GetRequiredService<IPromptBuilder>();
            var llmService = _parentServiceProvider.GetRequiredService<ILLMService>();
            var llmSettings = _parentServiceProvider.GetRequiredService<IOptions<LLMSettings>>().Value;
            var promptingSettings = _parentServiceProvider.GetRequiredService<IOptions<PromptingSettings>>().Value;

            IReadOnlyList<RetrievedContext> context = Array.Empty<RetrievedContext>();
            string promptText = request.Prompt;

            if (!string.IsNullOrEmpty(request.RepositoryId))
            {
                try
                {
                    var contextOrchestrator = _parentServiceProvider.GetRequiredService<IContextOrchestrator>();
                    context = await contextOrchestrator.OrchestrateContextAsync(
                        request.Prompt,
                        request.RepositoryId,
                        request.ActiveFilePath,
                        request.CursorLine,
                        request.SelectedCode,
                        promptingSettings.MaxPromptCharacters / 4,
                        cancellationToken).ConfigureAwait(false);
                    
                    var promptObj = await promptBuilder.BuildAsync(request.Prompt, context, cancellationToken).ConfigureAwait(false);
                    promptText = promptObj.Text;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to perform context orchestration for prompt. Falling back to ungrounded generation.");
                }
            }

            var inferenceRequest = new InferenceRequest(
                promptText,
                request.ModelId ?? llmSettings.ModelId,
                llmSettings.MaxOutputTokens,
                llmSettings.Temperature,
                llmSettings.TopP,
                context);

            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            // If context was retrieved, send it first
            if (context.Count > 0)
            {
                var contextData = context.Select(c => new
                {
                    filePath = c.FilePath,
                    symbolName = c.SymbolName,
                    startLine = c.StartLine,
                    endLine = c.EndLine,
                    content = c.Content
                }).ToList();

                var contextJson = JsonSerializer.Serialize(new { type = "context", data = contextData });
                await httpContext.Response.WriteAsync($"data: {contextJson}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }

            try
            {
                await foreach (var chunk in llmService.StreamAsync(inferenceRequest, cancellationToken))
                {
                    var chunkJson = JsonSerializer.Serialize(new { type = "content", text = chunk });
                    await httpContext.Response.WriteAsync($"data: {chunkJson}\n\n", cancellationToken);
                    await httpContext.Response.Body.FlushAsync(cancellationToken);
                }

                var doneJson = JsonSerializer.Serialize(new { type = "done" });
                await httpContext.Response.WriteAsync($"data: {doneJson}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                var errorJson = JsonSerializer.Serialize(new { type = "cancelled" });
                await httpContext.Response.WriteAsync($"data: {errorJson}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                var errorJson = JsonSerializer.Serialize(new { type = "error", message = ex.Message });
                await httpContext.Response.WriteAsync($"data: {errorJson}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(CancellationToken.None);
            }
        });

        // 4. POST /explain-selection
        app.MapPost("/explain-selection", async (HttpContext httpContext, [FromBody] ExplainSelectionRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.CodeSnippet))
            {
                httpContext.Response.StatusCode = 400;
                await httpContext.Response.WriteAsync("CodeSnippet is required.", cancellationToken);
                return;
            }

            var promptBuilder = _parentServiceProvider.GetRequiredService<IPromptBuilder>();
            var llmService = _parentServiceProvider.GetRequiredService<ILLMService>();
            var llmSettings = _parentServiceProvider.GetRequiredService<IOptions<LLMSettings>>().Value;
            var promptingSettings = _parentServiceProvider.GetRequiredService<IOptions<PromptingSettings>>().Value;

            var explainPrompt = $"Explain the following code snippet {(string.IsNullOrEmpty(request.FilePath) ? "" : $"from file `{request.FilePath}`")}:\n\n" +
                                $"```{(string.IsNullOrEmpty(request.LanguageId) ? "" : request.LanguageId)}\n" +
                                $"{request.CodeSnippet}\n" +
                                $"```\n\n" +
                                $"Please explain what this code does, how it works, and point out any bugs, performance issues, or architectural improvements if applicable.";
            
            IReadOnlyList<RetrievedContext> context = Array.Empty<RetrievedContext>();

            if (!string.IsNullOrEmpty(request.RepositoryId))
            {
                try
                {
                    var contextOrchestrator = _parentServiceProvider.GetRequiredService<IContextOrchestrator>();
                    context = await contextOrchestrator.OrchestrateContextAsync(
                        explainPrompt,
                        request.RepositoryId,
                        request.FilePath,
                        request.CursorLine,
                        request.CodeSnippet,
                        promptingSettings.MaxPromptCharacters / 4,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to perform context orchestration for explain-selection.");
                }
            }

            var promptObj = await promptBuilder.BuildAsync(explainPrompt, context, cancellationToken).ConfigureAwait(false);

            var inferenceRequest = new InferenceRequest(
                promptObj.Text,
                llmSettings.ModelId,
                llmSettings.MaxOutputTokens,
                llmSettings.Temperature,
                llmSettings.TopP,
                context);

            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            if (context.Count > 0)
            {
                var contextData = context.Select(c => new
                {
                    filePath = c.FilePath,
                    symbolName = c.SymbolName,
                    startLine = c.StartLine,
                    endLine = c.EndLine,
                    content = c.Content
                }).ToList();

                var contextJson = JsonSerializer.Serialize(new { type = "context", data = contextData });
                await httpContext.Response.WriteAsync($"data: {contextJson}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }

            try
            {
                await foreach (var chunk in llmService.StreamAsync(inferenceRequest, cancellationToken))
                {
                    var chunkJson = JsonSerializer.Serialize(new { type = "content", text = chunk });
                    await httpContext.Response.WriteAsync($"data: {chunkJson}\n\n", cancellationToken);
                    await httpContext.Response.Body.FlushAsync(cancellationToken);
                }

                var doneJson = JsonSerializer.Serialize(new { type = "done" });
                await httpContext.Response.WriteAsync($"data: {doneJson}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                var errorJson = JsonSerializer.Serialize(new { type = "cancelled" });
                await httpContext.Response.WriteAsync($"data: {errorJson}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                var errorJson = JsonSerializer.Serialize(new { type = "error", message = ex.Message });
                await httpContext.Response.WriteAsync($"data: {errorJson}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(CancellationToken.None);
            }
        });

        // 4.5. POST /completion
        app.MapPost("/completion", async (HttpContext httpContext, [FromBody] CompletionRequest request, CancellationToken cancellationToken) =>
        {
            var completionBuilder = _parentServiceProvider.GetRequiredService<ICompletionContextBuilder>();
            var llmService = _parentServiceProvider.GetRequiredService<ILLMService>();
            var llmSettings = _parentServiceProvider.GetRequiredService<IOptions<LLMSettings>>().Value;

            var prompt = await completionBuilder.BuildCompletionPromptAsync(request, cancellationToken).ConfigureAwait(false);

            var generationParams = new GenerationParameters
            {
                Temperature = 0.0f,
                TopP = 1.0f,
                MaxTokens = 128,
                StopSequences = new[] { "\n\n", "<|end|>", "<|user|>", "<|system|>" }
            };

            var inferenceRequest = new InferenceRequest(
                Prompt: prompt,
                ModelId: llmSettings.ModelId,
                MaxOutputTokens: 128,
                Temperature: 0.0,
                TopP: 1.0,
                Context: Array.Empty<RetrievedContext>(),
                Parameters: generationParams,
                RawPrompt: true
            );

            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            try
            {
                await foreach (var chunk in llmService.StreamAsync(inferenceRequest, cancellationToken).ConfigureAwait(false))
                {
                    var chunkJson = JsonSerializer.Serialize(new { type = "content", text = chunk });
                    await httpContext.Response.WriteAsync($"data: {chunkJson}\n\n", cancellationToken).ConfigureAwait(false);
                    await httpContext.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                var doneJson = JsonSerializer.Serialize(new { type = "done" });
                await httpContext.Response.WriteAsync($"data: {doneJson}\n\n", cancellationToken).ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                var errorJson = JsonSerializer.Serialize(new { type = "cancelled" });
                await httpContext.Response.WriteAsync($"data: {errorJson}\n\n", CancellationToken.None).ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var errorJson = JsonSerializer.Serialize(new { type = "error", message = ex.Message });
                await httpContext.Response.WriteAsync($"data: {errorJson}\n\n", CancellationToken.None).ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
        });

        // 5. POST /edit/plan
        app.MapPost("/edit/plan", async ([FromBody] EditPlanRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return Results.BadRequest(new { error = "Prompt is required." });
            }

            var promptBuilder = _parentServiceProvider.GetRequiredService<IPromptBuilder>();
            var llmService = _parentServiceProvider.GetRequiredService<ILLMService>();
            var llmSettings = _parentServiceProvider.GetRequiredService<IOptions<LLMSettings>>().Value;
            var promptingSettings = _parentServiceProvider.GetRequiredService<IOptions<PromptingSettings>>().Value;
            var workspaceEditService = _parentServiceProvider.GetRequiredService<IWorkspaceEditService>();

            var editPrompt = $"[EDIT_REQUEST] {request.Prompt}";
            IReadOnlyList<RetrievedContext> context = Array.Empty<RetrievedContext>();

            try
            {
                var contextOrchestrator = _parentServiceProvider.GetRequiredService<IContextOrchestrator>();
                context = await contextOrchestrator.OrchestrateContextAsync(
                    request.Prompt,
                    request.RepositoryId,
                    request.ActiveFilePath,
                    request.CursorLine,
                    request.SelectedCode,
                    promptingSettings.MaxPromptCharacters / 4,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to perform context orchestration for edit plan.");
            }

            var promptObj = await promptBuilder.BuildAsync(editPrompt, context, cancellationToken).ConfigureAwait(false);
            var promptText = promptObj.Text;

            try
            {
                var memoryOrchestrator = _parentServiceProvider.GetRequiredService<DevPilot.Core.Memory.PersistentContextOrchestrator>();
                var memoryPromptBuilder = _parentServiceProvider.GetRequiredService<DevPilot.RAG.MemoryAwarePromptBuilder>();
                var memory = await memoryOrchestrator.LoadMemoryContextAsync(request.RepositoryId, request.RepositoryPath, cancellationToken).ConfigureAwait(false);
                promptText = memoryPromptBuilder.EnrichPromptWithMemory(promptText, memory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load/enrich prompt with workspace memory.");
            }

            var inferenceRequest = new InferenceRequest(
                promptText,
                llmSettings.ModelId,
                llmSettings.MaxOutputTokens,
                llmSettings.Temperature,
                llmSettings.TopP,
                context);

            var fullGenerated = new StringBuilder();
            try
            {
                await foreach (var chunk in llmService.StreamAsync(inferenceRequest, cancellationToken))
                {
                    fullGenerated.Append(chunk);
                }
            }
            catch (Exception ex)
            {
                return Results.Problem($"Model generation failed: {ex.Message}");
            }

            var generatedText = fullGenerated.ToString();
            var plan = ExtractEditPlan(generatedText);
            if (plan == null)
            {
                return Results.BadRequest(new { error = "Failed to parse structured edit plan from model output.", rawOutput = generatedText });
            }

            var preview = await workspaceEditService.PreviewPlanAsync(plan, request.RepositoryPath, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { plan, preview });
        });

        // 6. POST /edit/apply
        app.MapPost("/edit/apply", async ([FromBody] ApplyEditPlanRequestDto request, CancellationToken cancellationToken) =>
        {
            if (request.Plan == null || string.IsNullOrWhiteSpace(request.RepositoryPath))
            {
                return Results.BadRequest(new { error = "Plan and RepositoryPath are required." });
            }

            var workspaceEditService = _parentServiceProvider.GetRequiredService<IWorkspaceEditService>();
            var (success, error) = await workspaceEditService.ApplyPlanAsync(request.Plan, request.RepositoryPath, cancellationToken).ConfigureAwait(false);

            if (!success)
            {
                return Results.BadRequest(new { error });
            }

            try
            {
                var memoryStore = _parentServiceProvider.GetRequiredService<IWorkspaceMemoryStore>();
                foreach (var fileEdit in request.Plan.FileEdits)
                {
                    var desc = $"Applied edits to {Path.GetFileName(fileEdit.FilePath)}: {string.Join(", ", fileEdit.Instructions.Select(i => i.EditDescription))}";
                    var repoId = string.IsNullOrEmpty(request.RepositoryId) ? "default" : request.RepositoryId;
                    
                    var ev = new WorkspaceEvent(
                        RepositoryId: repoId,
                        EventType: "fix",
                        TimestampUtc: DateTime.UtcNow,
                        FilePath: fileEdit.FilePath,
                        SymbolName: fileEdit.Instructions.FirstOrDefault()?.TargetSymbol,
                        Description: desc,
                        Outcome: "success",
                        Payload: JsonSerializer.Serialize(fileEdit)
                    );
                    await memoryStore.SaveEventAsync(ev, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record applied edit plan in workspace memory store.");
            }

            return Results.Ok(new { success = true });
        });

        // 7. POST /edit/revert
        app.MapPost("/edit/revert", async ([FromBody] RevertEditPlanRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.RepositoryPath))
            {
                return Results.BadRequest(new { error = "RepositoryPath is required." });
            }

            var workspaceEditService = _parentServiceProvider.GetRequiredService<IWorkspaceEditService>();
            var (success, error) = await workspaceEditService.RevertLastPlanAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);

            if (!success)
            {
                return Results.BadRequest(new { error });
            }

            return Results.Ok(new { success = true });
        });

        // 8. POST /diagnostics/fix
        app.MapPost("/diagnostics/fix", async ([FromBody] FixDiagnosticRequest request, CancellationToken cancellationToken) =>
        {
            if (request.Diagnostic == null || string.IsNullOrWhiteSpace(request.SurroundingCode) || string.IsNullOrWhiteSpace(request.RepositoryPath))
            {
                return Results.BadRequest(new { error = "Diagnostic, SurroundingCode, and RepositoryPath are required." });
            }

            var promptBuilder = _parentServiceProvider.GetRequiredService<DiagnosticAwarePromptBuilder>();
            var llmService = _parentServiceProvider.GetRequiredService<ILLMService>();
            var llmSettings = _parentServiceProvider.GetRequiredService<IOptions<LLMSettings>>().Value;
            var workspaceEditService = _parentServiceProvider.GetRequiredService<IWorkspaceEditService>();

            string? activeSymbolContent = null;
            var siblingSymbols = new List<string>();

            if (!string.IsNullOrEmpty(request.RepositoryId) && !string.IsNullOrEmpty(request.RepositoryPath))
            {
                try
                {
                    var symbolStore = _parentServiceProvider.GetRequiredService<ISymbolStore>();
                    var chunkStore = _parentServiceProvider.GetRequiredService<IChunkStore>();

                    var relativePath = request.FilePath;
                    if (relativePath.StartsWith(request.RepositoryPath, StringComparison.OrdinalIgnoreCase))
                    {
                        relativePath = relativePath.Substring(request.RepositoryPath.Length).TrimStart('\\', '/');
                    }
                    relativePath = relativePath.Replace("\\", "/");

                    var fileId = DeterministicId($"{request.RepositoryId}:{relativePath}");
                    var symbols = await symbolStore.ListByFileAsync(fileId, cancellationToken).ConfigureAwait(false);

                    if (symbols.Count > 0)
                    {
                        var activeSymbol = symbols
                            .Where(s => request.Diagnostic.Line >= s.StartLine && request.Diagnostic.Line <= s.EndLine)
                            .OrderBy(s => s.EndLine - s.StartLine)
                            .FirstOrDefault();

                        if (activeSymbol != null)
                        {
                            var chunk = await chunkStore.GetAsync(activeSymbol.SymbolId, cancellationToken).ConfigureAwait(false);
                            if (chunk != null)
                            {
                                activeSymbolContent = chunk.Content;
                            }

                            var siblings = symbols.Where(s => s.ParentSymbol == activeSymbol.ParentSymbol && s.SymbolId != activeSymbol.SymbolId);
                            foreach (var sib in siblings.Take(3))
                            {
                                siblingSymbols.Add($"{sib.Kind} {sib.Name} (Lines {sib.StartLine}-{sib.EndLine})");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to perform context orchestration for diagnostic fix.");
                }
            }

            var prompt = promptBuilder.BuildDiagnosticFixPrompt(
                request.Diagnostic,
                request.SurroundingCode,
                activeSymbolContent,
                siblingSymbols);

            try
            {
                var memoryOrchestrator = _parentServiceProvider.GetRequiredService<DevPilot.Core.Memory.PersistentContextOrchestrator>();
                var memoryPromptBuilder = _parentServiceProvider.GetRequiredService<DevPilot.RAG.MemoryAwarePromptBuilder>();
                var memory = await memoryOrchestrator.LoadMemoryContextAsync(request.RepositoryId ?? "default", request.RepositoryPath, cancellationToken).ConfigureAwait(false);
                prompt = memoryPromptBuilder.EnrichPromptWithMemory(prompt, memory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load/enrich prompt with workspace memory.");
            }

            var inferenceRequest = new InferenceRequest(
                Prompt: prompt,
                ModelId: llmSettings.ModelId,
                MaxOutputTokens: llmSettings.MaxOutputTokens,
                Temperature: 0.0,
                TopP: 1.0,
                Context: Array.Empty<RetrievedContext>()
            );

            var fullGenerated = new StringBuilder();
            try
            {
                await foreach (var chunk in llmService.StreamAsync(inferenceRequest, cancellationToken))
                {
                    fullGenerated.Append(chunk);
                }
            }
            catch (Exception ex)
            {
                return Results.Problem($"Model generation failed: {ex.Message}");
            }

            var generatedText = fullGenerated.ToString();
            var plan = ExtractEditPlan(generatedText);
            if (plan == null)
            {
                return Results.BadRequest(new { error = "Failed to parse structured edit plan from model output.", rawOutput = generatedText });
            }

            var validationError = ValidateEditPlan(plan, request.RepositoryPath);
            if (validationError != null)
            {
                return Results.BadRequest(new { error = $"Validation failed: {validationError}", rawOutput = generatedText });
            }

            var preview = await workspaceEditService.PreviewPlanAsync(plan, request.RepositoryPath, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { plan, preview });
        });

        // 9. POST /diagnostics/analyze-test
        app.MapPost("/diagnostics/analyze-test", async (HttpContext httpContext, [FromBody] TestFailureAnalysisRequest request, CancellationToken cancellationToken) =>
        {
            var promptBuilder = _parentServiceProvider.GetRequiredService<DiagnosticAwarePromptBuilder>();
            var llmService = _parentServiceProvider.GetRequiredService<ILLMService>();
            var llmSettings = _parentServiceProvider.GetRequiredService<IOptions<LLMSettings>>().Value;

            string? targetMethodCode = null;

            try
            {
                var frames = StackTraceParser.Parse(request.StackTrace);
                if (frames.Count > 0)
                {
                    foreach (var frame in frames)
                    {
                        var path = frame.FilePath;
                        if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(request.RepositoryPath))
                        {
                            path = Path.Combine(request.RepositoryPath, path);
                        }

                        if (File.Exists(path))
                        {
                            var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
                            var startLine = Math.Max(0, frame.Line - 8);
                            var endLine = Math.Min(lines.Length - 1, frame.Line + 7);
                            var codeBuilder = new StringBuilder();
                            for (int i = startLine; i <= endLine; i++)
                            {
                                var prefix = (i + 1) == frame.Line ? "=> " : "   ";
                                codeBuilder.AppendLine($"{prefix}{i + 1}: {lines[i]}");
                            }
                            targetMethodCode = codeBuilder.ToString();
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse stack trace or read failing file lines for test analysis.");
            }

            var prompt = promptBuilder.BuildTestFailureAnalysisPrompt(
                request.TestName,
                request.ErrorMessage,
                request.StackTrace,
                targetMethodCode);

            var inferenceRequest = new InferenceRequest(
                Prompt: prompt,
                ModelId: llmSettings.ModelId,
                MaxOutputTokens: llmSettings.MaxOutputTokens,
                Temperature: 0.0,
                TopP: 1.0,
                Context: Array.Empty<RetrievedContext>()
            );

            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            try
            {
                await foreach (var chunk in llmService.StreamAsync(inferenceRequest, cancellationToken).ConfigureAwait(false))
                {
                    var chunkJson = JsonSerializer.Serialize(new { type = "content", text = chunk });
                    await httpContext.Response.WriteAsync($"data: {chunkJson}\n\n", cancellationToken).ConfigureAwait(false);
                    await httpContext.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                var doneJson = JsonSerializer.Serialize(new { type = "done" });
                await httpContext.Response.WriteAsync($"data: {doneJson}\n\n", cancellationToken).ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                var errorJson = JsonSerializer.Serialize(new { type = "cancelled" });
                await httpContext.Response.WriteAsync($"data: {errorJson}\n\n", CancellationToken.None).ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var errorJson = JsonSerializer.Serialize(new { type = "error", message = ex.Message });
                await httpContext.Response.WriteAsync($"data: {errorJson}\n\n", CancellationToken.None).ConfigureAwait(false);
                await httpContext.Response.Body.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
        });

        // 10. POST /execution/analyze
        app.MapPost("/execution/analyze", async ([FromBody] AnalyzeExecutionEventRequest request, CancellationToken cancellationToken) =>
        {
            if (request.Event == null)
            {
                return Results.BadRequest(new { error = "Event is required." });
            }
            if (string.IsNullOrWhiteSpace(request.RepositoryPath))
            {
                return Results.BadRequest(new { error = "RepositoryPath is required." });
            }

            var terminalOrchestrator = _parentServiceProvider.GetRequiredService<TerminalOrchestrator>();
            var contextOrchestrator = _parentServiceProvider.GetRequiredService<ExecutionContextOrchestrator>();
            var promptBuilder = _parentServiceProvider.GetRequiredService<ExecutionAwarePromptBuilder>();
            var llmService = _parentServiceProvider.GetRequiredService<ILLMService>();
            var llmSettings = _parentServiceProvider.GetRequiredService<IOptions<LLMSettings>>().Value;
            var workspaceEditService = _parentServiceProvider.GetRequiredService<IWorkspaceEditService>();

            var ev = request.Event;
            if (string.IsNullOrWhiteSpace(ev.Message) && !string.IsNullOrWhiteSpace(ev.RawOutput))
            {
                ev = terminalOrchestrator.ParseTerminalOutput(ev.RawOutput);
            }

            var (surroundingCode, activeSymbolCode, siblingSymbols) = await contextOrchestrator.ResolveContextAsync(
                ev,
                request.RepositoryId,
                request.RepositoryPath,
                cancellationToken).ConfigureAwait(false);

            var prompt = promptBuilder.BuildExecutionFixPrompt(
                ev,
                surroundingCode,
                activeSymbolCode,
                siblingSymbols);

            try
            {
                var memoryOrchestrator = _parentServiceProvider.GetRequiredService<DevPilot.Core.Memory.PersistentContextOrchestrator>();
                var memoryPromptBuilder = _parentServiceProvider.GetRequiredService<DevPilot.RAG.MemoryAwarePromptBuilder>();
                var memory = await memoryOrchestrator.LoadMemoryContextAsync(request.RepositoryId ?? "default", request.RepositoryPath, cancellationToken).ConfigureAwait(false);
                prompt = memoryPromptBuilder.EnrichPromptWithMemory(prompt, memory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load/enrich prompt with workspace memory.");
            }

            var inferenceRequest = new InferenceRequest(
                Prompt: prompt,
                ModelId: llmSettings.ModelId,
                MaxOutputTokens: llmSettings.MaxOutputTokens,
                Temperature: 0.0,
                TopP: 1.0,
                Context: Array.Empty<RetrievedContext>()
            );

            var fullGenerated = new StringBuilder();
            try
            {
                await foreach (var chunk in llmService.StreamAsync(inferenceRequest, cancellationToken))
                {
                    fullGenerated.Append(chunk);
                }
            }
            catch (Exception ex)
            {
                return Results.Problem($"Model generation failed: {ex.Message}");
            }

            var generatedText = fullGenerated.ToString();
            var plan = ExtractEditPlan(generatedText);
            if (plan == null)
            {
                return Results.BadRequest(new { error = "Failed to parse structured edit plan from model output.", rawOutput = generatedText });
            }

            var validationError = ValidateEditPlan(plan, request.RepositoryPath);
            if (validationError != null)
            {
                return Results.BadRequest(new { error = $"Validation failed: {validationError}", rawOutput = generatedText });
            }

            var preview = await workspaceEditService.PreviewPlanAsync(plan, request.RepositoryPath, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { plan, preview });
        });

        // 11. Execution pipeline orchestration endpoints
        app.MapGet("/execution/pipelines", async ([FromQuery] string? workflowId, CancellationToken cancellationToken) =>
        {
            var orchestrator = _parentServiceProvider.GetRequiredService<IExecutionPipelineOrchestrator>();
            var pipelines = await orchestrator.ListAsync(workflowId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(pipelines);
        });

        app.MapGet("/execution/pipeline/{id}", async (string id, CancellationToken cancellationToken) =>
        {
            var orchestrator = _parentServiceProvider.GetRequiredService<IExecutionPipelineOrchestrator>();
            var state = await orchestrator.GetAsync(id, cancellationToken).ConfigureAwait(false);
            return state == null
                ? Results.NotFound(new { error = $"Execution pipeline '{id}' was not found." })
                : Results.Ok(state);
        });

        app.MapPost("/execution/start", async ([FromBody] ExecutionStartRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.WorkflowId) || string.IsNullOrWhiteSpace(request.Objective))
            {
                return Results.BadRequest(new { error = "WorkflowId and Objective are required." });
            }

            var orchestrator = _parentServiceProvider.GetRequiredService<IExecutionPipelineOrchestrator>();
            try
            {
                var state = await orchestrator.StartAsync(
                    new StartExecutionPipelineRequest(
                        request.WorkflowId,
                        request.WorkflowTaskId,
                        request.Objective,
                        request.RepositoryId,
                        request.RepositoryPath,
                        request.DryRun ?? true,
                        request.ValidationOnly ?? false),
                    cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(request.RepositoryPath))
                {
                    var targets = request.TargetPaths ?? Array.Empty<string>();
                    state = await orchestrator.PrepareRollbackAsync(state.Pipeline.PipelineId, request.RepositoryPath, targets, cancellationToken).ConfigureAwait(false);
                }

                return Results.Ok(state);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/execution/validate", async ([FromBody] ExecutionValidateRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.PipelineId))
            {
                return Results.BadRequest(new { error = "PipelineId is required." });
            }

            var orchestrator = _parentServiceProvider.GetRequiredService<IExecutionPipelineOrchestrator>();
            try
            {
                var state = await orchestrator.CompleteValidationAsync(
                    new CompleteExecutionValidationRequest(
                        request.PipelineId,
                        request.IsValid,
                        request.Messages ?? Array.Empty<string>(),
                        request.Diagnostics ?? Array.Empty<NormalizedDiagnostic>(),
                        request.RawOutput,
                        request.Metadata),
                    cancellationToken).ConfigureAwait(false);
                return Results.Ok(state);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/execution/approve", async ([FromBody] ExecutionApproveRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.PipelineId))
            {
                return Results.BadRequest(new { error = "PipelineId is required." });
            }

            var orchestrator = _parentServiceProvider.GetRequiredService<IExecutionPipelineOrchestrator>();
            try
            {
                var state = await orchestrator.ApproveAsync(
                    new ApproveExecutionPipelineRequest(
                        request.PipelineId,
                        request.ApprovedBy ?? "user",
                        request.Notes),
                    cancellationToken).ConfigureAwait(false);
                return Results.Ok(state);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/execution/apply", async ([FromBody] ExecutionApplyRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.PipelineId) || request.Plan == null || string.IsNullOrWhiteSpace(request.RepositoryPath))
            {
                return Results.BadRequest(new { error = "PipelineId, Plan, and RepositoryPath are required." });
            }

            var orchestrator = _parentServiceProvider.GetRequiredService<IExecutionPipelineOrchestrator>();
            var workspaceEditService = _parentServiceProvider.GetRequiredService<IWorkspaceEditService>();
            var pipelineState = await orchestrator.GetAsync(request.PipelineId, cancellationToken).ConfigureAwait(false);
            if (pipelineState == null)
            {
                return Results.NotFound(new { error = $"Execution pipeline '{request.PipelineId}' was not found." });
            }

            var preview = await workspaceEditService.PreviewPlanAsync(request.Plan, request.RepositoryPath, cancellationToken).ConfigureAwait(false);
            if (preview.FilePreviews.Any(file => !file.IsValid))
            {
                var messages = preview.FilePreviews
                    .Where(file => !file.IsValid)
                    .Select(file => $"{file.FilePath}: {file.ErrorMessage}")
                    .ToList();
                var failed = await orchestrator.CompleteValidationAsync(
                    new CompleteExecutionValidationRequest(request.PipelineId, false, messages, RawOutput: string.Join(Environment.NewLine, messages)),
                    cancellationToken).ConfigureAwait(false);
                return Results.BadRequest(new { error = "Patch preview validation failed.", state = failed });
            }

            try
            {
                if (pipelineState.Pipeline.DryRun || pipelineState.Pipeline.ValidationOnly)
                {
                    return Results.BadRequest(new { error = "Dry-run and validation-only pipelines cannot apply repository changes." });
                }

                var applyResult = await workspaceEditService.ApplyPlanAsync(request.Plan, request.RepositoryPath, cancellationToken).ConfigureAwait(false);
                if (!applyResult.Success)
                {
                    var failed = await orchestrator.CompleteValidationAsync(
                        new CompleteExecutionValidationRequest(request.PipelineId, false, new[] { applyResult.ErrorMessage ?? "Patch apply failed." }),
                        cancellationToken).ConfigureAwait(false);
                    return Results.BadRequest(new { error = applyResult.ErrorMessage, state = failed });
                }

                var state = await orchestrator.MarkAppliedAsync(
                    new ApplyExecutionPipelineRequest(
                        request.PipelineId,
                        JsonSerializer.Serialize(new { preview, applyResult }),
                        request.Metadata),
                    cancellationToken).ConfigureAwait(false);
                return Results.Ok(new { state, preview, applyResult });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/execution/rollback", async ([FromBody] ExecutionRollbackRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.PipelineId) || string.IsNullOrWhiteSpace(request.RepositoryPath))
            {
                return Results.BadRequest(new { error = "PipelineId and RepositoryPath are required." });
            }

            var orchestrator = _parentServiceProvider.GetRequiredService<IExecutionPipelineOrchestrator>();
            var workspaceEditService = _parentServiceProvider.GetRequiredService<IWorkspaceEditService>();
            try
            {
                await orchestrator.TriggerRollbackAsync(request.PipelineId, request.Reason, cancellationToken).ConfigureAwait(false);
                var revertResult = await workspaceEditService.RevertLastPlanAsync(request.RepositoryPath, cancellationToken).ConfigureAwait(false);
                if (!revertResult.Success)
                {
                    return Results.BadRequest(new { error = revertResult.ErrorMessage });
                }

                var state = await orchestrator.MarkRollbackCompletedAsync(request.PipelineId, request.Metadata, cancellationToken).ConfigureAwait(false);
                return Results.Ok(new { state, revertResult });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/execution/cancel", async ([FromBody] ExecutionCancelRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.PipelineId))
            {
                return Results.BadRequest(new { error = "PipelineId is required." });
            }

            var orchestrator = _parentServiceProvider.GetRequiredService<IExecutionPipelineOrchestrator>();
            try
            {
                var state = await orchestrator.CancelAsync(request.PipelineId, request.Reason, cancellationToken).ConfigureAwait(false);
                return Results.Ok(state);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 12. Workflow orchestration endpoints
        app.MapGet("/workflow/list", async ([FromQuery] string? repositoryId, CancellationToken cancellationToken) =>
        {
            var orchestrator = _parentServiceProvider.GetRequiredService<ITaskGraphOrchestrator>();
            var workflows = await orchestrator.ListAsync(repositoryId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(workflows);
        });

        app.MapGet("/workflow/{id}", async (string id, CancellationToken cancellationToken) =>
        {
            var orchestrator = _parentServiceProvider.GetRequiredService<ITaskGraphOrchestrator>();
            var state = await orchestrator.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (state == null)
            {
                return Results.NotFound(new { error = $"Workflow '{id}' was not found." });
            }

            return Results.Ok(new { state, progress = orchestrator.GetProgressSnapshot(state) });
        });

        app.MapPost("/workflow/start", async ([FromBody] WorkflowStartRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Objective))
            {
                return Results.BadRequest(new { error = "Objective is required." });
            }

            var orchestrator = _parentServiceProvider.GetRequiredService<ITaskGraphOrchestrator>();
            var state = await orchestrator.StartAsync(
                new StartWorkflowRequest(new EngineeringWorkflowRequest(
                    request.Objective,
                    request.RepositoryId,
                    request.RepositoryPath,
                    Constraints: request.Constraints ?? Array.Empty<string>())),
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(new { state, progress = orchestrator.GetProgressSnapshot(state) });
        });

        app.MapPost("/workflow/advance", async ([FromBody] WorkflowAdvanceRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.WorkflowId) || string.IsNullOrWhiteSpace(request.TaskId))
            {
                return Results.BadRequest(new { error = "WorkflowId and TaskId are required." });
            }

            if (!Enum.TryParse<WorkflowTaskStatus>(request.TargetStatus, ignoreCase: true, out var targetStatus))
            {
                return Results.BadRequest(new { error = $"Invalid target status '{request.TargetStatus}'." });
            }

            var orchestrator = _parentServiceProvider.GetRequiredService<ITaskGraphOrchestrator>();
            try
            {
                var state = await orchestrator.AdvanceAsync(
                    new AdvanceWorkflowRequest(
                        request.WorkflowId,
                        request.TaskId,
                        targetStatus,
                        request.Reason,
                        request.ApprovalGranted,
                        request.Metadata),
                    cancellationToken).ConfigureAwait(false);
                return Results.Ok(new { state, progress = orchestrator.GetProgressSnapshot(state) });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/workflow/pause", async ([FromBody] WorkflowIdRequestDto request, CancellationToken cancellationToken) =>
        {
            var orchestrator = _parentServiceProvider.GetRequiredService<ITaskGraphOrchestrator>();
            try
            {
                var state = await orchestrator.PauseAsync(request.WorkflowId, request.Reason, cancellationToken).ConfigureAwait(false);
                return Results.Ok(new { state, progress = orchestrator.GetProgressSnapshot(state) });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/workflow/resume", async ([FromBody] WorkflowIdRequestDto request, CancellationToken cancellationToken) =>
        {
            var orchestrator = _parentServiceProvider.GetRequiredService<ITaskGraphOrchestrator>();
            try
            {
                var state = await orchestrator.ResumeAsync(request.WorkflowId, cancellationToken).ConfigureAwait(false);
                return Results.Ok(new { state, progress = orchestrator.GetProgressSnapshot(state) });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/workflow/cancel", async ([FromBody] WorkflowIdRequestDto request, CancellationToken cancellationToken) =>
        {
            var orchestrator = _parentServiceProvider.GetRequiredService<ITaskGraphOrchestrator>();
            try
            {
                var state = await orchestrator.CancelAsync(request.WorkflowId, request.Reason, cancellationToken).ConfigureAwait(false);
                return Results.Ok(new { state, progress = orchestrator.GetProgressSnapshot(state) });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 13. POST /memory/events
        app.MapPost("/memory/events", async ([FromBody] ListEventsRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.RepositoryId))
            {
                return Results.BadRequest(new { error = "RepositoryId is required." });
            }

            var memoryStore = _parentServiceProvider.GetRequiredService<IWorkspaceMemoryStore>();
            var events = await memoryStore.ListEventsAsync(request.RepositoryId, request.Limit ?? 20, cancellationToken).ConfigureAwait(false);
            return Results.Ok(events);
        });

        // 14. Knowledge Graph endpoints
        app.MapGet("/graph/node/{id}", async (string id, CancellationToken cancellationToken) =>
        {
            var graphStore = _parentServiceProvider.GetRequiredService<IGraphStore>();
            var node = await graphStore.GetNodeAsync(id, cancellationToken).ConfigureAwait(false);
            return node == null
                ? Results.NotFound(new { error = $"Graph node '{id}' was not found." })
                : Results.Ok(node);
        });

        app.MapGet("/graph/relationships/{id}", async (string id, [FromQuery] string? direction, CancellationToken cancellationToken) =>
        {
            var graphStore = _parentServiceProvider.GetRequiredService<IGraphStore>();
            var dir = GraphDirection.Both;
            if (!string.IsNullOrEmpty(direction) && Enum.TryParse<GraphDirection>(direction, ignoreCase: true, out var parsed))
            {
                dir = parsed;
            }

            var node = await graphStore.GetNodeAsync(id, cancellationToken).ConfigureAwait(false);
            if (node == null)
            {
                return Results.NotFound(new { error = $"Graph node '{id}' was not found." });
            }

            var relationships = await graphStore.GetRelationshipsAsync(id, dir, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { node, relationships });
        });

        app.MapPost("/graph/query", async ([FromBody] GraphQueryRequestDto request, CancellationToken cancellationToken) =>
        {
            var graphStore = _parentServiceProvider.GetRequiredService<IGraphStore>();
            var traversalService = _parentServiceProvider.GetRequiredService<IGraphTraversalService>();

            GraphNodeKind? nodeKind = null;
            if (!string.IsNullOrEmpty(request.NodeKindFilter) && Enum.TryParse<GraphNodeKind>(request.NodeKindFilter, ignoreCase: true, out var nk))
            {
                nodeKind = nk;
            }

            var nodes = await graphStore.QueryNodesAsync(
                nodeKind,
                request.LabelContains,
                request.MetadataContains,
                request.EntityId,
                request.MaxResults ?? 50,
                cancellationToken).ConfigureAwait(false);

            GraphRelationshipKind? relKind = null;
            if (!string.IsNullOrEmpty(request.RelationshipKindFilter) && Enum.TryParse<GraphRelationshipKind>(request.RelationshipKindFilter, ignoreCase: true, out var rk))
            {
                relKind = rk;
            }

            var relationships = await graphStore.QueryRelationshipsAsync(
                relKind,
                maxResults: request.MaxResults ?? 50,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return Results.Ok(new { nodes, relationships, totalMatches = nodes.Count });
        });

        app.MapPost("/graph/lineage", async ([FromBody] GraphLineageRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.NodeId))
            {
                return Results.BadRequest(new { error = "NodeId is required." });
            }

            var traversalService = _parentServiceProvider.GetRequiredService<IGraphTraversalService>();

            var dir = GraphDirection.Both;
            if (!string.IsNullOrEmpty(request.Direction) && Enum.TryParse<GraphDirection>(request.Direction, ignoreCase: true, out var parsed))
            {
                dir = parsed;
            }

            IReadOnlyList<GraphRelationshipKind>? kindFilter = null;
            if (request.RelationshipKindFilter is { Count: > 0 })
            {
                var parsedKinds = new List<GraphRelationshipKind>();
                foreach (var k in request.RelationshipKindFilter)
                {
                    if (Enum.TryParse<GraphRelationshipKind>(k, ignoreCase: true, out var rk))
                    {
                        parsedKinds.Add(rk);
                    }
                }
                kindFilter = parsedKinds;
            }

            var lineageRequest = new GraphLineageRequest(
                request.NodeId,
                dir,
                request.MaxDepth ?? 10,
                kindFilter);

            var result = await traversalService.GetLineageAsync(lineageRequest, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        });

        // 15. Contextual Reasoning endpoints
        app.MapPost("/reasoning/correlate", async ([FromBody] CorrelateRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.RepositoryId))
            {
                return Results.BadRequest(new { error = "RepositoryId is required." });
            }

            var correlationEngine = _parentServiceProvider.GetRequiredService<IEngineeringCorrelationEngine>();
            
            var failures = await correlationEngine.CorrelateFailuresToWorkflowsAsync(request.RepositoryId, cancellationToken).ConfigureAwait(false);
            var patches = await correlationEngine.CorrelatePatchesToDiagnosticsAsync(request.RepositoryId, cancellationToken).ConfigureAwait(false);
            var execution = await correlationEngine.CorrelateExecutionToChangesAsync(request.RepositoryId, cancellationToken).ConfigureAwait(false);
            var architecture = await correlationEngine.CorrelateArchitectureViolationsAsync(request.RepositoryId, cancellationToken).ConfigureAwait(false);

            return Results.Ok(new { failures, patches, execution, architecture });
        });

        app.MapPost("/reasoning/root-cause", async ([FromBody] RootCauseRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.FailureNodeId))
            {
                return Results.BadRequest(new { error = "FailureNodeId is required." });
            }

            var rootCauseReasoner = _parentServiceProvider.GetRequiredService<IRootCauseReasoner>();
            try
            {
                var result = await rootCauseReasoner.AnalyzeRootCauseAsync(request.FailureNodeId, cancellationToken).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPost("/reasoning/impact-analysis", async ([FromBody] ImpactAnalysisRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.TargetNodeId))
            {
                return Results.BadRequest(new { error = "TargetNodeId is required." });
            }

            var traversalService = _parentServiceProvider.GetRequiredService<IGraphTraversalService>();
            var rankingEngine = _parentServiceProvider.GetRequiredService<IContextRankingEngine>();

            // Get downstream path
            var lineageRequest = new GraphLineageRequest(
                request.TargetNodeId,
                GraphDirection.Outgoing,
                request.MaxDepth ?? 6);

            var lineage = await traversalService.GetLineageAsync(lineageRequest, cancellationToken).ConfigureAwait(false);

            // Fetch candidate context nodes
            var candidates = lineage.Chain.Select(step => step.Node).ToList();

            // Rank candidates
            var ranked = await rankingEngine.RankContextAsync(request.TargetNodeId, candidates, cancellationToken).ConfigureAwait(false);

            return Results.Ok(new { targetNodeId = request.TargetNodeId, lineage, ranked });
        });

        // 16. Failure Attribution Engine endpoints
        app.MapPost("/failure/analyze", async ([FromBody] FailureAnalyzeRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.FailureNodeId))
            {
                return Results.BadRequest(new { error = "FailureNodeId is required." });
            }

            var attributionEngine = _parentServiceProvider.GetRequiredService<IFailureAttributionEngine>();
            try
            {
                var result = await attributionEngine.AttributeFailureAsync(request.FailureNodeId, cancellationToken).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPost("/failure/lineage", async ([FromBody] FailureLineageRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.FailureNodeId))
            {
                return Results.BadRequest(new { error = "FailureNodeId is required." });
            }

            var lineageResolver = _parentServiceProvider.GetRequiredService<IFailureLineageResolver>();
            try
            {
                var result = await lineageResolver.ResolveLineageAsync(request.FailureNodeId, cancellationToken).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        app.MapPost("/failure/patch-impact", async ([FromBody] PatchImpactRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.PatchNodeId))
            {
                return Results.BadRequest(new { error = "PatchNodeId is required." });
            }

            var patchImpactAnalyzer = _parentServiceProvider.GetRequiredService<IPatchImpactAnalyzer>();
            try
            {
                var result = await patchImpactAnalyzer.AnalyzePatchImpactAsync(request.PatchNodeId, cancellationToken).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        });

        // 17. Architecture Reasoning Engine endpoints
        app.MapPost("/architecture/analyze", async ([FromBody] ArchitectureAnalyzeRequestDto request, CancellationToken cancellationToken) =>
        {
            var repoId = string.IsNullOrWhiteSpace(request.RepositoryId) ? "devpilot-workspace" : request.RepositoryId;
            var reasoningEngine = _parentServiceProvider.GetRequiredService<IArchitectureReasoningEngine>();
            var result = await reasoningEngine.RunFullAnalysisAsync(repoId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        });

        app.MapPost("/architecture/violations", async ([FromBody] ArchitectureAnalyzeRequestDto request, CancellationToken cancellationToken) =>
        {
            var repoId = string.IsNullOrWhiteSpace(request.RepositoryId) ? "devpilot-workspace" : request.RepositoryId;
            var boundaryAnalyzer = _parentServiceProvider.GetRequiredService<IDependencyBoundaryAnalyzer>();
            var rules = new List<LayerBoundaryRule>
            {
                new LayerBoundaryRule("Contracts", Array.Empty<string>()),
                new LayerBoundaryRule("Core", new[] { "Contracts" }),
                new LayerBoundaryRule("LocalService", new[] { "Contracts", "Core" })
            };
            var result = await boundaryAnalyzer.AnalyzeBoundariesAsync(repoId, rules, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        });

        app.MapPost("/architecture/impact-analysis", async ([FromBody] ArchitectureImpactRequestDto request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.SourceModuleId) || string.IsNullOrWhiteSpace(request.TargetModuleId))
            {
                return Results.BadRequest(new { error = "SourceModuleId and TargetModuleId are required." });
            }

            var migrationAnalyzer = _parentServiceProvider.GetRequiredService<IMigrationImpactAnalyzer>();
            var result = await migrationAnalyzer.AnalyzeMigrationImpactAsync(request.SourceModuleId, request.TargetModuleId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        });

        // 18. Modernization Workflows Engine endpoints
        app.MapPost("/modernization/plan", async ([FromBody] ModernizationPlanRequestDto request, CancellationToken cancellationToken) =>
        {
            var repoId = string.IsNullOrWhiteSpace(request.RepositoryId) ? "devpilot-workspace" : request.RepositoryId;
            var modernizationEngine = _parentServiceProvider.GetRequiredService<ModernizationEngine>();
            var result = await modernizationEngine.GenerateAndRegisterPlanAsync(repoId, request.Type, request.TargetPayload, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        });

        app.MapPost("/modernization/analyze", async ([FromBody] ModernizationAnalyzeRequestDto request, CancellationToken cancellationToken) =>
        {
            var repoId = string.IsNullOrWhiteSpace(request.RepositoryId) ? "devpilot-workspace" : request.RepositoryId;
            var impactAnalyzer = _parentServiceProvider.GetRequiredService<IDependencyImpactAnalyzer>();
            var result = await impactAnalyzer.AnalyzeModernizationImpactAsync(repoId, request.Type, request.TargetPayload, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        });

        app.MapPost("/modernization/execute", async ([FromBody] ModernizationExecuteRequestDto request, CancellationToken cancellationToken) =>
        {
            var modernizationEngine = _parentServiceProvider.GetRequiredService<ModernizationEngine>();
            try
            {
                if (request.Action == "approve")
                {
                    var plan = await modernizationEngine.ApprovePlanAsync(request.PlanId, cancellationToken).ConfigureAwait(false);
                    return Results.Ok(plan);
                }
                else if (request.Action == "execute-step")
                {
                    if (string.IsNullOrWhiteSpace(request.StepId))
                    {
                        return Results.BadRequest(new { error = "StepId is required for execute-step action." });
                    }
                    var plan = await modernizationEngine.ExecuteStepAsync(request.PlanId, request.StepId, cancellationToken).ConfigureAwait(false);
                    return Results.Ok(plan);
                }
                else if (request.Action == "rollback")
                {
                    var plan = await modernizationEngine.RollbackPlanAsync(request.PlanId, cancellationToken).ConfigureAwait(false);
                    return Results.Ok(plan);
                }
                else
                {
                    return Results.BadRequest(new { error = "Invalid modernization action. Supported: approve, execute-step, rollback." });
                }
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 19. Productization Layer Endpoints
        app.MapGet("/product/settings", () =>
        {
            var settingsManager = _parentServiceProvider.GetRequiredService<ISettingsManager>();
            return Results.Ok(settingsManager.GetSettings());
        });

        app.MapPost("/product/settings", ([FromBody] SaveSettingsRequestDto request) =>
        {
            var settingsManager = _parentServiceProvider.GetRequiredService<ISettingsManager>();
            var newSettings = new ProductSettings(
                ModelStoragePath: request.ModelStoragePath,
                ActiveLlmModel: request.ActiveLlmModel,
                ActiveEmbeddingModel: request.ActiveEmbeddingModel,
                HardwareProviderPreference: request.HardwareProviderPreference,
                LogLevelThreshold: request.LogLevelThreshold
            );
            settingsManager.SaveSettings(newSettings);
            return Results.Ok(newSettings);
        });

        app.MapGet("/product/models", () =>
        {
            var modelManager = _parentServiceProvider.GetRequiredService<IProductModelManager>();
            return Results.Ok(modelManager.GetModelsStatus());
        });

        app.MapPost("/product/models/download", async ([FromBody] ModelDownloadRequestDto request, CancellationToken cancellationToken) =>
        {
            var modelManager = _parentServiceProvider.GetRequiredService<IProductModelManager>();
            await modelManager.StartDownloadAsync(request.ModelId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { status = "Downloading", modelId = request.ModelId });
        });

        app.MapGet("/product/dependencies", () =>
        {
            var bootstrapper = _parentServiceProvider.GetRequiredService<IDependencyBootstrapper>();
            return Results.Ok(bootstrapper.VerifyDependencies());
        });

        app.MapPost("/product/dependencies/repair", async ([FromBody] DependencyRepairRequestDto request) =>
        {
            var bootstrapper = _parentServiceProvider.GetRequiredService<IDependencyBootstrapper>();
            var success = await bootstrapper.RunRepairToolAsync(request.DependencyName).ConfigureAwait(false);
            return Results.Ok(new { success });
        });

        app.MapGet("/product/diagnostics", () =>
        {
            var diag = _parentServiceProvider.GetRequiredService<IRuntimeDiagnosticsManager>();
            var response = new
            {
                tokenThroughput = diag.GetTokenThroughput(),
                peakWorkingSetMemory = diag.GetPeakWorkingSetMemory(),
                activeDeviceDescription = diag.GetActiveDeviceDescription()
            };
            return Results.Ok(response);
        });

        app.MapGet("/product/onboarding", () =>
        {
            var onboarding = _parentServiceProvider.GetRequiredService<IOnboardingManager>();
            var response = new
            {
                completed = onboarding.IsOnboardingCompleted(),
                hardwareDetails = onboarding.DetectHardwareCapabilities()
            };
            return Results.Ok(response);
        });

        app.MapPost("/product/onboarding/complete", ([FromBody] CompleteOnboardingRequestDto request) =>
        {
            var onboarding = _parentServiceProvider.GetRequiredService<IOnboardingManager>();
            onboarding.CompleteOnboarding();
            return Results.Ok(new { completed = true });
        });

        app.MapGet("/product/updates", () =>
        {
            var updateManager = _parentServiceProvider.GetRequiredService<IUpdateManager>();
            return Results.Ok(updateManager.CheckForUpdates());
        });

        app.MapPost("/product/updates/apply", async ([FromBody] ApplyUpdateRequestDto request, CancellationToken cancellationToken) =>
        {
            var updateManager = _parentServiceProvider.GetRequiredService<IUpdateManager>();
            var success = await updateManager.ApplyUpdateAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { success });
        });

        app.MapGet("/product/logs", () =>
        {
            var logViewer = _parentServiceProvider.GetRequiredService<ILogViewerService>();
            return Results.Ok(logViewer.RetrieveLatestLogs(20));
        });

        _logger.LogInformation("DevPilot local service web host started on http://localhost:5071");

        try
        {
            await app.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("DevPilot local service web host stopped due to cancellation.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DevPilot local service web host encountered an error.");
        }
    }

    private static EditPlan? ExtractEditPlan(string text)
    {
        var jsonStart = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (jsonStart == -1)
        {
            jsonStart = text.IndexOf("{", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            jsonStart += 7;
        }

        if (jsonStart == -1) return null;

        var jsonEnd = text.LastIndexOf("```", StringComparison.OrdinalIgnoreCase);
        if (jsonEnd == -1 || jsonEnd <= jsonStart)
        {
            jsonEnd = text.LastIndexOf("}", StringComparison.OrdinalIgnoreCase);
        }

        if (jsonEnd == -1 || jsonEnd <= jsonStart) return null;

        var jsonString = text.Substring(jsonStart, jsonEnd - jsonStart + 1).Trim();
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<EditPlan>(jsonString, options);
        }
        catch
        {
            return null;
        }
    }

    private static string? ValidateEditPlan(EditPlan plan, string? repositoryPath)
    {
        if (plan.FileEdits == null || plan.FileEdits.Count == 0)
        {
            return "Edit plan contains no file edits.";
        }

        foreach (var edit in plan.FileEdits)
        {
            if (string.IsNullOrWhiteSpace(edit.FilePath))
            {
                return "File path is empty.";
            }

            var targetPath = edit.FilePath;
            if (!Path.IsPathRooted(targetPath) && !string.IsNullOrWhiteSpace(repositoryPath))
            {
                targetPath = Path.Combine(repositoryPath, edit.FilePath);
            }

            if (!File.Exists(targetPath))
            {
                return $"File '{edit.FilePath}' does not exist.";
            }

            if (edit.Instructions == null || edit.Instructions.Count == 0)
            {
                return $"No edit instructions provided for file '{edit.FilePath}'.";
            }

            string fileContent;
            try
            {
                fileContent = File.ReadAllText(targetPath);
            }
            catch (Exception ex)
            {
                return $"Failed to read file '{edit.FilePath}': {ex.Message}";
            }

            foreach (var inst in edit.Instructions)
            {
                if (string.IsNullOrEmpty(inst.SearchContent))
                {
                    return "SearchContent is empty.";
                }

                var normalizedFile = fileContent.Replace("\r\n", "\n");
                var normalizedSearch = inst.SearchContent.Replace("\r\n", "\n");

                if (!normalizedFile.Contains(normalizedSearch))
                {
                    return $"Target code block to replace could not be found in file '{edit.FilePath}'. Please ensure code structure matches exactly.";
                }
            }
        }

        return null;
    }

    private static string DeterministicId(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static long ToMegabytes(long bytes) => bytes / 1024 / 1024;
}

public record SearchRequestDto(string Query, int MaxResults, string? RepositoryId);
public record ChatRequestDto(
    string Prompt,
    string? RepositoryId,
    int MaxContextChunks,
    string? ModelId,
    string? ActiveFilePath = null,
    int? CursorLine = null,
    string? SelectedCode = null,
    string? SurroundingLines = null,
    string? LanguageId = null,
    IReadOnlyList<string>? VisibleImports = null);

public record ExplainSelectionRequestDto(
    string CodeSnippet,
    string? FilePath,
    string? LanguageId,
    string? RepositoryId,
    int? CursorLine = null,
    string? SurroundingLines = null,
    IReadOnlyList<string>? VisibleImports = null);

public record EditPlanRequestDto(
    string Prompt,
    string RepositoryId,
    string RepositoryPath,
    string? ActiveFilePath = null,
    int? CursorLine = null,
    string? SelectedCode = null,
    string? SurroundingLines = null,
    string? LanguageId = null,
    IReadOnlyList<string>? VisibleImports = null);

public record ApplyEditPlanRequestDto(
    EditPlan Plan,
    string RepositoryPath,
    string? RepositoryId = null);

public record RevertEditPlanRequestDto(
    string RepositoryPath);

public record ListEventsRequestDto(
    string RepositoryId,
    int? Limit = null);

public record WorkflowStartRequestDto(
    string Objective,
    string? RepositoryId = null,
    string? RepositoryPath = null,
    IReadOnlyList<string>? Constraints = null);

public record WorkflowAdvanceRequestDto(
    string WorkflowId,
    string TaskId,
    string TargetStatus,
    string? Reason = null,
    bool ApprovalGranted = false,
    string? Metadata = null);

public record WorkflowIdRequestDto(
    string WorkflowId,
    string? Reason = null);

public record ExecutionStartRequestDto(
    string WorkflowId,
    string Objective,
    string? WorkflowTaskId = null,
    string? RepositoryId = null,
    string? RepositoryPath = null,
    bool? DryRun = null,
    bool? ValidationOnly = null,
    IReadOnlyList<string>? TargetPaths = null);

public record ExecutionValidateRequestDto(
    string PipelineId,
    bool IsValid,
    IReadOnlyList<string>? Messages = null,
    IReadOnlyList<NormalizedDiagnostic>? Diagnostics = null,
    string? RawOutput = null,
    string? Metadata = null);

public record ExecutionApproveRequestDto(
    string PipelineId,
    string? ApprovedBy = null,
    string? Notes = null);

public record ExecutionApplyRequestDto(
    string PipelineId,
    EditPlan? Plan,
    string RepositoryPath,
    string? Metadata = null);

public record ExecutionRollbackRequestDto(
    string PipelineId,
    string RepositoryPath,
    string? Reason = null,
    string? Metadata = null);

public record ExecutionCancelRequestDto(
    string PipelineId,
    string? Reason = null);

public record GraphQueryRequestDto(
    string? NodeKindFilter = null,
    string? RelationshipKindFilter = null,
    string? LabelContains = null,
    string? MetadataContains = null,
    string? EntityId = null,
    int? MaxResults = null);

public record GraphLineageRequestDto(
    string NodeId,
    string? Direction = null,
    int? MaxDepth = null,
    IReadOnlyList<string>? RelationshipKindFilter = null);

public record CorrelateRequestDto(string RepositoryId);
public record RootCauseRequestDto(string FailureNodeId);
public record ImpactAnalysisRequestDto(string TargetNodeId, int? MaxDepth = null);

public record FailureAnalyzeRequestDto(string FailureNodeId);
public record FailureLineageRequestDto(string FailureNodeId);
public record PatchImpactRequestDto(string PatchNodeId);

public record ArchitectureAnalyzeRequestDto(string? RepositoryId = null);
public record ArchitectureImpactRequestDto(string SourceModuleId, string TargetModuleId);

public record ModernizationPlanRequestDto(ModernizationType Type, string TargetPayload, string? RepositoryId = null);
public record ModernizationAnalyzeRequestDto(ModernizationType Type, string TargetPayload, string? RepositoryId = null);
public record ModernizationExecuteRequestDto(string PlanId, string Action, string? StepId = null);

public record SaveSettingsRequestDto(string ModelStoragePath, string ActiveLlmModel, string ActiveEmbeddingModel, string HardwareProviderPreference, string LogLevelThreshold);
public record ModelDownloadRequestDto(string ModelId);
public record DependencyRepairRequestDto(string DependencyName);
public record CompleteOnboardingRequestDto();
public record ApplyUpdateRequestDto();
