namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Implement missing interface/abstract members.
    /// </summary>
    public async Task<object> ImplementMissingMembersAsync(
        string filePath,
        int line,
        int column,
        bool preview = true)
    {
        if (await GetCSharpOnlyToolErrorAsync(filePath, "implement_missing_members") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

        // This is a convenience wrapper around apply_code_action_by_title
        // Looking for actions like "Implement interface", "Implement abstract class"
        var actionsResult = await GetCodeActionsAtPositionAsync(
            filePath, line, column,
            includeCodeFixes: true,
            includeRefactorings: true);

        // Check if we got actions
        var resultDict = actionsResult as dynamic;
        if (resultDict?.success != true)
        {
            return actionsResult;
        }

        // Look for implement actions
        var implementTitles = new[]
        {
            "Implement interface",
            "Implement abstract class",
            "Implement all members explicitly",
            "Implement remaining members",
            "Implement missing members"
        };

        foreach (var title in implementTitles)
        {
            var result = await ApplyCodeActionByTitleAsync(
                filePath, line, column, title,
                preview: preview);

            var dict = result as dynamic;
            if (dict?.success == true)
            {
                return result;
            }
        }

        return CreateErrorResponse(
            ErrorCodes.SymbolNotFound,
            "No 'implement members' action found at this position",
            hint: "Position cursor on a class that implements an interface or extends an abstract class",
            context: new { filePath, line, column }
        );
    }
}
