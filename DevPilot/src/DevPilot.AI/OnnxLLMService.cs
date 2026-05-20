using DevPilot.Contracts;
using DevPilot.Core;
using DevPilot.AI.Generation;
using DevPilot.AI.PromptPipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace DevPilot.AI;

public sealed class OnnxLLMService : ILLMService, IDisposable
{
    private readonly LLMSettings _settings;
    private readonly ILogger<OnnxLLMService> _logger;
    private readonly OnnxSessionFactory _sessionFactory;
    private readonly ILlmTokenizer _tokenizer;
    private readonly IModelManager _modelManager;
    
    private readonly PromptTemplateService _templateService = new();
    private readonly PromptSanitizer _promptSanitizer = new();
    private readonly AssistantResponseExtractor _responseExtractor = new();
    private readonly SamplingEngine _samplingEngine = new();

    private readonly Lazy<InferenceSession?> _session;
    private ModelDescriptor? _descriptor;
    private TimeSpan _loadDuration = TimeSpan.Zero;

    public OnnxLLMService(
        IOptions<LLMSettings> settings,
        ILogger<OnnxLLMService> logger,
        OnnxSessionFactory sessionFactory,
        ILlmTokenizer tokenizer,
        IModelManager modelManager)
    {
        _settings = settings.Value;
        _logger = logger;
        _sessionFactory = sessionFactory;
        _tokenizer = tokenizer;
        _modelManager = modelManager;
        _session = new Lazy<InferenceSession?>(LoadSession);
    }

    public TimeSpan LoadDuration => _loadDuration;

    public bool IsLoaded => _session.IsValueCreated && _session.Value is not null;

    public string ModelPath => Path.GetFullPath(_settings.ModelPath);

