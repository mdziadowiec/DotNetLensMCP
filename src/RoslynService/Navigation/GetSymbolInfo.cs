using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> GetSymbolInfoAsync(string filePath, int line, int column)
    {
        EnsureSolutionLoaded();

        var (context, contextError) = await GetDocumentAnalysisContextAsync(filePath, line, column);
        if (contextError != null)
            return contextError;

        var (symbol, symbolError) = ResolveSymbolAtPosition(context!, filePath, line, column);
        if (symbolError != null)
            return symbolError;

        var symbolData = await FormatSymbolInfoAsync(symbol!);

        return CreateSuccessResponse(
            data: symbolData,
            suggestedNextTools: new[]
            {
                $"find_references to see all usages of {symbol!.Name}",
                symbol is Microsoft.CodeAnalysis.INamedTypeSymbol ? $"get_type_members for {symbol.Name} to see members" : null,
                $"go_to_definition to navigate to {symbol.Name}'s definition"
            }.Where(s => s != null).ToArray()!
        );
    }
}
