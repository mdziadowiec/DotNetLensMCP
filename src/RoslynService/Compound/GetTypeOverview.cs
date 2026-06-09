using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Gets a comprehensive overview of a type in a single call.
    /// Combines type info, base types summary, member counts, and Godot attributes.
    /// </summary>
    public async Task<object> GetTypeOverviewAsync(string typeName)
    {
        EnsureSolutionLoaded();

        if (string.IsNullOrWhiteSpace(typeName))
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidParameter,
                "typeName is required"
            );
        }

        var type = await FindTypeByNameAsync(typeName);
        if (type == null)
        {
            return CreateErrorResponse(
                ErrorCodes.TypeNotFound,
                $"Type '{typeName}' not found",
                hint: "Try using fully-qualified name or use search_symbols"
            );
        }

        // Base type chain (first 3)
        var baseTypes = new List<string>();
        var currentBase = type.BaseType;
        var count = 0;
        while (currentBase != null && currentBase.SpecialType != SpecialType.System_Object && count < 3)
        {
            baseTypes.Add(currentBase.ToDisplayString());
            currentBase = currentBase.BaseType;
            count++;
        }

        // Member counts
        var members = type.GetMembers().Where(m => !m.IsImplicitlyDeclared).ToList();
        var memberSummary = new
        {
            methods = members.OfType<IMethodSymbol>().Count(m => m.MethodKind == MethodKind.Ordinary),
            properties = members.OfType<IPropertySymbol>().Count(),
            fields = members.OfType<IFieldSymbol>().Count(),
            events = members.OfType<IEventSymbol>().Count(),
            constructors = members.OfType<IMethodSymbol>().Count(m => m.MethodKind == MethodKind.Constructor)
        };

        // Godot attributes check
        var godotAttributes = new
        {
            hasExport = members.Any(m => HasAttribute(m, "Export")),
            hasSignal = members.Any(m => HasAttribute(m, "Signal")),
            hasTool = HasAttribute(type, "Tool"),
            hasGlobalClass = HasAttribute(type, "GlobalClass")
        };

        return CreateSuccessResponse(
            data: new
            {
                typeName = type.ToDisplayString(),
                simpleName = type.Name,
                typeKind = type.TypeKind.ToString(),
                @namespace = type.ContainingNamespace?.ToDisplayString(),
                isAbstract = type.IsAbstract,
                isSealed = type.IsSealed,
                isStatic = type.IsStatic,
                baseTypes,
                baseTypeCount = type.BaseType != null ? baseTypes.Count + (currentBase != null ? 1 : 0) : 0,
                interfaceCount = type.AllInterfaces.Length,
                memberSummary,
                godotAttributes,
                location = GetSymbolLocation(type)
            },
            suggestedNextTools: new[]
            {
                $"get_type_members for detailed member list",
                $"get_base_types for full inheritance chain",
                $"get_derived_types to find classes inheriting from {type.Name}",
                godotAttributes.hasExport ? "get_attributes('Export') to see exported properties" : null
            }.Where(s => s != null).ToArray()!
        );
    }

}
