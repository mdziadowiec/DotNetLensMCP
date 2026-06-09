using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> GetOutgoingCallsAsync(string filePath, int line, int column, int? maxDepth = null)
    {
        EnsureSolutionLoaded();

        var depth = maxDepth ?? 1;

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
        var strategy = SyntaxStrategy(document);

        var methodBody = strategy.FindMethodBody(syntaxTree.GetRoot(), token.Span);
        if (methodBody == null)
        {
            return CreateErrorResponse(
                ErrorCodes.NotAMethod,
                "No method declaration found at position",
                hint: "Place cursor inside a method body",
                context: new { filePath, line, column }
            );
        }

        var methodSymbol = strategy.GetMethodSymbol(semanticModel, methodBody);
        if (methodSymbol == null)
        {
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                "Could not resolve method symbol",
                context: new { filePath, line, column }
            );
        }

        var calls = new List<object>();
        var visited = new HashSet<string>();

        foreach (var invNode in strategy.FindInvocations(methodBody))
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invNode);
            var calledMethod = symbolInfo.Symbol as IMethodSymbol;
            if (calledMethod == null) continue;

            var key = calledMethod.ToDisplayString();
            if (visited.Contains(key)) continue;
            visited.Add(key);

            var location = calledMethod.Locations.FirstOrDefault(l => l.IsInSource);
            var lineSpan = location?.GetLineSpan();

            calls.Add(new
            {
                method = calledMethod.ToDisplayString(),
                shortName = $"{calledMethod.ContainingType?.Name}.{calledMethod.Name}",
                returnType = calledMethod.ReturnType.ToDisplayString(),
                isAsync = calledMethod.IsAsync,
                isExternal = !location?.IsInSource ?? true,
                location = lineSpan != null ? new
                {
                    filePath = FormatPath(lineSpan.Value.Path),
                    line = lineSpan.Value.StartLinePosition.Line,
                    column = lineSpan.Value.StartLinePosition.Character
                } : null
            });
        }

        foreach (var access in strategy.FindMemberAccesses(methodBody))
        {
            var symbolInfo = semanticModel.GetSymbolInfo(access);
            if (symbolInfo.Symbol is IPropertySymbol prop)
            {
                var key = prop.ToDisplayString();
                if (visited.Contains(key)) continue;
                visited.Add(key);

                var location = prop.Locations.FirstOrDefault(l => l.IsInSource);
                var lineSpan = location?.GetLineSpan();

                calls.Add(new
                {
                    method = prop.ToDisplayString(),
                    shortName = $"{prop.ContainingType?.Name}.{prop.Name}",
                    returnType = prop.Type.ToDisplayString(),
                    isAsync = false,
                    isProperty = true,
                    isExternal = !location?.IsInSource ?? true,
                    location = lineSpan != null ? new
                    {
                        filePath = FormatPath(lineSpan.Value.Path),
                        line = lineSpan.Value.StartLinePosition.Line,
                        column = lineSpan.Value.StartLinePosition.Character
                    } : null
                });
            }
        }

        return CreateSuccessResponse(
            data: new
            {
                method = methodSymbol.ToDisplayString(),
                containingType = methodSymbol.ContainingType?.ToDisplayString(),
                calls
            },
            suggestedNextTools: new[]
            {
                "get_outgoing_calls on a called method to trace deeper",
                "find_callers to see who calls this method"
            },
            totalCount: calls.Count,
            returnedCount: calls.Count
        );
    }
}
