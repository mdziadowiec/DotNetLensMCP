using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> ValidateCodeAsync(string code, string? contextFilePath = null, bool standalone = false, string? language = null)
    {
        EnsureSolutionLoaded();

        var isVb = IsVbFilePath(contextFilePath) ||
                   string.Equals(language, "vbnet", StringComparison.OrdinalIgnoreCase);

        var valStrategy = ValidationStrategy(isVb);

        try
        {
            SyntaxTree syntaxTree;
            Compilation compilation;

            if (standalone)
            {
                syntaxTree = valStrategy.ParseCode(code);
                var references = _solution!.Projects.First().MetadataReferences;
                compilation = valStrategy.CreateStandaloneCompilation("ValidationAssembly", syntaxTree, references);
            }
            else if (!string.IsNullOrEmpty(contextFilePath))
            {
                Document document;
                try
                {
                    document = await GetDocumentAsync(contextFilePath);
                }
                catch (FileNotFoundException)
                {
                    return CreateErrorResponse(
                        ErrorCodes.FileNotInSolution,
                        $"Context file not found: {contextFilePath}",
                        hint: "Check the file path or use standalone=true",
                        context: new { contextFilePath }
                    );
                }

                var existingTree = await document.GetSyntaxTreeAsync();
                var existingRoot = await existingTree!.GetRootAsync();
                var project = document.Project;

                var wrappedCode = valStrategy.WrapCodeWithContext(code, existingRoot);
                syntaxTree = valStrategy.ParseCode(wrappedCode);
                compilation = (await GetProjectCompilationAsync(project))!.AddSyntaxTrees(syntaxTree);
            }
            else
            {
                var wrappedCode = valStrategy.WrapCodeDefault(code);
                syntaxTree = valStrategy.ParseCode(wrappedCode);
                var references = _solution!.Projects.First().MetadataReferences;
                compilation = valStrategy.CreateStandaloneCompilation("ValidationAssembly", syntaxTree, references);
            }

            var diagnostics = compilation.GetDiagnostics();
            var errors = diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => new
                {
                    id = d.Id,
                    message = d.GetMessage(),
                    line = d.Location.GetLineSpan().StartLinePosition.Line,
                    column = d.Location.GetLineSpan().StartLinePosition.Character
                })
                .ToList();

            var warnings = diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Warning)
                .Select(d => new
                {
                    id = d.Id,
                    message = d.GetMessage(),
                    line = d.Location.GetLineSpan().StartLinePosition.Line,
                    column = d.Location.GetLineSpan().StartLinePosition.Character
                })
                .ToList();

            return CreateSuccessResponse(
                data: new
                {
                    compiles = errors.Count == 0,
                    errorCount = errors.Count,
                    warningCount = warnings.Count,
                    errors,
                    warnings
                },
                suggestedNextTools: errors.Count > 0
                    ? new[] { "Fix the errors and validate again" }
                    : new[] { "Code is valid - safe to write to file" }
            );
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(
                ErrorCodes.AnalysisFailed,
                $"Validation failed: {ex.Message}",
                hint: "Check syntax of the code snippet",
                context: new { codeLength = code.Length }
            );
        }
    }
}
