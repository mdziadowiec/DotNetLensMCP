using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> GetMethodOverloadsAsync(string filePath, int line, int column)
    {
        EnsureSolutionLoaded();

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

        if (symbol is not IMethodSymbol methodSymbol)
        {
            return CreateErrorResponse(
                ErrorCodes.NotAMethod,
                "Not a method symbol",
                hint: "This tool requires a method symbol. Use get_method_signature with type and method names instead.",
                context: new { actualKind = symbol.Kind.ToString(), symbolName = symbol.Name }
            );
        }

        // Get all members of the containing type with the same name
        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
        {
            return CreateErrorResponse(
                ErrorCodes.NotAMethod,
                "Method does not have a containing type",
                context: new { filePath, line, column, methodName = methodSymbol.Name }
            );
        }

        var overloads = containingType.GetMembers(methodSymbol.Name)
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .ToList();

        var overloadList = overloads.Select(m =>
        {
            var location = m.Locations.FirstOrDefault(loc => loc.IsInSource);
            var lineSpan = location?.GetLineSpan();

            return new
            {
                signature = m.ToDisplayString(),
                parameters = m.Parameters.Select(p => new
                {
                    name = p.Name,
                    type = p.Type.ToDisplayString(),
                    isOptional = p.IsOptional,
                    defaultValue = p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null
                }).ToList(),
                returnType = m.ReturnType.ToDisplayString(),
                isAsync = m.IsAsync,
                isStatic = m.IsStatic,
                location = lineSpan != null ? new
                {
                    filePath = FormatPath(lineSpan.Value.Path),
                    line = lineSpan.Value.StartLinePosition.Line,
                    column = lineSpan.Value.StartLinePosition.Character
                } : null
            };
        }).ToList();

        return CreateSuccessResponse(
            data: new
            {
                methodName = methodSymbol.Name,
                containingType = containingType.ToDisplayString(),
                overloads = overloadList
            },
            suggestedNextTools: new[]
            {
                "find_references to see where overloads are called",
                "get_method_signature for detailed signature info"
            },
            totalCount: overloadList.Count,
            returnedCount: overloadList.Count
        );
    }
}
