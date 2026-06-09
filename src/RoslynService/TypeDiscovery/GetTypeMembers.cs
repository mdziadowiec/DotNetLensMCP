using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Gets all members of a type by name (methods, properties, fields, events).
    /// Supports fully-qualified names, simple names, and partial matches.
    /// </summary>
    public async Task<object> GetTypeMembersAsync(
        string typeName,
        bool includeInherited = false,
        string? memberKind = null,
        string verbosity = "compact",
        int maxResults = 100)
    {
        EnsureSolutionLoaded();

        if (string.IsNullOrWhiteSpace(typeName))
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidParameter,
                "typeName is required",
                hint: "Provide a type name like 'MyClass' or 'MyNamespace.MyService'",
                context: new { parameter = "typeName" }
            );
        }

        var type = await FindTypeByNameAsync(typeName);
        if (type == null)
        {
            return CreateErrorResponse(
                ErrorCodes.TypeNotFound,
                $"Type '{typeName}' not found",
                hint: "Try using fully-qualified name (e.g., 'MyNamespace.MyClass') or check spelling. Use search_symbols to find available types.",
                context: new { typeName }
            );
        }

        // Collect members
        var allMembers = new List<ISymbol>();

        if (includeInherited)
        {
            // Walk up the inheritance chain
            var currentType = type;
            while (currentType != null && currentType.SpecialType != SpecialType.System_Object)
            {
                allMembers.AddRange(currentType.GetMembers().Where(m => !m.IsImplicitlyDeclared));
                currentType = currentType.BaseType;
            }
        }
        else
        {
            allMembers.AddRange(type.GetMembers().Where(m => !m.IsImplicitlyDeclared));
        }

        // Filter by member kind if specified
        if (!string.IsNullOrEmpty(memberKind))
        {
            allMembers = allMembers.Where(m =>
            {
                return memberKind.ToLower() switch
                {
                    "method" => m is IMethodSymbol ms && ms.MethodKind == MethodKind.Ordinary,
                    "property" => m is IPropertySymbol,
                    "field" => m is IFieldSymbol,
                    "event" => m is IEventSymbol,
                    _ => true
                };
            }).ToList();
        }

        // Remove duplicates (from inheritance) and limit
        var uniqueMembers = allMembers
            .GroupBy(m => m.Name + m.Kind.ToString())
            .Select(g => g.First())
            .Take(maxResults)
            .ToList();

        var totalCount = allMembers.GroupBy(m => m.Name + m.Kind.ToString()).Count();

        // Format members based on verbosity
        var formattedMembers = uniqueMembers.Select(m => FormatMember(m, verbosity)).ToList();

        // Count by kind
        var countByKind = uniqueMembers
            .GroupBy(m => GetMemberKindString(m))
            .ToDictionary(g => g.Key, g => g.Count());

        return CreateSuccessResponse(
            data: new
            {
                typeName = type.ToDisplayString(),
                typeKind = type.TypeKind.ToString(),
                totalMembers = totalCount,
                memberCounts = countByKind,
                members = formattedMembers
            },
            suggestedNextTools: new[]
            {
                $"get_method_signature to get detailed parameter info for a specific method",
                $"get_base_types for {type.Name} to see inheritance chain",
                $"get_attributes to find [Export] or [Signal] decorated members"
            },
            totalCount: totalCount,
            returnedCount: uniqueMembers.Count,
            verbosity: verbosity
        );
    }
}
