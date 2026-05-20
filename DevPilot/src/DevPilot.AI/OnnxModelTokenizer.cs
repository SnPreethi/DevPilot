using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.Tokenizers;

namespace DevPilot.AI;

public sealed class OnnxModelTokenizer : IEmbeddingTokenizer, ILlmTokenizer
{
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly LLMSettings _llmSettings;
    private readonly IModelManager _modelManager;
    private readonly IExecutionProviderSelector _providerSelector;
    private readonly ILogger<OnnxModelTokenizer> _logger;
    private readonly Lazy<BertTokenizer?> _embeddingTokenizer;
    private readonly Lazy<Tokenizer?> _llmTokenizer;
    private readonly SimpleHashingTokenizer _fallbackTokenizer = new();
    private string _resolvedTokenizerPath = "";

    public OnnxModelTokenizer(
        IOptions<EmbeddingSettings> embeddingSettings,
        IOptions<LLMSettings> llmSettings,
        IModelManager modelManager,
        IExecutionProviderSelector providerSelector,
        ILogger<OnnxModelTokenizer> logger)
    {
        _embeddingSettings = embeddingSettings.Value;
        _llmSettings = llmSettings.Value;
        _modelManager = modelManager;
        _providerSelector = providerSelector;
        _logger = logger;
        _embeddingTokenizer = new Lazy<BertTokenizer?>(LoadEmbeddingTokenizer);
        _llmTokenizer = new Lazy<Tokenizer?>(LoadLlmTokenizer);
    }

    public bool IsAvailable => _llmTokenizer.Value is not null;

    public string TokenizerPath => _resolvedTokenizerPath;

    public TokenizedText Tokenize(string text, int maxTokens)
    {
        var tokenizer = _embeddingTokenizer.Value;
        if (tokenizer is null)
        {
            return _fallbackTokenizer.Tokenize(text, maxTokens);
        }

        var limit = Math.Max(2, maxTokens);
        var ids = tokenizer.EncodeToIds(text, addSpecialTokens: true, considerNormalization: true).ToList();
        var wasTruncated = ids.Count > limit;
        if (wasTruncated)
        {
            ids = ids.Take(limit).ToList();
            ids[^1] = tokenizer.SeparatorTokenId;
        }

        while (ids.Count < limit)
        {
            ids.Add(tokenizer.PaddingTokenId);
        }

        var inputIds = ids.Select(id => (long)id).ToArray();
        var attentionMask = ids.Select(id => id == tokenizer.PaddingTokenId ? 0L : 1L).ToArray();
        return new TokenizedText(inputIds, attentionMask, new long[limit], wasTruncated);
    }

    public IReadOnlyList<int> Encode(string text, int maxTokens)
    {
        var tokenizer = _llmTokenizer.Value
            ?? throw new FileNotFoundException("Phi tokenizer model file was not found.", TokenizerPath);

        var ids = tokenizer.EncodeToIds(text).ToList();
        var limit = Math.Max(1, maxTokens);
        return ids.Count > limit ? ids.Take(limit).ToArray() : ids;
    }

    public string Decode(IEnumerable<int> tokenIds)
    {
        var tokenizer = _llmTokenizer.Value
            ?? throw new FileNotFoundException("Phi tokenizer model file was not found.", TokenizerPath);

        return tokenizer.Decode(tokenIds);
    }

    private BertTokenizer? LoadEmbeddingTokenizer()
    {
        var path = Path.GetFullPath(_embeddingSettings.VocabularyPath);
        if (!File.Exists(path))
        {
            _logger.LogWarning("MiniLM vocabulary was not found at {VocabularyPath}. Hash tokenizer fallback remains available only while the model is missing.", path);
            return null;
        }

        return BertTokenizer.Create(path, new BertOptions
        {
            LowerCaseBeforeTokenization = true
        });
    }

    private Tokenizer? LoadLlmTokenizer()
    {
        // Resolve tokenizer path from provider-aware ModelRegistry
        var provider = _providerSelector.SelectProvider();
        var descriptor = _modelManager.Resolve(provider);
        var resolvedPath = Path.GetFullPath(descriptor.TokenizerPath);

        // Fallback to configured path if descriptor tokenizer path is empty/missing
        if (string.IsNullOrWhiteSpace(descriptor.TokenizerPath) || !File.Exists(resolvedPath))
        {
            resolvedPath = Path.GetFullPath(_llmSettings.TokenizerModelPath);
        }

        _resolvedTokenizerPath = resolvedPath;

        if (!File.Exists(resolvedPath))
        {
            _logger.LogWarning("Phi tokenizer model was not found at {TokenizerPath}.", resolvedPath);
            return null;
        }

        _logger.LogInformation("Loading Phi tokenizer from {TokenizerPath}.", resolvedPath);
        using var stream = File.OpenRead(resolvedPath);
        return LlamaTokenizer.Create(
            stream,
            addBeginOfSentence: true,
            addEndOfSentence: false,
            specialTokens: null);
    }
}
