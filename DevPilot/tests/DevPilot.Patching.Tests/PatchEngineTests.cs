using Xunit;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using DevPilot.Contracts;
using DevPilot.Patching;

namespace DevPilot.Patching.Tests;

public class PatchEngineTests
{
    [Fact]
    public void ApplyPatch_ShouldReplaceUniqueBlock()
    {
        var content = "using System;\npublic class Foo {\n    public void Bar() {\n        Console.WriteLine(\"Old\");\n    }\n}";
        var search = "        Console.WriteLine(\"Old\");";
        var replacement = "        Console.WriteLine(\"New\");";

        var (patched, diff, success, error) = SearchReplacePatchEngine.ApplyPatch(content, search, replacement);

        Assert.True(success);
        Assert.Contains("Console.WriteLine(\"New\");", patched);
        Assert.DoesNotContain("Console.WriteLine(\"Old\");", patched);
        Assert.Contains("+        Console.WriteLine(\"New\");", diff);
        Assert.Contains("-        Console.WriteLine(\"Old\");", diff);
    }

    [Fact]
    public void ApplyPatch_ShouldFailIfSearchBlockNotFound()
    {
        var content = "public class Foo {}";
        var search = "Console.WriteLine();";
        var replacement = "Console.Write();";

        var (_, _, success, error) = SearchReplacePatchEngine.ApplyPatch(content, search, replacement);

        Assert.False(success);
        Assert.Equal("The search block was not found in the file.", error);
    }

    [Fact]
    public void ApplyPatch_ShouldFailIfSearchBlockNotUnique()
    {
        var content = "public class Foo {\n    void M1() { Call(); }\n    void M2() { Call(); }\n}";
        var search = "Call();";
        var replacement = "NewCall();";

        var (_, _, success, error) = SearchReplacePatchEngine.ApplyPatch(content, search, replacement);

        Assert.False(success);
        Assert.Contains("matches multiple locations", error);
    }

    [Fact]
    public async Task WorkspaceEditService_ShouldApplyAndRevertSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var testFilePath = "TestFile.cs";
        var fullPath = Path.Combine(tempDir, testFilePath);
        var originalContent = "using System;\npublic class Test {\n    public void Run() {\n        // ToDo\n    }\n}";
        await File.WriteAllTextAsync(fullPath, originalContent);

        var editPlan = new EditPlan(
            "Replace ToDo with implementation",
            new List<FileEditOperation> {
                new FileEditOperation(testFilePath, new List<PatchInstruction> {
                    new PatchInstruction("Run", "Implement Run method", "        // ToDo", "        Console.WriteLine(\"Running!\");")
                })
            }
        );

        var service = new WorkspaceEditService();

        // 1. Preview
        var preview = await service.PreviewPlanAsync(editPlan, tempDir);
        Assert.True(preview.FilePreviews[0].IsValid);
        Assert.Contains("Console.WriteLine(\"Running!\");", preview.FilePreviews[0].PatchedContent);

        // 2. Apply
        var (applySuccess, applyError) = await service.ApplyPlanAsync(editPlan, tempDir);
        Assert.True(applySuccess);
        Assert.Null(applyError);

        var fileContentAfterApply = await File.ReadAllTextAsync(fullPath);
        Assert.Contains("Console.WriteLine(\"Running!\");", fileContentAfterApply);

        // 3. Revert
        var (revertSuccess, revertError) = await service.RevertLastPlanAsync(tempDir);
        Assert.True(revertSuccess);
        Assert.Null(revertError);

        var fileContentAfterRevert = await File.ReadAllTextAsync(fullPath);
        Assert.Equal(originalContent, fileContentAfterRevert);

        try
        {
            Directory.Delete(tempDir, true);
        }
        catch
        {
        }
    }
}
