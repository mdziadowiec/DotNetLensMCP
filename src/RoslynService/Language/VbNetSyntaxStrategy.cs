using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using VBSyntax = Microsoft.CodeAnalysis.VisualBasic.Syntax;
using VBSyntaxKind = Microsoft.CodeAnalysis.VisualBasic.SyntaxKind;

namespace DotNetLensMcp;

internal sealed class VbNetSyntaxStrategy : ILanguageSyntaxStrategy, ICodeValidationStrategy
{
    public SyntaxNode? FindMethodBody(SyntaxNode root, TextSpan span)
        => root.FindNode(span).AncestorsAndSelf().OfType<VBSyntax.MethodBlockSyntax>().FirstOrDefault();

    public IEnumerable<SyntaxNode> FindInvocations(SyntaxNode scope)
        => scope.DescendantNodes().OfType<VBSyntax.InvocationExpressionSyntax>();

    public IEnumerable<SyntaxNode> FindMemberAccesses(SyntaxNode scope)
        => scope.DescendantNodes().OfType<VBSyntax.MemberAccessExpressionSyntax>();

    public string? GetInvocationMethodName(SyntaxNode invocationNode)
    {
        if (invocationNode is not VBSyntax.InvocationExpressionSyntax inv) return null;
        return inv.Expression switch
        {
            VBSyntax.MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            VBSyntax.IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };
    }

    public SyntaxNode? FindContainingMember(SyntaxNode node)
        => node.AncestorsAndSelf().FirstOrDefault(n =>
            n is VBSyntax.MethodBlockSyntax or
            VBSyntax.PropertyBlockSyntax or
            VBSyntax.ConstructorBlockSyntax or
            VBSyntax.AccessorBlockSyntax or
            VBSyntax.ClassBlockSyntax or
            VBSyntax.StructureBlockSyntax or
            VBSyntax.InterfaceBlockSyntax or
            VBSyntax.ModuleBlockSyntax);

    public IEnumerable<string> ExtractImports(SyntaxNode root)
        => root.DescendantNodes()
            .OfType<VBSyntax.ImportsStatementSyntax>()
            .SelectMany(i => i.ImportsClauses)
            .Select(c => c.ToString())
            .Where(s => !string.IsNullOrEmpty(s));

    public string? ExtractNamespace(SyntaxNode root)
        => root.DescendantNodes()
            .OfType<VBSyntax.NamespaceBlockSyntax>()
            .FirstOrDefault()?.NamespaceStatement.Name.ToString();

    public IEnumerable<SyntaxNode> FindTypeDeclarations(SyntaxNode root)
        => root.DescendantNodes().Where(n =>
            n is VBSyntax.ClassBlockSyntax or
            VBSyntax.InterfaceBlockSyntax or
            VBSyntax.StructureBlockSyntax or
            VBSyntax.ModuleBlockSyntax);

    public (string name, string kind, int memberCount) ExtractTypeInfo(SyntaxNode typeNode, SemanticModel? semanticModel)
        => typeNode switch
        {
            VBSyntax.ClassBlockSyntax cls       => (cls.ClassStatement.Identifier.Text,      "Class",     cls.Members.Count),
            VBSyntax.InterfaceBlockSyntax iface => (iface.InterfaceStatement.Identifier.Text, "Interface", iface.Members.Count),
            VBSyntax.StructureBlockSyntax str   => (str.StructureStatement.Identifier.Text,   "Structure", str.Members.Count),
            VBSyntax.ModuleBlockSyntax mod      => (mod.ModuleStatement.Identifier.Text,      "Module",    mod.Members.Count),
            _                                   => ("", "", 0)
        };

    public IEnumerable<SyntaxNode> FindBlockStatements(SyntaxNode root, TextSpan span)
    {
        var startTok = root.FindToken(span.Start);
        foreach (var ancestor in startTok.Parent?.AncestorsAndSelf() ?? Enumerable.Empty<SyntaxNode>())
        {
            IEnumerable<VBSyntax.StatementSyntax>? blockStmts = ancestor switch
            {
                VBSyntax.MethodBlockSyntax m       => m.Statements,
                VBSyntax.MultiLineIfBlockSyntax ifB => ifB.Statements,
                VBSyntax.ElseBlockSyntax eb         => eb.Statements,
                VBSyntax.ElseIfBlockSyntax eib      => eib.Statements,
                VBSyntax.ForBlockSyntax f           => f.Statements,
                VBSyntax.ForEachBlockSyntax fe      => fe.Statements,
                VBSyntax.WhileBlockSyntax w         => w.Statements,
                VBSyntax.DoLoopBlockSyntax d        => d.Statements,
                _                                   => null
            };

            if (blockStmts != null)
            {
                var filtered = blockStmts
                    .Where(s => s.Span.IntersectsWith(span))
                    .Cast<SyntaxNode>()
                    .ToList();
                if (filtered.Count > 0) return filtered;
            }
        }
        return [];
    }

