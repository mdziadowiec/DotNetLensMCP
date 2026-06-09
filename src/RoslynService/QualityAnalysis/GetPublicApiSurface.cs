using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> GetPublicApiSurfaceAsync(
        string? projectFilter = null,
        bool includeInternal = false)
    {
        EnsureSolutionLoaded();

        var targetProjects = projectFilter != null
            ? _solution!.Projects.Where(p => p.Name.Equals(projectFilter, StringComparison.OrdinalIgnoreCase))
            : _solution!.Projects;

        var projectResults = new List<object>();
        var totalTypeCount = 0;

        foreach (var project in targetProjects)
        {
            var compilation = await GetProjectCompilationAsync(project);
            if (compilation == null) continue;

            var namespaceGroups = new Dictionary<string, List<object>>();

            foreach (var type in GetAllNamedTypes(compilation))
            {
                if (type.IsAnonymousType || type.IsImplicitlyDeclared) continue;
                if (!type.Locations.Any(l => l.IsInSource)) continue;
                if (!IsPublicApiAccessibility(type.DeclaredAccessibility, includeInternal)) continue;

                var nsName = type.ContainingNamespace?.IsGlobalNamespace == true
                    ? "(global)"
                    : type.ContainingNamespace?.ToDisplayString() ?? "(global)";

                if (!namespaceGroups.TryGetValue(nsName, out var nsTypes))
                {
                    nsTypes = [];
                    namespaceGroups[nsName] = nsTypes;
                }

                var members = type.GetMembers()
                    .Where(m => !m.IsImplicitlyDeclared
                        && m.Kind != SymbolKind.NamedType
                        && IsPublicApiAccessibility(m.DeclaredAccessibility, includeInternal))
                    .Select(m => (object)new
                    {
                        name = m.Name,
                        kind = m.Kind.ToString(),
                        signature = m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        isStatic = m.IsStatic,
                        isAbstract = m.IsAbstract,
                        location = GetSymbolLocation(m)
                    })
                    .ToList();

                nsTypes.Add(new
                {
                    typeName = type.Name,
                    fullName = type.ToDisplayString(),
                    kind = type.TypeKind.ToString(),
                    memberCount = members.Count,
                    members,
                    location = GetSymbolLocation(type)
                });
                totalTypeCount++;
            }

            projectResults.Add(new
            {
                project = project.Name,
                namespaces = namespaceGroups
                    .OrderBy(kv => kv.Key)
                    .Select(kv => new { @namespace = kv.Key, types = kv.Value })
                    .Cast<object>()
                    .ToList()
            });
        }

        return CreateSuccessResponse(
            data: new { includeInternal, projects = projectResults },
            suggestedNextTools: new[] { "get_type_overview for a specific type's details" },
            totalCount: totalTypeCount,
            returnedCount: totalTypeCount
        );
    }

    private static bool IsPublicApiAccessibility(Accessibility accessibility, bool includeInternal) =>
        accessibility is Accessibility.Public
            or Accessibility.Protected
            or Accessibility.ProtectedAndInternal
            or Accessibility.ProtectedOrInternal
        || (includeInternal && accessibility == Accessibility.Internal);
}
