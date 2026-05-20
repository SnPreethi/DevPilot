using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;

namespace DevPilot.Patching;

public interface IWorkspaceEditService
{
    Task<EditPlanPreview> PreviewPlanAsync(EditPlan plan, string repositoryPath, CancellationToken cancellationToken = default);
    Task<(bool Success, string? ErrorMessage)> ApplyPlanAsync(EditPlan plan, string repositoryPath, CancellationToken cancellationToken = default);
    Task<(bool Success, string? ErrorMessage)> RevertLastPlanAsync(string repositoryPath, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceEditService : IWorkspaceEditService
{
    private static readonly ConcurrentDictionary<string, string> BackupCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> LastModifiedPaths = new();

    public async Task<EditPlanPreview> PreviewPlanAsync(
        EditPlan plan,
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var previews = new List<FileEditPreview>();

        foreach (var fileEdit in plan.FileEdits)
        {
            var fullPath = Path.Combine(repositoryPath, fileEdit.FilePath);
            if (!File.Exists(fullPath))
            {
                previews.Add(new FileEditPreview(fileEdit.FilePath, "", "", false, $"File does not exist: {fileEdit.FilePath}"));
                continue;
            }

            try
            {
                var content = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
                var currentContent = content;
                var cumulativeDiff = new StringBuilder();
                var isValid = true;
                string? errorMsg = null;

                foreach (var instruction in fileEdit.Instructions)
                {
                    var (patched, diff, success, error) = SearchReplacePatchEngine.ApplyPatch(
                        currentContent,
                        instruction.SearchContent,
                        instruction.ReplacementContent);

                    if (!success)
                    {
                        isValid = false;
                        errorMsg = error;
                        break;
                    }

                    currentContent = patched;
                    if (!string.IsNullOrEmpty(diff))
                    {
                        cumulativeDiff.AppendLine($"// Symbol: {instruction.TargetSymbol} - {instruction.EditDescription}");
                        cumulativeDiff.AppendLine(diff);
                        cumulativeDiff.AppendLine();
                    }
                }

                previews.Add(new FileEditPreview(fileEdit.FilePath, cumulativeDiff.ToString(), currentContent, isValid, errorMsg));
            }
            catch (Exception ex)
            {
                previews.Add(new FileEditPreview(fileEdit.FilePath, "", "", false, $"Failed to preview: {ex.Message}"));
            }
        }

        return new EditPlanPreview(plan.ReasoningSummary, previews);
    }

    public async Task<(bool Success, string? ErrorMessage)> ApplyPlanAsync(
        EditPlan plan,
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        // 1. First run a dry-run preview to verify plan is entirely valid (atomic check)
        var preview = await PreviewPlanAsync(plan, repositoryPath, cancellationToken).ConfigureAwait(false);
        foreach (var p in preview.FilePreviews)
        {
            if (!p.IsValid)
            {
                return (false, $"Cannot apply plan. File {p.FilePath} validation failed: {p.ErrorMessage}");
            }
        }

        // 2. Perform atomic backups and apply changes
        var backupsToApply = new Dictionary<string, string>(); // Path -> OriginalContent
        var contentsToApply = new Dictionary<string, string>(); // Path -> NewContent

        try
        {
            foreach (var fileEdit in plan.FileEdits)
            {
                var fullPath = Path.Combine(repositoryPath, fileEdit.FilePath);
                var originalContent = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
                
                var currentContent = originalContent;
                foreach (var instruction in fileEdit.Instructions)
                {
                    var (patched, _, _, _) = SearchReplacePatchEngine.ApplyPatch(
                        currentContent,
                        instruction.SearchContent,
                        instruction.ReplacementContent);
                    currentContent = patched;
                }

                backupsToApply[fullPath] = originalContent;
                contentsToApply[fullPath] = currentContent;
            }

            // Write all files atomically
            lock (LastModifiedPaths)
            {
                LastModifiedPaths.Clear();
                foreach (var kvp in contentsToApply)
                {
                    var fullPath = kvp.Key;
                    BackupCache[fullPath] = backupsToApply[fullPath];
                    LastModifiedPaths.Add(fullPath);
                }
            }

            foreach (var kvp in contentsToApply)
            {
                await File.WriteAllTextAsync(kvp.Key, kvp.Value, cancellationToken).ConfigureAwait(false);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            // Rollback immediately if anything goes wrong during actual file write
            foreach (var kvp in backupsToApply)
            {
                try
                {
                    if (File.Exists(kvp.Key))
                    {
                        await File.WriteAllTextAsync(kvp.Key, kvp.Value, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Ignore nested write failures during emergency rollback
                }
            }

            return (false, $"Error applying plan to disk: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> RevertLastPlanAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var pathsToRevert = new List<string>();
        lock (LastModifiedPaths)
        {
            pathsToRevert.AddRange(LastModifiedPaths);
        }

        if (pathsToRevert.Count == 0)
        {
            return (false, "No applied plan history found to revert.");
        }

        try
        {
            foreach (var fullPath in pathsToRevert)
            {
                if (BackupCache.TryGetValue(fullPath, out var originalContent))
                {
                    await File.WriteAllTextAsync(fullPath, originalContent, cancellationToken).ConfigureAwait(false);
                }
            }

            lock (LastModifiedPaths)
            {
                LastModifiedPaths.Clear();
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Revert failed: {ex.Message}");
        }
    }
}
