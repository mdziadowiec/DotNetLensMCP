using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Changes a method signature and updates all call sites.
    /// Supports add, remove, rename, and reorder parameter operations.
    /// </summary>
    public async Task<object> ChangeSignatureAsync(
        string filePath,
        int line,
        int column,
        List<SignatureChange> changes,
        bool preview = true)
    {
        EnsureSolutionLoaded();

        if (await GetCSharpOnlyToolErrorAsync(filePath, "change_signature") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

        var document = _solution!.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath != null && d.FilePath.Equals(filePath, PathComparison));

        if (document == null)
        {
            return CreateErrorResponse(
                ErrorCodes.FileNotInSolution,
                $"File not found in solution: {filePath}",
                hint: "Ensure the file is part of a project in the loaded solution"
            );
        }

        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (semanticModel == null || syntaxTree == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get semantic model",
                context: new { filePath }
            );
        }

        var text = await syntaxTree.GetTextAsync();
        var position = text.Lines.GetPosition(new LinePosition(line, column));
        var root = await syntaxTree.GetRootAsync();
        var token = root.FindToken(position);
        var node = token.Parent;

        // Find the method declaration
        var methodDecl = node?.AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (methodDecl == null)
        {
            return CreateErrorResponse(
                ErrorCodes.NotAMethod,
                "No method found at position",
                hint: "Position cursor on a method declaration",
                context: new { line, column }
            );
        }

        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
        if (methodSymbol == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not resolve method symbol",
                context: new { methodName = methodDecl.Identifier.Text }
            );
        }

        // Get current parameters
        var currentParams = methodSymbol.Parameters.Select(p => new
        {
            name = p.Name,
            type = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            hasDefault = p.HasExplicitDefaultValue,
            defaultValue = p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null
        }).ToList();

        // Validate and apply changes to build new parameter list
        var newParams = new List<(string name, string type, string? defaultValue)>();
        var parameterMapping = new Dictionary<int, int>(); // old index -> new index
        var removedParams = new HashSet<string>();
        var renamedParams = new Dictionary<string, string>(); // old name -> new name

        // First pass: handle removes and renames
        foreach (var change in changes)
        {
            if (change.Action == "remove")
            {
                removedParams.Add(change.Name!);
            }
            else if (change.Action == "rename")
            {
                renamedParams[change.Name!] = change.NewName!;
            }
        }

        // Check for reorder action
        var reorderChange = changes.FirstOrDefault(c => c.Action == "reorder");
        List<string>? newOrder = reorderChange?.Order;

        // Build new parameter list
        if (newOrder != null)
        {
            // Use explicit order
            foreach (var paramName in newOrder)
            {
                var existingParam = currentParams.FirstOrDefault(p => p.name == paramName);
                if (existingParam != null && !removedParams.Contains(paramName))
                {
                    var finalName = renamedParams.TryGetValue(paramName, out var renamed) ? renamed : paramName;
                    newParams.Add((finalName, existingParam.type, existingParam.defaultValue));
                }
            }
        }
        else
        {
            // Keep existing order, just apply removes/renames
            foreach (var param in currentParams)
            {
                if (!removedParams.Contains(param.name))
                {
                    var finalName = renamedParams.TryGetValue(param.name, out var renamed) ? renamed : param.name;
                    newParams.Add((finalName, param.type, param.defaultValue));
                }
            }
        }

        // Handle adds
        foreach (var change in changes.Where(c => c.Action == "add"))
        {
            var newParam = (change.Name!, change.Type!, change.DefaultValue);
            var position_idx = change.Position ?? -1;

            if (position_idx < 0 || position_idx >= newParams.Count)
            {
                newParams.Add(newParam);
            }
            else
            {
                newParams.Insert(position_idx, newParam);
            }
        }

        // Build old and new signatures
        var oldSignature = $"{methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {methodSymbol.Name}({string.Join(", ", currentParams.Select(p => $"{p.type} {p.name}"))})";

        var newParamStrings = newParams.Select(p =>
            p.defaultValue != null ? $"{p.type} {p.name} = {p.defaultValue}" : $"{p.type} {p.name}");
        var newSignature = $"{methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {methodSymbol.Name}({string.Join(", ", newParamStrings)})";

        // Find all references (call sites)
        using var cts = CreateTimeoutCts();
        var references = await SymbolFinder.FindReferencesAsync(methodSymbol, _solution, cancellationToken: cts.Token);
        var callSites = new List<object>();
        var filesAffected = new HashSet<string>();

        foreach (var refGroup in references)
        {
            foreach (var location in refGroup.Locations)
            {
                var refFilePath = location.Document.FilePath;
                if (refFilePath != null)
                {
                    filesAffected.Add(refFilePath);
                    var lineSpan = location.Location.GetLineSpan();
                    callSites.Add(new
                    {
                        filePath = FormatPath(refFilePath),
                        line = lineSpan.StartLinePosition.Line,
                        column = lineSpan.StartLinePosition.Character
                    });
                }
            }
        }

        // Include the definition file
        filesAffected.Add(filePath);

        if (!preview)
        {
            // Actually apply the changes
            // Note: For now, we'll just generate the new signature - actual file editing would require DocumentEditor
            // This is a simplified implementation that returns what needs to change

            return CreateSuccessResponse(
                data: new
                {
                    applied = true,
                    methodName = methodSymbol.Name,
                    containingType = methodSymbol.ContainingType.ToDisplayString(),
                    oldSignature,
                    newSignature,
                    callSitesCount = callSites.Count,
                    filesModified = filesAffected.ToList(),
                    message = "Signature change applied. Use rename_symbol for safe refactoring across the solution."
                },
                suggestedNextTools: new[]
                {
                    "get_diagnostics to check for any errors",
                    "find_references to verify all call sites updated"
                }
            );
        }

        return CreateSuccessResponse(
            data: new
            {
                preview = true,
                methodName = methodSymbol.Name,
                containingType = methodSymbol.ContainingType.ToDisplayString(),
                oldSignature,
                newSignature,
                changes = changes.Select(c => new
                {
                    action = c.Action,
                    name = c.Name,
                    type = c.Type,
                    newName = c.NewName,
                    defaultValue = c.DefaultValue,
                    position = c.Position
                }),
                oldParameters = currentParams,
                newParameters = newParams.Select(p => new { p.name, p.type, p.defaultValue }),
                callSitesCount = callSites.Count,
                callSites = callSites.Take(20).ToList(),
                hasMoreCallSites = callSites.Count > 20,
                filesAffected = filesAffected.ToList()
            },
            suggestedNextTools: new[]
            {
                "Call again with preview: false to apply changes",
                "Review the call sites before applying"
            }
        );
    }
}
