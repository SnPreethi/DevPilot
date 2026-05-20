namespace DevPilot.AI;

public interface IEmbeddingTokenizer
{
    TokenizedText Tokenize(string text, int maxTokens);
}
