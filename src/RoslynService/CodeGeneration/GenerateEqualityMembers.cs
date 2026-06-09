using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace DotNetLensMcp;

public partial class RoslynService
{

    /// <summary>
    /// Generate Equals, GetHashCode, and operators for a type.
    /// </summary>
    public async Task<object> GenerateEqualityMembersAsync(
        string filePath,
        int line,
        int column,
        bool includeOperators = true,
        bool preview = true)
    {
        EnsureSolutionLoaded();

        if (await GetCSharpOnlyToolErrorAsync(filePath, "generate_equality_members") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

        Document document;
        try
        {
            document = await GetDocumentAsync(filePath);
        }
        catch (FileNotFoundException)
        {
            return CreateErrorResponse(
                ErrorCodes.FileNotInSolution,
                $"File not found in solution: {filePath}",
                hint: "Check the file path or reload the solution",
                context: new { filePath }
            );
        }

        var syntaxTree = await document.GetSyntaxTreeAsync();
        var semanticModel = await document.GetSemanticModelAsync();

        if (syntaxTree == null || semanticModel == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not analyze file",
                context: new { filePath }
            );
        }

        var root = await syntaxTree.GetRootAsync();
        var position = GetPosition(syntaxTree, line, column);
        var node = root.FindToken(position).Parent;

        // Find the type declaration
        var typeNode = node?.AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        if (typeNode == null)
        {
            return CreateErrorResponse(
                ErrorCodes.NotAType,
                "No type declaration found at this position",
                hint: "Position cursor on a class or struct declaration",
                context: new { filePath, line, column }
            );
        }

        var typeSymbol = semanticModel.GetDeclaredSymbol(typeNode) as INamedTypeSymbol;
        if (typeSymbol == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get type symbol",
                context: new { typeName = typeNode.Identifier.Text }
            );
        }

        var typeName = typeSymbol.Name;

        // Get all fields and auto-properties to include in equality
        var members = typeSymbol.GetMembers()
            .Where(m =>
                (m is IFieldSymbol field && !field.IsStatic && !field.IsConst) ||
                (m is IPropertySymbol prop && !prop.IsStatic && prop.GetMethod != null))
            .Select(m => m.Name)
            .ToList();

        if (members.Count == 0)
        {
            return CreateSuccessResponse(
                data: new
                {
                    message = "No instance fields or properties found to compare",
                    typeName
                }
            );
        }

        // Generate Equals method
        var equalsCode = new StringBuilder();
        equalsCode.AppendLine($@"
    public override bool Equals(object? obj)
    {{
        return obj is {typeName} other && Equals(other);
    }}

    public bool Equals({typeName}? other)
    {{
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return {string.Join(" && ", members.Select(m => $"{m} == other.{m}"))};
    }}

    public override int GetHashCode()
    {{
        return HashCode.Combine({string.Join(", ", members.Take(8))});
    }}");

        if (includeOperators)
        {
            equalsCode.AppendLine($@"
    public static bool operator ==({typeName}? left, {typeName}? right)
    {{
        return Equals(left, right);
    }}

    public static bool operator !=({typeName}? left, {typeName}? right)
    {{
        return !Equals(left, right);
    }}");
        }

        // Find where to insert (before closing brace of type)
        var closeBrace = typeNode.CloseBraceToken;
        var insertPosition = closeBrace.SpanStart;

        if (preview)
        {
            return CreateSuccessResponse(
                data: new
                {
                    preview = true,
                    typeName,
                    membersCompared = members,
                    includeOperators,
                    generatedCode = equalsCode.ToString().Trim()
                },
                suggestedNextTools: new[] { "generate_equality_members with preview=false to apply" }
            );
        }

        // Apply the change
        var sourceText = await document.GetTextAsync();
        var newText = sourceText.Replace(new TextSpan(insertPosition, 0), equalsCode.ToString());
        await File.WriteAllTextAsync(filePath, newText.ToString());

        // Clear cache
        _documentCache.Clear();

        return CreateSuccessResponse(
            data: new
            {
                applied = true,
                typeName,
                membersCompared = members,
                includeOperators
            },
            suggestedNextTools: new[] { "get_diagnostics to verify changes" }
        );
    }

}
