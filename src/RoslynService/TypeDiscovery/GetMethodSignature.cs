using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Gets detailed method signature including parameters, return type, and modifiers.
    /// </summary>
    public async Task<object> GetMethodSignatureAsync(
        string typeName,
        string methodName,
        int? overloadIndex = null)
    {
        EnsureSolutionLoaded();

        if (string.IsNullOrWhiteSpace(typeName))
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidParameter,
                "typeName is required",
                hint: "Provide a type name like 'MyClass' or 'MyNamespace.MyService'"
            );
        }

        if (string.IsNullOrWhiteSpace(methodName))
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidParameter,
                "methodName is required",
                hint: "Provide a method name like 'ProcessData' or 'Calculate'"
            );
        }

        var type = await FindTypeByNameAsync(typeName);
        if (type == null)
        {
            return CreateErrorResponse(
                ErrorCodes.TypeNotFound,
                $"Type '{typeName}' not found",
                hint: "Try using fully-qualified name or use get_type_members to list available types"
            );
        }

        // Find all methods with this name (including inherited)
        var methods = new List<IMethodSymbol>();
        var currentType = type;
        while (currentType != null)
        {
            methods.AddRange(currentType.GetMembers(methodName).OfType<IMethodSymbol>());
            currentType = currentType.BaseType;
        }

        if (methods.Count == 0)
        {
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                $"Method '{methodName}' not found on type '{type.Name}'",
                hint: $"Use get_type_members for {type.Name} with memberKind='Method' to see available methods",
                context: new { typeName = type.ToDisplayString(), methodName }
            );
        }

        // Select the specific overload or first one
        var method = overloadIndex.HasValue && overloadIndex.Value < methods.Count
            ? methods[overloadIndex.Value]
            : methods[0];

        var parameters = method.Parameters.Select(p => new
        {
            name = p.Name,
            type = p.Type.ToDisplayString(),
            isOptional = p.IsOptional,
            hasDefaultValue = p.HasExplicitDefaultValue,
            defaultValue = p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null,
            isParams = p.IsParams,
            refKind = p.RefKind.ToString(),
            isNullable = p.NullableAnnotation == NullableAnnotation.Annotated
        }).ToList();

        var typeParameters = method.TypeParameters.Select(tp => new
        {
            name = tp.Name,
            constraints = tp.ConstraintTypes.Select(c => c.ToDisplayString()).ToArray()
        }).ToList();

        return CreateSuccessResponse(
            data: new
            {
                name = method.Name,
                containingType = method.ContainingType.ToDisplayString(),
                fullSignature = method.ToDisplayString(),
                returnType = method.ReturnType.ToDisplayString(),
                isAsync = method.IsAsync,
                isStatic = method.IsStatic,
                isVirtual = method.IsVirtual,
                isOverride = method.IsOverride,
                isAbstract = method.IsAbstract,
                isExtensionMethod = method.IsExtensionMethod,
                accessibility = method.DeclaredAccessibility.ToString(),
                parameters,
                typeParameters,
                overloadCount = methods.Count,
                selectedOverload = overloadIndex ?? 0,
                documentation = method.GetDocumentationCommentXml(),
                location = GetSymbolLocation(method)
            },
            suggestedNextTools: new[]
            {
                $"find_callers to see where {method.Name} is called",
                $"get_type_members for {type.Name} to see other methods",
                methods.Count > 1 ? $"get_method_signature with overloadIndex=0..{methods.Count - 1} to see other overloads" : null
            }.Where(s => s != null).ToArray()!
        );
    }
}
