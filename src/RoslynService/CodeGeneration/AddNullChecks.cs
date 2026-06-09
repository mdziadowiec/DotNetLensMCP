using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace DotNetLensMcp;

public partial class RoslynService
{

    /// <summary>
    /// Add null check guard clauses to method parameters.
    /// </summary>
    public async Task<object> AddNullChecksAsync(
        string filePath,
        int line,
        int column,
        bool preview = true)
    {
        EnsureSolutionLoaded();

        if (await GetCSharpOnlyToolErrorAsync(filePath, "add_null_checks") is { } unsupportedLanguageError)
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

        // Find the method
        var methodNode = node?.AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (methodNode == null)
        {
            return CreateErrorResponse(
                ErrorCodes.NotAMethod,
                "No method found at this position",
                hint: "Position cursor on a method declaration",
                context: new { filePath, line, column }
            );
        }

        // Get parameters that are reference types and could be null
        var nullableParams = methodNode.ParameterList.Parameters
            .Where(p =>
            {
                var paramSymbol = semanticModel.GetDeclaredSymbol(p);
                if (paramSymbol == null) return false;

                var type = paramSymbol.Type;
                // Check if it's a reference type or nullable value type
                return type.IsReferenceType ||
                       type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            })
            .Select(p => p.Identifier.Text)
            .ToList();

        if (nullableParams.Count == 0)
        {
            return CreateSuccessResponse(
                data: new
                {
                    message = "No nullable parameters found that need null checks",
                    methodName = methodNode.Identifier.Text
                }
            );
        }

        // Generate null check code
        var nullChecks = new StringBuilder();
        foreach (var param in nullableParams)
        {
            nullChecks.AppendLine($"        ArgumentNullException.ThrowIfNull({param});");
        }

        // Find where to insert (after opening brace of method body)
        var body = methodNode.Body;
        if (body == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Method has no body (possibly expression-bodied)",
                hint: "This tool works with block-bodied methods",
                context: new { methodName = methodNode.Identifier.Text }
            );
        }

        var openBrace = body.OpenBraceToken;
        var insertPosition = openBrace.Span.End;

        // Get the text and create the modified version
        var sourceText = await document.GetTextAsync();
        var newText = sourceText.Replace(new TextSpan(insertPosition, 0), "\n" + nullChecks.ToString());

        if (preview)
        {
            return CreateSuccessResponse(
                data: new
                {
                    preview = true,
                    methodName = methodNode.Identifier.Text,
                    parametersWithNullChecks = nullableParams,
                    generatedCode = nullChecks.ToString().Trim(),
                    changes = new[]
                    {
                        new
                        {
                            filePath,
                            insertAfterLine = openBrace.GetLocation().GetLineSpan().StartLinePosition.Line,
                            newCode = nullChecks.ToString().Trim()
                        }
                    }
                },
                suggestedNextTools: new[] { "add_null_checks with preview=false to apply" }
            );
        }

        // Apply the change
        await File.WriteAllTextAsync(filePath, newText.ToString());

        // Reload solution to pick up changes
        ClearWorkspaceCaches();

        return CreateSuccessResponse(
            data: new
            {
                applied = true,
                methodName = methodNode.Identifier.Text,
                parametersWithNullChecks = nullableParams,
                generatedCode = nullChecks.ToString().Trim()
            },
            suggestedNextTools: new[] { "get_diagnostics to verify changes" }
        );
    }

}
