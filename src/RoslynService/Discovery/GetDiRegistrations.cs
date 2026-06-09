using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> GetDiRegistrationsAsync(string? projectName = null)
    {
        EnsureSolutionLoaded();

        var registrations = new List<object>();
        var diMethodPatterns = new[]
        {
            "AddScoped", "AddTransient", "AddSingleton", "AddHostedService",
            "TryAddScoped", "TryAddTransient", "TryAddSingleton",
            "AddKeyedScoped", "AddKeyedTransient", "AddKeyedSingleton"
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
                    var methodName = strategy.GetInvocationMethodName(invNode);
                    if (methodName == null || !diMethodPatterns.Any(p => methodName.StartsWith(p))) continue;

                    var symbolInfo = semanticModel.GetSymbolInfo(invNode);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) continue;

                    var containingType = methodSymbol.ContainingType?.ToDisplayString();
                    if (containingType == null || !containingType.Contains("ServiceCollection")) continue;

                    var lifetime = methodName.Contains("Scoped") ? "Scoped"
                        : methodName.Contains("Transient") ? "Transient"
                        : methodName.Contains("Singleton") ? "Singleton"
                        : methodName.Contains("Hosted") ? "Singleton"
                        : "Unknown";

                    var typeArgs = methodSymbol.TypeArguments;
                    var serviceType = typeArgs.Length > 0 ? typeArgs[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) : null;
                    var implementationType = typeArgs.Length > 1 ? typeArgs[1].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) : serviceType;

                    var lineSpan = invNode.GetLocation().GetLineSpan();
                    registrations.Add(new
                    {
                        lifetime,
                        serviceType,
                        implementationType,
                        method = methodName,
                        location = new
                        {
                            filePath = FormatPath(lineSpan.Path),
                            line = lineSpan.StartLinePosition.Line,
                            column = lineSpan.StartLinePosition.Character
                        }
                    });
                }
            }
        }

        return CreateSuccessResponse(
            data: new { registrations },
            suggestedNextTools: new[] { "find_references", "get_type_overview" },
            totalCount: registrations.Count,
            returnedCount: registrations.Count
        );
    }
}
