using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> GetContainingMemberAsync(string filePath, int line, int column)
    {
        EnsureSolutionLoaded();

        var (context, contextError) = await GetDocumentAnalysisContextAsync(filePath, line, column);
        if (contextError != null)
            return contextError;

        var position = GetPosition(context!.SyntaxTree, line, column);
        var token = context.SyntaxTree.GetRoot().FindToken(position);

        var memberNode = token.Parent != null
            ? SyntaxStrategy(context.Document).FindContainingMember(token.Parent)
            : null;

        if (memberNode == null)
        {
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                "No containing member found",
                hint: "Position may be outside any method, property, or type declaration",
                context: new { filePath, line, column }
            );
        }

        var symbol = context.SemanticModel.GetDeclaredSymbol(memberNode);
        if (symbol == null)
        {
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                "Could not resolve symbol for containing member",
                context: new { filePath, line, column }
            );
        }

        var span = memberNode.Span;
        var lineSpan = context.SyntaxTree.GetLineSpan(span);

        return CreateSuccessResponse(
            data: new
            {
                memberName = symbol.Name,
                memberKind = symbol.Kind.ToString(),
                containingType = symbol.ContainingType?.ToDisplayString(),
                signature = symbol.ToDisplayString(),
                span = new
                {
                    startLine = lineSpan.StartLinePosition.Line,
                    startColumn = lineSpan.StartLinePosition.Character,
                    endLine = lineSpan.EndLinePosition.Line,
                    endColumn = lineSpan.EndLinePosition.Character
                }
            },
            suggestedNextTools: new[]
            {
                $"find_references to see usages of {symbol.Name}",
                "get_symbol_info for more details"
            }
        );
    }
}
