using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> GetTypeHierarchyAsync(string filePath, int line, int column, int? maxDerivedTypes = null)
    {
        EnsureSolutionLoaded();

        var maxDerivedToReturn = maxDerivedTypes ?? 50; // Default to 50

        var (context, contextError) = await GetDocumentAnalysisContextAsync(filePath, line, column);
        if (contextError != null)
            return contextError;

        if (context == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get document analysis context",
                context: new { filePath, line, column }
            );
        }

        var (symbol, symbolError) = ResolveSymbolAtPosition(context, filePath, line, column);
        if (symbolError != null)
            return symbolError;

        if (symbol == null)
        {
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                "No symbol found at position",
                context: new { filePath, line, column }
            );
        }

        if (symbol is not INamedTypeSymbol typeSymbol)
        {
            return CreateErrorResponse(
                ErrorCodes.NotAType,
                "Not a type symbol",
                hint: "This tool requires a type symbol (class, struct, interface). Use get_base_types or get_derived_types with a type name instead.",
                context: new { actualKind = symbol.Kind.ToString(), symbolName = symbol.Name }
            );
        }

        // Get base types
        var baseTypes = new List<object>();
        var currentBase = typeSymbol.BaseType;
        while (currentBase != null && currentBase.SpecialType != SpecialType.System_Object)
        {
            baseTypes.Add(FormatTypeInfo(currentBase));
            currentBase = currentBase.BaseType;
        }

        // Get interfaces
        var interfaces = typeSymbol.AllInterfaces
            .Select(i => FormatTypeInfo(i))
            .ToList();

        // Get derived types
        var derivedTypes = await SymbolFinder.FindDerivedClassesAsync(typeSymbol, _solution!, transitive: false);
        var allDerived = derivedTypes.ToList();
        var totalDerived = allDerived.Count;

        var derivedList = allDerived
            .Take(maxDerivedToReturn)
            .Select(d => FormatTypeInfo(d))
            .ToList();

        return CreateSuccessResponse(
            data: new
            {
                typeName = typeSymbol.ToDisplayString(),
                baseTypes,
                interfaces,
                derivedTypes = derivedList
            },
            suggestedNextTools: new[]
            {
                $"get_type_members for {typeSymbol.Name} to see all members",
                "get_derived_types by name for transitive derived types",
                "find_implementations for interface implementations"
            },
            totalCount: totalDerived,
            returnedCount: derivedList.Count
        );
    }
}
