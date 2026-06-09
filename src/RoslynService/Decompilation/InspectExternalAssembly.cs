using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> InspectExternalAssemblyAsync(
        string assemblyName,
        string mode = "summary",
        string? namespaceFilter = null)
    {
        EnsureSolutionLoaded();

        var assembly = await FindAssemblySymbolAsync(assemblyName);
        if (assembly == null)
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                $"Assembly '{assemblyName}' is not referenced by any project in the loaded solution.",
                hint: "Use roslyn_get_nuget_dependencies to list referenced assemblies, then pass the exact assembly name.");

        return mode switch
        {
            "summary"   => BuildAssemblySummary(assembly),
            "namespace" => BuildAssemblyNamespace(assembly, namespaceFilter),
            _ => CreateErrorResponse(
                ErrorCodes.InvalidArgument,
                $"Unknown mode '{mode}'. Expected \"summary\" or \"namespace\".")
        };
    }

    private object BuildAssemblySummary(IAssemblySymbol assembly)
    {
        var byNamespace = new SortedDictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        foreach (var type in GetAllNamedTypesInNamespace(assembly.GlobalNamespace))
        {
            if (type.DeclaredAccessibility != Accessibility.Public || type.ContainingType != null)
                continue;
            var ns = type.ContainingNamespace.ToDisplayString();
            if (!byNamespace.TryGetValue(ns, out var list))
                byNamespace[ns] = list = [];
            list.Add(type);
        }

        var namespaceTree = byNamespace.Select(kv => new
        {
            @namespace  = kv.Key,
            typeCount   = kv.Value.Count,
            publicTypes = kv.Value.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList()
        }).ToList();

        var id = assembly.Identity;
        return CreateSuccessResponse(
            data: new
            {
                mode            = "summary",
                name            = id.Name,
                version         = id.Version.ToString(),
                targetFramework = GetAssemblyTargetFramework(assembly),
                publicKeyToken  = FormatPublicKeyToken(id.PublicKeyToken),
                namespaceTree
            },
            suggestedNextTools: new[]
            {
                "roslyn_inspect_external_assembly with mode=\"namespace\" and namespaceFilter to get full type details",
                "roslyn_peek_il to disassemble a specific method"
            },
            totalCount: byNamespace.Values.Sum(l => l.Count),
            returnedCount: byNamespace.Values.Sum(l => l.Count));
    }

    private object BuildAssemblyNamespace(IAssemblySymbol assembly, string? namespaceFilter)
    {
        if (namespaceFilter == null)
            return CreateErrorResponse(
                ErrorCodes.InvalidArgument,
                "'namespaceFilter' is required when mode=\"namespace\".",
                hint: "Use mode=\"summary\" first to discover available namespaces.");

        var types = GetAllNamedTypesInNamespace(assembly.GlobalNamespace)
            .Where(t => t.DeclaredAccessibility == Accessibility.Public
                     && t.ContainingType == null
                     && string.Equals(t.ContainingNamespace.ToDisplayString(), namespaceFilter, StringComparison.Ordinal))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        if (types.Count == 0)
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                $"Namespace '{namespaceFilter}' not found or contains no public types in assembly '{assembly.Identity.Name}'.",
                hint: "Use mode=\"summary\" to see available namespaces.");

        var typeInfos = types.Select(ExternalTypeInfo).ToList();

        var id = assembly.Identity;
        return CreateSuccessResponse(
            data: new
            {
                mode            = "namespace",
                name            = id.Name,
                version         = id.Version.ToString(),
                targetFramework = GetAssemblyTargetFramework(assembly),
                publicKeyToken  = FormatPublicKeyToken(id.PublicKeyToken),
                @namespace      = namespaceFilter,
                types           = typeInfos
            },
            suggestedNextTools: new[]
            {
                "roslyn_peek_il to disassemble a specific method",
                "roslyn_get_type_members to explore a source type's members"
            },
            totalCount: typeInfos.Count,
            returnedCount: typeInfos.Count);
    }

    private static object ExternalTypeInfo(INamedTypeSymbol t)
    {
        var kind = t.TypeKind switch
        {
            TypeKind.Interface => "interface",
            TypeKind.Struct    => "struct",
            TypeKind.Enum      => "enum",
            TypeKind.Delegate  => "delegate",
            _                  => "class"
        };

        var modifiers = new List<string>();
        if (t.IsAbstract && !t.IsStatic && t.TypeKind != TypeKind.Interface) modifiers.Add("abstract");
        if (t.IsSealed   && !t.IsStatic)                                      modifiers.Add("sealed");
        if (t.IsStatic)                                                        modifiers.Add("static");

        var baseType   = t.BaseType is { SpecialType: not SpecialType.System_Object }
            ? t.BaseType.ToDisplayString() : null;
        var interfaces = t.Interfaces.Select(i => i.ToDisplayString()).ToList();

        var isInterface = t.TypeKind == TypeKind.Interface;
        var members = new List<object>();
        var seen    = new HashSet<string>(StringComparer.Ordinal);

        var memberSources = isInterface
            ? new[] { t }.Concat(t.AllInterfaces.Cast<ITypeSymbol>())
            : [(ITypeSymbol)t];

        foreach (var source in memberSources)
        {
            foreach (var m in source.GetMembers())
            {
                var accessible = m.DeclaredAccessibility == Accessibility.Public
                    || (isInterface && m.DeclaredAccessibility == Accessibility.NotApplicable);
                if (!accessible || m.IsImplicitlyDeclared) continue;
                if (m is IMethodSymbol { MethodKind:
                        MethodKind.PropertyGet or MethodKind.PropertySet or
                        MethodKind.EventAdd    or MethodKind.EventRemove or
                        MethodKind.EventRaise  or MethodKind.StaticConstructor or
                        MethodKind.DelegateInvoke })
                    continue;
                var sig = m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                if (!seen.Add(sig)) continue;
                members.Add(new
                {
                    kind      = ExternalMemberKind(m),
                    signature = sig,
                    xmlDoc    = XmlDocSummary(m)
                });
            }
        }

        return new
        {
            kind,
            fullName   = t.ToDisplayString(),
            modifiers,
            baseType,
            interfaces,
            members,
            attributes = t.GetAttributes().Select(a => a.AttributeClass?.Name ?? "Attribute").ToList(),
            xmlDoc     = XmlDocSummary(t)
        };
    }

    private static string ExternalMemberKind(ISymbol s) => s switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor }                              => "constructor",
        IMethodSymbol { MethodKind: MethodKind.Conversion or MethodKind.UserDefinedOperator } => "operator",
        IMethodSymbol { MethodKind: MethodKind.Destructor }                               => "destructor",
        IMethodSymbol                                                                       => "method",
        IPropertySymbol                                                                     => "property",
        IFieldSymbol                                                                        => "field",
        IEventSymbol                                                                        => "event",
        _                                                                                   => "symbol"
    };

    private static string? XmlDocSummary(ISymbol s)
    {
        var xml = s.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml)) return null;
        var start = xml.IndexOf("<summary>", StringComparison.Ordinal);
        var end   = xml.IndexOf("</summary>", StringComparison.Ordinal);
        if (start < 0 || end <= start) return null;
        return xml[(start + "<summary>".Length)..end].Trim();
    }

    private static string? GetAssemblyTargetFramework(IAssemblySymbol assembly)
    {
        var attr = assembly.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass is { Name: "TargetFrameworkAttribute" } ac &&
            string.Equals(
                ac.ContainingNamespace?.ToDisplayString(),
                "System.Runtime.Versioning",
                StringComparison.Ordinal));
        return attr?.ConstructorArguments.FirstOrDefault().Value as string;
    }

    private static string? FormatPublicKeyToken(ImmutableArray<byte> key)
        => key.IsDefaultOrEmpty ? null : string.Concat(key.Select(b => b.ToString("x2")));
}
