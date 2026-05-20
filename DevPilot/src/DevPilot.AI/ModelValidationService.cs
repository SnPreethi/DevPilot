using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using System.Diagnostics;

namespace DevPilot.AI;

public sealed class ModelValidationService : IModelValidationService
{
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly LLMSettings _llmSettings;
    private readonly IRuntimeCapabilityService _runtimeCapabilityService;
    private readonly ITokenizerValidationService _tokenizerValidationService;
    private readonly IInferenceProfiler _profiler;
    private readonly OnnxSessionFactory _sessionFactory;
    private readonly ILlmTokenizer _llmTokenizer;
    private readonly IModelManager _modelManager;

    public ModelValidationService(
        IOptions<EmbeddingSettings> embeddingSettings,
        IOptions<LLMSettings> llmSettings,
        IRuntimeCapabilityService runtimeCapabilityService,
        ITokenizerValidationService tokenizerValidationService,
        IInferenceProfiler profiler,
        OnnxSessionFactory sessionFactory,
        ILlmTokenizer llmTokenizer,
        IModelManager modelManager)
    {
        _embeddingSettings = embeddingSettings.Value;
        _llmSettings = llmSettings.Value;
        _runtimeCapabilityService = runtimeCapabilityService;
        _tokenizerValidationService = tokenizerValidationService;
        _profiler = profiler;
        _sessionFactory = sessionFactory;
        _llmTokenizer = llmTokenizer;
        _modelManager = modelManager;
    }

    public async Task<RuntimeValidationReport> ValidateAsync(
        string probePrompt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = _runtimeCapabilityService.GetCapabilities();
        var tokenizer = _tokenizerValidationService.Validate(probePrompt, _embeddingSettings.MaxTokens);
        tokenizer = ValidateTokenizerArtifacts(tokenizer);
        var stopwatch = Stopwatch.StartNew();
        var embedding = ValidateModel(
            _embeddingSettings.ModelName,
            _embeddingSettings.ModelPath,
            expectedDimensions: _embeddingSettings.Dimensions,
            runtime.SelectedProvider,
            cancellationToken);

        var llmDescriptor = _modelManager.Resolve(runtime.SelectedProvider);
        var llm = ValidateModel(
            _llmSettings.ModelId,
            llmDescriptor.ModelPath,
            expectedDimensions: null,
            runtime.SelectedProvider,
            cancellationToken);
        stopwatch.Stop();
        var profile = await _profiler.ProfileAsync(probePrompt, cancellationToken).ConfigureAwait(false);
        profile = profile with { ModelValidationDuration = stopwatch.Elapsed };

        return new RuntimeValidationReport(runtime, embedding, llm, tokenizer, profile);
    }

    private TokenizerValidationResult ValidateTokenizerArtifacts(TokenizerValidationResult tokenizer)
    {
        var issues = tokenizer.Issues.ToList();
        var embeddingVocabPath = Path.GetFullPath(_embeddingSettings.VocabularyPath);
        if (!File.Exists(embeddingVocabPath))
        {
            issues.Add(new ModelValidationIssue(
                RuntimeValidationSeverity.Error,
                "EMBEDDING_TOKENIZER_MISSING",
                $"MiniLM vocabulary file was not found at {embeddingVocabPath}."));
        }

        if (!_llmTokenizer.IsAvailable)
        {
            issues.Add(new ModelValidationIssue(
                RuntimeValidationSeverity.Error,
                "LLM_TOKENIZER_MISSING",
                $"Phi tokenizer file was not found at {_llmTokenizer.TokenizerPath}."));
        }

        return tokenizer with
        {
            IsCompatible = !issues.Any(issue => issue.Severity == RuntimeValidationSeverity.Error),
            Issues = issues
        };
    }

    private ModelValidationResult ValidateModel(
        string modelName,
        string configuredPath,
        int? expectedDimensions,
        ExecutionProviderKind provider,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var modelPath = Path.GetFullPath(configuredPath);
        var issues = new List<ModelValidationIssue>();
        if (!File.Exists(modelPath))
        {
            issues.Add(new ModelValidationIssue(
                RuntimeValidationSeverity.Error,
                "MODEL_FILE_MISSING",
                $"Model file was not found at {modelPath}."));
            return new ModelValidationResult(
                modelName,
                modelPath,
                Exists: false,
                Loaded: false,
                IsCompatible: false,
                provider.ToString(),
                TimeSpan.Zero,
                [],
                [],
                issues);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var session = _sessionFactory.Create(modelPath);
            stopwatch.Stop();
            var inputNames = session.InputMetadata.Keys.ToList();
            var outputNames = session.OutputMetadata.Keys.ToList();

            if (inputNames.Count == 0)
            {
                issues.Add(new ModelValidationIssue(RuntimeValidationSeverity.Error, "MODEL_NO_INPUTS", "Model exposes no ONNX inputs."));
            }

            if (outputNames.Count == 0)
            {
                issues.Add(new ModelValidationIssue(RuntimeValidationSeverity.Error, "MODEL_NO_OUTPUTS", "Model exposes no ONNX outputs."));
            }

            if (expectedDimensions.HasValue)
            {
                var dimensionMatch = session.OutputMetadata.Values.Any(metadata =>
                    metadata.Dimensions.Any(dimension => dimension == expectedDimensions.Value || dimension < 0));
                if (!dimensionMatch)
                {
                    issues.Add(new ModelValidationIssue(
                        RuntimeValidationSeverity.Warning,
                        "MODEL_DIMENSION_UNVERIFIED",
                        $"Could not verify expected embedding dimension {expectedDimensions.Value} from ONNX output metadata."));
                }
            }

            return new ModelValidationResult(
                modelName,
                modelPath,
                Exists: true,
                Loaded: true,
                IsCompatible: !issues.Any(issue => issue.Severity == RuntimeValidationSeverity.Error),
                provider.ToString(),
                stopwatch.Elapsed,
                inputNames,
                outputNames,
                issues);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            issues.Add(new ModelValidationIssue(RuntimeValidationSeverity.Error, "MODEL_LOAD_FAILED", ex.Message));
            return new ModelValidationResult(
                modelName,
                modelPath,
                Exists: true,
                Loaded: false,
                IsCompatible: false,
                provider.ToString(),
                stopwatch.Elapsed,
                [],
                [],
                issues);
        }
    }
}
