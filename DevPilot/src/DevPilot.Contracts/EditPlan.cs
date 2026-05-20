using System.Collections.Generic;

namespace DevPilot.Contracts;

public sealed record PatchInstruction(
    string TargetSymbol,
    string EditDescription,
    string SearchContent,
    string ReplacementContent);

public sealed record FileEditOperation(
    string FilePath,
    List<PatchInstruction> Instructions);

public sealed record EditPlan(
    string ReasoningSummary,
    List<FileEditOperation> FileEdits);

public sealed record FileEditPreview(
    string FilePath,
    string DiffContent,
    string PatchedContent,
    bool IsValid,
    string? ErrorMessage);

public sealed record EditPlanPreview(
    string ReasoningSummary,
    List<FileEditPreview> FilePreviews);
