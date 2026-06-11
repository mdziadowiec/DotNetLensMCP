using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Analyzes impact of changing a symbol.
    /// </summary>
    public async Task<object> AnalyzeChangeImpactAsync(
        string filePath,
        int line,
        int column,
        string changeType,
        string? newValue = null)
    {
        EnsureSolutionLoaded();

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
                hint: "Place cursor on a symbol (method, property, type, etc.)",
                context: new { filePath, line, column }
            );
        }

        var symbolInfo = semanticModel.GetSymbolInfo(node);
        var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(node);

        if (symbol == null)
        {
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                "Could not resolve symbol",
                context: new { filePath, line, column }
            );
        }

        // Find all references
        using var cts = CreateTimeoutCts();
        var references = await SymbolFinder.FindReferencesAsync(symbol, _solution!, cancellationToken: cts.Token);
        var allLocations = references.SelectMany(r => r.Locations).ToList();

        var impactedLocations = new List<object>();
        var breakingChanges = 0;
        var warnings = 0;

        foreach (var location in allLocations)
        {
            if (!location.Location.IsInSource) continue;

            var refDocument = location.Document;
            var refSemanticModel = await refDocument.GetSemanticModelAsync();
            var lineSpan = location.Location.GetLineSpan();

            string impact;
            string severity;

            switch (changeType.ToLower())
            {
                case "rename":
                    impact = "Reference will need to be updated";
                    severity = "info";
                    break;

                case "changetype":
                case "change_type":
                    impact = $"Usage may be incompatible with new type";
                    severity = "warning";
                    warnings++;
                    break;

                case "addparameter":
                case "add_parameter":
                    impact = "Call site missing new parameter";
                    severity = "error";
                    breakingChanges++;
                    break;

                case "removeparameter":
                case "remove_parameter":
                    impact = "Call site has extra parameter";
                    severity = "error";
                    breakingChanges++;
                    break;

                case "changeaccessibility":
                case "change_accessibility":
                    if (symbol.DeclaredAccessibility == Accessibility.Public)
                    {
                        impact = "External usages may lose access";
                        severity = "error";
                        breakingChanges++;
                    }
                    else
                    {
                        impact = "Accessibility change may affect visibility";
                        severity = "warning";
                        warnings++;
                    }
                    break;

                case "delete":
                    impact = "Reference will be broken";
                    severity = "error";
                    breakingChanges++;
                    break;

                default:
                    impact = "Unknown impact";
                    severity = "info";
                    break;
            }

            impactedLocations.Add(new
            {
                filePath = FormatPath(lineSpan.Path),
                line = lineSpan.StartLinePosition.Line,
                column = lineSpan.StartLinePosition.Character,
                impact,
                severity
            });
        }

        return CreateSuccessResponse(
            data: new
            {
                symbol = symbol.ToDisplayString(),
                symbolKind = symbol.Kind.ToString(),
                changeType,
                newValue,
                totalReferences = allLocations.Count,
                breakingChanges,
                warnings,
                safe = breakingChanges == 0,
                impactedLocations
            },
            suggestedNextTools: breakingChanges > 0
                ? new[] { "Fix breaking changes before applying", "Use rename_symbol for safe renames" }
                : new[] { "Safe to proceed with change" },
            totalCount: allLocations.Count,
            returnedCount: impactedLocations.Count
        );
    }
}
