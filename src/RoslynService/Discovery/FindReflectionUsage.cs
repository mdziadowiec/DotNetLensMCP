using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> FindReflectionUsageAsync(string? projectName = null, int maxResults = 100)
    {
        EnsureSolutionLoaded();

        var usages = new List<object>();
        var reflectionApis = new[]
        {
            "GetType", "GetMethod", "GetProperty", "GetField", "GetEvent",
            "GetMember", "GetMembers", "GetMethods", "GetProperties", "GetFields",
            "CreateInstance", "Invoke", "GetValue", "SetValue", "DynamicInvoke",
            "MakeGenericType", "MakeGenericMethod"
        };

        var projects = projectName != null
            ? _solution!.Projects.Where(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase))
            : _solution!.Projects;

        foreach (var project in projects)
        {
            var compilation = await GetProjectCompilationAsync(project);
            if (compilation == null) continue;

            foreach (var document in project.Documents)
            {
                var syntaxTree = await document.GetSyntaxTreeAsync();
                if (syntaxTree == null) continue;

                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = await syntaxTree.GetRootAsync();
                var strategy = SyntaxStrategy(document);

                foreach (var invNode in strategy.FindInvocations(root))
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(invNode);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) continue;

                    var containingNamespace = methodSymbol.ContainingType?.ContainingNamespace?.ToDisplayString();
                    if (containingNamespace == null) continue;

                    var isReflection = containingNamespace.StartsWith("System.Reflection")
                        || (containingNamespace == "System" && methodSymbol.ContainingType?.Name == "Type" && reflectionApis.Contains(methodSymbol.Name))
                        || (containingNamespace == "System" && methodSymbol.ContainingType?.Name == "Activator")
                        || (containingNamespace == "System" && methodSymbol.ContainingType?.Name == "Delegate" && methodSymbol.Name == "DynamicInvoke");

                    if (!isReflection) continue;

                    var lineSpan = invNode.GetLocation().GetLineSpan();
                    usages.Add(new
                    {
                        reflectionApi = $"{methodSymbol.ContainingType?.Name}.{methodSymbol.Name}",
                        context = invNode.ToString().Length > 200 ? invNode.ToString()[..200] + "..." : invNode.ToString(),
                        location = new
                        {
                            filePath = FormatPath(lineSpan.Path),
                            line = lineSpan.StartLinePosition.Line,
                            column = lineSpan.StartLinePosition.Character
                        }
                    });
                    if (usages.Count >= maxResults) break;
                }
                if (usages.Count >= maxResults) break;
            }
            if (usages.Count >= maxResults) break;
        }

        return CreateSuccessResponse(
            data: new { usages },
            suggestedNextTools: new[] { "find_references", "get_symbol_info" },
            totalCount: usages.Count,
            returnedCount: usages.Count
        );
    }
}
