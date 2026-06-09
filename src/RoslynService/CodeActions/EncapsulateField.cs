namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Encapsulate a field into a property.
    /// </summary>
    public async Task<object> EncapsulateFieldAsync(
        string filePath,
        int line,
        int column,
        bool preview = true)
    {
        if (await GetCSharpOnlyToolErrorAsync(filePath, "encapsulate_field") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

        // Look for encapsulate field actions
        var encapsulateTitles = new[]
        {
            "Encapsulate field",
            "Encapsulate field (and use property)",
            "Encapsulate field (but still use field)"
        };

        foreach (var title in encapsulateTitles)
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
            "No 'encapsulate field' action found at this position",
            hint: "Position cursor on a field declaration",
            context: new { filePath, line, column }
        );
    }
}
