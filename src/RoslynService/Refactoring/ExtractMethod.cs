using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Extracts a code block into a new method.
    /// Uses data flow analysis to determine parameters and return value.
    /// </summary>
    public async Task<object> ExtractMethodAsync(
        string filePath,
        int startLine,
        int endLine,
        string methodName,
        string accessibility = "private",
        bool preview = true)
    {
        EnsureSolutionLoaded();

        if (await GetCSharpOnlyToolErrorAsync(filePath, "extract_method") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

        var document = _solution!.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath != null && d.FilePath.Equals(filePath, PathComparison));

        if (document == null)
        {
            return CreateErrorResponse(
                ErrorCodes.FileNotInSolution,
                $"File not found in solution: {filePath}",
                hint: "Ensure the file is part of a project in the loaded solution"
            );
        }

        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (semanticModel == null || syntaxTree == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get semantic model",
                context: new { filePath }
            );
        }

        var text = await syntaxTree.GetTextAsync();
        var root = await syntaxTree.GetRootAsync();

        // Get the text span for the selection
        var startPosition = text.Lines[startLine].Start;
        var endPosition = text.Lines[endLine].End;
        var selectionSpan = TextSpan.FromBounds(startPosition, endPosition);

        // Find statements in the selection
        var nodesInSelection = root.DescendantNodes()
            .Where(n => selectionSpan.Contains(n.Span))
            .ToList();

        var statements = nodesInSelection
            .OfType<StatementSyntax>()
            .Where(s => s.Parent is BlockSyntax)
            .OrderBy(s => s.SpanStart)
            .ToList();

        if (statements.Count == 0)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "No statements found in selection",
                hint: "Select one or more complete statements to extract",
                context: new { startLine, endLine }
            );
        }

        // Get the containing method
        var containingMethod = statements[0].Ancestors()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (containingMethod == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Selection must be inside a method",
                context: new { startLine, endLine }
            );
        }

        // Perform data flow analysis
        var firstStatement = statements.First();
        var lastStatement = statements.Last();

        DataFlowAnalysis? dataFlow = null;
        try
        {
            dataFlow = semanticModel.AnalyzeDataFlow(firstStatement, lastStatement);
        }
        catch
        {
            // Fall back to analyzing individual statement
            dataFlow = semanticModel.AnalyzeDataFlow(firstStatement);
        }

        if (dataFlow == null || !dataFlow.Succeeded)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Data flow analysis failed",
                hint: "The selection may contain unsupported constructs",
                context: new { startLine, endLine }
            );
        }

        // Determine parameters (variables that flow into the selection)
        var parameters = dataFlow.DataFlowsIn
            .Where(s => s.Kind == SymbolKind.Local || s.Kind == SymbolKind.Parameter)
            .Select(s => new
            {
                name = s.Name,
                type = s switch
                {
                    ILocalSymbol local => local.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    IParameterSymbol param => param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    _ => "object"
                },
                reason = "read inside selection"
            })
            .ToList();

        // Determine return value (variables assigned inside that are used after)
        var returnCandidates = dataFlow.DataFlowsOut
            .Where(s => s.Kind == SymbolKind.Local)
            .ToList();

        string returnType = "void";
        string? returnVariable = null;
        string? returnReason = null;

        if (returnCandidates.Count == 1)
        {
            var returnSymbol = returnCandidates[0] as ILocalSymbol;
            if (returnSymbol != null)
            {
                returnType = returnSymbol.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                returnVariable = returnSymbol.Name;
                returnReason = $"variable '{returnVariable}' assigned inside and used after selection";
            }
        }
        else if (returnCandidates.Count > 1)
        {
            // Multiple out values - would need out parameters or tuple
            returnType = $"({string.Join(", ", returnCandidates.Cast<ILocalSymbol>().Select(s => $"{s.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {s.Name}"))})";
            returnReason = "multiple variables flow out - consider using tuple or out parameters";
        }

        // Build the extracted method code
        var sb = new StringBuilder();
        var paramString = string.Join(", ", parameters.Select(p => $"{p.type} {p.name}"));

        sb.AppendLine($"{accessibility} {returnType} {methodName}({paramString})");
        sb.AppendLine("{");

        // Add the original statements
        foreach (var statement in statements)
        {
            var statementText = statement.ToFullString();
            // Indent each line
            foreach (var line in statementText.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    sb.AppendLine($"    {line.TrimStart()}");
                }
            }
        }

        // Add return statement if needed
        if (returnVariable != null && returnCandidates.Count == 1)
        {
            sb.AppendLine($"    return {returnVariable};");
        }
        else if (returnCandidates.Count > 1)
        {
            sb.AppendLine($"    return ({string.Join(", ", returnCandidates.Select(s => s.Name))});");
        }

        sb.AppendLine("}");

        // Build the replacement call
        var callArgs = string.Join(", ", parameters.Select(p => p.name));
        string replacementCode;

        if (returnType == "void")
        {
            replacementCode = $"{methodName}({callArgs});";
        }
        else if (returnCandidates.Count == 1)
        {
            replacementCode = $"var {returnVariable} = {methodName}({callArgs});";
        }
        else
        {
            var varNames = string.Join(", ", returnCandidates.Select(s => s.Name));
            replacementCode = $"var ({varNames}) = {methodName}({callArgs});";
        }

        return CreateSuccessResponse(
            data: new
            {
                preview,
                methodName,
                signature = $"{accessibility} {returnType} {methodName}({paramString})",
                parameters,
                returnType,
                returnVariable,
                returnReason,
                statementsExtracted = statements.Count,
                extractedCode = sb.ToString(),
                replacementCode,
                location = new
                {
                    filePath,
                    startLine,
                    endLine
                }
            },
            suggestedNextTools: preview
                ? new[] { "Call again with preview: false to apply", "validate_code to check the extracted method" }
                : new[] { "get_diagnostics to check for errors", "Use Edit tool to insert the method" }
        );
    }
}
