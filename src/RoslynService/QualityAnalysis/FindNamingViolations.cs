using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> FindNamingViolationsAsync(
        string? projectFilter = null,
        int maxResults = 100)
    {
        EnsureSolutionLoaded();

        var violations = new List<object>();

        var projects = projectFilter != null
            ? _solution!.Projects.Where(p => p.Name.Equals(projectFilter, StringComparison.OrdinalIgnoreCase))
            : _solution!.Projects;

        foreach (var project in projects)
        {
            if (violations.Count >= maxResults) break;

            // Naming conventions are C#-idiomatic; skip VB projects
            if (project.Language != LanguageNames.CSharp) continue;

            var compilation = await GetProjectCompilationAsync(project);
            if (compilation == null) continue;

            foreach (var type in GetAllNamedTypes(compilation))
            {
                if (violations.Count >= maxResults) break;
                if (type.IsAnonymousType || type.IsImplicitlyDeclared) continue;
                if (!type.Locations.Any(l => l.IsInSource)) continue;

                CheckTypeNaming(type, violations, maxResults);

                foreach (var member in type.GetMembers().Where(m => !m.IsImplicitlyDeclared && m.Kind != SymbolKind.NamedType))
                {
                    if (violations.Count >= maxResults) break;
                    CheckMemberNaming(member, violations, maxResults);
                }
            }
        }

        var bySeverity = violations
            .GroupBy(v => ((dynamic)v).violationType as string ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count());

        return CreateSuccessResponse(
            data: new { summary = bySeverity, violations },
            suggestedNextTools: new[]
            {
                "rename_symbol to fix a naming violation",
                "get_diagnostics for analyzer-detected naming issues"
            },
            totalCount: violations.Count,
            returnedCount: violations.Count
        );
    }

    private void CheckTypeNaming(INamedTypeSymbol type, List<object> violations, int maxResults)
    {
        if (violations.Count >= maxResults) return;

        if (type.TypeKind == TypeKind.Interface)
        {
            if (type.Name.Length < 2 || type.Name[0] != 'I' || !char.IsUpper(type.Name[1]))
                AddNamingViolation(violations, "interface_name",
                    $"Interface '{type.Name}' should start with 'I' followed by an uppercase letter",
                    type, null);
        }
        else if (type.Name.Length > 0 && !char.IsUpper(type.Name[0]))
        {
            AddNamingViolation(violations, "type_name",
                $"Type '{type.Name}' should start with uppercase (PascalCase)",
                type, null);
        }
    }

    private void CheckMemberNaming(ISymbol member, List<object> violations, int maxResults)
    {
        if (violations.Count >= maxResults) return;

        switch (member)
        {
            case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary
                                        || method.MethodKind == MethodKind.ExplicitInterfaceImplementation:
                if (method.Name.Length > 0 && !char.IsUpper(method.Name[0]) && method.Name[0] != '_')
                    AddNamingViolation(violations, "method_name",
                        $"Method '{method.Name}' should start with uppercase (PascalCase)",
                        member, member.ContainingType?.ToDisplayString());

                foreach (var p in method.Parameters)
                {
                    if (violations.Count >= maxResults) return;
                    if (p.Name.Length > 0 && !char.IsLower(p.Name[0]) && p.Name[0] != '_')
                        AddNamingViolation(violations, "parameter_name",
                            $"Parameter '{p.Name}' should start with lowercase (camelCase)",
                            p, member.ContainingType?.ToDisplayString());
                }
                break;

            case IPropertySymbol property:
                if (property.Name.Length > 0 && !char.IsUpper(property.Name[0]))
                    AddNamingViolation(violations, "property_name",
                        $"Property '{property.Name}' should start with uppercase (PascalCase)",
                        member, member.ContainingType?.ToDisplayString());
                break;

            case IFieldSymbol field when !field.IsConst:
                var isPrivateOrProtected = field.DeclaredAccessibility
                    is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal;
                if (isPrivateOrProtected && !field.Name.StartsWith("_", StringComparison.Ordinal)
                    && field.Name.Length > 0 && !char.IsUpper(field.Name[0]))
                    AddNamingViolation(violations, "field_name",
                        $"Private/protected field '{field.Name}' should start with '_'",
                        member, member.ContainingType?.ToDisplayString());
                break;
        }
    }

    private void AddNamingViolation(
        List<object> violations,
        string violationType,
        string description,
        ISymbol symbol,
        string? containingType)
    {
        violations.Add(new
        {
            violationType,
            description,
            symbolName = symbol.Name,
            containingType = containingType ?? symbol.ContainingType?.ToDisplayString(),
            location = GetSymbolLocation(symbol)
        });
    }
}
