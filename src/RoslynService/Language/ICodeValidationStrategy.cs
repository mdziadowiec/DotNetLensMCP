using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

internal interface ICodeValidationStrategy
{
    SyntaxTree ParseCode(string code);
    Compilation CreateStandaloneCompilation(string name, SyntaxTree tree, IEnumerable<MetadataReference> references);
    string WrapCodeWithContext(string code, SyntaxNode contextRoot);
    string WrapCodeDefault(string code);
}
