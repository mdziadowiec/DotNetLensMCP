using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> FindObsoleteUsageAsync(
        string? projectFilter = null,
        int maxResults = 100)
    {
        EnsureSolutionLoaded();

        var usages = new List<object>();
        var seen = new HashSet<string>();

        var projects = projectFilter != null
            ? _solution!.Projects.Where(p => p.Name.Equals(projectFilter, StringComparison.OrdinalIgnoreCase))
            : _solution!.Projects;

        foreach (var project in projects)
        {
            if (usages.Count >= maxResults) break;

            foreach (var document in project.Documents)
            {
                if (usages.Count >= maxResults) break;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel == null) continue;

                var root = await document.GetSyntaxRootAsync();
                if (root == null) continue;

                var strategy = SyntaxStrategy(document);

                // Check invocations (method calls, including constructor-like calls)
                foreach (var invocation in strategy.FindInvocations(root))
                {
                    if (usages.Count >= maxResults) break;

                    var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                    var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
                    if (symbol == null) continue;

                    var obs = GetObsoleteAttributeInfo(symbol);
                    if (obs == null) continue;

                    var loc = invocation.GetLocation().GetLineSpan();
                    var key = $"{symbol.ToDisplayString()}|{loc.Path}|{loc.StartLinePosition.Line}";
                    if (!seen.Add(key)) continue;

                    usages.Add(new
                    {
                        obsoleteSymbol = symbol.ToDisplayString(),
                        obsoleteKind = symbol.Kind.ToString(),
                        obs.Value.message,
                        isError = obs.Value.isError,
                        filePath = FormatPath(loc.Path),
                        line = loc.StartLinePosition.Line,
                        column = loc.StartLinePosition.Character,
                        project = document.Project.Name
                    });
                }

                // Check member accesses for non-method symbols (properties, fields, events)
                foreach (var access in strategy.FindMemberAccesses(root))
                {
                    if (usages.Count >= maxResults) break;

                    var symbolInfo = semanticModel.GetSymbolInfo(access);
                    var symbol = symbolInfo.Symbol;
                    // Methods are already covered by invocation scan
                    if (symbol == null || symbol.Kind == SymbolKind.Method) continue;

                    var obs = GetObsoleteAttributeInfo(symbol);
                    if (obs == null) continue;

                    var loc = access.GetLocation().GetLineSpan();
                    var key = $"{symbol.ToDisplayString()}|{loc.Path}|{loc.StartLinePosition.Line}";
                    if (!seen.Add(key)) continue;

                    usages.Add(new
                    {
                        obsoleteSymbol = symbol.ToDisplayString(),
                        obsoleteKind = symbol.Kind.ToString(),
                        obs.Value.message,
                        isError = obs.Value.isError,
                        filePath = FormatPath(loc.Path),
                        line = loc.StartLinePosition.Line,
                        column = loc.StartLinePosition.Character,
                        project = document.Project.Name
                    });
                }
            }
        }

        var byError = usages.Count > 0
            ? usages.GroupBy(u => ((dynamic)u).isError)
                .ToDictionary(g => g.Key ? "errors" : "warnings", g => g.Count())
            : new Dictionary<string, int>();

        return CreateSuccessResponse(
            data: new { summary = byError, usages },
            suggestedNextTools: new[]
            {
                "go_to_definition to navigate to obsolete symbol",
                "find_references to find all usages of a specific symbol"
            },
            totalCount: usages.Count,
            returnedCount: usages.Count
        );
    }

    private static (string? message, bool isError)? GetObsoleteAttributeInfo(ISymbol symbol)
    {
        var attr = symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name is "ObsoleteAttribute" or "Obsolete");
        if (attr == null) return null;

        var message = attr.ConstructorArguments.Length > 0
            ? attr.ConstructorArguments[0].Value as string
            : null;
        var isError = attr.ConstructorArguments.Length > 1 && attr.ConstructorArguments[1].Value is true;

        return (message, isError);
    }
}
