using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> AnalyzeControlFlowAsync(
        string filePath,
        int startLine,
        int endLine)
    {
        EnsureSolutionLoaded();

        var document = await GetDocumentAsync(filePath);
        if (document == null)
        {
            return CreateErrorResponse(
                ErrorCodes.FileNotInSolution,
                $"File '{filePath}' not found in solution",
                hint: "Check the file path or reload the solution"
            );
        }

        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get semantic model"
            );
        }

        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get syntax tree"
            );
        }

        var root = await syntaxTree.GetRootAsync();
        var text = await syntaxTree.GetTextAsync();

        var startPosition = text.Lines[Math.Max(0, startLine)].Start;
        var endPosition = text.Lines[Math.Min(text.Lines.Count - 1, endLine)].End;
        var span = TextSpan.FromBounds(startPosition, endPosition);

        var statements = SyntaxStrategy(document).FindBlockStatements(root, span).ToList();
        if (statements.Count == 0)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "No statements found in the specified region",
                context: new { startLine, endLine }
            );
        }

        SyntaxNode firstStatement = statements.First();
        SyntaxNode lastStatement = statements.Last();

        ControlFlowAnalysis? controlFlow = null;
        try
        {
            controlFlow = semanticModel.AnalyzeControlFlow(firstStatement, lastStatement);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                $"Control flow analysis failed: {ex.Message}"
            );
        }

        if (controlFlow == null || !controlFlow.Succeeded)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Control flow analysis did not succeed"
            );
        }

        var entryPoints = controlFlow.EntryPoints.Select(ep =>
        {
            var lineSpan = ep.GetLocation().GetLineSpan();
            return new
            {
                line = lineSpan.StartLinePosition.Line,
                kind = ep.GetType().Name.Replace("Syntax", "")
            };
        }).ToList();

        var exitPoints = controlFlow.ExitPoints.Select(ep =>
        {
            var lineSpan = ep.GetLocation().GetLineSpan();
            return new
            {
                line = lineSpan.StartLinePosition.Line,
                kind = ep.GetType().Name.Replace("Syntax", "")
            };
        }).ToList();

        var returnStatements = controlFlow.ReturnStatements.Select(rs =>
        {
            var lineSpan = rs.GetLocation().GetLineSpan();
            return new
            {
                line = lineSpan.StartLinePosition.Line,
                text = rs.ToString().Trim()
            };
        }).ToList();

        return CreateSuccessResponse(
            data: new
            {
                region = new { startLine, endLine },
                succeeded = controlFlow.Succeeded,
                entryPoints,
                exitPoints,
                returnStatements,
                endPointIsReachable = controlFlow.EndPointIsReachable,
                startPointIsReachable = controlFlow.StartPointIsReachable
            },
            suggestedNextTools: new[]
            {
                "analyze_data_flow to analyze variable usage in the same region",
                "get_diagnostics to check for unreachable code warnings"
            }
        );
    }
}
