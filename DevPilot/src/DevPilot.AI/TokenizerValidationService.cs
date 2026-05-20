using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Options;

namespace DevPilot.AI;

public sealed class TokenizerValidationService : ITokenizerValidationService
{
    private readonly IEmbeddingTokenizer _tokenizer;
    private readonly ITokenEstimator _tokenEstimator;

    public TokenizerValidationService(
        IEmbeddingTokenizer tokenizer,
        ITokenEstimator tokenEstimator)
    {
        _tokenizer = tokenizer;
        _tokenEstimator = tokenEstimator;
    }

    public TokenizerValidationResult Validate(string text, int maxTokens)
    {
        var issues = new List<ModelValidationIssue>();
        var safeMaxTokens = Math.Max(2, maxTokens);
        var tokenized = _tokenizer.Tokenize(text, safeMaxTokens);
        var activeTokens = tokenized.AttentionMask.Count(value => value == 1);
        var estimatedTokens = _tokenEstimator.Estimate(text);
        var wasTruncated = estimatedTokens > safeMaxTokens || activeTokens >= safeMaxTokens;

        if (tokenized.InputIds.Length != safeMaxTokens ||
            tokenized.AttentionMask.Length != safeMaxTokens ||
            tokenized.TokenTypeIds.Length != safeMaxTokens)
        {
            issues.Add(new ModelValidationIssue(
                RuntimeValidationSeverity.Error,
                "TOKENIZER_SHAPE_MISMATCH",
                "Tokenizer output arrays do not match the requested maximum token length."));
        }

        if (activeTokens == 0)
        {
            issues.Add(new ModelValidationIssue(
                RuntimeValidationSeverity.Error,
                "TOKENIZER_EMPTY_OUTPUT",
                "Tokenizer produced no active tokens."));
        }

        if (wasTruncated)
        {
            issues.Add(new ModelValidationIssue(
                RuntimeValidationSeverity.Warning,
                "TOKENIZER_TRUNCATION",
                "Input text exceeds the configured token window and will be truncated."));
        }

        return new TokenizerValidationResult(
            !issues.Any(issue => issue.Severity == RuntimeValidationSeverity.Error),
            safeMaxTokens,
            tokenized.InputIds.Length,
            activeTokens,
            wasTruncated,
            estimatedTokens,
            issues);
    }
}
