using DevPilot.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DevPilot.Indexer;

public sealed partial class CodeChunker : ICodeChunker
{
    private const int DefaultMaxLines = 120;
    private readonly ILogger<CodeChunker> _logger;

    public CodeChunker(ILogger<CodeChunker> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<CodeChunk> ChunkAsync(
        RepositoryDescriptor repository,
        RepositoryFile file,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(file.FullPath, cancellationToken).ConfigureAwait(false);
        var language = DetectLanguage(file.FullPath);
        var fileId = DeterministicId($"{repository.Id}:{file.RelativePath}:{content.Length}");

        foreach (var chunk in CreateChunks(repository.Id, fileId, file.RelativePath, content, language))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return chunk;
        }
    }

    public IReadOnlyList<CodeChunk> Chunk(
        string repositoryId,
        string fileId,
        string relativePath,
        string content,
        string language)
    {
        return CreateChunks(repositoryId, fileId, relativePath, content, language).ToList();
    }

    private IEnumerable<CodeChunk> CreateChunks(
        string repositoryId,
        string fileId,
        string relativePath,
        string content,
        string language)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            yield break;
        }

        if (language == "csharp")
        {
            foreach (var chunk in ChunkCSharp(repositoryId, fileId, relativePath, content, language))
            {
                yield return chunk;
            }

            yield break;
        }

        if (language == "typescript" || language == "javascript")
        {
            foreach (var chunk in ChunkTypeScriptJavaScript(repositoryId, fileId, relativePath, content, language))
            {
                yield return chunk;
            }

            yield break;
        }

        if (language == "markdown")
        {
            foreach (var chunk in ChunkMarkdown(repositoryId, fileId, relativePath, content, language))
            {
                yield return chunk;
            }

            yield break;
        }

        foreach (var chunk in ChunkByLines(repositoryId, fileId, relativePath, content, language, "file", null))
        {
            yield return chunk;
        }
    }

    private IEnumerable<CodeChunk> ChunkCSharp(
        string repositoryId,
        string fileId,
        string relativePath,
        string content,
        string language)
    {
        SyntaxTree tree;
        try
        {
            tree = CSharpSyntaxTree.ParseText(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falling back to line chunking for C# file {FilePath}.", relativePath);
            return ChunkByLines(repositoryId, fileId, relativePath, content, language, "file", null);
        }

        var root = tree.GetRoot();
        var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Select(u => u.Name.ToString())
            .ToList();

        var nodes = root.DescendantNodes()
            .Where(node => node is ClassDeclarationSyntax or InterfaceDeclarationSyntax or MethodDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax or EnumDeclarationSyntax)
            .OrderBy(node => node.SpanStart)
            .ToList();

        if (nodes.Count == 0)
        {
            return ChunkByLines(repositoryId, fileId, relativePath, content, language, "file", null);
        }

        return nodes.Select(node =>
        {
            var span = tree.GetLineSpan(node.Span);
            var startLine = span.StartLinePosition.Line + 1;
            var endLine = span.EndLinePosition.Line + 1;
            var symbolName = GetCSharpSymbolName(node) ?? "unknown";
            
            var chunkType = node switch
            {
                ClassDeclarationSyntax => "class",
                InterfaceDeclarationSyntax => "interface",
                StructDeclarationSyntax => "struct",
                RecordDeclarationSyntax => "record",
                EnumDeclarationSyntax => "enum",
                MethodDeclarationSyntax => "method",
                _ => "symbol"
            };

            var ns = GetNamespace(node);
            var parent = GetParentSymbol(node);

            // Extract references
            var referenced = node.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Select(i => i.Identifier.ValueText)
                .Where(name => !CSharpKeywords.Contains(name) && name != symbolName)
                .Distinct()
                .ToList();

            var definitionLocation = $"{relativePath}:{startLine}:{endLine}";

            return CreateChunk(
                repositoryId,
                fileId,
                relativePath,
                symbolName,
                chunkType,
                startLine,
                endLine,
                node.ToFullString(),
                language,
                symbolKind: chunkType,
                @namespace: ns,
                parentSymbol: parent,
                referencedSymbols: referenced,
                importedNamespaces: usings,
                fileDependencies: Array.Empty<string>(),
                definitionLocation: definitionLocation);
        });
    }

    private IEnumerable<CodeChunk> ChunkTypeScriptJavaScript(
        string repositoryId,
        string fileId,
        string relativePath,
        string content,
        string language)
    {
        var lines = SplitLines(content);
        var chunks = new List<CodeChunk>();

        // 1. Extract imports/requires
        var imports = new List<string>();
        var importRegex1 = new Regex(@"import\s+(?:[^""']*?\s+from\s+)?[""']([^""']+)[""']", RegexOptions.Compiled);
        var importRegex2 = new Regex(@"require\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.Compiled);

        foreach (var line in lines)
        {
            var match1 = importRegex1.Match(line);
            if (match1.Success) imports.Add(match1.Groups[1].Value);
            var match2 = importRegex2.Match(line);
            if (match2.Success) imports.Add(match2.Groups[1].Value);
        }

        // 2. Scan for symbols
        var classRegex = new Regex(@"class\s+([A-Za-z0-9_]+)", RegexOptions.Compiled);
        var interfaceRegex = new Regex(@"interface\s+([A-Za-z0-9_]+)", RegexOptions.Compiled);
        var functionRegex = new Regex(@"(?:async\s+)?function\s+([A-Za-z0-9_]+)", RegexOptions.Compiled);
        var methodRegex = new Regex(@"(?:async\s+)?([A-Za-z0-9_]+)\s*\([^)]*\)\s*\{", RegexOptions.Compiled);

        string? currentClass = null;
        int classStart = -1;
        int classEnd = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            var classMatch = classRegex.Match(line);
            if (classMatch.Success)
            {
                var className = classMatch.Groups[1].Value;
                var endLineIndex = FindMatchingBrace(lines, i);
                var chunkLines = lines[i..(endLineIndex + 1)];
                chunks.Add(CreateJsTsChunk(chunkLines, className, "class", i + 1, endLineIndex + 1, imports, null));
                
                currentClass = className;
                classStart = i;
                classEnd = endLineIndex;
                continue;
            }

            var interfaceMatch = interfaceRegex.Match(line);
            if (interfaceMatch.Success)
            {
                var interfaceName = interfaceMatch.Groups[1].Value;
                var endLineIndex = FindMatchingBrace(lines, i);
                var chunkLines = lines[i..(endLineIndex + 1)];
                chunks.Add(CreateJsTsChunk(chunkLines, interfaceName, "interface", i + 1, endLineIndex + 1, imports, null));
                continue;
            }

            var funcMatch = functionRegex.Match(line);
            if (funcMatch.Success)
            {
                var funcName = funcMatch.Groups[1].Value;
                var endLineIndex = FindMatchingBrace(lines, i);
                var chunkLines = lines[i..(endLineIndex + 1)];
                chunks.Add(CreateJsTsChunk(chunkLines, funcName, "function", i + 1, endLineIndex + 1, imports, null));
                continue;
            }

            var methodMatch = methodRegex.Match(line);
            if (methodMatch.Success)
            {
                var methodName = methodMatch.Groups[1].Value;
                if (methodName == "constructor") continue;

                var endLineIndex = FindMatchingBrace(lines, i);
                var chunkLines = lines[i..(endLineIndex + 1)];
                
                string? parent = null;
                if (currentClass != null && i >= classStart && i <= classEnd)
                {
                    parent = currentClass;
                }

                chunks.Add(CreateJsTsChunk(chunkLines, methodName, "method", i + 1, endLineIndex + 1, imports, parent));
            }
        }

        if (chunks.Count == 0)
        {
            return ChunkByLines(repositoryId, fileId, relativePath, content, language, "file", null);
        }

        return chunks;

        CodeChunk CreateJsTsChunk(string[] chunkLines, string name, string kind, int start, int end, List<string> imps, string? parent)
        {
            var chunkText = string.Join(Environment.NewLine, chunkLines);
            var refs = ExtractJsTsReferences(chunkText, name);
            return CreateChunk(
                repositoryId,
                fileId,
                relativePath,
                name,
                kind,
                start,
                end,
                chunkText,
                language,
                symbolKind: kind,
                @namespace: null,
                parentSymbol: parent,
                referencedSymbols: refs,
                importedNamespaces: imps,
                fileDependencies: Array.Empty<string>(),
                definitionLocation: $"{relativePath}:{start}:{end}");
        }
    }

    private static int FindMatchingBrace(string[] lines, int startLineIndex)
    {
        var braceCount = 0;
        var foundOpen = false;
        for (var i = startLineIndex; i < lines.Length; i++)
        {
            var line = lines[i];
            for (var j = 0; j < line.Length; j++)
            {
                var ch = line[j];
                if (ch == '{')
                {
                    braceCount++;
                    foundOpen = true;
                }
                else if (ch == '}')
                {
                    braceCount--;
                }
            }
            if (foundOpen && braceCount <= 0)
            {
                return i;
            }
        }
        return lines.Length - 1;
    }

    private IEnumerable<CodeChunk> ChunkMarkdown(
        string repositoryId,
        string fileId,
        string relativePath,
        string content,
        string language)
    {
        var lines = SplitLines(content);
        var headingIndexes = lines
            .Select((line, index) => new { line, index })
            .Where(item => MarkdownHeadingRegex().IsMatch(item.line))
            .Select(item => item.index)
            .ToList();

        if (headingIndexes.Count == 0)
        {
            return ChunkByLines(repositoryId, fileId, relativePath, content, language, "section", null);
        }

        var chunks = new List<CodeChunk>();
        for (var i = 0; i < headingIndexes.Count; i++)
        {
            var start = headingIndexes[i];
            var endExclusive = i + 1 < headingIndexes.Count ? headingIndexes[i + 1] : lines.Length;
            var sectionLines = lines[start..endExclusive];
            var heading = MarkdownHeadingRegex().Replace(lines[start], string.Empty).Trim();
            chunks.Add(CreateChunk(
                repositoryId,
                fileId,
                relativePath,
                string.IsNullOrWhiteSpace(heading) ? null : heading,
                "section",
                start + 1,
                endExclusive,
                string.Join(Environment.NewLine, sectionLines),
                language));
        }

        return chunks;
    }

    private static IEnumerable<CodeChunk> ChunkByLines(
        string repositoryId,
        string fileId,
        string relativePath,
        string content,
        string language,
        string chunkType,
        string? symbolName)
    {
        var lines = SplitLines(content);
        for (var index = 0; index < lines.Length; index += DefaultMaxLines)
        {
            var endExclusive = Math.Min(index + DefaultMaxLines, lines.Length);
            yield return CreateChunk(
                repositoryId,
                fileId,
                relativePath,
                symbolName,
                chunkType,
                index + 1,
                endExclusive,
                string.Join(Environment.NewLine, lines[index..endExclusive]),
                language);
        }
    }

    private static CodeChunk CreateChunk(
        string repositoryId,
        string fileId,
        string relativePath,
        string? symbolName,
        string chunkType,
        int startLine,
        int endLine,
        string content,
        string language,
        string? symbolKind = null,
        string? @namespace = null,
        string? parentSymbol = null,
        IReadOnlyList<string>? referencedSymbols = null,
        IReadOnlyList<string>? importedNamespaces = null,
        IReadOnlyList<string>? fileDependencies = null,
        string? definitionLocation = null)
    {
        var chunkId = DeterministicId($"{repositoryId}:{fileId}:{relativePath}:{chunkType}:{startLine}:{endLine}:{symbolName}");
        var chunkHash = DeterministicId(content);
        var tokenEstimate = Math.Max(1, (int)Math.Ceiling(content.Length / 4d));
        return new CodeChunk(
            chunkId,
            repositoryId,
            fileId,
            relativePath,
            symbolName,
            chunkType,
            startLine,
            endLine,
            content,
            language,
            chunkHash,
            tokenEstimate,
            symbolKind,
            @namespace,
            parentSymbol,
            referencedSymbols,
            importedNamespaces,
            fileDependencies,
            definitionLocation);
    }

    private static string? GetCSharpSymbolName(SyntaxNode node)
    {
        return node switch
        {
            ClassDeclarationSyntax declaration => declaration.Identifier.Text,
            InterfaceDeclarationSyntax declaration => declaration.Identifier.Text,
            StructDeclarationSyntax declaration => declaration.Identifier.Text,
            RecordDeclarationSyntax declaration => declaration.Identifier.Text,
            EnumDeclarationSyntax declaration => declaration.Identifier.Text,
            MethodDeclarationSyntax declaration => declaration.Identifier.Text,
            _ => null
        };
    }

    private static string? GetNamespace(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is NamespaceDeclarationSyntax ns) return ns.Name.ToString();
            if (current is FileScopedNamespaceDeclarationSyntax fns) return fns.Name.ToString();
            current = current.Parent;
        }
        return null;
    }

    private static string? GetParentSymbol(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current is ClassDeclarationSyntax cds) return cds.Identifier.Text;
            if (current is InterfaceDeclarationSyntax ids) return ids.Identifier.Text;
            current = current.Parent;
        }
        return null;
    }

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while", "var", "Task", "List", "Dictionary", "Action", "Func", "Console",
        "TaskAwaiter", "ConfigureAwait", "GetAwaiter", "GetResult"
    };

    private static readonly HashSet<string> JsKeywords = new(StringComparer.Ordinal)
    {
        "const", "let", "var", "function", "class", "export", "import", "from", "default",
        "return", "if", "else", "for", "while", "do", "switch", "case", "break", "continue",
        "new", "this", "super", "try", "catch", "finally", "throw", "async", "await", "true",
        "false", "null", "undefined", "console", "log", "error", "warn", "info", "Promise",
        "resolve", "reject", "then", "catch", "finally"
    };

    private static List<string> ExtractJsTsReferences(string text, string excludeName)
    {
        var matches = Regex.Matches(text, @"\b[A-Za-z_][A-Za-z0-9_]*\b");
        return matches.Cast<Match>()
            .Select(m => m.Value)
            .Where(val => !JsKeywords.Contains(val) && val != excludeName)
            .Distinct()
            .ToList();
    }

    private static string DetectLanguage(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".md" => "markdown",
            ".ts" => "typescript",
            ".js" => "javascript",
            ".py" => "python",
            ".java" => "java",
            ".json" => "json",
            ".yaml" or ".yml" => "yaml",
            _ => "text"
        };
    }

    private static string[] SplitLines(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    }

    private static string DeterministicId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [GeneratedRegex("^#{1,6}\\s+")]
    private static partial Regex MarkdownHeadingRegex();
}
