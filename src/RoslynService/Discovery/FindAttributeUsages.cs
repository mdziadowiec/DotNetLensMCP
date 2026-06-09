using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> FindAttributeUsagesAsync(string attributeName, string? projectName = null, int maxResults = 100)
    {
        EnsureSolutionLoaded();

        var results = new List<object>();
        var projects = projectName != null
            ? _solution!.Projects.Where(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase))
            : _solution!.Projects;

        foreach (var project in projects)
        {
            var compilation = await GetProjectCompilationAsync(project);
            if (compilation == null) continue;

            foreach (var type in GetAllNamedTypes(compilation))
            {
                // Check the type itself
                foreach (var attr in type.GetAttributes())
                {
                    if (MatchesAttribute(attr, attributeName))
                    {
                        results.Add(new
                        {
                            symbolName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                            symbolKind = "Type",
                            attributeName = attr.AttributeClass?.Name,
                            arguments = attr.ConstructorArguments.Select(a => a.ToCSharpString()).ToList(),
                            namedArguments = attr.NamedArguments.ToDictionary(a => a.Key, a => a.Value.ToCSharpString()),
                            location = GetSymbolLocation(type)
                        });
                        if (results.Count >= maxResults) break;
                    }
                }

                if (results.Count >= maxResults) break;

                // Check members
                foreach (var member in type.GetMembers())
                {
                    foreach (var attr in member.GetAttributes())
                    {
                        if (MatchesAttribute(attr, attributeName))
                        {
                            results.Add(new
                            {
                                symbolName = $"{type.Name}.{member.Name}",
                                symbolKind = member.Kind.ToString(),
                                attributeName = attr.AttributeClass?.Name,
                                arguments = attr.ConstructorArguments.Select(a => a.ToCSharpString()).ToList(),
                                namedArguments = attr.NamedArguments.ToDictionary(a => a.Key, a => a.Value.ToCSharpString()),
                                location = GetSymbolLocation(member)
                            });
                            if (results.Count >= maxResults) break;
                        }
                    }
                    if (results.Count >= maxResults) break;
                }
                if (results.Count >= maxResults) break;
            }
            if (results.Count >= maxResults) break;
        }

        return CreateSuccessResponse(
            data: new { attributeFilter = attributeName, usages = results },
            suggestedNextTools: new[] { "get_type_overview", "find_references" },
            totalCount: results.Count,
            returnedCount: results.Count
        );
    }

    private static bool MatchesAttribute(AttributeData attr, string attributeName)
    {
        var className = attr.AttributeClass?.Name;
        if (className == null) return false;

        return className.Equals(attributeName, StringComparison.OrdinalIgnoreCase)
            || className.Equals($"{attributeName}Attribute", StringComparison.OrdinalIgnoreCase)
            || className.Replace("Attribute", "").Equals(attributeName, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNamedTypes(Compilation compilation)
    {
        var stack = new Stack<INamespaceSymbol>();
        stack.Push(compilation.GlobalNamespace);

        while (stack.Count > 0)
        {
            var ns = stack.Pop();
            foreach (var type in ns.GetTypeMembers())
            {
                yield return type;
                foreach (var nested in GetNestedTypes(type))
                    yield return nested;
            }
            foreach (var childNs in ns.GetNamespaceMembers())
                stack.Push(childNs);
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var deepNested in GetNestedTypes(nested))
                yield return deepNested;
        }
    }
}
