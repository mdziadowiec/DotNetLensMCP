using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using VBSyntax = Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> FindDisposableMisuseAsync(
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

            var compilation = await GetProjectCompilationAsync(project);
            if (compilation == null) continue;

            var disposableType = compilation.GetTypeByMetadataName("System.IDisposable");
            var asyncDisposableType = compilation.GetTypeByMetadataName("System.IAsyncDisposable");

            foreach (var document in project.Documents)
            {
                if (violations.Count >= maxResults) break;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel == null) continue;

                var root = await document.GetSyntaxRootAsync();
                if (root == null) continue;

                if (IsVisualBasicDocument(document))
                {
                    // VB: flag Dim x As IDisposable not inside a Using block
                    foreach (var localDecl in root.DescendantNodes().OfType<VBSyntax.LocalDeclarationStatementSyntax>())
                    {
                        if (violations.Count >= maxResults) break;
                        if (localDecl.Ancestors().OfType<VBSyntax.UsingBlockSyntax>().Any()) continue;

                        foreach (var declarator in localDecl.Declarators)
                        {
                            if (declarator.Initializer?.Value == null) continue;

                            var typeInfo = semanticModel.GetTypeInfo(declarator.Initializer.Value);
                            if (!IsDisposable(typeInfo.Type, disposableType, asyncDisposableType)) continue;

                            var loc = localDecl.GetLocation().GetLineSpan();
                            violations.Add(new
                            {
                                violationType = "disposable_not_in_using",
                                description = "IDisposable variable not inside a Using block",
                                variableName = declarator.Names.FirstOrDefault()?.Identifier.Text,
                                typeName = typeInfo.Type?.ToDisplayString(),
                                filePath = FormatPath(loc.Path),
                                line = loc.StartLinePosition.Line,
                                project = project.Name
                            });
                        }
                    }
                }
                else
                {
                    // C#: flag local declarations of IDisposable without 'using' keyword or using statement
                    foreach (var localDecl in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
                    {
                        if (violations.Count >= maxResults) break;
                        if (localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) continue;
                        if (localDecl.Ancestors().OfType<UsingStatementSyntax>().Any()) continue;

                        var typeInfo = semanticModel.GetTypeInfo(localDecl.Declaration.Type);
                        if (!IsDisposable(typeInfo.Type, disposableType, asyncDisposableType)) continue;

                        var name = localDecl.Declaration.Variables.FirstOrDefault()?.Identifier.Text;
                        var loc = localDecl.GetLocation().GetLineSpan();
                        violations.Add(new
                        {
                            violationType = "disposable_not_using",
                            description = "IDisposable variable declared without 'using' — may not be disposed",
                            variableName = name,
                            typeName = typeInfo.Type?.ToDisplayString(),
                            filePath = FormatPath(loc.Path),
                            line = loc.StartLinePosition.Line,
                            project = project.Name
                        });
                    }
                }
            }
        }

        return CreateSuccessResponse(
            data: new { violations },
            suggestedNextTools: new[]
            {
                "get_code_actions_at_position to apply 'Add using' fix",
                "analyze_data_flow to trace variable lifecycle"
            },
            totalCount: violations.Count,
            returnedCount: violations.Count
        );
    }

    private static bool IsDisposable(
        ITypeSymbol? type,
        INamedTypeSymbol? disposableType,
        INamedTypeSymbol? asyncDisposableType)
    {
        if (type == null) return false;
        if (type.TypeKind == TypeKind.Interface) return false; // Only flag concrete types
        if (disposableType == null && asyncDisposableType == null) return false;

        return type.AllInterfaces.Any(i =>
            (disposableType != null && SymbolEqualityComparer.Default.Equals(i, disposableType))
            || (asyncDisposableType != null && SymbolEqualityComparer.Default.Equals(i, asyncDisposableType)));
    }
}
