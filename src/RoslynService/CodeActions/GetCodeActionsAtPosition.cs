using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Get all available code actions (fixes + refactorings) at a position.
    /// </summary>
    public async Task<object> GetCodeActionsAtPositionAsync(
        string filePath,
        int line,
        int column,
        int? endLine = null,
        int? endColumn = null,
        bool includeCodeFixes = true,
        bool includeRefactorings = true)
    {
        EnsureSolutionLoaded();

        if (await GetCSharpOnlyToolErrorAsync(filePath, "get_code_actions_at_position") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

        var (context, contextError) = await GetDocumentSyntaxContextAsync(filePath, line, column);
        if (contextError != null)
            return contextError;

        var (startPosition, endPosition) = GetPositionRange(context!.SyntaxTree, line, column, endLine, endColumn);

        var allActions = await GetAllCodeActionsAtPositionAsync(
            context.Document,
            startPosition,
            endPosition,
            includeCodeFixes,
            includeRefactorings);

        if (allActions.Count == 0)
        {
            return CreateSuccessResponse(
                data: new
                {
                    position = new { line, column, endLine, endColumn },
                    actions = Array.Empty<object>(),
                    message = "No code actions available at this position"
                },
                suggestedNextTools: new[]
                {
                    "Try a different position or selection",
                    "get_diagnostics to check for issues"
                },
                totalCount: 0,
                returnedCount: 0
            );
        }

        // Group by kind and deduplicate by title
        var actions = allActions
            .GroupBy(a => a.action.Title)
            .Select((g, index) => new
            {
                index,
                title = g.Key,
                kind = g.First().kind,
                equivalenceKey = g.First().action.EquivalenceKey,
                count = g.Count()
            })
            .OrderBy(a => a.kind)
            .ThenBy(a => a.title)
            .ToList();

        return CreateSuccessResponse(
            data: new
            {
                position = new { line, column, endLine, endColumn },
                actions,
                fixCount = actions.Count(a => a.kind == "fix"),
                refactoringCount = actions.Count(a => a.kind == "refactoring")
            },
            suggestedNextTools: new[]
            {
                actions.Count > 0 ? $"apply_code_action_by_title with title=\"{actions[0].title}\" to apply" : null
            }.Where(s => s != null).ToArray()!,
            totalCount: actions.Count,
            returnedCount: actions.Count
        );
    }
}