    public async Task<InferenceResult> GenerateAsync(
        InferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        var session = _session.Value;
        if (session is not null)
        {
            var generatedTokenIds = await DecodeAsync(session, request, cancellationToken).ConfigureAwait(false);
            var generatedAnswer = _tokenizer.Decode(generatedTokenIds);
            var extractedAnswer = _responseExtractor.ExtractResponse(generatedAnswer);
            stopwatch.Stop();
            return new InferenceResult(
                extractedAnswer,
                request.ModelId ?? _settings.ModelId,
                stopwatch.Elapsed,
                EstimateTokens(request.Prompt),
                generatedTokenIds.Count,
                UsedFallback: false);
        }

        if (!_settings.AllowExtractiveFallback)
        {
            throw new FileNotFoundException("Local LLM model was not found.", Path.GetFullPath(_settings.ModelPath));
        }

        var answer = BuildGroundedFallbackAnswer(request);
        await Task.Yield();

        stopwatch.Stop();
        return new InferenceResult(
            answer,
            request.ModelId ?? _settings.ModelId,
            stopwatch.Elapsed,
            EstimateTokens(request.Prompt),
            EstimateTokens(answer),
            UsedFallback: true);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        InferenceRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var session = _session.Value;
        if (session is null)
        {
            var result = await GenerateAsync(request, cancellationToken).ConfigureAwait(false);
            foreach (var token in result.Answer.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return token + " ";
                await Task.Yield();
            }

            yield break;
        }

        await foreach (var token in DecodeStreamAsync(session, request, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return token;
        }
    }

    public void Dispose()
    {
        if (_session.IsValueCreated)
        {
            _session.Value?.Dispose();
        }
    }

    private InferenceSession? LoadSession()
    {
        var activeProvider = _sessionFactory.ActiveProvider;
        _descriptor = _modelManager.Resolve(activeProvider);
        var modelPath = Path.GetFullPath(_descriptor.ModelPath);

        if (!File.Exists(modelPath))
        {
            _logger.LogWarning(
                "Phi-3 ONNX model was not found at {ModelPath}. Using grounded extractive fallback.",
                modelPath);
            return null;
        }

        _logger.LogInformation("Loading Phi-3 ONNX model from {ModelPath} for target {Target} (FP16={UsesFp16}).",
            modelPath, _descriptor.Target, _descriptor.UsesFp16);
        var stopwatch = Stopwatch.StartNew();
        var session = _sessionFactory.Create(modelPath);
        foreach (var input in session.InputMetadata)
        {
            _logger.LogInformation(
                "LLM Input => Name: {Name}, Type: {Type}",
                input.Key,
                input.Value.ElementType);
        }
        foreach (var output in session.OutputMetadata)
        {
            _logger.LogInformation(
                "LLM Output => Name: {Name}, Type: {Type}",
                output.Key,
                output.Value.ElementType);
        }
        stopwatch.Stop();
        _loadDuration = stopwatch.Elapsed;
        return session;
    }

    private Task<IReadOnlyList<int>> DecodeAsync(
        InferenceSession session,
        InferenceRequest request,
        CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            var generated = new List<int>();
            await foreach (var token in DecodeTokenIdsAsync(session, request, cancellationToken).ConfigureAwait(false))
            {
                generated.Add(token);
            }

            return (IReadOnlyList<int>)generated;
        }, cancellationToken);
    }

    private async IAsyncEnumerable<string> DecodeStreamAsync(
        InferenceSession session,
        InferenceRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var generatedTokens = new List<int>();
        var streamEmitter = new BufferedTokenEmitter();
        
        var paramsObj = request.Parameters ?? new GenerationParameters();
        var stopSequences = paramsObj.StopSequences.Concat(new[] { "<|end|>", "<|user|>" }).Distinct();
        var stopDetector = new StopSequenceDetector(stopSequences);
        var accumulatedText = new StringBuilder();

        await foreach (
            var tokenId in DecodeTokenIdsAsync(session, request, cancellationToken).ConfigureAwait(false))
        {
            generatedTokens.Add(tokenId);

            var chunks = streamEmitter.ProcessToken(tokenId, _tokenizer);
            foreach (var chunk in chunks)
            {
                accumulatedText.Append(chunk);
                if (stopDetector.ShouldStop(accumulatedText.ToString(), out var matchedSeq))
                {
                    _logger.LogInformation("[DIAGNOSTICS] Stream stop sequence triggered: '{StopSeq}'", matchedSeq);
                    yield break;
                }
                yield return chunk;
            }

            await Task.Yield();
        }

        var remaining = streamEmitter.Flush(_tokenizer);
        if (!string.IsNullOrEmpty(remaining))
        {
            accumulatedText.Append(remaining);
            if (!stopDetector.ShouldStop(accumulatedText.ToString(), out _))
            {
                yield return remaining;
            }
        }
    }

    private async IAsyncEnumerable<int> DecodeTokenIdsAsync(
        InferenceSession session,
        InferenceRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_tokenizer.IsAvailable)
        {
            yield break;
        }

        // 1. Get request generation parameters
        var paramsObj = request.Parameters ?? new GenerationParameters();

        // 2. Format Prompt with Context and ChatML template
        var sanitizedPrompt = _promptSanitizer.Sanitize(request.Prompt);
        var promptLimit = Math.Max(1, _settings.MaxSequenceLength - Math.Max(1, paramsObj.MaxTokens));
        string formattedPrompt;

        if (request.RawPrompt)
        {
            formattedPrompt = request.Prompt;
        }
        else if (request.Context.Count > 0)
        {
            var contextList = request.Context.ToList();
            formattedPrompt = _templateService.FormatGroundedPrompt(sanitizedPrompt, contextList);

            // Progressive context pruning of lowest relevance chunks to ensure prompt fits within token budget
            while (contextList.Count > 0)
            {
                var estimatedTokens = _tokenizer.Encode(formattedPrompt, _settings.MaxSequenceLength).Count;
                if (estimatedTokens <= promptLimit)
                {
                    break;
                }

                _logger.LogInformation("Prompt token count {Current} exceeds limit {Limit}. Pruning lowest relevance context chunk.", estimatedTokens, promptLimit);
                contextList.RemoveAt(contextList.Count - 1);
                formattedPrompt = _templateService.FormatGroundedPrompt(sanitizedPrompt, contextList);
            }
        }
        else
        {
            // Standard instruct template fallback
            formattedPrompt = $"<|user|>\n{sanitizedPrompt}<|end|>\n<|assistant|>\n";
        }

        var inputIds = _tokenizer.Encode(formattedPrompt, promptLimit).Select(id => (long)id).ToList();
        var maxOutputTokens = Math.Min(paramsObj.MaxTokens, _settings.MaxOutputTokens);
        using var cacheState = new KvCacheState();
        var recentTokens = new List<int>();

        var prefillStopwatch = Stopwatch.StartNew();
        if (_settings.DiagnosticsLevel == "Debug" || _settings.DiagnosticsLevel == "Benchmark")
        {
            _logger.LogInformation("[DIAGNOSTICS] Prompt Prefill started. Prompt size: {PromptSize} tokens.", inputIds.Count);
            Console.WriteLine($"[DIAGNOSTICS] Prompt Prefill started. Prompt size: {inputIds.Count} tokens.");
        }

        var totalDecodeStopwatch = Stopwatch.StartNew();
        var decodeLatencies = new List<double>();
        var cacheHitCount = 0;
        var terminationReason = "MaxTokens";

        // Create the highly optimized execution context for this generation session
        var logitsMetadata = session.OutputMetadata["logits"];
        var vocabSize = logitsMetadata.Dimensions.Length > 0 ? (int)logitsMetadata.Dimensions[^1] : 32064;
        using var context = new GenerationExecutionContext(session, vocabSize, _sessionFactory.ActiveProvider);

        try
        {
            for (var step = 0; step < maxOutputTokens; step++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    terminationReason = "Cancellation";
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var stepStopwatch = Stopwatch.StartNew();

                var isPrefill = step == 0;
                if (!isPrefill)
                {
                    if (_settings.DiagnosticsLevel == "Debug")
                    {
                        _logger.LogInformation("[DIAGNOSTICS] Incremental Decode step {Step} started.", step);
                    }
                }

                var nextToken = await Task.Run(
                    () => GenerateNextToken(context, inputIds, cacheState, recentTokens, paramsObj, isPrefill, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                
                stepStopwatch.Stop();
                var elapsedMs = stepStopwatch.Elapsed.TotalMilliseconds;

                if (isPrefill)
                {
                    prefillStopwatch.Stop();
                    if (_settings.DiagnosticsLevel == "Debug" || _settings.DiagnosticsLevel == "Benchmark")
                    {
                        _logger.LogInformation("[DIAGNOSTICS] Prompt Prefill completed. Prefill latency: {Latency:0.0} ms.", prefillStopwatch.Elapsed.TotalMilliseconds);
                        Console.WriteLine($"[DIAGNOSTICS] Prompt Prefill completed. Prefill latency: {prefillStopwatch.Elapsed.TotalMilliseconds:0.0} ms.");
                    }
                }
                else
                {
                    decodeLatencies.Add(elapsedMs);
                    cacheHitCount++;
                    if (_settings.DiagnosticsLevel == "Debug")
                    {
                        _logger.LogInformation("[DIAGNOSTICS] Incremental Decode step {Step} completed. Latency: {Latency:0.0} ms. KV Cache Reused.", step, elapsedMs);
                    }
                }

                if (_settings.DiagnosticsLevel == "Debug")
                {
                    Console.WriteLine($"[LLM] Generated token id: {nextToken}");
                    var decodedPiece = _tokenizer.Decode([(int)nextToken]);
                    Console.WriteLine($"[LLM] Token text: '{decodedPiece}'");
                }

                // Check standard end token ID or Phi-3 specific stop sequences
                if (nextToken == 32007 || nextToken == 2 || _settings.StopTokenIds.Any(stopTokenId => stopTokenId == nextToken))
                {
                    terminationReason = "StopToken";
                    if (_settings.DiagnosticsLevel == "Debug")
                    {
                        _logger.LogInformation("[DIAGNOSTICS] Stop token encountered: {TokenId}.", nextToken);
                    }
                    yield break;
                }

                inputIds.Add(nextToken);
                recentTokens.Add((int)nextToken);
                if (recentTokens.Count > 128)
                {
                    recentTokens.RemoveAt(0);
                }
                yield return checked((int)nextToken);
                await Task.Yield();
            }
        }
        finally
        {
            totalDecodeStopwatch.Stop();
            var totalTokens = decodeLatencies.Count + 1;
            var avgDecodeMs = decodeLatencies.Count > 0 ? decodeLatencies.Average() : 0.0;
            var tokensPerSec = totalDecodeStopwatch.Elapsed.TotalSeconds > 0 ? totalTokens / totalDecodeStopwatch.Elapsed.TotalSeconds : 0.0;

            if (cancellationToken.IsCancellationRequested)
            {
                terminationReason = "Cancellation";
            }

            if (_settings.DiagnosticsLevel == "Debug" || _settings.DiagnosticsLevel == "Benchmark")
            {
                _logger.LogInformation(
                    "[DIAGNOSTICS] Generation finished. Reason: {Reason}. Metrics => Total Tokens: {TotalTokens}, Prefill Latency: {PrefillLatency:0.0} ms, Avg Decode Latency: {AvgDecode:0.0} ms, Speed: {Speed:0.0} tok/sec, Cache Reuse Count: {CacheHits}.",
                    terminationReason, totalTokens, prefillStopwatch.Elapsed.TotalMilliseconds, avgDecodeMs, tokensPerSec, cacheHitCount);

                Console.WriteLine($"[DIAGNOSTICS] Generation finished. Reason: {terminationReason}.");
                Console.WriteLine($"[DIAGNOSTICS] Metrics => Total Tokens: {totalTokens}");
                Console.WriteLine($"[DIAGNOSTICS] Metrics => Prefill Latency: {prefillStopwatch.Elapsed.TotalMilliseconds:0.0} ms");
                Console.WriteLine($"[DIAGNOSTICS] Metrics => Avg Decode Latency: {avgDecodeMs:0.0} ms");
                Console.WriteLine($"[DIAGNOSTICS] Metrics => Speed: {tokensPerSec:0.0} tokens/sec");
                Console.WriteLine($"[DIAGNOSTICS] Metrics => KV Cache Reuse Count: {cacheHitCount}");
                Console.WriteLine($"[DIAGNOSTICS] Metrics => GPU Execution: True");

                // Print context memory/GC stats at the end of the session
                var finalGen0 = GC.CollectionCount(0) - context.InitialGen0Collections;
                var finalGen1 = GC.CollectionCount(1) - context.InitialGen1Collections;
                var finalGen2 = GC.CollectionCount(2) - context.InitialGen2Collections;
                Console.WriteLine($"[DIAGNOSTICS] Profile => Steps: {context.StepsExecuted}, OrtValue Reuses: {context.OrtValueReuseCount}, Binding Reuses: {context.IoBindingReuseCount}");
                Console.WriteLine($"[DIAGNOSTICS] Profile => GC Collections during run: Gen0: {finalGen0}, Gen1: {finalGen1}, Gen2: {finalGen2}");
            }
        }
    }

    private long GenerateNextToken(
        GenerationExecutionContext context,
        IReadOnlyList<long> inputIds,
        KvCacheState cacheState,
        IReadOnlyList<int> recentTokens,
        GenerationParameters paramsObj,
        bool isPrefill,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = context.Session;
        var useFp16 = _descriptor?.UsesFp16 ?? false;
        var ioBinding = context.IoBinding;

        if (isPrefill)
        {
            var tokens = inputIds.ToArray();
            var sequenceLength = tokens.Length;

            using var inputIdsOrtValue = OrtValue.CreateTensorValueFromMemory(tokens, new long[] { 1L, sequenceLength });
            ioBinding.BindInput("input_ids", inputIdsOrtValue);

            long[] attentionMask = Enumerable.Repeat(1L, sequenceLength).ToArray();
            using var attentionMaskOrtValue = OrtValue.CreateTensorValueFromMemory(attentionMask, new long[] { 1L, sequenceLength });
            ioBinding.BindInput("attention_mask", attentionMaskOrtValue);

            if (session.InputMetadata.ContainsKey("position_ids"))
            {
                long[] positionIds = Enumerable.Range(0, sequenceLength).Select(value => (long)value).ToArray();
                using var positionIdsOrtValue = OrtValue.CreateTensorValueFromMemory(positionIds, new long[] { 1L, sequenceLength });
                ioBinding.BindInput("position_ids", positionIdsOrtValue);
            }

            // Bind past_key_values for layer 0..31 with empty shapes
            var emptyShape = new[] { 1L, 32L, 0L, 96L };
            var allocator = OrtAllocator.DefaultInstance;
            var pastKeyValuesDummies = new List<OrtValue>();
            try
            {
                for (var layer = 0; layer < 32; layer++)
                {
                    var keyOrtVal = OrtValue.CreateAllocatedTensorValue(allocator, useFp16 ? TensorElementType.Float16 : TensorElementType.Float, emptyShape);
                    var valOrtVal = OrtValue.CreateAllocatedTensorValue(allocator, useFp16 ? TensorElementType.Float16 : TensorElementType.Float, emptyShape);

                    ioBinding.BindInput($"past_key_values.{layer}.key", keyOrtVal);
                    ioBinding.BindInput($"past_key_values.{layer}.value", valOrtVal);

                    pastKeyValuesDummies.Add(keyOrtVal);
                    pastKeyValuesDummies.Add(valOrtVal);
                }

                // Bind present key-value outputs to device allocator so ORT allocates the correct shape
                var providerName = context.ProviderKind == ExecutionProviderKind.Cuda ? "Cuda" :
                                   context.ProviderKind == ExecutionProviderKind.DirectML ? "Dml" : "Cpu";
                using var gpuMemoryInfo = new OrtMemoryInfo(providerName, OrtAllocatorType.DeviceAllocator, 0, OrtMemType.Default);
                for (var layer = 0; layer < 32; layer++)
                {
                    ioBinding.BindOutputToDevice($"present.{layer}.key", gpuMemoryInfo);
                    ioBinding.BindOutputToDevice($"present.{layer}.value", gpuMemoryInfo);
                }

                // Run inference prefill
                using var runOptions = new RunOptions();
                var results = session.RunWithBoundResults(runOptions, ioBinding);

                // Copy logits
                using var logitsOrtValue = results[0];
                CopyLogitsToBuffer(logitsOrtValue, context.LogitsBuffer, useFp16);

                // Save KV cache present tensors
                SavePresentKvCache(results, cacheState);
            }
            finally
            {
                foreach (var dummy in pastKeyValuesDummies)
                {
                    dummy.Dispose();
                }
            }
        }
        else
        {
            // Incremental Decode Step (Highly Optimized, Pinned Buffers and Persistent OrtValues!)
            var currentSequenceLength = inputIds.Count;

            // 1. Direct array updates (Zero allocations!)
            context.InputIdsBuffer[0] = inputIds[^1];
            
            if (session.InputMetadata.ContainsKey("position_ids"))
            {
                context.PositionIdsBuffer[0] = currentSequenceLength - 1L;
            }

            // 2. Slice and bind the dynamic attention mask (Only wrapper allocation!)
            using var attentionMaskOrtValue = OrtValue.CreateTensorValueFromMemory(context.AttentionMaskBuffer, new long[] { 1L, currentSequenceLength });
            ioBinding.BindInput("attention_mask", attentionMaskOrtValue);

            // 3. Overwrite the active past_key_values bindings with our GPU-resident cache references
            foreach (var kv in cacheState.Values)
            {
                ioBinding.BindInput(kv.Key, (OrtValue)kv.Value);
            }

            // 4. Overwrite present key-value outputs to device allocator so ORT allocates the new, larger shape
            var providerName = context.ProviderKind == ExecutionProviderKind.Cuda ? "Cuda" :
                               context.ProviderKind == ExecutionProviderKind.DirectML ? "Dml" : "Cpu";
            using var gpuMemoryInfo = new OrtMemoryInfo(providerName, OrtAllocatorType.DeviceAllocator, 0, OrtMemType.Default);
            for (var layer = 0; layer < 32; layer++)
            {
                ioBinding.BindOutputToDevice($"present.{layer}.key", gpuMemoryInfo);
                ioBinding.BindOutputToDevice($"present.{layer}.value", gpuMemoryInfo);
            }

            // 5. Run inference step completely on device
            using var runOptions = new RunOptions();
            var results = session.RunWithBoundResults(runOptions, ioBinding);

            // 6. Span-copy native logits directly to our pre-allocated C# buffer (Zero allocations!)
            using var logitsOrtValue = results[0];
            CopyLogitsToBuffer(logitsOrtValue, context.LogitsBuffer, useFp16);

            // 7. Save KV cache present tensors GPU memory pointers for the next step
            SavePresentKvCache(results, cacheState);
            
            context.StepsExecuted++;
            context.OrtValueReuseCount += 3; // input_ids, position_ids, and past_key_values reusable references
            context.IoBindingReuseCount++;
        }

        // 7. Sample next token with the advanced Sampling Engine
        return _samplingEngine.SampleToken(
            context.LogitsBuffer,
            recentTokens,
            paramsObj.Temperature,
            paramsObj.TopK,
            paramsObj.TopP,
            paramsObj.RepetitionPenalty,
            paramsObj.FrequencyPenalty,
            paramsObj.PresencePenalty);
    }

    private static void CopyLogitsToBuffer(OrtValue logitsOrtValue, float[] logitsBuffer, bool useFp16)
    {
        var logitsShape = logitsOrtValue.GetTensorTypeAndShape().Shape;
        var logitsRank = logitsShape.Length;
        var vocabSize = (int)logitsShape[^1];
        var seqDim = logitsRank >= 3 ? (int)logitsShape[^2] : 1;
        var seqIdx = seqDim - 1;

        var stride = vocabSize;
        var offset = seqIdx * stride;

        if (useFp16)
        {
            var fp16Logits = logitsOrtValue.GetTensorDataAsSpan<Float16>();
            for (var t = 0; t < vocabSize; t++)
            {
                logitsBuffer[t] = (float)fp16Logits[offset + t];
            }
        }
        else
        {
            var fp32Logits = logitsOrtValue.GetTensorDataAsSpan<float>();
            for (var t = 0; t < vocabSize; t++)
            {
                logitsBuffer[t] = fp32Logits[offset + t];
            }
        }
    }

    private static void SavePresentKvCache(IReadOnlyList<OrtValue> results, KvCacheState cacheState)
    {
        var newCache = new Dictionary<string, OrtValue>();
        for (var i = 1; i < results.Count; i++)
        {
            var layerIndex = (i - 1) / 2;
            var suffix = (i - 1) % 2 == 0 ? "key" : "value";
            var cacheKey = $"past_key_values.{layerIndex}.{suffix}";
            newCache[cacheKey] = results[i];
        }

        // Dispose old cached OrtValues
        foreach (var oldVal in cacheState.Values.Values)
        {
            if (oldVal is OrtValue ortVal)
            {
                ortVal.Dispose();
            }
        }
        cacheState.Values.Clear();

        // Store new cached OrtValues
        foreach (var kv in newCache)
        {
            cacheState.Values[kv.Key] = kv.Value;
        }
    }

    private static string BuildGroundedFallbackAnswer(InferenceRequest request)
    {
        if (request.Context.Count == 0)
        {
            return "I could not find enough relevant context in the indexed repository to answer that question.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("Based on the retrieved repository context:");
        builder.AppendLine();

        foreach (var context in request.Context.Take(3))
        {
            var title = string.IsNullOrWhiteSpace(context.SymbolName)
                ? context.FilePath
                : $"{context.SymbolName} in {context.FilePath}";

            builder.Append("- ");
            builder.Append(title);
            builder.Append(" appears relevant");
            builder.Append(" (lines ");
            builder.Append(context.StartLine);
            builder.Append('-');
            builder.Append(context.EndLine);
            builder.Append("). ");
            builder.Append(CreateSnippet(context.Content));
            builder.AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("This answer is limited to the retrieved context. If the flow is implemented elsewhere, index more files or increase the retrieval count.");
        return builder.ToString();
    }

    private static string CreateSnippet(string content)
    {
        var normalized = string.Join(" ", content.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 300 ? normalized : normalized[..300];
    }

    private static int EstimateTokens(string text)
    {
        return Math.Max(1, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
