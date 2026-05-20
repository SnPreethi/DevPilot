using DevPilot.Contracts;
using DevPilot.Core;
using Microsoft.Extensions.Options;
using System.Text;

namespace DevPilot.RAG;

public sealed class GroundedPromptBuilder : IPromptBuilder
{
    private readonly PromptingSettings _settings;
    private readonly ITokenEstimator _tokenEstimator;

    public GroundedPromptBuilder(
        IOptions<PromptingSettings> settings,
        ITokenEstimator tokenEstimator)
    {
        _settings = settings.Value;
        _tokenEstimator = tokenEstimator;
    }

    public Task<GroundedPrompt> BuildAsync(
        string question,
        IReadOnlyList<RetrievedContext> context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var builder = new StringBuilder();
        builder.AppendLine("You are a local offline code assistant.");
        builder.AppendLine("Use ONLY the provided context.");
        builder.AppendLine("If the answer is not in the context, say so.");
        builder.AppendLine();
        builder.AppendLine("Context:");

        foreach (var item in context)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AppendLine("---");
            builder.Append("File: ");
            builder.AppendLine(item.FilePath);
            builder.Append("Lines: ");
            builder.Append(item.StartLine);
            builder.Append('-');
            builder.AppendLine(item.EndLine.ToString());
            if (!string.IsNullOrWhiteSpace(item.SymbolName))
            {
                builder.Append("Symbol: ");
                builder.AppendLine(item.SymbolName);
            }

            builder.AppendLine(Truncate(item.Content, _settings.MaxChunkCharacters));
        }

        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("Question:");
        builder.AppendLine(question);

        var prompt = Truncate(builder.ToString(), _settings.MaxPromptCharacters);
        return Task.FromResult(new GroundedPrompt(prompt, context, _tokenEstimator.Estimate(prompt)));
    }

    private static string Truncate(string value, int maxCharacters)
    {
        if (maxCharacters <= 0 || value.Length <= maxCharacters)
        {
            return value;
        }

        return value[..maxCharacters];
    }

}
