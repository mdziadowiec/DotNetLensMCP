using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> GoToDefinitionAsync(string filePath, int line, int column)
    {
        EnsureSolutionLoaded();

        var (context, contextError) = await GetDocumentAnalysisContextAsync(filePath, line, column);
        if (contextError != null)
            return contextError;

        var (symbol, symbolError) = ResolveSymbolAtPosition(
            context!,
            filePath,
            line,
            column,
            hint: "Position may be on whitespace or non-symbol token. Try positioning on an identifier.");
        if (symbolError != null)
            return symbolError;

        var definitionLocation = symbol!.Locations.FirstOrDefault(loc => loc.IsInSource);

        if (definitionLocation == null)
        {
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                "Symbol definition not in source",
                hint: "Symbol is defined in metadata (external library). Use get_type_members to explore its API.",
                context: new { symbolName = symbol.Name, symbolKind = symbol.Kind.ToString(), fullyQualifiedName = symbol.ToDisplayString() }
            );
        }

        var defLineSpan = definitionLocation.GetLineSpan();

        return CreateSuccessResponse(
            data: new
            {
                symbol = new
                {
                    name = symbol.Name,
                    kind = symbol.Kind.ToString(),
                    fullyQualifiedName = symbol.ToDisplayString(),
                    containingType = symbol.ContainingType?.ToDisplayString(),
                    containingNamespace = symbol.ContainingNamespace?.ToDisplayString()
                },
                definition = new
                {
                    filePath = FormatPath(defLineSpan.Path),
                    line = defLineSpan.StartLinePosition.Line,
                    column = defLineSpan.StartLinePosition.Character,
                    endLine = defLineSpan.EndLinePosition.Line,
                    endColumn = defLineSpan.EndLinePosition.Character
                }
            },
            suggestedNextTools: new[]
            {
                $"find_references to see all usages of {symbol.Name}",
                $"get_symbol_info for more details about {symbol.Name}",
                symbol is INamedTypeSymbol ? $"get_type_members for {symbol.Name}" : null
            }.Where(s => s != null).ToArray()!
        );
    }
}
