using System.Collections.Generic;

namespace DevPilot.Contracts;

public sealed record CompletionRequest(
    string FilePath,
    string LanguageId,
    int CursorLine,
    int CursorColumn,
    string PrefixContent,
    string SuffixContent,
    string? RepositoryId = null,
    string? RepositoryPath = null,
    string? ActiveSymbol = null,
    IReadOnlyList<string>? Imports = null,
    IReadOnlyList<string>? NearbySymbols = null);

public sealed record CompletionResult(
    string CompletionText);
