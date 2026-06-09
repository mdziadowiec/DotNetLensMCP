using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Gets full inheritance chain by type name.
    /// </summary>
    public async Task<object> GetBaseTypesAsync(string typeName)
    {
        EnsureSolutionLoaded();

        if (string.IsNullOrWhiteSpace(typeName))
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidParameter,
                "typeName is required",
                hint: "Provide a type name like 'MyClass' or 'MyBaseService'"
            );
        }

        var type = await FindTypeByNameAsync(typeName);
        if (type == null)
        {
            return CreateErrorResponse(
                ErrorCodes.TypeNotFound,
                $"Type '{typeName}' not found",
                hint: "Try using fully-qualified name or use search_symbols to find available types"
            );
        }

        // Walk up the inheritance chain
        var baseTypes = new List<object>();
        var currentBase = type.BaseType;
        while (currentBase != null && currentBase.SpecialType != SpecialType.System_Object)
        {
            baseTypes.Add(new
            {
                name = currentBase.Name,
                fullName = currentBase.ToDisplayString(),
                isAbstract = currentBase.IsAbstract,
                location = GetSymbolLocation(currentBase)
            });
            currentBase = currentBase.BaseType;
        }

        // Collect all interfaces
        var interfaces = type.AllInterfaces
            .Select(i => new
            {
                name = i.Name,
                fullName = i.ToDisplayString()
            })
            .ToList();

        return CreateSuccessResponse(
            data: new
            {
                typeName = type.ToDisplayString(),
                typeKind = type.TypeKind.ToString(),
                isAbstract = type.IsAbstract,
                isSealed = type.IsSealed,
                baseTypes,
                interfaces,
                location = GetSymbolLocation(type)
            },
            suggestedNextTools: new[]
            {
                $"get_type_members to see members of {type.Name}",
                $"get_derived_types to find classes inheriting from {type.Name}",
                baseTypes.Count > 0 ? $"get_type_members for {((dynamic)baseTypes[0]).name} to see inherited members" : null
            }.Where(s => s != null).ToArray()!
        );
    }
}
