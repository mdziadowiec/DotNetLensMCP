using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> PeekIlAsync(string methodSymbol)
    {
        EnsureSolutionLoaded();

        // Ensure all project compilations are loaded so FindAssemblyPath can search them
        foreach (var project in _solution!.Projects)
            await GetProjectCompilationAsync(project);

        var method = ResolveMetadataMethod(methodSymbol);
        if (method == null)
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                $"Method '{methodSymbol}' not found in any referenced (metadata-only) assembly.",
                hint: "Pass a fully-qualified method name, e.g. \"Newtonsoft.Json.JsonConvert.SerializeObject(object)\". " +
                      "For source methods use roslyn_go_to_definition instead.");

        if (method.Locations.Any(l => l.IsInSource))
            return CreateErrorResponse(
                ErrorCodes.InvalidOperation,
                $"'{methodSymbol}' is defined in source. Use roslyn_go_to_definition to navigate to it.",
                hint: "peek_il only works on methods from referenced (closed-source) assemblies.");

        if (method.IsAbstract || (method.ContainingType?.TypeKind == TypeKind.Interface && !method.IsStatic))
            return CreateErrorResponse(
                ErrorCodes.InvalidOperation,
                $"'{methodSymbol}' has no IL body (abstract or interface instance member).",
                hint: "Choose a concrete implementation. Use roslyn_find_implementations to locate one.");

        var assembly = method.ContainingAssembly!;
        var assemblyPath = FindAssemblyPath(assembly);
        if (assemblyPath == null)
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                $"Could not locate on-disk path for assembly '{assembly.Identity.Name}'.",
                hint: "The assembly may only exist as a facade/reference assembly without a real .dll on disk.");

        var token = MetadataTokenOf(method) ?? MetadataTokenFromPE(GetDisassembler().Cache, assemblyPath, method);
        if (token == null)
            return CreateErrorResponse(
                ErrorCodes.InvalidOperation,
                $"Could not determine metadata token for '{methodSymbol}'.",
                hint: "Try providing the full signature including parameter types.");

        var ilText = GetDisassembler().DisassembleMethod(assemblyPath, token.Value);

        return CreateSuccessResponse(
            data: new
            {
                methodFullName  = method.ToDisplayString(),
                assemblyName    = assembly.Identity.Name,
                assemblyVersion = assembly.Identity.Version.ToString(),
                il              = ilText
            },
            suggestedNextTools: new[]
            {
                "roslyn_inspect_external_assembly to browse the assembly's public API",
                "roslyn_get_type_members to see other members of the same type"
            });
    }

    private IMethodSymbol? ResolveMetadataMethod(string methodSymbol)
    {
        var parenIdx = methodSymbol.IndexOf('(', StringComparison.Ordinal);
        var nameWithoutParams = parenIdx >= 0 ? methodSymbol[..parenIdx] : methodSymbol;
        var paramSignature = parenIdx >= 0 ? methodSymbol[parenIdx..] : null;

        string typeName;
        string memberName;

        var ctorIdx = nameWithoutParams.LastIndexOf("..ctor", StringComparison.Ordinal);
        if (ctorIdx >= 0)
        {
            typeName = nameWithoutParams[..ctorIdx];
            memberName = ".ctor";
        }
        else
        {
            var lastDot = nameWithoutParams.LastIndexOf('.');
            if (lastDot <= 0) return null;
            typeName = nameWithoutParams[..lastDot];
            memberName = nameWithoutParams[(lastDot + 1)..];
        }

        foreach (var compilation in _compilationCache.Values)
        {
            var container = compilation.GetTypeByMetadataName(typeName);
            if (container == null || container.Locations.Any(l => l.IsInSource))
                continue;

            var candidates = container.GetMembers(memberName).OfType<IMethodSymbol>().ToList();
            if (candidates.Count == 0) continue;

            if (paramSignature == null || candidates.Count == 1)
                return candidates[0];

            // Match by display-string suffix
            foreach (var candidate in candidates)
            {
                if (candidate.ToDisplayString().Contains(paramSignature, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            // Fallback: match by parameter count
            var paramTypes = ParseParamTypes(paramSignature.TrimStart('(').TrimEnd(')'));
            var byCount = candidates.Where(c => c.Parameters.Length == paramTypes.Count).ToList();
            if (byCount.Count == 1) return byCount[0];
            if (byCount.Count > 1) return byCount[0];
        }

        return null;
    }

    private static List<string> ParseParamTypes(string paramList)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(paramList)) return result;

        var depth = 0;
        var start = 0;
        for (var i = 0; i < paramList.Length; i++)
        {
            var c = paramList[i];
            if (c is '<' or '(') depth++;
            else if (c is '>' or ')') depth--;
            else if (c == ',' && depth == 0)
            {
                result.Add(paramList[start..i].Trim());
                start = i + 1;
            }
        }
        var last = paramList[start..].Trim();
        if (last.Length > 0) result.Add(last);
        return result;
    }

    private static int? MetadataTokenOf(IMethodSymbol method)
    {
        var prop = method.GetType().GetProperty("MetadataToken",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Instance);
        if (prop?.GetValue(method) is int token && token != 0)
            return token;
        return null;
    }

    private static int? MetadataTokenFromPE(PEFileCache cache, string assemblyPath, IMethodSymbol method)
    {
        var pe = cache.Get(assemblyPath);
        var reader = pe.Metadata;

        var typeName = method.ContainingType?.MetadataName ?? "";
        var ns = method.ContainingNamespace?.ToDisplayString() ?? "";
        var roslynParamNames = method.Parameters.Select(p => p.Name).ToArray();
        var roslynParamCount = method.Parameters.Length;

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (!string.Equals(reader.GetString(typeDef.Name), typeName, StringComparison.Ordinal))
                continue;
            if (!string.IsNullOrEmpty(ns) &&
                !string.Equals(reader.GetString(typeDef.Namespace), ns, StringComparison.Ordinal))
                continue;

            var candidates = new List<MethodDefinitionHandle>();
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var methodDef = reader.GetMethodDefinition(methodHandle);
                if (!string.Equals(reader.GetString(methodDef.Name), method.Name, StringComparison.Ordinal))
                    continue;

                var peParams = methodDef.GetParameters()
                    .Select(ph => reader.GetParameter(ph))
                    .Where(p => p.SequenceNumber > 0)
                    .ToList();

                if (peParams.Count != roslynParamCount) continue;
                candidates.Add(methodHandle);
            }

            if (candidates.Count == 0) return null;
            if (candidates.Count == 1) return MetadataTokens.GetToken(candidates[0]);

            // Multiple overloads — match by parameter names
            foreach (var methodHandle in candidates)
            {
                var methodDef = reader.GetMethodDefinition(methodHandle);
                var peParamNames = methodDef.GetParameters()
                    .Select(ph => reader.GetParameter(ph))
                    .Where(p => p.SequenceNumber > 0)
                    .Select(p => reader.GetString(p.Name))
                    .ToArray();

                if (roslynParamNames.SequenceEqual(peParamNames, StringComparer.Ordinal))
                    return MetadataTokens.GetToken(methodHandle);
            }

            return MetadataTokens.GetToken(candidates[0]);
        }

        return null;
    }
}
