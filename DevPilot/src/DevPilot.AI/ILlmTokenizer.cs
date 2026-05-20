namespace DevPilot.AI;

public interface ILlmTokenizer
{
    bool IsAvailable { get; }

    string TokenizerPath { get; }

    IReadOnlyList<int> Encode(string text, int maxTokens);

    string Decode(IEnumerable<int> tokenIds);
}
