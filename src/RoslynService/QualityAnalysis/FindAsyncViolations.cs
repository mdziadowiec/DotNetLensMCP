using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> FindAsyncViolationsAsync(
        string? projectFilter = null,
        int maxResults = 100)
    {
        EnsureSolutionLoaded();

        var violations = new List<object>();

        var projects = projectFilter != null
            ? _solution!.Projects.Where(p => p.Name.Equals(projectFilter, StringComparison.OrdinalIgnoreCase))
            : _solution!.Projects;

        foreach (var project in projects)
        {
            if (violations.Count >= maxResults) break;

            foreach (var document in project.Documents)
            {
                if (violations.Count >= maxResults) break;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel == null) continue;

                var root = await document.GetSyntaxRootAsync();
                if (root == null) continue;

                var strategy = SyntaxStrategy(document);

                // Check for async void / Async Sub
                foreach (var methodNode in strategy.FindMethodNodes(root))
                {
                    if (violations.Count >= maxResults) break;

                    var sym = strategy.GetMethodSymbol(semanticModel, methodNode);
                    if (sym == null || !sym.IsAsync || !sym.ReturnsVoid) continue;

                    var loc = methodNode.GetLocation().GetLineSpan();
                    violations.Add(new
                    {
                        violationType = "async_void",
                        description = "async void / Async Sub: unhandled exceptions will crash the process",
                        symbol = sym.ToDisplayString(),
                        filePath = FormatPath(loc.Path),
                        line = loc.StartLinePosition.Line,
                        project = project.Name
                    });
                }

                if (violations.Count >= maxResults) break;

                // Check for .Wait() / .GetResult() blocking calls on Task types
                foreach (var invocation in strategy.FindInvocations(root))
                {
                    if (violations.Count >= maxResults) break;

                    var methodName = strategy.GetInvocationMethodName(invocation);
                    if (methodName is not ("Wait" or "GetResult" or "GetAwaiter")) continue;

                    var symInfo = semanticModel.GetSymbolInfo(invocation);
                    if (symInfo.Symbol is not IMethodSymbol callee) continue;

                    var typeName = callee.ContainingType?.ToDisplayString() ?? "";
                    if (!IsAsyncTaskType(typeName)) continue;

                    // GetAwaiter alone is not a violation; only flag if followed by .GetResult()
                    if (methodName == "GetAwaiter") continue;

                    var loc = invocation.GetLocation().GetLineSpan();
                    violations.Add(new
                    {
                        violationType = methodName == "Wait" ? "task_wait_blocking" : "task_getresult_blocking",
                        description = $"Task.{methodName}(): synchronous block on async operation, can deadlock",
                        symbol = callee.ContainingType?.ToDisplayString(),
                        filePath = FormatPath(loc.Path),
                        line = loc.StartLinePosition.Line,
                        project = project.Name
                    });
                }

                // Check for .Result property access on Task types
                foreach (var access in strategy.FindMemberAccesses(root))
                {
                    if (violations.Count >= maxResults) break;

                    var symInfo = semanticModel.GetSymbolInfo(access);
                    if (symInfo.Symbol is not IPropertySymbol prop) continue;
                    if (prop.Name != "Result") continue;

                    var typeName = prop.ContainingType?.ToDisplayString() ?? "";
                    if (!IsAsyncTaskType(typeName)) continue;

                    var loc = access.GetLocation().GetLineSpan();
                    violations.Add(new
                    {
                        violationType = "task_result_blocking",
                        description = "Task.Result: synchronous block on async operation, can deadlock",
                        symbol = prop.ContainingType?.ToDisplayString(),
                        filePath = FormatPath(loc.Path),
                        line = loc.StartLinePosition.Line,
                        project = project.Name
                    });
                }
            }
        }

        var bySeverity = violations
            .GroupBy(v => ((dynamic)v).violationType as string ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count());

        return CreateSuccessResponse(
            data: new { summary = bySeverity, violations },
            suggestedNextTools: new[]
            {
                "analyze_method to review the full context of a violating method",
                "get_diagnostics for compiler-detected async warnings"
            },
            totalCount: violations.Count,
            returnedCount: violations.Count
        );
    }

    private static bool IsAsyncTaskType(string fullTypeName) =>
        fullTypeName.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal)
        || fullTypeName.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal);
}
