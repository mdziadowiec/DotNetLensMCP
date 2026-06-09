using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotNetLensMcp;

internal sealed class CSharpSyntaxStrategy : ILanguageSyntaxStrategy, ICodeValidationStrategy
{
    public SyntaxNode? FindMethodBody(SyntaxNode root, TextSpan span)
        => root.FindNode(span).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();

    public IEnumerable<SyntaxNode> FindInvocations(SyntaxNode scope)
        => scope.DescendantNodes().OfType<InvocationExpressionSyntax>();

    public IEnumerable<SyntaxNode> FindMemberAccesses(SyntaxNode scope)
        => scope.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

    public string? GetInvocationMethodName(SyntaxNode invocationNode)
    {
        if (invocationNode is not InvocationExpressionSyntax inv) return null;
        return inv.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };
    }

    public SyntaxNode? FindContainingMember(SyntaxNode node)
        => node.AncestorsAndSelf().FirstOrDefault(n =>
            n is MethodDeclarationSyntax or
            PropertyDeclarationSyntax or
            ConstructorDeclarationSyntax or
            ClassDeclarationSyntax or
            StructDeclarationSyntax or
            InterfaceDeclarationSyntax);

    public IEnumerable<string> ExtractImports(SyntaxNode root)
        => root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(u => u.Name?.ToString() ?? "")
            .Where(s => !string.IsNullOrEmpty(s));

    public string? ExtractNamespace(SyntaxNode root)
        => root.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault()?.Name.ToString();

    public IEnumerable<SyntaxNode> FindTypeDeclarations(SyntaxNode root)
        => root.DescendantNodes().OfType<TypeDeclarationSyntax>();

    public (string name, string kind, int memberCount) ExtractTypeInfo(SyntaxNode typeNode, SemanticModel? semanticModel)
    {
        if (typeNode is not TypeDeclarationSyntax td) return ("", "", 0);
        var symbol = semanticModel?.GetDeclaredSymbol(td);
        return (
            name: symbol?.Name ?? td.Identifier.Text,
            kind: td.Kind().ToString().Replace("Declaration", ""),
            memberCount: td.Members.Count
        );
    }

    public IEnumerable<SyntaxNode> FindBlockStatements(SyntaxNode root, TextSpan span)
    {
        var node = root.FindNode(span);
        return node.DescendantNodesAndSelf().OfType<StatementSyntax>();
    }

    public IEnumerable<SyntaxNode> FindMethodNodes(SyntaxNode root)
        => root.DescendantNodes().OfType<MethodDeclarationSyntax>();

    public IMethodSymbol? GetMethodSymbol(SemanticModel semanticModel, SyntaxNode methodNode)
        => methodNode is MethodDeclarationSyntax m ? semanticModel.GetDeclaredSymbol(m) : null;

    public int CountNodeComplexity(SyntaxNode node) => node switch
    {
        IfStatementSyntax => 1,
        ConditionalExpressionSyntax => 1,
        CaseSwitchLabelSyntax => 1,
        CasePatternSwitchLabelSyntax => 1,
        WhileStatementSyntax => 1,
        ForStatementSyntax => 1,
        ForEachStatementSyntax => 1,
        CatchClauseSyntax => 1,
        ConditionalAccessExpressionSyntax => 1,
        BinaryExpressionSyntax b when
            b.Kind() is SyntaxKind.LogicalAndExpression
                     or SyntaxKind.LogicalOrExpression
                     or SyntaxKind.CoalesceExpression => 1,
        _ => 0
    };

    public bool IsControlFlowNode(SyntaxNode node)
        => node is IfStatementSyntax
                or WhileStatementSyntax
                or ForStatementSyntax
                or ForEachStatementSyntax
                or SwitchStatementSyntax
                or TryStatementSyntax
                or LockStatementSyntax
                or UsingStatementSyntax;

    public int CognitiveWeight(SyntaxNode node, int depth) => node switch
    {
        IfStatementSyntax      => 1 + depth,
        WhileStatementSyntax   => 1 + depth,
        ForStatementSyntax     => 1 + depth,
        ForEachStatementSyntax => 1 + depth,
        SwitchStatementSyntax  => 1 + depth,
        CatchClauseSyntax      => 1 + depth,
        ConditionalExpressionSyntax => 1 + depth,
        ElseClauseSyntax       => 1,
        BinaryExpressionSyntax b when
            b.Kind() is SyntaxKind.LogicalAndExpression
                     or SyntaxKind.LogicalOrExpression => 1,
        _ => 0
    };

    public int? GetParameterCount(SyntaxNode node)
        => (node as MethodDeclarationSyntax)?.ParameterList.Parameters.Count;

    // ICodeValidationStrategy
    public SyntaxTree ParseCode(string code)
        => CSharpSyntaxTree.ParseText(code);

    public Compilation CreateStandaloneCompilation(string name, SyntaxTree tree, IEnumerable<MetadataReference> references)
        => CSharpCompilation.Create(name, [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    public string WrapCodeWithContext(string code, SyntaxNode contextRoot)
    {
        var csRoot = contextRoot as CompilationUnitSyntax;
        var usings = csRoot?.Usings.ToFullString() ?? "";
        var nsName = csRoot?.Members.OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "";
        return $@"
{usings}
namespace {(string.IsNullOrEmpty(nsName) ? "ValidationNamespace" : nsName)} {{
    public class ValidationClass {{
        public void ValidationMethod() {{
            {code}
        }}
    }}
}}";
    }

    public string WrapCodeDefault(string code) => $@"
using System;
using System.Collections.Generic;
using System.Linq;

namespace ValidationNamespace {{
    public class ValidationClass {{
        public void ValidationMethod() {{
            {code}
        }}
    }}
}}";
}
