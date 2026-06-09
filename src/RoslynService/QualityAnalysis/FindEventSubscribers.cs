using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VBSyntax = Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> FindEventSubscribersAsync(
        string? typeName = null,
        string? eventName = null,
        string? projectFilter = null,
        int maxResults = 100)
    {
        EnsureSolutionLoaded();

        var results = new List<object>();

        var projects = projectFilter != null
            ? _solution!.Projects.Where(p => p.Name.Equals(projectFilter, StringComparison.OrdinalIgnoreCase))
            : _solution!.Projects;

        foreach (var project in projects)
        {
            if (results.Count >= maxResults) break;

            foreach (var document in project.Documents)
            {
                if (results.Count >= maxResults) break;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel == null) continue;

                var root = await document.GetSyntaxRootAsync();
                if (root == null) continue;

                if (IsVisualBasicDocument(document))
                {
                    // VB: AddRemoveHandlerStatementSyntax represents both AddHandler and RemoveHandler
                    foreach (var stmt in root.DescendantNodes().OfType<VBSyntax.AddRemoveHandlerStatementSyntax>())
                    {
                        if (results.Count >= maxResults) break;

                        var symInfo = semanticModel.GetSymbolInfo(stmt.EventExpression);
                        if (symInfo.Symbol is not IEventSymbol evSym) continue;
                        if (!MatchesEventFilter(evSym, typeName, eventName)) continue;

                        var isAdd = stmt.AddHandlerOrRemoveHandlerKeyword.IsKind(
                            Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.AddHandlerKeyword);
                        var kind = isAdd ? "subscribe" : "unsubscribe";
                        var handlerKind = DetermineHandlerKind(stmt.DelegateExpression);
                        var loc = stmt.GetLocation().GetLineSpan();

                        results.Add(BuildSubscriberResult(evSym, kind, handlerKind,
                            stmt.DelegateExpression.ToString(), loc, document.Project.Name));
                    }
                }
                else
                {
                    // C#: += / -= assignment on event symbols
                    foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                    {
                        if (results.Count >= maxResults) break;

                        var kind = assignment.Kind() switch
                        {
                            SyntaxKind.AddAssignmentExpression => "subscribe",
                            SyntaxKind.SubtractAssignmentExpression => "unsubscribe",
                            _ => null
                        };
                        if (kind == null) continue;

                        var symInfo = semanticModel.GetSymbolInfo(assignment.Left);
                        if (symInfo.Symbol is not IEventSymbol evSym) continue;
                        if (!MatchesEventFilter(evSym, typeName, eventName)) continue;

                        var handlerKind = DetermineHandlerKind(assignment.Right);
                        var loc = assignment.GetLocation().GetLineSpan();

                        results.Add(BuildSubscriberResult(evSym, kind, handlerKind,
                            assignment.Right.ToString(), loc, document.Project.Name));
                    }
                }
            }
        }

        return CreateSuccessResponse(
            data: new
            {
                filter = new { typeName, eventName },
                subscribers = results
            },
            suggestedNextTools: new[]
            {
                "find_references to find all usages of the event symbol",
                "go_to_definition to navigate to the event declaration"
            },
            totalCount: results.Count,
            returnedCount: results.Count
        );
    }

    private static bool MatchesEventFilter(IEventSymbol evSym, string? typeName, string? eventName)
    {
        if (typeName != null && !evSym.ContainingType.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (eventName != null && !evSym.Name.Equals(eventName, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static string DetermineHandlerKind(SyntaxNode handlerExpr)
    {
        return handlerExpr switch
        {
            // C# lambda / anonymous function
            LambdaExpressionSyntax => "lambda",
            AnonymousMethodExpressionSyntax => "anonymous_method",
            // Anything else is treated as a method reference
            _ => "method_reference"
        };
    }

    private object BuildSubscriberResult(
        IEventSymbol evSym, string kind, string handlerKind,
        string handlerText,
        Microsoft.CodeAnalysis.FileLinePositionSpan loc,
        string projectName) => new
        {
            eventSymbol = evSym.ToDisplayString(),
            eventName = evSym.Name,
            containingType = evSym.ContainingType.ToDisplayString(),
            kind,
            handlerKind,
            handlerText = handlerText.Length > 80 ? handlerText[..80] + "…" : handlerText,
            filePath = FormatPath(loc.Path),
            line = loc.StartLinePosition.Line,
            column = loc.StartLinePosition.Character,
            project = projectName
        };
}
