using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace DotNetLensMcp;

public partial class RoslynService
{
    private object FormatMember(ISymbol member, string verbosity)
    {
        var kind = GetMemberKindString(member);

        // Summary: minimal info
        if (verbosity == "summary")
        {
            return new
            {
                name = member.Name,
                kind
            };
        }

        // Compact: add signature and key properties
        var result = new Dictionary<string, object>
        {
            ["name"] = member.Name,
            ["kind"] = kind,
            ["accessibility"] = member.DeclaredAccessibility.ToString(),
            ["isStatic"] = member.IsStatic
        };

        if (member is IMethodSymbol method)
        {
            result["signature"] = method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            result["returnType"] = method.ReturnType.ToDisplayString();
            result["isAsync"] = method.IsAsync;
            result["isVirtual"] = method.IsVirtual;
            result["isOverride"] = method.IsOverride;
            result["isAbstract"] = method.IsAbstract;
        }
        else if (member is IPropertySymbol property)
        {
            result["type"] = property.Type.ToDisplayString();
            result["hasGetter"] = property.GetMethod != null;
            result["hasSetter"] = property.SetMethod != null;
            result["isVirtual"] = property.IsVirtual;
        }
        else if (member is IFieldSymbol field)
        {
            result["type"] = field.Type.ToDisplayString();
            result["isReadOnly"] = field.IsReadOnly;
            result["isConst"] = field.IsConst;
        }
        else if (member is IEventSymbol evt)
        {
            result["type"] = evt.Type.ToDisplayString();
        }

        // Full: add documentation, attributes, location
        if (verbosity == "full")
        {
            result["documentation"] = member.GetDocumentationCommentXml() ?? "";
            result["attributes"] = member.GetAttributes()
                .Select(a => a.AttributeClass?.Name ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .ToArray();
            result["location"] = GetSymbolLocation(member) ?? new { filePath = "", line = 0, column = 0 };
            result["containingType"] = member.ContainingType?.ToDisplayString() ?? "";
        }

        return result;
    }

    private static string GetMemberKindString(ISymbol member)
    {
        return member switch
        {
            IMethodSymbol m when m.MethodKind == MethodKind.Ordinary => "Method",
            IMethodSymbol m when m.MethodKind == MethodKind.Constructor => "Constructor",
            IPropertySymbol => "Property",
            IFieldSymbol => "Field",
            IEventSymbol => "Event",
            _ => member.Kind.ToString()
        };
    }
}
