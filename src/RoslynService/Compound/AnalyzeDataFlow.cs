using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> AnalyzeDataFlowAsync(
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
                "Could not get semantic model",
                hint: "Check for syntax errors with get_diagnostics"
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

        SyntaxNode? firstStatement = null;
        SyntaxNode? lastStatement = null;
        if (statements.Count > 0)
        {
            firstStatement = statements.First();
            lastStatement = statements.Last();
        }

        if (firstStatement == null || lastStatement == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "No statements found in the specified region",
                hint: "Ensure the line range contains executable statements",
                context: new { startLine, endLine }
            );
        }

        DataFlowAnalysis? dataFlow = null;
        try
        {
            dataFlow = firstStatement == lastStatement
                ? semanticModel.AnalyzeDataFlow(firstStatement)
                : semanticModel.AnalyzeDataFlow(firstStatement, lastStatement);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                $"Data flow analysis failed: {ex.Message}",
                hint: "Try selecting a different code region"
            );
        }

        if (dataFlow == null || !dataFlow.Succeeded)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Data flow analysis did not succeed",
                hint: "The selected region may not be suitable for data flow analysis"
            );
        }

        return CreateSuccessResponse(
            data: new
            {
                region = new { startLine, endLine },
                succeeded = dataFlow.Succeeded,
                variablesDeclared = dataFlow.VariablesDeclared.Select(s => s.Name).ToArray(),
                alwaysAssigned = dataFlow.AlwaysAssigned.Select(s => s.Name).ToArray(),
                dataFlowsIn = dataFlow.DataFlowsIn.Select(s => s.Name).ToArray(),
                dataFlowsOut = dataFlow.DataFlowsOut.Select(s => s.Name).ToArray(),
                readInside = dataFlow.ReadInside.Select(s => s.Name).ToArray(),
                writtenInside = dataFlow.WrittenInside.Select(s => s.Name).ToArray(),
                readOutside = dataFlow.ReadOutside.Select(s => s.Name).ToArray(),
                writtenOutside = dataFlow.WrittenOutside.Select(s => s.Name).ToArray(),
                captured = dataFlow.Captured.Select(s => s.Name).ToArray(),
                capturedInside = dataFlow.CapturedInside.Select(s => s.Name).ToArray(),
                capturedOutside = dataFlow.CapturedOutside.Select(s => s.Name).ToArray(),
                unsafeAddressTaken = dataFlow.UnsafeAddressTaken.Select(s => s.Name).ToArray()
            },
            suggestedNextTools: new[]
            {
                "analyze_control_flow to analyze branching in the same region",
                "get_diagnostics to check for related warnings"
            }
        );
    }
}
