namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Inline a variable.
    /// </summary>
    public async Task<object> InlineVariableAsync(
        string filePath,
        int line,
        int column,
        bool preview = true)
    {
        if (await GetCSharpOnlyToolErrorAsync(filePath, "inline_variable") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

        var inlineTitles = new[]
        {
            "Inline variable",
            "Inline temporary variable",
            "Inline 'temp'"
        };

        foreach (var title in inlineTitles)
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

        // Try partial match with "Inline"
        var inlineResult = await ApplyCodeActionByTitleAsync(
            filePath, line, column, "Inline",
            preview: preview);

        var inlineDict = inlineResult as dynamic;
        if (inlineDict?.success == true)
        {
            return inlineResult;
        }

        return CreateErrorResponse(
            ErrorCodes.SymbolNotFound,
            "No 'inline variable' action found at this position",
            hint: "Position cursor on a variable that can be inlined",
            context: new { filePath, line, column }
        );
    }
}
