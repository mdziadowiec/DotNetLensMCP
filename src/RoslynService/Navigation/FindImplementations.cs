using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> FindImplementationsAsync(string filePath, int line, int column, int? maxResults = null)
    {
        EnsureSolutionLoaded();

        var maxResultsToReturn = maxResults ?? 50;

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
                hint: "This tool requires a type symbol (interface, class, or abstract class). Use get_derived_types with a type name instead.",
                context: new { actualKind = symbol.Kind.ToString(), symbolName = symbol.Name }
            );
        }

        using var cts = CreateTimeoutCts();
        var implementations = await SymbolFinder.FindImplementationsAsync(typeSymbol, _solution!, cancellationToken: cts.Token);
        var allImplementations = implementations.ToList();
        var totalImplementations = allImplementations.Count;

        var implementationList = new List<object>();
        foreach (var impl in allImplementations)
        {
            if (implementationList.Count >= maxResultsToReturn)
                break;

            var locations = impl.Locations
                .Where(loc => loc.IsInSource)
                .Select(loc =>
                {
                    var lineSpan = loc.GetLineSpan();
                    return new
                    {
                        filePath = FormatPath(lineSpan.Path),
                        line = lineSpan.StartLinePosition.Line,
                        column = lineSpan.StartLinePosition.Character
                    };
                })
                .ToList();

            implementationList.Add(new
            {
                name = impl.ToDisplayString(),
                kind = impl.TypeKind.ToString(),
                containingNamespace = impl.ContainingNamespace?.ToDisplayString(),
                locations
            });
        }

        return CreateSuccessResponse(
            data: new
            {
                baseType = typeSymbol.ToDisplayString(),
                totalImplementations,
                implementations = implementationList
            },
            suggestedNextTools: new[]
            {
                $"get_type_members for each implementation to see their members",
                $"get_type_hierarchy for {typeSymbol.Name} to see full inheritance tree"
            },
            totalCount: totalImplementations,
            returnedCount: implementationList.Count
        );
    }
}
