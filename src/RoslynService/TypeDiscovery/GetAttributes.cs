using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Finds all symbols with specific attributes, with Godot-specific parsing.
    /// </summary>
    public async Task<object> GetAttributesAsync(
        string attributeName,
        string? scope = null,
        bool parseGodotHints = true,
        int maxResults = 100)
    {
        EnsureSolutionLoaded();

        if (string.IsNullOrWhiteSpace(attributeName))
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidParameter,
                "attributeName is required",
                hint: "Provide an attribute name like 'Export', 'Signal', 'Tool', or 'GlobalClass'"
            );
        }

        var results = new List<object>();
        var totalFound = 0;

        // Determine which projects/documents to search
        IEnumerable<Project> projectsToSearch = _solution!.Projects;
        if (!string.IsNullOrEmpty(scope))
        {
            if (scope.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
            {
                var projectName = scope.Substring("project:".Length);
                projectsToSearch = projectsToSearch.Where(p =>
                    p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
            }
            // file: scope handled below
        }

        foreach (var project in projectsToSearch)
        {
            var compilation = await GetProjectCompilationAsync(project);
            if (compilation == null) continue;

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                // Handle file: scope
                if (!string.IsNullOrEmpty(scope) && scope.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    var filePath = scope.Substring("file:".Length);
                    if (!syntaxTree.FilePath.EndsWith(filePath, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = await syntaxTree.GetRootAsync();

                // Find all declarations with attributes using language-neutral symbol binding.
                foreach (var decl in root.DescendantNodes())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(decl);
                    if (symbol == null) continue;

                    var matchingAttrs = symbol.GetAttributes()
                        .Where(a =>
                            a.AttributeClass?.Name.Contains(attributeName, StringComparison.OrdinalIgnoreCase) == true ||
                            a.AttributeClass?.Name.Contains($"{attributeName}Attribute", StringComparison.OrdinalIgnoreCase) == true)
                        .ToList();

                    if (matchingAttrs.Count == 0) continue;

                    totalFound++;
                    if (results.Count >= maxResults) continue;

                    foreach (var attr in matchingAttrs)
                    {
                        var attrInfo = new Dictionary<string, object?>
                        {
                            ["name"] = attr.AttributeClass?.Name,
                            ["fullName"] = attr.AttributeClass?.ToDisplayString()
                        };

                        // Godot-specific parsing for [Export] attributes
                        if (parseGodotHints && attributeName.Equals("Export", StringComparison.OrdinalIgnoreCase))
                        {
                            var args = attr.ConstructorArguments;
                            if (args.Length > 0)
                            {
                                attrInfo["godotHint"] = args[0].Value?.ToString();
                            }
                            if (args.Length > 1)
                            {
                                attrInfo["godotHintString"] = args[1].Value?.ToString();
                            }
                        }

                        // Include named arguments
                        if (attr.NamedArguments.Length > 0)
                        {
                            attrInfo["namedArguments"] = attr.NamedArguments
                                .ToDictionary(na => na.Key, na => na.Value.Value?.ToString());
                        }

                        results.Add(new
                        {
                            symbolName = symbol.Name,
                            symbolKind = symbol.Kind.ToString(),
                            containingType = symbol.ContainingType?.ToDisplayString(),
                            memberType = symbol is IPropertySymbol ps ? ps.Type.ToDisplayString() :
                                        symbol is IFieldSymbol fs ? fs.Type.ToDisplayString() :
                                        symbol is IMethodSymbol ms ? ms.ReturnType.ToDisplayString() : null,
                            location = GetSymbolLocation(symbol),
                            attribute = attrInfo
                        });
                    }
                }
            }
        }

        return CreateSuccessResponse(
            data: new
            {
                attributeName,
                totalFound,
                symbols = results
            },
            suggestedNextTools: new[]
            {
                "get_symbol_info to get more details about a specific symbol",
                "get_type_members to see all members of a containing type"
            },
            totalCount: totalFound,
            returnedCount: results.Count
        );
    }
}
