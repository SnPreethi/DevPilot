using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DevPilot.AI;

public sealed partial class SimpleHashingTokenizer : IEmbeddingTokenizer
{
    public TokenizedText Tokenize(string text, int maxTokens)
    {
        var tokens = TokenRegex()
            .Matches(text.ToLowerInvariant())
            .Select(match => StableTokenId(match.Value))
            .Take(Math.Max(1, maxTokens - 2))
            .ToList();

        tokens.Insert(0, 101);
        tokens.Add(102);

        while (tokens.Count < maxTokens)
        {
            tokens.Add(0);
        }

        var attentionMask = tokens.Select(token => token == 0 ? 0L : 1L).ToArray();
        return new TokenizedText(tokens.ToArray(), attentionMask, new long[maxTokens]);
    }

    private static long StableTokenId(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return BitConverter.ToUInt32(bytes, 0) % 30_000 + 999;
    }

    [GeneratedRegex("[a-z0-9_\\.]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
