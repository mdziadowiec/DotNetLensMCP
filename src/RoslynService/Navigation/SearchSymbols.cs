using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> SearchSymbolsAsync(string query, string? kind, int maxResults, string? namespaceFilter = null, int offset = 0)
    {
        EnsureSolutionLoaded();

        var allResults = new List<object>();

        // Check if query contains glob patterns
        bool isGlobPattern = query.Contains('*') || query.Contains('?');

        foreach (var project in _solution!.Projects)
        {
            var compilation = await GetProjectCompilationAsync(project);
            if (compilation == null) continue;

            var symbols = compilation.GetSymbolsWithName(
                name => isGlobPattern ? MatchesGlobPattern(name, query) : name.Contains(query, StringComparison.OrdinalIgnoreCase),
                SymbolFilter.All);

            foreach (var symbol in symbols)
            {
                // Filter by kind
                if (!string.IsNullOrEmpty(kind))
                {
                    bool matches = false;

                    // For type symbols (Class, Interface, Struct, Enum), check TypeKind
                    if (symbol is INamedTypeSymbol namedType)
                    {
                        matches = namedType.TypeKind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        // For other symbols (Method, Property, Field, etc.), check SymbolKind
                        matches = symbol.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase);
                    }

                    if (!matches)
                        continue;
                }

                // Filter by namespace
                if (!string.IsNullOrEmpty(namespaceFilter))
                {
                    var symbolNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? "";
                    bool namespaceMatches = MatchesGlobPattern(symbolNamespace, namespaceFilter);

                    if (!namespaceMatches)
                        continue;
                }

                var location = symbol.Locations.FirstOrDefault(loc => loc.IsInSource);
                if (location == null) continue;

                var lineSpan = location.GetLineSpan();

                allResults.Add(new
                {
                    name = symbol.Name,
                    fullyQualifiedName = symbol.ToDisplayString(),
                    kind = symbol.Kind.ToString(),
                    containingType = symbol.ContainingType?.ToDisplayString(),
                    containingNamespace = symbol.ContainingNamespace?.ToDisplayString(),
                    location = new
                    {
                        filePath = FormatPath(lineSpan.Path),
                        line = lineSpan.StartLinePosition.Line,
                        column = lineSpan.StartLinePosition.Character
                    }
                });

                // Continue collecting until we have offset + maxResults (to handle pagination)
                if (allResults.Count >= offset + maxResults + 100) // +100 buffer for accurate totalCount estimation
                    break;
            }

            if (allResults.Count >= offset + maxResults + 100)
                break;
        }

        // Apply pagination
        var totalCount = allResults.Count;
        var paginatedResults = allResults.Skip(offset).Take(maxResults).ToList();
        var hasMore = offset + paginatedResults.Count < totalCount;

        return CreateSuccessResponse(
            data: new
            {
                query,
                offset,
                hasMore,
                results = paginatedResults,
                pagination = new
                {
                    nextOffset = hasMore ? offset + paginatedResults.Count : (int?)null
                }
            },
            suggestedNextTools: new[]
            {
                "get_symbol_info at a result location for detailed info",
                "get_type_members for type results",
                "find_references to see all usages"
            },
            totalCount: totalCount,
            returnedCount: paginatedResults.Count
        );
    }
}
