using DevPilot.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;

namespace DevPilot.AI;

public sealed class OnnxEmbeddingModel : IDisposable
{
    private readonly EmbeddingSettings _settings;
    private readonly ILogger<OnnxEmbeddingModel> _logger;
    private readonly OnnxSessionFactory _sessionFactory;
    private readonly Lazy<InferenceSession?> _session;
    private TimeSpan _loadDuration = TimeSpan.Zero;

    public OnnxEmbeddingModel(
        IOptions<EmbeddingSettings> settings,
        ILogger<OnnxEmbeddingModel> logger,
        OnnxSessionFactory sessionFactory)
    {
        _settings = settings.Value;
        _logger = logger;
        _sessionFactory = sessionFactory;
        _session = new Lazy<InferenceSession?>(LoadSession);
    }

    public InferenceSession? Session => _session.Value;

    public TimeSpan LoadDuration => _loadDuration;

    public bool IsLoaded => _session.IsValueCreated && _session.Value is not null;

    public string ModelPath => Path.GetFullPath(_settings.ModelPath);

    private InferenceSession? LoadSession()
    {
        var modelPath = Path.GetFullPath(_settings.ModelPath);
        if (!File.Exists(modelPath))
        {
            if (_settings.AllowDeterministicFallback)
            {
                _logger.LogWarning(
                    "ONNX embedding model was not found at {ModelPath}. Using deterministic local fallback embeddings.",
                    modelPath);
                return null;
            }

            throw new FileNotFoundException("ONNX embedding model file was not found.", modelPath);
        }

        _logger.LogInformation("Loading ONNX embedding model from {ModelPath}.", modelPath);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var session = _sessionFactory.Create(modelPath);
        stopwatch.Stop();
        _loadDuration = stopwatch.Elapsed;
        return session;
    }

    public void Dispose()
    {
        if (_session.IsValueCreated)
        {
            _session.Value?.Dispose();
        }
    }
}
