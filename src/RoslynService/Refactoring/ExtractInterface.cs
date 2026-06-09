using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> ExtractInterfaceAsync(string filePath, int line, int column, string interfaceName, List<string>? includeMemberNames)
    {
        EnsureSolutionLoaded();

        if (await GetCSharpOnlyToolErrorAsync(filePath, "extract_interface") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

        Document document;
        try
        {
            document = await GetDocumentAsync(filePath);
        }
        catch (FileNotFoundException)
        {
            return CreateErrorResponse(
                ErrorCodes.FileNotInSolution,
                $"File not found in solution: {filePath}",
                hint: "Check the file path or reload the solution",
                context: new { filePath }
            );
        }

        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get semantic model",
                hint: "Check for syntax errors with get_diagnostics",
                context: new { filePath, line, column }
            );
        }

        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get syntax tree",
                context: new { filePath, line, column }
            );
        }

        var position = GetPosition(syntaxTree, line, column);
        var token = syntaxTree.GetRoot().FindToken(position);
        var node = token.Parent;

        if (node == null)
        {
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                "No symbol found at position",
                hint: "Try adjusting the line/column or use search_symbols to find symbols by name",
                context: new { filePath, line, column }
            );
        }

        var symbolInfo = semanticModel.GetSymbolInfo(node);
        var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(node);

        if (symbol is not INamedTypeSymbol typeSymbol)
        {
            return CreateErrorResponse(
                ErrorCodes.NotAType,
                "Not a type symbol",
                hint: "Place cursor on a class or struct to extract an interface",
                context: new { actualKind = symbol?.Kind.ToString() ?? "Unknown" }
            );
        }

        // Get public members
        var members = typeSymbol.GetMembers()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public)
            .Where(m => m is IMethodSymbol or IPropertySymbol or IEventSymbol)
            .Where(m => !m.IsStatic)
            .Where(m => m is not IMethodSymbol method || method.MethodKind == MethodKind.Ordinary)
            .ToList();

        // Filter by included names if specified
        if (includeMemberNames != null && includeMemberNames.Count > 0)
        {
            members = members.Where(m => includeMemberNames.Contains(m.Name)).ToList();
        }

        // Generate interface code
        var interfaceCode = GenerateInterfaceCode(interfaceName, members, typeSymbol.ContainingNamespace);

        return CreateSuccessResponse(
            data: new
            {
                className = typeSymbol.Name,
                interfaceName,
                members = members.Select(m => new
                {
                    name = m.Name,
                    kind = m.Kind.ToString(),
                    signature = m.ToDisplayString()
                }).ToList(),
                interfaceCode,
                suggestedFileName = $"{interfaceName}.cs"
            },
            suggestedNextTools: new[]
            {
                $"Create file {interfaceName}.cs with the generated code",
                $"Add : {interfaceName} to {typeSymbol.Name} class declaration"
            },
            totalCount: members.Count,
            returnedCount: members.Count
        );
    }
}
