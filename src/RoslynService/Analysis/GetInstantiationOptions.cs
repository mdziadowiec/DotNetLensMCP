using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Gets all ways to instantiate a type.
    /// </summary>
    public async Task<object> GetInstantiationOptionsAsync(string typeName)
    {
        EnsureSolutionLoaded();

        var type = await FindTypeByNameAsync(typeName);
        if (type == null)
        {
            return CreateErrorResponse(
                ErrorCodes.TypeNotFound,
                $"Type not found: {typeName}",
                hint: "Use fully qualified name like Namespace.ClassName",
                context: new { typeName }
            );
        }

        // Get constructors
        var constructors = type.Constructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public ||
                        c.DeclaredAccessibility == Accessibility.Protected)
            .Select(c => new
            {
                signature = c.ToDisplayString(),
                accessibility = c.DeclaredAccessibility.ToString(),
                parameters = c.Parameters.Select(p => new
                {
                    name = p.Name,
                    type = p.Type.ToDisplayString(),
                    isOptional = p.IsOptional,
                    defaultValue = p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null
                }).ToList(),
                isObsolete = c.GetAttributes().Any(a => a.AttributeClass?.Name == "ObsoleteAttribute")
            })
            .ToList();

        // Find static factory methods
        var factoryMethods = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.IsStatic &&
                       m.DeclaredAccessibility == Accessibility.Public &&
                       SymbolEqualityComparer.Default.Equals(m.ReturnType, type))
            .Select(m => new
            {
                name = m.Name,
                signature = m.ToDisplayString(),
                parameters = m.Parameters.Select(p => new
                {
                    name = p.Name,
                    type = p.Type.ToDisplayString()
                }).ToList()
            })
            .ToList();

        // Find factory methods in other types that return this type
        var externalFactories = new List<object>();
        foreach (var project in _solution!.Projects)
        {
            var compilation = await GetProjectCompilationAsync(project);
            if (compilation == null) continue;

            var allTypes = compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type).OfType<INamedTypeSymbol>();
            foreach (var t in allTypes)
            {
                if (SymbolEqualityComparer.Default.Equals(t, type)) continue;

                var factories = t.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(m => m.IsStatic &&
                               m.DeclaredAccessibility == Accessibility.Public &&
                               (m.Name.StartsWith("Create") || m.Name.StartsWith("Build") || m.Name.StartsWith("New")) &&
                               SymbolEqualityComparer.Default.Equals(m.ReturnType, type))
                    .Take(5); // Limit to avoid too many

                foreach (var f in factories)
                {
                    externalFactories.Add(new
                    {
                        containingType = t.ToDisplayString(),
                        name = f.Name,
                        signature = f.ToDisplayString()
                    });
                }
            }

            if (externalFactories.Count >= 10) break; // Limit total
        }

        // Check for common patterns
        var implementsIDisposable = type.AllInterfaces.Any(i => i.Name == "IDisposable");
        var hasBuilder = _solution.Projects
            .SelectMany(p => p.Documents)
            .Any(d => d.Name.Contains($"{type.Name}Builder"));

        string? hint = null;
        if (implementsIDisposable)
            hint = "Type implements IDisposable - consider using 'using' statement";
        else if (type.IsAbstract)
            hint = "Type is abstract - cannot instantiate directly, use derived type";
        else if (type.TypeKind == TypeKind.Interface)
            hint = "Type is an interface - cannot instantiate directly";

        return CreateSuccessResponse(
            data: new
            {
                typeName = type.ToDisplayString(),
                typeKind = type.TypeKind.ToString(),
                isAbstract = type.IsAbstract,
                isStatic = type.IsStatic,
                constructors,
                factoryMethods,
                externalFactories,
                implementsIDisposable,
                hasBuilder,
                hint
            },
            suggestedNextTools: new[]
            {
                "get_method_signature for constructor parameter details",
                "get_type_members to see what methods are available after creation"
            },
            totalCount: constructors.Count + factoryMethods.Count + externalFactories.Count,
            returnedCount: constructors.Count + factoryMethods.Count + externalFactories.Count
        );
    }
}
