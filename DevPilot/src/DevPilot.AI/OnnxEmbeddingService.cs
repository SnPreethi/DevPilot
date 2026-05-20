using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Security.Cryptography;
using System.Text;
namespace DevPilot.AI;
public sealed class OnnxEmbeddingService : IEmbeddingService
{
    private readonly OnnxEmbeddingModel _model;
    private readonly IEmbeddingTokenizer _tokenizer;
    private readonly EmbeddingSettings _settings;
    private readonly ILogger<OnnxEmbeddingService> _logger;
    public OnnxEmbeddingService(
        OnnxEmbeddingModel model,
        IEmbeddingTokenizer tokenizer,
        IOptions<EmbeddingSettings> settings,
        ILogger<OnnxEmbeddingService> logger)
    {
        _model = model;
        _tokenizer = tokenizer;
        _settings = settings.Value;
        _logger = logger;
    }
    public async Task<EmbeddingResult> GenerateEmbeddingAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        var results = await GenerateEmbeddingsAsync(
            new EmbeddingBatchRequest([request.Input], request.ModelId),
            cancellationToken).ConfigureAwait(false);
        return results[0];
    }
    public Task<IReadOnlyList<EmbeddingResult>> GenerateEmbeddingsAsync(
        EmbeddingBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run<IReadOnlyList<EmbeddingResult>>(
            () => request.Inputs
                .Select(input => GenerateEmbedding(input, cancellationToken))
                .ToList(),
            cancellationToken);
    }

    private EmbeddingResult GenerateEmbedding(string input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = _model.Session;
        if (session is null)
        {return GenerateDeterministicEmbedding(input);}
        try
        {
            var result = GenerateOnnxEmbedding(session, input);
            if (result.Dimension != _settings.Dimensions)
            {
                _logger.LogWarning(
                    "Embedding model returned {ActualDimensions} dimensions, expected {ExpectedDimensions}.",
                    result.Dimension,
                    _settings.Dimensions);
            }
            return result;
        }
        catch (Exception ex) when (_settings.AllowDeterministicFallback)
        {
            _logger.LogWarning(ex, "ONNX embedding inference failed. Using deterministic local fallback embedding.");
            return GenerateDeterministicEmbedding(input);
        }
    }
    private EmbeddingResult GenerateOnnxEmbedding(InferenceSession session, string input)
    {
        var tokenized = _tokenizer.Tokenize(input, _settings.MaxTokens);
        _logger.LogInformation(
            "Embedding tokenizer produced {TokenCount} active tokens.",
            tokenized.AttentionMask.Count(x => x == 1));

        foreach (var inputMetadata in session.InputMetadata)
        {
            _logger.LogInformation(
                "ONNX Input => Name: {Name}, ElementType: {Type}",
                inputMetadata.Key,
                inputMetadata.Value.ElementType);
        }
        var shape = new[] { 1, _settings.MaxTokens };
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(tokenized.InputIds, shape)),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(tokenized.AttentionMask, shape))
        };
        if (session.InputMetadata.ContainsKey("token_type_ids"))
        {inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(tokenized.TokenTypeIds, shape)));}
        using var results = session.Run(inputs);
        var output = results.First().AsTensor<float>();
        _logger.LogInformation(
            "Embedding output dimensions: {Dimensions}",
            string.Join(" x ", output.Dimensions.ToArray()));
        var dimensions = output.Dimensions[^1];
        var vector = new float[dimensions];
        if (output.Dimensions.Length >= 3)
        {
            var tokenCount = output.Dimensions[^2];
            for (var token = 0; token < tokenCount; token++)
            {
                if (tokenized.AttentionMask[token] == 0)
                {continue;}
                for (var dimension = 0; dimension < dimensions; dimension++)
                {vector[dimension] += output[0, token, dimension];}
            }
            var activeTokens = Math.Max(1, tokenized.AttentionMask.Count(mask => mask == 1));
            for (var dimension = 0; dimension < dimensions; dimension++)
            {vector[dimension] /= activeTokens;}
        }
        else
        {
            for (var dimension = 0; dimension < dimensions; dimension++)
            {vector[dimension] = output[0, dimension];}
        }
        Normalize(vector);
        _logger.LogInformation(
            "Final embedding vector dimensions: {Dimension}",
            vector.Length);
        return new EmbeddingResult(_settings.ModelName, vector, vector.Length);
    }
    private EmbeddingResult GenerateDeterministicEmbedding(string input)
    {
        var vector = new float[_settings.Dimensions];
        foreach (var token in input.Split([' ', '\r', '\n', '\t', '.', ',', ';', ':', '(', ')', '{', '}', '[', ']'], StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token.ToLowerInvariant()));
            var index = BitConverter.ToUInt32(bytes, 0) % vector.Length;
            var sign = bytes[4] % 2 == 0 ? 1f : -1f;
            vector[index] += sign;
        }
        Normalize(vector);
        return new EmbeddingResult(_settings.ModelName, vector, vector.Length);
    }
    private static void Normalize(float[] vector)
    {
        var magnitude = Math.Sqrt(vector.Sum(value => value * value));
        if (magnitude == 0)
        {return;}

        for (var i = 0; i < vector.Length; i++)
        {vector[i] = (float)(vector[i] / magnitude);}
    }
}
