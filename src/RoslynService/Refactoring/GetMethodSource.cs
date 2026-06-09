using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Gets the actual source code of a method by name.
    /// </summary>
    public async Task<object> GetMethodSourceAsync(string typeName, string methodName, int? overloadIndex = null)
    {
        EnsureSolutionLoaded();

        var type = await FindTypeByNameAsync(typeName);
        if (type == null)
        {
            return CreateErrorResponse(
                ErrorCodes.TypeNotFound,
                $"Type not found: {typeName}",
                hint: "Use search_symbols to find the correct type name",
                context: new { typeName }
            );
        }

        // Find all methods with matching name
        var methods = type.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .ToList();

        if (methods.Count == 0)
        {
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                $"Method '{methodName}' not found in type '{typeName}'",
                hint: "Use get_type_members to see available methods",
                context: new { typeName, methodName }
            );
        }

        // Select specific overload or default to first
        var index = overloadIndex ?? 0;
        if (index < 0 || index >= methods.Count)
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidParameter,
                $"Invalid overloadIndex {index}. Available: 0 to {methods.Count - 1}",
                context: new { overloadIndex, availableOverloads = methods.Count }
            );
        }

        var method = methods[index];
        var location = method.Locations.FirstOrDefault(l => l.IsInSource);

        if (location == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Method is defined in metadata (external library), source not available",
                hint: "This method is from a compiled assembly, not source code",
                context: new { typeName, methodName, isFromMetadata = true }
            );
        }

        if (await GetCSharpOnlyToolErrorAsync(location.SourceTree?.FilePath, "get_method_source") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

        // Get the syntax node
        var syntaxTree = location.SourceTree;
        if (syntaxTree == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get syntax tree for method",
                context: new { typeName, methodName }
            );
        }

        var root = await syntaxTree.GetRootAsync();
        var node = root.FindNode(location.SourceSpan);

        // Find the method declaration
        var methodDecl = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodDecl == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not find method declaration syntax",
                context: new { typeName, methodName }
            );
        }

        var lineSpan = location.GetLineSpan();
        var sourceText = methodDecl.ToFullString();

        // Also get just the body if available
        string? bodySource = null;
        if (methodDecl.Body != null)
        {
            bodySource = methodDecl.Body.ToFullString();
        }
        else if (methodDecl.ExpressionBody != null)
        {
            bodySource = methodDecl.ExpressionBody.ToFullString();
        }

        return CreateSuccessResponse(
            data: new
            {
                typeName = type.ToDisplayString(),
                methodName = method.Name,
                signature = method.ToDisplayString(),
                overloadIndex = index,
                totalOverloads = methods.Count,
                location = new
                {
                    filePath = FormatPath(lineSpan.Path),
                    startLine = lineSpan.StartLinePosition.Line,
                    endLine = lineSpan.EndLinePosition.Line
                },
                fullSource = sourceText,
                bodySource,
                lineCount = sourceText.Split('\n').Length
            },
            suggestedNextTools: new[]
            {
                "get_outgoing_calls to see what this method calls",
                "find_callers to see who calls this method"
            }
        );
    }

    /// <summary>
    /// Gets source code for multiple methods in a single call (batch optimization).
    /// </summary>
    public async Task<object> GetMethodSourceBatchAsync(
        List<Dictionary<string, object>> methods,
        int maxMethods = 20)
    {
        EnsureSolutionLoaded();

        if (methods == null || methods.Count == 0)
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidParameter,
                "methods array is required and must not be empty",
                hint: "Provide an array like [{typeName: 'MyClass', methodName: 'MyMethod'}, ...]",
                context: new { parameter = "methods" }
            );
        }

        if (methods.Count > maxMethods)
        {
            return CreateErrorResponse(
                ErrorCodes.InvalidParameter,
                $"Too many methods requested ({methods.Count}). Maximum is {maxMethods}",
                hint: $"Split request into batches of {maxMethods} or fewer"
            );
        }

        var results = new List<object>();
        var errors = new List<object>();

        foreach (var methodReq in methods)
        {
            var typeName = methodReq.TryGetValue("typeName", out var tn) ? tn?.ToString() : null;
            var methodName = methodReq.TryGetValue("methodName", out var mn) ? mn?.ToString() : null;
            int? overloadIndex = methodReq.TryGetValue("overloadIndex", out var oi) && oi != null
                ? Convert.ToInt32(oi)
                : null;

            if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(methodName))
            {
                errors.Add(new
                {
                    typeName,
                    methodName,
                    success = false,
                    error = "typeName and methodName are required"
                });
                continue;
            }

            var result = await GetMethodSourceAsync(typeName, methodName, overloadIndex);

            // Check if result was successful
            var resultDict = result as dynamic;
            if (resultDict?.success == true)
            {
                results.Add(new
                {
                    typeName,
                    methodName,
                    success = true,
                    data = resultDict.data
                });
            }
            else
            {
                errors.Add(new
                {
                    typeName,
                    methodName,
                    success = false,
                    error = resultDict?.error
                });
            }
        }

        return CreateSuccessResponse(
            data: new
            {
                totalRequested = methods.Count,
                successCount = results.Count,
                errorCount = errors.Count,
                results,
                errors = errors.Count > 0 ? errors : null
            },
            suggestedNextTools: new[]
            {
                results.Count > 0 ? "analyze_method for deeper analysis of specific methods" : null,
                errors.Count > 0 ? "Check type/method names - some were not found" : null
            }.Where(s => s != null).ToArray()!,
            totalCount: methods.Count,
            returnedCount: results.Count
        );
    }
}
