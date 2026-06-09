using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> FindGodObjectsAsync(
        int memberThreshold = 20,
        int lineThreshold = 500,
        int outgoingNamespaceThreshold = 5,
        string? projectFilter = null)
    {
        EnsureSolutionLoaded();

        var projects = projectFilter != null
            ? _solution!.Projects.Where(p => p.Name.Equals(projectFilter, StringComparison.OrdinalIgnoreCase))
            : _solution!.Projects;

        var candidates = new List<(INamedTypeSymbol type, int memberCount, int lineCount)>();

        // Phase 1: collect large types (same logic as FindLargeClasses)
        foreach (var project in projects)
        {
            // Only C# projects — VB invocation syntax differs and this is a C#-idiomatic heuristic
            if (project.Language != LanguageNames.CSharp) continue;

            var compilation = await GetProjectCompilationAsync(project);
            if (compilation == null) continue;

            foreach (var type in GetAllNamedTypes(compilation))
            {
                if (type.IsImplicitClass || type.IsAnonymousType || type.IsImplicitlyDeclared) continue;
                if (!type.Locations.Any(l => l.IsInSource)) continue;

                var memberCount = type.GetMembers()
                    .Count(m => !m.IsImplicitlyDeclared && m.Kind != SymbolKind.NamedType);
                var lineCount = type.Locations
                    .Where(l => l.IsInSource)
                    .Sum(l =>
                    {
                        var span = l.GetLineSpan();
                        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
                    });

                if (memberCount >= memberThreshold || lineCount >= lineThreshold)
                    candidates.Add((type, memberCount, lineCount));
            }
        }

        // Phase 2: compute outgoing namespace coupling via using directives.
        // Syntactic analysis avoids dependency on perfect symbol binding, which can
        // degrade when third-party packages affect workspace loading.
        var godObjects = new List<object>();

        foreach (var (type, memberCount, lineCount) in candidates)
        {
            var ownNamespace = type.ContainingNamespace?.ToDisplayString() ?? "";
            var outgoingNamespaces = new HashSet<string>(StringComparer.Ordinal);

            foreach (var location in type.Locations.Where(l => l.IsInSource))
            {
                if (location.SourceTree == null) continue;

                var root = await location.SourceTree.GetRootAsync();
                var isVb = location.SourceTree.FilePath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase);
                var strategy = SyntaxStrategy(isVb);

                foreach (var ns in strategy.ExtractImports(root))
                {
                    if (!string.IsNullOrEmpty(ns) && ns != ownNamespace && ns != "<global namespace>")
                        outgoingNamespaces.Add(ns);
                }
            }

            if (outgoingNamespaces.Count >= outgoingNamespaceThreshold)
            {
                godObjects.Add(new
                {
                    typeName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    fullName = type.ToDisplayString(),
                    kind = type.TypeKind.ToString(),
                    memberCount,
                    lineCount,
                    outgoingNamespaceCount = outgoingNamespaces.Count,
                    outgoingNamespaces = outgoingNamespaces.OrderBy(n => n).ToList(),
                    location = GetSymbolLocation(type),
                    project = type.ContainingAssembly?.Name
                });
            }
        }

        godObjects.Sort((a, b) =>
            ((dynamic)b).outgoingNamespaceCount - (int)((dynamic)a).outgoingNamespaceCount);

        return CreateSuccessResponse(
            data: new
            {
                thresholds = new { memberThreshold, lineThreshold, outgoingNamespaceThreshold },
                godObjects
            },
            suggestedNextTools: new[]
            {
                "get_type_overview for a detailed breakdown of a god object",
                "find_large_classes to find large classes without coupling filter"
            },
            totalCount: godObjects.Count,
            returnedCount: godObjects.Count
        );
    }
}
