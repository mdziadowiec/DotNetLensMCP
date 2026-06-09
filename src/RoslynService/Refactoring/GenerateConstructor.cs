using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace DotNetLensMcp;

public partial class RoslynService
{
    /// <summary>
    /// Generates a constructor from fields and/or properties of a type.
    /// </summary>
    public async Task<object> GenerateConstructorAsync(
        string filePath,
        int line,
        int column,
        bool includeProperties = false,
        bool initializeToDefault = false)
    {
        EnsureSolutionLoaded();

        if (await GetCSharpOnlyToolErrorAsync(filePath, "generate_constructor") is { } unsupportedLanguageError)
            return unsupportedLanguageError;

        var document = _solution!.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => d.FilePath != null && d.FilePath.Equals(filePath, PathComparison));

        if (document == null)
        {
            return CreateErrorResponse(
                ErrorCodes.FileNotInSolution,
                $"File not found in solution: {filePath}",
                hint: "Ensure the file is part of a project in the loaded solution"
            );
        }

        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (semanticModel == null || syntaxTree == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not get semantic model",
                context: new { filePath }
            );
        }

        var text = await syntaxTree.GetTextAsync();
        var position = text.Lines.GetPosition(new LinePosition(line, column));
        var root = await syntaxTree.GetRootAsync();
        var node = root.FindToken(position).Parent;

        // Find the type declaration
        var typeDecl = node?.AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        if (typeDecl == null)
        {
            return CreateErrorResponse(
                ErrorCodes.NotAType,
                "No type declaration found at position",
                hint: "Position cursor on a class or struct declaration",
                context: new { line, column }
            );
        }

        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        if (typeSymbol == null)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "Could not resolve type symbol",
                context: new { typeName = typeDecl.Identifier.Text }
            );
        }

        // Collect fields
        var fields = typeSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => !f.IsStatic && !f.IsConst && !f.IsImplicitlyDeclared)
            .Select(f => new
            {
                name = f.Name,
                type = f.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                isReadOnly = f.IsReadOnly,
                isNullable = f.NullableAnnotation == NullableAnnotation.Annotated,
                paramName = ToCamelCase(f.Name.TrimStart('_'))
            })
            .ToList();

        // Collect properties if requested
        var properties = new List<dynamic>();
        if (includeProperties)
        {
            properties = typeSymbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => !p.IsStatic && !p.IsReadOnly && p.SetMethod != null &&
                           p.SetMethod.DeclaredAccessibility != Accessibility.Private)
                .Select(p => new
                {
                    name = p.Name,
                    type = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    isReadOnly = false,
                    isNullable = p.NullableAnnotation == NullableAnnotation.Annotated,
                    paramName = ToCamelCase(p.Name)
                })
                .Cast<dynamic>()
                .ToList();
        }

        var allMembers = fields.Cast<dynamic>().Concat(properties).ToList();

        if (allMembers.Count == 0)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                "No fields or properties found to initialize",
                hint: includeProperties
                    ? "The type has no instance fields or settable properties"
                    : "The type has no instance fields. Try includeProperties: true",
                context: new { typeName = typeSymbol.Name }
            );
        }

        // Build constructor code
        var sb = new StringBuilder();
        var typeName = typeSymbol.Name;
        var accessibility = typeSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";

        // Parameters
        var parameters = allMembers
            .Select(m => $"{m.type} {m.paramName}")
            .ToList();

        sb.AppendLine($"{accessibility} {typeName}({string.Join(", ", parameters)})");
        sb.AppendLine("{");

        foreach (var member in allMembers)
        {
            string memberName = member.name;
            string paramName = member.paramName;

            if (initializeToDefault && (bool)member.isNullable)
            {
                sb.AppendLine($"    {memberName} = {paramName} ?? default;");
            }
            else
            {
                sb.AppendLine($"    {memberName} = {paramName};");
            }
        }

        sb.Append("}");

        return CreateSuccessResponse(
            data: new
            {
                typeName = typeSymbol.ToDisplayString(),
                constructorCode = sb.ToString(),
                parameterCount = allMembers.Count,
                fields = fields.Select(f => f.name).ToList(),
                properties = properties.Select(p => (string)p.name).ToList(),
                parameters = allMembers.Select(m => new { name = (string)m.paramName, type = (string)m.type }).ToList()
            },
            suggestedNextTools: new[]
            {
                "validate_code to check the constructor compiles",
                "Use Edit tool to insert the constructor into the class"
            }
        );
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (name.Length == 1) return name.ToLowerInvariant();
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
