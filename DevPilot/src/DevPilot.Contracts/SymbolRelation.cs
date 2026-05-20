namespace DevPilot.Contracts;

public sealed record SymbolRelation(
    string FromSymbolId,
    string ToSymbolId,
    string RelationType);