    public IEnumerable<SyntaxNode> FindMethodNodes(SyntaxNode root)
        => root.DescendantNodes().OfType<VBSyntax.MethodBlockSyntax>();

    public IMethodSymbol? GetMethodSymbol(SemanticModel semanticModel, SyntaxNode methodNode)
        => methodNode is VBSyntax.MethodBlockSyntax m
            ? semanticModel.GetDeclaredSymbol(m.SubOrFunctionStatement)
            : null;

    public int CountNodeComplexity(SyntaxNode node) => node switch
    {
        VBSyntax.IfStatementSyntax => 1,
        VBSyntax.ElseIfStatementSyntax => 1,
        VBSyntax.CaseBlockSyntax => 1,
        VBSyntax.ForBlockSyntax => 1,
        VBSyntax.ForEachBlockSyntax => 1,
        VBSyntax.WhileBlockSyntax => 1,
        VBSyntax.DoLoopBlockSyntax => 1,
        VBSyntax.CatchBlockSyntax => 1,
        VBSyntax.BinaryExpressionSyntax vbBin when
            vbBin.Kind() is VBSyntaxKind.AndAlsoExpression
                         or VBSyntaxKind.OrElseExpression => 1,
        _ => 0
    };

    public bool IsControlFlowNode(SyntaxNode node)
        => node is VBSyntax.MultiLineIfBlockSyntax
                or VBSyntax.ForBlockSyntax
                or VBSyntax.ForEachBlockSyntax
                or VBSyntax.WhileBlockSyntax
                or VBSyntax.SelectBlockSyntax
                or VBSyntax.TryBlockSyntax
                or VBSyntax.DoLoopBlockSyntax;

    public int CognitiveWeight(SyntaxNode node, int depth) => node switch
    {
        VBSyntax.MultiLineIfBlockSyntax => 1 + depth,
        VBSyntax.ForBlockSyntax         => 1 + depth,
        VBSyntax.ForEachBlockSyntax     => 1 + depth,
        VBSyntax.WhileBlockSyntax       => 1 + depth,
        VBSyntax.SelectBlockSyntax      => 1 + depth,
        VBSyntax.CatchBlockSyntax       => 1 + depth,
        VBSyntax.ElseBlockSyntax        => 1,
        VBSyntax.BinaryExpressionSyntax vbBin when
            vbBin.Kind() is VBSyntaxKind.AndAlsoExpression
                         or VBSyntaxKind.OrElseExpression => 1,
        _ => 0
    };

    public int? GetParameterCount(SyntaxNode node)
        => (node as VBSyntax.MethodBlockSyntax)?.SubOrFunctionStatement.ParameterList?.Parameters.Count;

    // ICodeValidationStrategy
    public SyntaxTree ParseCode(string code)
        => VisualBasicSyntaxTree.ParseText(code);

    public Compilation CreateStandaloneCompilation(string name, SyntaxTree tree, IEnumerable<MetadataReference> references)
        => VisualBasicCompilation.Create(name, [tree], references,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    public string WrapCodeWithContext(string code, SyntaxNode contextRoot)
    {
        var imports = string.Join("\n", contextRoot.DescendantNodes()
            .OfType<VBSyntax.ImportsStatementSyntax>()
            .Select(i => i.ToFullString().TrimEnd()));
        var nsName = contextRoot.DescendantNodes()
            .OfType<VBSyntax.NamespaceBlockSyntax>()
            .FirstOrDefault()?.NamespaceStatement.Name.ToString() ?? "";

        return $"""
{imports}
Namespace {(string.IsNullOrEmpty(nsName) ? "ValidationNamespace" : nsName)}
    Public Class ValidationClass
        Public Sub ValidationMethod()
            {code}
        End Sub
    End Class
End Namespace
""";
    }

    public string WrapCodeDefault(string code) => $"""
Imports System
Imports System.Collections.Generic
Imports System.Linq

Public Class ValidationClass
    Public Sub ValidationMethod()
        {code}
    End Sub
End Class
""";
}
