namespace DevPilot.Contracts;

public sealed record CodeChunk(
    string ChunkId,
    string RepositoryId,
    string FileId,
    string FilePath,
    string? SymbolName,
    string ChunkType,
    int StartLine,
    int EndLine,
    string Content,
    string Language,
    string ChunkHash = "",
    int TokenEstimate = 0,
    string? SymbolKind = null,
    string? Namespace = null,
    string? ParentSymbol = null,
    System.Collections.Generic.IReadOnlyList<string>? ReferencedSymbols = null,
    System.Collections.Generic.IReadOnlyList<string>? ImportedNamespaces = null,
    System.Collections.Generic.IReadOnlyList<string>? FileDependencies = null,
    string? DefinitionLocation = null);
