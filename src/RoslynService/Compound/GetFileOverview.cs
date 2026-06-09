using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> GetFileOverviewAsync(string filePath)
    {
        EnsureSolutionLoaded();

        var document = await GetDocumentAsync(filePath);
        if (document == null)
        {
            return CreateErrorResponse(
                ErrorCodes.FileNotInSolution,
                $"File '{filePath}' not found in solution"
            );
        }

        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxTree = await document.GetSyntaxTreeAsync();

        if (semanticModel == null || syntaxTree == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not analyze file"
            );
        }

        var root = await syntaxTree.GetRootAsync();

        var diagnostics = semanticModel.GetDiagnostics()
            .GroupBy(d => d.Severity)
            .ToDictionary(
                g => g.Key.ToString(),
                g => g.Count()
            );

        var strategy = SyntaxStrategy(document);

        var typeDeclarationList = strategy.FindTypeDeclarations(root)
            .Select(n =>
            {
                var (name, kind, memberCount) = strategy.ExtractTypeInfo(n, semanticModel);
                return new
                {
                    name,
                    kind,
                    line = n.GetLocation().GetLineSpan().StartLinePosition.Line,
                    memberCount
                };
            })
            .ToList();

        var usings = strategy.ExtractImports(root).ToList();
        var namespaceName = strategy.ExtractNamespace(root);

        return CreateSuccessResponse(
            data: new
            {
                filePath = FormatPath(document.FilePath),
                projectName = document.Project.Name,
                @namespace = namespaceName,
                diagnosticSummary = diagnostics,
                usingCount = usings.Count,
                typeDeclarations = typeDeclarationList,
                lineCount = (await syntaxTree.GetTextAsync()).Lines.Count
            },
            suggestedNextTools: new[]
            {
                diagnostics.ContainsKey("Error") ? "get_diagnostics for detailed error info" : null,
                typeDeclarationList.Count > 0 ? $"get_type_members for {typeDeclarationList[0].name} to see members" : null
            }.Where(s => s != null).ToArray()!
        );
    }
}
