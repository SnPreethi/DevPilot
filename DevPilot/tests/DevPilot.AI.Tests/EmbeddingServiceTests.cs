using DevPilot.AI;
using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DevPilot.AI.Tests;

public sealed class EmbeddingServiceTests
{
    [Fact]
    public async Task GenerateEmbeddingAsync_UsesDeterministicFallbackWhenModelIsMissing()
    {
        var settings = Options.Create(new EmbeddingSettings
        {
            ModelPath = "missing-model.onnx",
            Dimensions = 32,
            AllowDeterministicFallback = true
        });
        using var model = new OnnxEmbeddingModel(settings, NullLogger<OnnxEmbeddingModel>.Instance, CreateSessionFactory());
        var service = new OnnxEmbeddingService(
            model,
            new SimpleHashingTokenizer(),
            settings,
            NullLogger<OnnxEmbeddingService>.Instance);

        var first = await service.GenerateEmbeddingAsync(new EmbeddingRequest("token refresh jwt validation"));
        var second = await service.GenerateEmbeddingAsync(new EmbeddingRequest("token refresh jwt validation"));

        Assert.Equal(32, first.Dimension);
        Assert.Equal(first.Vector, second.Vector);
    }

    [Fact]
    public async Task OnnxLlmService_MissingModel_ReturnsGroundedFallback()
    {
        var settings = Options.Create(new LLMSettings
        {
            ModelPath = "missing-phi3.onnx",
            AllowExtractiveFallback = true
        });
        using var service = new OnnxLLMService(settings, NullLogger<OnnxLLMService>.Instance, CreateSessionFactory(), new TestLlmTokenizer(), CreateModelManager());
        var context = new RetrievedContext("chunk-1", "Auth.cs", "Validate", "method", 1, 5, "jwt validation", 0.8);

        var result = await service.GenerateAsync(new InferenceRequest("prompt", null, 64, 0.2, 0.9, [context]));

        Assert.True(result.UsedFallback);
        Assert.Contains("Auth.cs", result.Answer);
    }

    [Fact]
    public async Task OnnxLlmService_StreamAsync_StreamsFallbackTokens()
    {
        var settings = Options.Create(new LLMSettings
        {
            ModelPath = "missing-phi3.onnx",
            AllowExtractiveFallback = true
        });
        using var service = new OnnxLLMService(settings, NullLogger<OnnxLLMService>.Instance, CreateSessionFactory(), new TestLlmTokenizer(), CreateModelManager());
        var context = new RetrievedContext("chunk-1", "Auth.cs", null, "file", 1, 5, "auth flow", 0.7);

        var tokens = new List<string>();
        await foreach (var token in service.StreamAsync(new InferenceRequest("prompt", null, 64, 0.2, 0.9, [context])))
        {
            tokens.Add(token);
        }

        Assert.NotEmpty(tokens);
    }

    [Fact]
    public void LocalModelManager_ReportsModelAvailability()
    {
        var manager = new LocalModelManager(
            Options.Create(new EmbeddingSettings { ModelPath = "missing-embedding.onnx" }),
            Options.Create(new LLMSettings { ModelPath = "missing-llm.onnx" }));

        var models = manager.ListModels();

        Assert.Equal(2, models.Count);
    }

    [Fact]
    public void ExecutionProviderSelector_SelectsBasedOnRuntimeAvailability()
    {
        var selector = new ExecutionProviderSelector(Options.Create(new RuntimeOptimizationSettings()));

        var selected = selector.SelectProvider();
        var statuses = selector.GetProviderStatuses();

        // On a machine with CUDA, it should select Cuda; otherwise Cpu is always valid
        Assert.True(
            selected == ExecutionProviderKind.Cuda ||
            selected == ExecutionProviderKind.DirectML ||
            selected == ExecutionProviderKind.Cpu);
        Assert.Contains(statuses, status => status.Provider == ExecutionProviderKind.Cpu && status.IsAvailable);
    }

    [Fact]
    public void TokenizerValidation_ReportsTruncationForLargePrompt()
    {
        var service = new TokenizerValidationService(
            new SimpleHashingTokenizer(),
            new ApproximateTokenEstimator(Options.Create(new TokenEstimationSettings
            {
                CharactersPerToken = 2,
                MinimumTokens = 1
            })));

        var result = service.Validate(new string('a', 100), maxTokens: 8);

        Assert.True(result.IsCompatible);
        Assert.True(result.WasTruncated);
        Assert.Equal(8, result.ProducedTokens);
        Assert.Contains(result.Issues, issue => issue.Code == "TOKENIZER_TRUNCATION");
    }

