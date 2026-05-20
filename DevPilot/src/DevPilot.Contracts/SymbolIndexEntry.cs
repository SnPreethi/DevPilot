using System.Collections.Generic;

namespace DevPilot.Contracts;

public sealed record SymbolIndexEntry(
    string SymbolId,
    string RepositoryId,
    string FileId,
    string FilePath,
    string Name,
    string Kind,
    string? Namespace,
    string? ParentSymbol,
    IReadOnlyList<string> ReferencedSymbols,
    IReadOnlyList<string> ImportedNamespaces,
    IReadOnlyList<string> FileDependencies,
    string DefinitionLocation,
    int StartLine,
    int EndLine);
