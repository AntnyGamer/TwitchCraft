using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TwitchCraftConsistency;

/// <summary>
/// Mechanical, token-preserving source-format checks for conventions not covered by dotnet format.
/// Every .cs file in the application and test projects is examined. No regex rewriting of C# is used.
/// </summary>
internal static class Program
{
    private const int MaxSignatureColumns = 120;
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    private readonly record struct Edit(int Start, int Length, string Replacement);

    private static int Main(string[] arguments)
    {
        if (arguments.Contains("--self-test", StringComparer.Ordinal))
        {
            return SelfTest();
        }

        bool check = arguments.Contains("--check", StringComparer.Ordinal);
        bool write = arguments.Contains("--write", StringComparer.Ordinal);
        if (check == write)
        {
            Console.Error.WriteLine("Usage: ConsistencyFixer --root <repo> (--check | --write), or --self-test");
            return 2;
        }

        int rootOption = Array.IndexOf(arguments, "--root");
        string repository = rootOption >= 0 && rootOption + 1 < arguments.Length
            ? Path.GetFullPath(arguments[rootOption + 1])
            : Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(repository, "TwitchCraft.slnx")))
        {
            Console.Error.WriteLine($"Not the TwitchCraft repository root: {repository}");
            return 2;
        }

        string[] projects = ["TwitchCraft_SOURCE", "TwitchCraft.Tests"];
        var files = projects
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(repository, project), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        int changed = 0;
        int errors = 0;
        foreach (string file in files)
        {
            byte[] bytes = File.ReadAllBytes(file);
            bool hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble());
            string source = File.ReadAllText(file, Encoding.UTF8);
            string revised;
            try
            {
                revised = Rewrite(source);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR {Path.GetRelativePath(repository, file)}: {ex.Message}");
                errors++;
                continue;
            }

            if (source == revised)
            {
                continue;
            }

            changed++;
            Console.WriteLine($"{(check ? "NEEDS FIX" : "UPDATED")} {Path.GetRelativePath(repository, file)}");
            if (write)
            {
                // Preserve the BOM choice, original line endings, and all source tokens.
                byte[] body = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(revised);
                if (hasBom)
                {
                    byte[] preamble = Encoding.UTF8.GetPreamble();
                    File.WriteAllBytes(file, [.. preamble, .. body]);
                }
                else
                {
                    File.WriteAllBytes(file, body);
                }
            }
        }

        Console.WriteLine($"Examined {files.Length} C# files; {changed} {(write ? "updated" : "require updates")}; {errors} errors.");
        return errors > 0 || (check && changed > 0) ? 1 : 0;
    }

    private static string Rewrite(string source)
    {
        SyntaxTree originalTree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        CompilationUnitSyntax root = originalTree.GetCompilationUnitRoot();
        string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var edits = new List<Edit>();

        // After file-scoped namespace declarations, always separate the first type.
        foreach (FileScopedNamespaceDeclarationSyntax declaration in root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>())
        {
            if (declaration.Members.Count > 0)
            {
                AddSeparator(source, newline, declaration.SemicolonToken.Span.End, declaration.Members[0].SpanStart, edits);
            }
        }

        // Between members: keep groups of fields together; separate all other member pairs.
        // This covers nested classes, records, structs, interfaces, and both namespace styles.
        foreach (TypeDeclarationSyntax declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            AddMemberSeparators(source, newline, declaration.Members, edits);
        }

        foreach (BaseNamespaceDeclarationSyntax declaration in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
        {
            AddMemberSeparators(source, newline, declaration.Members, edits);
        }

        AddMemberSeparators(source, newline, root.Members, edits);

        // Consistent, compact signatures at <=120 columns. Beyond 120 columns,
        // put every parameter on a separate line. Never touch invocation arguments.
        foreach (BaseMethodDeclarationSyntax declaration in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            AddParameterLayout(source, newline, declaration.ParameterList, edits);
        }

        foreach (DelegateDeclarationSyntax declaration in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
        {
            AddParameterLayout(source, newline, declaration.ParameterList, edits);
        }

        foreach (LocalFunctionStatementSyntax declaration in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
        {
            AddParameterLayout(source, newline, declaration.ParameterList, edits);
        }

        string revised = source;
        int previousStart = source.Length + 1;
        foreach (Edit edit in edits.OrderByDescending(edit => edit.Start))
        {
            if (edit.Start + edit.Length > previousStart)
            {
                throw new InvalidOperationException("Overlapping syntax edits: refusing to write.");
            }

            revised = revised.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);
            previousStart = edit.Start;
        }

        // This formatter is not permitted to add, remove, or change C# code tokens.
        CompilationUnitSyntax after = CSharpSyntaxTree.ParseText(revised, ParseOptions).GetCompilationUnitRoot();
        string[] beforeTokens = root.DescendantTokens().Select(token => $"{token.RawKind}:{token.Text}").ToArray();
        string[] afterTokens = after.DescendantTokens().Select(token => $"{token.RawKind}:{token.Text}").ToArray();
        if (!beforeTokens.SequenceEqual(afterTokens, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("C# token sequence changed: refusing to write.");
        }

        int beforeErrors = root.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
        int afterErrors = after.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
        if (beforeErrors != afterErrors)
        {
            throw new InvalidOperationException("C# parser error count changed: refusing to write.");
        }

        return revised;
    }

    private static void AddMemberSeparators(
        string source,
        string newline,
        SyntaxList<MemberDeclarationSyntax> members,
        List<Edit> edits)
    {
        for (int index = 1; index < members.Count; index++)
        {
            MemberDeclarationSyntax previous = members[index - 1];
            MemberDeclarationSyntax next = members[index];
            if (IsField(previous) && IsField(next))
            {
                continue;
            }

            AddSeparator(source, newline, previous.Span.End, next.SpanStart, edits);
        }
    }

    private static bool IsField(MemberDeclarationSyntax member)
        => member is FieldDeclarationSyntax or EventFieldDeclarationSyntax;

    private static void AddSeparator(string source, string newline, int previousEnd, int nextStart, List<Edit> edits)
    {
        int previousLineEnd = source.IndexOf('\n', previousEnd);
        if (previousLineEnd < 0)
        {
            return;
        }

        int startOfGap = previousLineEnd + 1;
        int currentLineStart = source.LastIndexOf('\n', Math.Max(0, nextStart - 1)) + 1;
        if (currentLineStart < startOfGap)
        {
            // Both members occur on one physical line: a broader formatter owns this case.
            return;
        }

        // If the first line between members is already empty, don't add a second blank line.
        int firstGapLineEnd = source.IndexOf('\n', startOfGap);
        if (firstGapLineEnd < 0 || firstGapLineEnd > currentLineStart)
        {
            firstGapLineEnd = currentLineStart;
        }

        // Conditional-compilation boundaries are not ordinary member gaps. Avoid
        // changing directive layout or implying a single active branch is the only code.
        string gapText = source[startOfGap..currentLineStart];
        if (gapText.Split('\n').Any(line => line.TrimStart().StartsWith('#')))
        {
            return;
        }

        string initialGapLine = source[startOfGap..firstGapLineEnd];
        if (initialGapLine.Length > 0 && string.IsNullOrWhiteSpace(initialGapLine))
        {
            return;
        }

        if (initialGapLine.Length == 0 && startOfGap < currentLineStart)
        {
            // A completely empty line already exists after the previous member.
            return;
        }

        if (startOfGap == currentLineStart || !string.IsNullOrWhiteSpace(initialGapLine))
        {
            // Insert before XML docs, comments, or conditional compilation directives.
            edits.Add(new Edit(startOfGap, 0, newline));
        }
    }

    private static void AddParameterLayout(string source, string newline, ParameterListSyntax list, List<Edit> edits)
    {
        if (list.Parameters.Count == 0 || list.OpenParenToken.IsMissing || list.CloseParenToken.IsMissing)
        {
            return;
        }

        // Never move comments, preprocessor directives, or multiline parameter expressions.
        if (list.DescendantTrivia(descendIntoTrivia: true).Any(trivia =>
                trivia.IsDirective || trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)))
        {
            return;
        }

        string[] parameters = list.Parameters.Select(parameter => parameter.ToString().Trim()).ToArray();
        if (parameters.Any(parameter => parameter.Contains('\n') || parameter.Contains('\r')))
        {
            return;
        }

        int open = list.OpenParenToken.Span.End;
        int close = list.CloseParenToken.SpanStart;
        if (close < open)
        {
            return;
        }

        string current = source[open..close];
        int beginningOfLine = source.LastIndexOf('\n', Math.Max(0, list.OpenParenToken.SpanStart - 1)) + 1;
        string prefix = source[beginningOfLine..list.OpenParenToken.SpanStart];
        string lineIndent = new(prefix.TakeWhile(character => character is ' ' or '\t').ToArray());
        string flattened = string.Join(", ", parameters);
        bool multiline = prefix.Length + flattened.Length + 2 > MaxSignatureColumns;
        string preferred = multiline
            ? newline + lineIndent + "    " + string.Join("," + newline + lineIndent + "    ", parameters) + newline + lineIndent
            : flattened;

        if (!string.Equals(current, preferred, StringComparison.Ordinal))
        {
            edits.Add(new Edit(open, close - open, preferred));
        }
    }

    private static int SelfTest()
    {
        const string input = """
            namespace Sample;
            class Example
            {
                private int a;
                private int b;
                /// <summary>Documentation.</summary>
                public void One() { }
                public void Two(string first, string second) { }
                public void VeryLongParameterList(string extremelyLongFirstArgumentWithDescription, string veryLongSecondArgumentWithDescription, CancellationToken cancellationToken = default) { }
            }
            """;

        string fixedSource = Rewrite(input);
        bool good = fixedSource.Contains("namespace Sample;\n\nclass Example", StringComparison.Ordinal)
            && fixedSource.Contains("private int a;\n    private int b;", StringComparison.Ordinal)
            && fixedSource.Contains("private int b;\n\n    ///", StringComparison.Ordinal)
            && fixedSource.Contains("One() { }\n\n    public void Two", StringComparison.Ordinal)
            && fixedSource.Contains("VeryLongParameterList(\n", StringComparison.Ordinal)
            && fixedSource == Rewrite(fixedSource);

        Console.WriteLine(good ? "Self-test passed (including idempotency)." : "Self-test FAILED.");
        return good ? 0 : 1;
    }
}
