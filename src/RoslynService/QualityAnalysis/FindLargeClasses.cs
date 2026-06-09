using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> FindLargeClassesAsync(
        int memberCountThreshold = 20,
        int lineCountThreshold = 500,
        string? projectFilter = null)
    {
        EnsureSolutionLoaded();

        var projects = projectFilter != null
            ? _solution!.Projects.Where(p => p.Name.Equals(projectFilter, StringComparison.OrdinalIgnoreCase))
            : _solution!.Projects;

        var typed = new List<(INamedTypeSymbol type, int memberCount, int lineCount)>();

        foreach (var project in projects)
        {
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

                if (memberCount >= memberCountThreshold || lineCount >= lineCountThreshold)
                    typed.Add((type, memberCount, lineCount));
            }
        }

        typed.Sort((a, b) => b.memberCount.CompareTo(a.memberCount));

        var results = typed.Select(t => (object)new
        {
            typeName = t.type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            fullName = t.type.ToDisplayString(),
            kind = t.type.TypeKind.ToString(),
            t.memberCount,
            t.lineCount,
            isPartial = t.type.Locations.Count(l => l.IsInSource) > 1,
            location = GetSymbolLocation(t.type),
            project = t.type.ContainingAssembly?.Name
        }).ToList();

        return CreateSuccessResponse(
            data: new { memberCountThreshold, lineCountThreshold, classes = results },
            suggestedNextTools: new[]
            {
                "get_type_overview for details on a specific class",
                "get_complexity_metrics for detailed method-level metrics"
            },
            totalCount: results.Count,
            returnedCount: results.Count
        );
    }
}
