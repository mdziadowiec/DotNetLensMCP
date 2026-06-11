using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> FindCallersAsync(string filePath, int line, int column, int? maxResults = null)
    {
        EnsureSolutionLoaded();

        var maxResultsToReturn = maxResults ?? 100; // Default to 100

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

        // Find callers works best for methods, properties, and constructors
        if (symbol is not (IMethodSymbol or IPropertySymbol))
        {
            return CreateErrorResponse(
                ErrorCodes.NotAMethod,
                "Not a callable symbol",
                hint: "This tool works for methods, properties, and constructors. Use find_references for other symbol types.",
                context: new { actualKind = symbol.Kind.ToString(), symbolName = symbol.Name }
            );
        }

        using var cts = CreateTimeoutCts();
        var callers = await SymbolFinder.FindCallersAsync(symbol, _solution!, cancellationToken: cts.Token);

        // First count total
        var totalCallers = 0;
        foreach (var caller in callers)
        {
            totalCallers += caller.Locations.Count(loc => loc.IsInSource);
        }

        var callerList = new List<object>();
        foreach (var caller in callers)
        {
            var callingSymbol = caller.CallingSymbol;
            var locations = caller.Locations;

            foreach (var location in locations.Where(loc => loc.IsInSource))
            {
                if (callerList.Count >= maxResultsToReturn)
                    break; // Stop at limit

                if (location.SourceTree == null) continue;

                var callerDocument = _solution!.GetDocument(location.SourceTree);
                if (callerDocument == null) continue;

                var lineSpan = location.GetLineSpan();
                var text = location.SourceTree.GetText();
                var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString().Trim();

                callerList.Add(new
                {
                    callingSymbol = new
                    {
                        name = callingSymbol.Name,
                        kind = callingSymbol.Kind.ToString(),
                        containingType = callingSymbol.ContainingType?.ToDisplayString(),
                        signature = callingSymbol.ToDisplayString()
                    },
                    location = new
                    {
                        filePath = FormatPath(callerDocument.FilePath),
                        line = lineSpan.StartLinePosition.Line,
                        column = lineSpan.StartLinePosition.Character,
                        lineText
                    }
                });
            }

            if (callerList.Count >= maxResultsToReturn)
                break; // Stop outer loop too
        }

        return CreateSuccessResponse(
            data: new
            {
                symbolName = symbol.Name,
                symbolKind = symbol.Kind.ToString(),
                symbolSignature = symbol.ToDisplayString(),
                callers = callerList
            },
            suggestedNextTools: new[]
            {
                "get_containing_member for caller context",
                "find_references for all usages including non-call references"
            },
            totalCount: totalCallers,
            returnedCount: callerList.Count
        );
    }
}
