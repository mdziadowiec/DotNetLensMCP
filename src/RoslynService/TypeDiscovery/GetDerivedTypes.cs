using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Finds all types inheriting from a base type, by name.
    /// </summary>
    public async Task<object> GetDerivedTypesAsync(
        string baseTypeName,
        bool includeTransitive = true,
        int maxResults = 100)
    {
        EnsureSolutionLoaded();

        if (string.IsNullOrWhiteSpace(baseTypeName))
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidParameter,
                "baseTypeName is required",
                hint: "Provide a base type name like 'BaseClass' or 'MyNamespace.BaseService'"
            );
        }

        var baseType = await FindTypeByNameAsync(baseTypeName);
        if (baseType == null)
        {
            return CreateErrorResponse(
                ErrorCodes.TypeNotFound,
                $"Base type '{baseTypeName}' not found",
                hint: "Try using fully-qualified name or use search_symbols to find available types"
            );
        }

        var derivedTypes = await SymbolFinder.FindDerivedClassesAsync(
            baseType, _solution!, transitive: includeTransitive);

        var derivedList = derivedTypes.ToList();
        var totalCount = derivedList.Count;

        var results = derivedList
            .Take(maxResults)
            .Select(dt => new
            {
                name = dt.Name,
                fullName = dt.ToDisplayString(),
                @namespace = dt.ContainingNamespace?.ToDisplayString(),
                isAbstract = dt.IsAbstract,
                isSealed = dt.IsSealed,
                location = GetSymbolLocation(dt),
                directBase = dt.BaseType?.ToDisplayString()
            })
            .ToList();

        return CreateSuccessResponse(
            data: new
            {
                baseType = baseType.ToDisplayString(),
                includeTransitive,
                totalDerived = totalCount,
                derivedTypes = results
            },
            suggestedNextTools: new[]
            {
                "get_type_members to see members of a derived type",
                "get_base_types to see the full inheritance chain of a type"
            },
            totalCount: totalCount,
            returnedCount: results.Count
        );
    }
}
