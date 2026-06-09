using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VBSyntax = Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> GetComplexityMetricsAsync(
        string filePath,
        int? line = null,
        int? column = null,
        List<string>? metrics = null)
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

        var syntaxTree = await document.GetSyntaxTreeAsync();
        var semanticModel = await document.GetSemanticModelAsync();

        if (syntaxTree == null || semanticModel == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not analyze file",
                context: new { filePath }
            );
        }

        var root = await syntaxTree.GetRootAsync();
        var strategy = SyntaxStrategy(document);
        var isVb = IsVisualBasicDocument(document);

        var requestedMetrics = metrics ?? new List<string>
        {
            "cyclomatic", "nesting", "loc", "parameters", "cognitive"
        };

        if (line.HasValue)
        {
            var position = GetPosition(syntaxTree, line.Value, column ?? 0);
            var node = root.FindToken(position).Parent;

            var methodNode = node != null ? strategy.FindMethodBody(root, root.FindToken(position).Span) : null;

            if (methodNode == null)
            {
                // Fallback: try accessor blocks
                if (isVb)
                {
                    var vbAccessor = node?.AncestorsAndSelf().OfType<VBSyntax.AccessorBlockSyntax>().FirstOrDefault();
                    if (vbAccessor == null)
                    {
                        return CreateErrorResponse(
                            ErrorCodes.SymbolNotFound,
                            "No method or property accessor found at this position",
                            hint: "Position cursor inside a method body",
                            context: new { filePath, line, column }
                        );
                    }
                    var accessorMetrics = CalculateComplexityMetrics(vbAccessor, requestedMetrics, strategy);
                    return CreateSuccessResponse(
                        data: new
                        {
                            scope = "accessor",
                            name = vbAccessor.BlockStatement.ToString(),
                            metrics = accessorMetrics
                        },
                        suggestedNextTools: new[]
                        {
                            accessorMetrics.TryGetValue("cyclomatic", out var accCC) && (int)accCC > 10
                                ? "Consider refactoring - cyclomatic complexity > 10"
                                : null
                        }.Where(s => s != null).ToArray()!
                    );
                }
                else
                {
                    var accessor = node?.AncestorsAndSelf().OfType<AccessorDeclarationSyntax>().FirstOrDefault();
                    if (accessor == null)
                    {
                        return CreateErrorResponse(
                            ErrorCodes.SymbolNotFound,
                            "No method or property accessor found at this position",
                            hint: "Position cursor inside a method body",
                            context: new { filePath, line, column }
                        );
                    }
                    var accessorMetrics = CalculateComplexityMetrics(accessor, requestedMetrics, strategy);
                    return CreateSuccessResponse(
                        data: new
                        {
                            scope = "accessor",
                            name = accessor.Parent is PropertyDeclarationSyntax prop
                                ? $"{prop.Identifier.Text}.{accessor.Keyword.Text}"
                                : accessor.Keyword.Text,
                            metrics = accessorMetrics
                        },
                        suggestedNextTools: new[]
                        {
                            accessorMetrics.TryGetValue("cyclomatic", out var accCC) && (int)accCC > 10
                                ? "Consider refactoring - cyclomatic complexity > 10"
                                : null
                        }.Where(s => s != null).ToArray()!
                    );
                }
            }

            var methodMetrics = CalculateComplexityMetrics(methodNode, requestedMetrics, strategy);
            var methodSymbol = strategy.GetMethodSymbol(semanticModel, methodNode);
            return CreateSuccessResponse(
                data: new
                {
                    scope = "method",
                    name = methodSymbol?.Name ?? "(unknown)",
                    containingType = methodSymbol?.ContainingType?.Name,
                    metrics = methodMetrics
                },
                suggestedNextTools: new[]
                {
                    methodMetrics.TryGetValue("cyclomatic", out var cc) && (int)cc > 10
                        ? "Consider refactoring - cyclomatic complexity > 10"
                        : null,
                    methodMetrics.TryGetValue("nesting", out var nest) && (int)nest > 4
                        ? "Consider refactoring - nesting depth > 4"
                        : null
                }.Where(s => s != null).ToArray()!
            );
        }

        // Analyze whole file
        var methods = strategy.FindMethodNodes(root)
            .Select(m =>
            {
                var sym = strategy.GetMethodSymbol(semanticModel, m);
                var methodMetrics = CalculateComplexityMetrics(m, requestedMetrics, strategy);
                return new
                {
                    name = sym?.Name ?? "(unknown)",
                    containingType = sym?.ContainingType?.Name,
                    line = m.GetLocation().GetLineSpan().StartLinePosition.Line,
                    metrics = methodMetrics
                };
            })
            .ToList();

        var fileTotals = new Dictionary<string, object>();
        foreach (var metric in requestedMetrics)
        {
            if (metric == "cyclomatic")
            {
                var total = methods.Sum(m => m.metrics.TryGetValue("cyclomatic", out var v) ? (int)v : 0);
                var avg = methods.Count > 0 ? (double)total / methods.Count : 0;
                fileTotals["avgCyclomatic"] = Math.Round(avg, 2);
                fileTotals["maxCyclomatic"] = methods.Count > 0
                    ? methods.Max(m => m.metrics.TryGetValue("cyclomatic", out var v) ? (int)v : 0)
                    : 0;
            }
            else if (metric == "nesting")
            {
                fileTotals["maxNesting"] = methods.Count > 0
                    ? methods.Max(m => m.metrics.TryGetValue("nesting", out var v) ? (int)v : 0)
                    : 0;
            }
            else if (metric == "loc")
            {
                fileTotals["totalLoc"] = methods.Sum(m => m.metrics.TryGetValue("loc", out var v) ? (int)v : 0);
            }
        }

        return CreateSuccessResponse(
            data: new
            {
                scope = "file",
                filePath,
                methodCount = methods.Count,
                fileTotals,
                methods
            },
            totalCount: methods.Count,
            returnedCount: methods.Count
        );
    }

    private static Dictionary<string, object> CalculateComplexityMetrics(SyntaxNode node, List<string> requestedMetrics, ILanguageSyntaxStrategy strategy)
    {
        var result = new Dictionary<string, object>();

        foreach (var metric in requestedMetrics)
        {
            switch (metric.ToLowerInvariant())
            {
                case "cyclomatic":
                    result["cyclomatic"] = node.DescendantNodes().Sum(n => strategy.CountNodeComplexity(n)) + 1;
                    break;
                case "nesting":
                    result["nesting"] = CalculateNestingDepth(node, strategy);
                    break;
                case "loc":
                    result["loc"] = CalculateLinesOfCode(node);
                    break;
                case "parameters":
                    result["parameters"] = strategy.GetParameterCount(node) ?? 0;
                    break;
                case "cognitive":
                    result["cognitive"] = CalculateCognitiveComplexity(node, strategy);
                    break;
            }
        }

        return result;
    }

    private static int CalculateNestingDepth(SyntaxNode node, ILanguageSyntaxStrategy strategy)
    {
        int maxDepth = 0;
        CalculateNestingDepthRecursive(node, 0, ref maxDepth, strategy);
        return maxDepth;
    }

    private static void CalculateNestingDepthRecursive(SyntaxNode node, int currentDepth, ref int maxDepth, ILanguageSyntaxStrategy strategy)
    {
        foreach (var child in node.ChildNodes())
        {
            int newDepth = strategy.IsControlFlowNode(child) ? currentDepth + 1 : currentDepth;
            maxDepth = Math.Max(maxDepth, newDepth);
            CalculateNestingDepthRecursive(child, newDepth, ref maxDepth, strategy);
        }
    }

    private static int CalculateLinesOfCode(SyntaxNode node)
    {
        return node.ToFullString()
            .Split('\n')
            .Count(l =>
            {
                var t = l.Trim();
                return !string.IsNullOrWhiteSpace(t) && !t.StartsWith("//") && !t.StartsWith("/*") && !t.StartsWith("*");
            });
    }

    private static int CalculateCognitiveComplexity(SyntaxNode node, ILanguageSyntaxStrategy strategy)
    {
        int complexity = 0;
        CalculateCognitiveComplexityRecursive(node, 0, ref complexity, strategy);
        return complexity;
    }

    private static void CalculateCognitiveComplexityRecursive(SyntaxNode node, int nestingLevel, ref int complexity, ILanguageSyntaxStrategy strategy)
    {
        foreach (var child in node.ChildNodes())
        {
            complexity += strategy.CognitiveWeight(child, nestingLevel);
            var newNesting = strategy.IsControlFlowNode(child) ? nestingLevel + 1 : nestingLevel;
            CalculateCognitiveComplexityRecursive(child, newNesting, ref complexity, strategy);
        }
    }
}
