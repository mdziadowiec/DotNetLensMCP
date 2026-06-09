namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Extract an expression to a variable.
    /// </summary>
    public async Task<object> ExtractVariableAsync(
        string filePath,
        int line,
        int column,
        int? endLine = null,
        int? endColumn = null,
        bool preview = true)
    {
        if (await GetCSharpOnlyToolErrorAsync(filePath, "extract_variable") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

        var extractTitles = new[]
        {
            "Introduce local",
            "Extract local variable",
            "Introduce variable for",
            "Extract variable"
        };

        foreach (var title in extractTitles)
        {
            var result = await ApplyCodeActionByTitleAsync(
                filePath, line, column, title,
                endLine, endColumn,
                preview: preview);

            var dict = result as dynamic;
            if (dict?.success == true)
            {
                return result;
            }
        }

        // Try partial match
        var extractResult = await ApplyCodeActionByTitleAsync(
            filePath, line, column, "Introduce",
            endLine, endColumn,
            preview: preview);

        var extractDict = extractResult as dynamic;
        if (extractDict?.success == true)
        {
            return extractResult;
        }

        return CreateErrorResponse(
            ErrorCodes.SymbolNotFound,
            "No 'extract variable' action found at this position",
            hint: "Select an expression to extract",
            context: new { filePath, line, column, endLine, endColumn }
        );
    }
}
