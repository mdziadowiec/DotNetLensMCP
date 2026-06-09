using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> FindReferencesAsync(string filePath, int line, int column, int? maxResults = null)
    {
        EnsureSolutionLoaded();

        var maxResultsToReturn = maxResults ?? 100;

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

        var references = await SymbolFinder.FindReferencesAsync(symbol, _solution!);
        var allLocations = references
            .SelectMany(r => r.Locations)
            .Where(loc => loc.Location.IsInSource)
            .ToList();

        var totalReferences = allLocations.Count;
        var referenceList = new List<object>();

        foreach (var loc in allLocations)
        {
            if (referenceList.Count >= maxResultsToReturn)
                break;

            var refDocument = _solution!.GetDocument(loc.Document.Id);
            if (refDocument == null) continue;

            var refTree = await refDocument.GetSyntaxTreeAsync();
            if (refTree == null) continue;

            var refSpan = loc.Location.SourceSpan;
            var lineSpan = refTree.GetLineSpan(refSpan);
            var text = refTree.GetText();
            var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString().Trim();

            referenceList.Add(new
            {
                filePath = FormatPath(refDocument.FilePath),
                line = lineSpan.StartLinePosition.Line,
                column = lineSpan.StartLinePosition.Character,
                lineText,
                kind = "read"
            });
        }

        return CreateSuccessResponse(
            data: new
            {
                symbolName = symbol.Name,
                symbolKind = symbol.Kind.ToString(),
                totalReferences,
                references = referenceList
            },
            suggestedNextTools: new[]
            {
                $"get_symbol_info to get details about {symbol.Name}",
                $"find_callers to see methods that call {symbol.Name}",
                symbol is INamedTypeSymbol ? $"get_type_members for {symbol.Name}" : null
            }.Where(s => s != null).ToArray()!,
            totalCount: totalReferences,
            returnedCount: referenceList.Count
        );
    }
}
