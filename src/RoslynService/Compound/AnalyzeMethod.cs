using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Gets comprehensive analysis of a method in a single call.
    /// Combines signature, callers, outgoing calls, and suggests data flow analysis.
    /// </summary>
    public async Task<object> AnalyzeMethodAsync(
        string typeName,
        string methodName,
        bool includeCallers = true,
        bool includeOutgoingCalls = false,
        int maxCallers = 20,
        int maxOutgoingCalls = 50)
    {
        EnsureSolutionLoaded();

        if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(methodName))
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidParameter,
                "typeName and methodName are required"
            );
        }

        var type = await FindTypeByNameAsync(typeName);
        if (type == null)
        {
            return CreateErrorResponse(
                ErrorCodes.TypeNotFound,
                $"Type '{typeName}' not found"
            );
        }

        var method = type.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();
        if (method == null)
        {
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                $"Method '{methodName}' not found on type '{type.Name}'"
            );
        }

        // Get signature details
        var signature = new
        {
            name = method.Name,
            fullSignature = method.ToDisplayString(),
            returnType = method.ReturnType.ToDisplayString(),
            parameters = method.Parameters.Select(p => new
            {
                name = p.Name,
                type = p.Type.ToDisplayString(),
                isOptional = p.IsOptional
            }).ToList(),
            isAsync = method.IsAsync,
            isStatic = method.IsStatic,
            isVirtual = method.IsVirtual,
            accessibility = method.DeclaredAccessibility.ToString()
        };

        // Get callers if requested
        List<object>? callers = null;
        var totalCallers = 0;
        if (includeCallers)
        {
            var callerSymbols = await SymbolFinder.FindCallersAsync(method, _solution!);
            var callerList = callerSymbols.ToList();
            totalCallers = callerList.Count;

            callers = callerList
                .Take(maxCallers)
                .Select(c => new
                {
                    callingMethod = c.CallingSymbol.ToDisplayString(),
                    containingType = c.CallingSymbol.ContainingType?.Name,
                    locations = c.Locations.Select(l =>
                    {
                        var lineSpan = l.GetLineSpan();
                        return new
                        {
                            filePath = FormatPath(lineSpan.Path),
                            line = lineSpan.StartLinePosition.Line
                        };
                    }).Take(3).ToList()
                })
                .Cast<object>()
                .ToList();
        }

        // Get outgoing calls if requested
        List<object>? outgoingCalls = null;
        var totalOutgoingCalls = 0;
        if (includeOutgoingCalls)
        {
            var location = method.Locations.FirstOrDefault(l => l.IsInSource);
            if (location?.SourceTree != null)
            {
                var root = await location.SourceTree.GetRootAsync();
                var node = root.FindNode(location.SourceSpan);
                var outgoingDoc = _solution!.GetDocument(location.SourceTree);
                var methodBody = SyntaxStrategy(outgoingDoc != null && IsVisualBasicDocument(outgoingDoc))
                    .FindMethodBody(root, location.SourceSpan);

                if (methodBody != null)
                {
                    var semanticModel = outgoingDoc != null ? await outgoingDoc.GetSemanticModelAsync() : null;

                    if (semanticModel != null)
                    {
                        var calls = new List<object>();
                        var visited = new HashSet<string>();
                        var docStrategy = SyntaxStrategy(outgoingDoc != null && IsVisualBasicDocument(outgoingDoc));

                        foreach (var invocation in docStrategy.FindInvocations(methodBody))
                        {
                            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                            if (symbolInfo.Symbol is IMethodSymbol calledMethod)
                            {
                                var key = calledMethod.ToDisplayString();
                                if (visited.Contains(key)) continue;
                                visited.Add(key);

                                var callLoc = calledMethod.Locations.FirstOrDefault(l => l.IsInSource);
                                calls.Add(new
                                {
                                    method = calledMethod.ToDisplayString(),
                                    shortName = $"{calledMethod.ContainingType?.Name}.{calledMethod.Name}",
                                    returnType = calledMethod.ReturnType.ToDisplayString(),
                                    isAsync = calledMethod.IsAsync,
                                    isExternal = callLoc == null
                                });
                            }
                        }

                        foreach (var access in docStrategy.FindMemberAccesses(methodBody))
                        {
                            var symbolInfo = semanticModel.GetSymbolInfo(access);
                            if (symbolInfo.Symbol is IPropertySymbol prop)
                            {
                                var key = prop.ToDisplayString();
                                if (visited.Contains(key)) continue;
                                visited.Add(key);

                                var propLoc = prop.Locations.FirstOrDefault(l => l.IsInSource);
                                calls.Add(new
                                {
                                    method = prop.ToDisplayString(),
                                    shortName = $"{prop.ContainingType?.Name}.{prop.Name}",
                                    returnType = prop.Type.ToDisplayString(),
                                    isAsync = false,
                                    isProperty = true,
                                    isExternal = propLoc == null
                                });
                            }
                        }

                        totalOutgoingCalls = calls.Count;
                        outgoingCalls = calls.Take(maxOutgoingCalls).ToList();
                    }
                }
            }
        }

        return CreateSuccessResponse(
            data: new
            {
                signature,
                callers,
                totalCallers,
                callersShown = callers?.Count ?? 0,
                outgoingCalls,
                totalOutgoingCalls,
                outgoingCallsShown = outgoingCalls?.Count ?? 0,
                location = GetSymbolLocation(method),
                overloadCount = type.GetMembers(methodName).OfType<IMethodSymbol>().Count()
            },
            suggestedNextTools: new[]
            {
                method.Locations.FirstOrDefault()?.IsInSource == true
                    ? $"analyze_data_flow to analyze variable flow within the method"
                    : null,
                $"find_implementations if {methodName} is virtual/interface method"
            }.Where(s => s != null).ToArray()!
        );
    }

}
