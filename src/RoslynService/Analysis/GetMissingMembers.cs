using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Gets members that must be implemented for interfaces/abstract classes.
    /// </summary>
    public async Task<object> GetMissingMembersAsync(string filePath, int line, int column)
    {
        EnsureSolutionLoaded();

        if (await GetCSharpOnlyToolErrorAsync(filePath, "get_missing_members") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

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

        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get semantic model",
                hint: "Check for syntax errors with get_diagnostics",
                context: new { filePath, line, column }
            );
        }

        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get syntax tree",
                context: new { filePath, line, column }
            );
        }

        var position = GetPosition(syntaxTree, line, column);
        var token = syntaxTree.GetRoot().FindToken(position);
        var node = token.Parent;

        // Find the type declaration
        var typeDecl = node?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeDecl == null)
        {
            return CreateErrorResponse(
                ErrorCodes.NotAType,
                "No type declaration found at position",
                hint: "Place cursor on a class or struct declaration",
                context: new { filePath, line, column }
            );
        }

        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        if (typeSymbol == null)
        {
            return CreateErrorResponse(
                ErrorCodes.SymbolNotFound,
                "Could not resolve type symbol",
                context: new { filePath, line, column }
            );
        }

        var missingMembers = new List<object>();

        // Check interfaces
        foreach (var iface in typeSymbol.AllInterfaces)
        {
            foreach (var member in iface.GetMembers())
            {
                if (member.IsStatic || member.DeclaredAccessibility == Accessibility.Private)
                    continue;

                var implementation = typeSymbol.FindImplementationForInterfaceMember(member);
                if (implementation == null)
                {
                    missingMembers.Add(new
                    {
                        fromInterface = iface.ToDisplayString(),
                        memberName = member.Name,
                        kind = member.Kind.ToString(),
                        signature = GetMemberSignature(member),
                        returnType = GetMemberReturnType(member)
                    });
                }
            }
        }

        // Check abstract base class members
        var baseType = typeSymbol.BaseType;
        while (baseType != null)
        {
            if (baseType.IsAbstract)
            {
                foreach (var member in baseType.GetMembers())
                {
                    if (!member.IsAbstract) continue;

                    var isImplemented = typeSymbol.GetMembers(member.Name)
                        .Any(m => m.IsOverride && !m.IsAbstract);

                    if (!isImplemented)
                    {
                        missingMembers.Add(new
                        {
                            fromAbstractClass = baseType.ToDisplayString(),
                            memberName = member.Name,
                            kind = member.Kind.ToString(),
                            signature = GetMemberSignature(member),
                            returnType = GetMemberReturnType(member)
                        });
                    }
                }
            }
            baseType = baseType.BaseType;
        }

        return CreateSuccessResponse(
            data: new
            {
                typeName = typeSymbol.ToDisplayString(),
                isAbstract = typeSymbol.IsAbstract,
                interfaces = typeSymbol.AllInterfaces.Select(i => i.ToDisplayString()).ToList(),
                missingMembers
            },
            suggestedNextTools: missingMembers.Count > 0
                ? new[] { "Use the signatures to implement the missing members" }
                : new[] { "All interface and abstract members are implemented" },
            totalCount: missingMembers.Count,
            returnedCount: missingMembers.Count
        );
    }

    // Helper for getting member signatures
    private string GetMemberSignature(ISymbol member)
    {
        return member switch
        {
            IMethodSymbol method => $"{method.ReturnType.ToDisplayString()} {method.Name}({string.Join(", ", method.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"))})",
            IPropertySymbol prop => $"{prop.Type.ToDisplayString()} {prop.Name} {{ {(prop.GetMethod != null ? "get; " : "")}{(prop.SetMethod != null ? "set; " : "")}}}",
            IEventSymbol evt => $"event {evt.Type.ToDisplayString()} {evt.Name}",
            _ => member.ToDisplayString()
        };
    }

    // Helper for getting member return types
    private string? GetMemberReturnType(ISymbol member)
    {
        return member switch
        {
            IMethodSymbol method => method.ReturnType.ToDisplayString(),
            IPropertySymbol prop => prop.Type.ToDisplayString(),
            IEventSymbol evt => evt.Type.ToDisplayString(),
            _ => null
        };
    }
}
