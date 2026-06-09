namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Gets members for multiple types in a single call (batch optimization).
    /// </summary>
    public async Task<object> GetTypeMembersBatchAsync(
        List<string> typeNames,
        bool includeInherited = false,
        string? memberKind = null,
        string verbosity = "compact",
        int maxResultsPerType = 50)
    {
        EnsureSolutionLoaded();

        if (typeNames == null || typeNames.Count == 0)
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidParameter,
                "typeNames array is required and must not be empty",
                hint: "Provide an array of type names like ['ServiceA', 'ServiceB', 'ControllerC']",
                context: new { parameter = "typeNames" }
            );
        }

        var results = new List<object>();
        var errors = new List<object>();

        foreach (var typeName in typeNames.Distinct())
        {
            var result = await GetTypeMembersAsync(typeName, includeInherited, memberKind, verbosity, maxResultsPerType);

            // Check if result was successful
            var resultDict = result as dynamic;
            if (resultDict?.success == true)
            {
                results.Add(new
                {
                    typeName,
                    success = true,
                    data = resultDict.data
                });
            }
            else
            {
                errors.Add(new
                {
                    typeName,
                    success = false,
                    error = resultDict?.error
                });
            }
        }

        return CreateSuccessResponse(
            data: new
            {
                totalRequested = typeNames.Count,
                successCount = results.Count,
                errorCount = errors.Count,
                results,
                errors = errors.Count > 0 ? errors : null
            },
            suggestedNextTools: new[]
            {
                results.Count > 0 ? "get_method_signature for detailed method info" : null,
                errors.Count > 0 ? "Check type names - some were not found" : null
            }.Where(s => s != null).ToArray()!,
            totalCount: typeNames.Count,
            returnedCount: results.Count
        );
    }
}
