using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DotNetLensMcp;

internal interface ILanguageSyntaxStrategy
{
    // Method body / scope
    SyntaxNode? FindMethodBody(SyntaxNode root, TextSpan span);

    // Invocations & member access (outgoing calls, DI, reflection)
    IEnumerable<SyntaxNode> FindInvocations(SyntaxNode scope);
    IEnumerable<SyntaxNode> FindMemberAccesses(SyntaxNode scope);
    string? GetInvocationMethodName(SyntaxNode invocationNode);

    // Containing member detection
    SyntaxNode? FindContainingMember(SyntaxNode node);

    // File-level structure (file overview, circular dependencies)
    IEnumerable<string> ExtractImports(SyntaxNode root);
    string? ExtractNamespace(SyntaxNode root);
    IEnumerable<SyntaxNode> FindTypeDeclarations(SyntaxNode root);
    (string name, string kind, int memberCount) ExtractTypeInfo(SyntaxNode typeNode, SemanticModel? semanticModel);

    // Data flow statement collection
    IEnumerable<SyntaxNode> FindBlockStatements(SyntaxNode root, TextSpan span);

    // Complexity metrics
    IEnumerable<SyntaxNode> FindMethodNodes(SyntaxNode root);
    IMethodSymbol? GetMethodSymbol(SemanticModel semanticModel, SyntaxNode methodNode);
    int CountNodeComplexity(SyntaxNode node);
    bool IsControlFlowNode(SyntaxNode node);
    int CognitiveWeight(SyntaxNode node, int depth);
    int? GetParameterCount(SyntaxNode node);
}
