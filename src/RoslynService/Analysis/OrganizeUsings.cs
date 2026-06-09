using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Sort and organize using directives in a single C# file (System namespaces first, then alphabetically).
    /// </summary>
    public async Task<object> OrganizeUsingsAsync(string filePath)
    {
        EnsureSolutionLoaded();

        if (await GetCSharpOnlyToolErrorAsync(filePath, "organize_usings") is { } unsupportedLanguageError)
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
        if (syntaxTree == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get syntax tree",
                context: new { filePath }
            );
        }

        var root = await syntaxTree.GetRootAsync();
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Not a valid C# file",
                hint: "This tool only works with C# source files",
                context: new { filePath }
            );
        }

        // Get all usings
        var usings = compilationUnit.Usings;

        // Sort them (System namespaces first, then alphabetically)
        var sortedUsings = usings
            .OrderBy(u => u.Name?.ToString().StartsWith("System") == true ? 0 : 1)
            .ThenBy(u => u.Name?.ToString())
            .ToList();

        // Create new compilation unit with sorted usings
        var newRoot = compilationUnit.WithUsings(SyntaxFactory.List(sortedUsings));

        return CreateSuccessResponse(
            data: new
            {
                organizedText = newRoot.ToFullString()
            },
            suggestedNextTools: new[]
            {
                "organize_usings_batch to process multiple files",
                "format_document_batch for consistent formatting"
            }
        );
    }
}