    [Fact]
    public async Task StreamingValidation_ObservesCancellationAndKeepsPartialText()
    {
        var settings = Options.Create(new LLMSettings
        {
            ModelPath = "missing-phi3.onnx",
            AllowExtractiveFallback = true
        });
        using var llm = new OnnxLLMService(settings, NullLogger<OnnxLLMService>.Instance, CreateSessionFactory(), new TestLlmTokenizer(), CreateModelManager());
        var service = new StreamingValidationService(llm, settings);

        var result = await service.ValidateAsync("Explain authentication flow");

        Assert.True(result.ProducedTokens);
        Assert.True(result.CancellationObserved);
        Assert.True(result.TokensReceived >= 2);
        Assert.NotEmpty(result.PartialText);
    }

    [Fact]
    public async Task ModelValidation_ReportsMissingModelsWithoutThrowing()
    {
        var embeddingSettings = Options.Create(new EmbeddingSettings
        {
            ModelPath = "missing-embedding.onnx",
            ModelName = "embedding-test",
            MaxTokens = 16,
            Dimensions = 384
        });
        var llmSettings = Options.Create(new LLMSettings
        {
            ModelPath = "missing-llm.onnx",
            ModelId = "llm-test"
        });
        var service = new ModelValidationService(
            embeddingSettings,
            llmSettings,
            new TestRuntimeCapabilityService(),
            new TokenizerValidationService(
                new SimpleHashingTokenizer(),
                new ApproximateTokenEstimator(Options.Create(new TokenEstimationSettings()))),
            new TestInferenceProfiler(),
            CreateSessionFactory(),
            new TestLlmTokenizer(),
            new TestModelManager("missing-llm.onnx"));

        var report = await service.ValidateAsync("Explain token validation");

        Assert.NotNull(report);
        Assert.False(report.EmbeddingModel.Exists);
        Assert.False(report.LlmModel.Exists);
        Assert.Contains(report.EmbeddingModel.Issues, issue => issue.Code == "MODEL_FILE_MISSING");
        Assert.Contains(report.LlmModel.Issues, issue => issue.Code == "MODEL_FILE_MISSING");
    }

    private static OnnxSessionFactory CreateSessionFactory()
    {
        var runtimeSettings = Options.Create(new RuntimeOptimizationSettings());
        var selector = new ExecutionProviderSelector(runtimeSettings);
        return new OnnxSessionFactory(
            selector,
            runtimeSettings,
            NullLogger<OnnxSessionFactory>.Instance);
    }

    private static IModelManager CreateModelManager()
    {
        return new DevPilot.AI.Registry.ModelRegistry(
            Options.Create(new LLMSettings { ModelPath = "models/llm/phi-3-mini/model.onnx" }));
    }

    private sealed class TestRuntimeCapabilityService : IRuntimeCapabilityService
    {
        public RuntimeCapabilityReport GetCapabilities()
        {
            var providers = new[]
            {
                new ExecutionProviderStatus(ExecutionProviderKind.Cpu, true, true, "Test CPU provider.")
            };

            return new RuntimeCapabilityReport(
                new RuntimeHardwareInfo("Test CPU", 1024, 512, false, false, false, "Test OS", "x64"),
                providers,
                ExecutionProviderKind.Cpu,
                HardwareAccelerationEnabled: false,
                DateTimeOffset.UtcNow);
        }
    }

    private sealed class TestInferenceProfiler : IInferenceProfiler
    {
        public Task<InferenceProfile> ProfileAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InferenceProfile(
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                1,
                1,
                UsedEmbeddingFallback: true,
                UsedLlmFallback: true,
                StreamingTokens: 0,
                StreamingPartial: ""));
        }
    }

    private sealed class TestLlmTokenizer : ILlmTokenizer
    {
        public bool IsAvailable => true;

        public string TokenizerPath => "test-tokenizer.model";

        public IReadOnlyList<int> Encode(string text, int maxTokens)
        {
            return [1, 2, 3];
        }

        public string Decode(IEnumerable<int> tokenIds)
        {
            return string.Join(' ', tokenIds);
        }
    }

    private sealed class TestModelManager : IModelManager
    {
        private readonly string _modelPath;
        public TestModelManager(string modelPath) => _modelPath = modelPath;

        public ModelDescriptor Resolve(ExecutionProviderKind provider)
        {
            return new ModelDescriptor
            {
                Name = "Test Model",
                ModelPath = _modelPath,
                TokenizerPath = "test-tokenizer.model",
                Target = ModelExecutionTarget.Cpu,
                SupportsKvCache = true,
                SupportsStreaming = true,
                UsesFp16 = false
            };
        }
    }
}
