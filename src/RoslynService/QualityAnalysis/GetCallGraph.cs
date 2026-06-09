using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> GetCallGraphAsync(
        string typeName,
        string methodName,
        int depth = 3,
        string direction = "callees")
    {
        EnsureSolutionLoaded();

        if (depth is < 1 or > 10)
            return CreateErrorResponse(ErrorCodes.InvalidParameter, "depth must be between 1 and 10");

        if (direction is not ("callers" or "callees" or "both"))
            return CreateErrorResponse(ErrorCodes.InvalidParameter, "direction must be 'callers', 'callees', or 'both'");

        var type = await FindTypeByNameAsync(typeName);
        if (type == null)
            return CreateErrorResponse(ErrorCodes.TypeNotFound, $"Type '{typeName}' not found");

        var rootMethod = type.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();
        if (rootMethod == null)
            return CreateErrorResponse(ErrorCodes.SymbolNotFound, $"Method '{methodName}' not found on '{type.Name}'");

        var nodes = new Dictionary<string, object>(StringComparer.Ordinal);
        var edges = new List<object>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var cycleNodes = new HashSet<string>(StringComparer.Ordinal);

        var rootFqn = rootMethod.OriginalDefinition.ToDisplayString();
        nodes[rootFqn] = MakeCallGraphNode(rootMethod, isRoot: true);
        visited.Add(rootFqn);

        var queue = new Queue<(IMethodSymbol sym, int remainingDepth)>();
        queue.Enqueue((rootMethod.OriginalDefinition, depth));

        while (queue.Count > 0)
        {
            var (current, remaining) = queue.Dequeue();
            if (remaining <= 0) continue;

            var currentFqn = current.ToDisplayString();

            if (direction is "callees" or "both")
                await ExpandCalleesAsync(current, currentFqn, remaining, nodes, edges, visited, cycleNodes, queue);

            if (direction is "callers" or "both")
                await ExpandCallersAsync(current, currentFqn, remaining, nodes, edges, visited, cycleNodes, queue);
        }

        return CreateSuccessResponse(
            data: new
            {
                root = rootFqn,
                direction,
                depth,
                nodeCount = nodes.Count,
                edgeCount = edges.Count,
                cycleCount = cycleNodes.Count,
                nodes = nodes.Values.ToList(),
                edges,
                cycleNodes = cycleNodes.ToList()
            },
            suggestedNextTools: new[]
            {
                "analyze_method for detailed analysis of a specific node",
                "find_callers for a targeted caller lookup without depth limit"
            }
        );
    }

    private async Task ExpandCalleesAsync(
        IMethodSymbol method,
        string methodFqn,
        int remaining,
        Dictionary<string, object> nodes,
        List<object> edges,
        HashSet<string> visited,
        HashSet<string> cycleNodes,
        Queue<(IMethodSymbol, int)> queue)
    {
        var srcLocation = method.Locations.FirstOrDefault(l => l.IsInSource);
        if (srcLocation?.SourceTree == null) return;

        var doc = _solution!.GetDocument(srcLocation.SourceTree);
        if (doc == null) return;

        var root = await srcLocation.SourceTree.GetRootAsync();
        var semanticModel = await doc.GetSemanticModelAsync();
        if (semanticModel == null) return;

        var methodBody = SyntaxStrategy(doc).FindMethodBody(root, srcLocation.SourceSpan);
        if (methodBody == null) return;

        var localSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var invocation in SyntaxStrategy(doc).FindInvocations(methodBody))
        {
            var info = semanticModel.GetSymbolInfo(invocation);
            if (info.Symbol is not IMethodSymbol callee) continue;

            var callee0 = callee.OriginalDefinition;
            var calleeFqn = callee0.ToDisplayString();
            if (!localSeen.Add(calleeFqn)) continue;

            edges.Add(new { from = methodFqn, to = calleeFqn, kind = "calls" });

            if (visited.Contains(calleeFqn))
            {
                cycleNodes.Add(calleeFqn);
                continue;
            }

            visited.Add(calleeFqn);
            nodes[calleeFqn] = MakeCallGraphNode(callee0, isRoot: false);

            if (remaining > 1 && callee0.Locations.Any(l => l.IsInSource))
                queue.Enqueue((callee0, remaining - 1));
        }
    }

    private async Task ExpandCallersAsync(
        IMethodSymbol method,
        string methodFqn,
        int remaining,
        Dictionary<string, object> nodes,
        List<object> edges,
        HashSet<string> visited,
        HashSet<string> cycleNodes,
        Queue<(IMethodSymbol, int)> queue)
    {
        var callers = await SymbolFinder.FindCallersAsync(method, _solution!);

        foreach (var caller in callers)
        {
            if (caller.CallingSymbol is not IMethodSymbol callerMethod) continue;

            var caller0 = callerMethod.OriginalDefinition;
            var callerFqn = caller0.ToDisplayString();

            edges.Add(new { from = callerFqn, to = methodFqn, kind = "calls" });

            if (visited.Contains(callerFqn))
            {
                cycleNodes.Add(callerFqn);
                continue;
            }

            visited.Add(callerFqn);
            nodes[callerFqn] = MakeCallGraphNode(caller0, isRoot: false);

            if (remaining > 1 && caller0.Locations.Any(l => l.IsInSource))
                queue.Enqueue((caller0, remaining - 1));
        }
    }

    private object MakeCallGraphNode(IMethodSymbol method, bool isRoot)
    {
        var srcLoc = method.Locations.FirstOrDefault(l => l.IsInSource);
        return new
        {
            id = method.ToDisplayString(),
            name = $"{method.ContainingType?.Name}.{method.Name}",
            containingType = method.ContainingType?.ToDisplayString(),
            kind = method.MethodKind.ToString(),
            isAsync = method.IsAsync,
            isExternal = srcLoc == null,
            isRoot,
            filePath = srcLoc != null ? FormatPath(srcLoc.GetLineSpan().Path) : null,
            line = srcLoc?.GetLineSpan().StartLinePosition.Line
        };
    }
}
