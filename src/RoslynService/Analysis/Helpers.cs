using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;

namespace DotNetLensMcp;

public partial class RoslynService
{
    private const int NearbyDiagnosticThreshold = 50;
    private const int MaxNearbyDiagnostics = 10;

    //TODO: implement more diagnostic IDs (CA1822, IDEE0028, CA1861 etc)
    private List<string> GetCommonFixSuggestions(string diagnosticId, string message)
    {
        // Common fix suggestions for well-known diagnostic IDs
        return diagnosticId switch
        {
            "CS0168" => new List<string> { "Remove unused variable", "Use the variable", "Prefix with underscore to indicate intentionally unused" },
            "CS0219" => new List<string> { "Remove unused variable", "Use the variable in an expression" },
            "CS1998" => new List<string> { "Add await keyword to async operation", "Remove async modifier if method doesn't need to be async", "Return Task.CompletedTask or Task.FromResult()" },
            "CS0162" => new List<string> { "Remove unreachable code", "Fix control flow logic" },
            "CS0649" => new List<string> { "Initialize the field", "Remove unused field", "Mark as obsolete if legacy code" },
            "CS8019" => new List<string> { "Remove unnecessary using directive", "Run 'Organize Usings'" },
            "CS0246" => new List<string> { "Add missing using directive", "Check type name spelling", "Add assembly reference" },
            "CS0103" => new List<string> { "Add missing using directive", "Check name spelling", "Declare the variable or method" },
            "CS4012" => new List<string> { "Move Utf8JsonReader to non-async context", "Use synchronous JSON parsing", "Wrap in Task.Run() for async operation" },
            "CS1503" => new List<string> { "Cast argument to expected type", "Change parameter type", "Fix argument expression" },
            _ => new List<string> { "Review diagnostic message for fix guidance", "Consult C# documentation for " + diagnosticId }
        };
    }

    private (Diagnostic? Diagnostic, List<Diagnostic> NearbyDiagnostics) FindDiagnosticAtPosition(
        DocumentAnalysisContext context,
        string diagnosticId,
        int line,
        int column)
    {
        var allDiagnostics = GetDocumentDiagnostics(context.SemanticModel, context.SyntaxTree);
        var position = GetPosition(context.SyntaxTree, line, column);

        var diagnostic = allDiagnostics.FirstOrDefault(d =>
            d.Id == diagnosticId &&
            d.Location.SourceSpan.Contains(position));

        diagnostic ??= allDiagnostics.FirstOrDefault(d =>
            d.Id == diagnosticId &&
            IsDiagnosticNearPosition(d, position));

        diagnostic ??= allDiagnostics.FirstOrDefault(d => d.Id == diagnosticId);

        var nearbyDiagnostics = allDiagnostics
            .Where(d => d.Location.SourceSpan.Contains(position) || IsDiagnosticNearPosition(d, position))
            .Take(MaxNearbyDiagnostics)
            .ToList();

        return (diagnostic, nearbyDiagnostics);
    }

    private static List<Diagnostic> GetDocumentDiagnostics(SemanticModel semanticModel, SyntaxTree syntaxTree)
    {
        var diagnostics = semanticModel.GetDiagnostics().ToList();
        diagnostics.AddRange(syntaxTree.GetDiagnostics());
        return diagnostics;
    }

    private static bool IsDiagnosticNearPosition(Diagnostic diagnostic, int position) =>
        Math.Abs(diagnostic.Location.SourceSpan.Start - position) < NearbyDiagnosticThreshold;

    private async Task<List<(CodeAction action, string kind)>> GetAllCodeActionsAtPositionAsync(
        Document document,
        int position,
        int? endPosition = null,
        bool includeCodeFixes = true,
        bool includeRefactorings = true)
    {
        var allActions = new List<(CodeAction action, string kind)>();
        var span = endPosition.HasValue
            ? TextSpan.FromBounds(position, endPosition.Value)
            : new TextSpan(position, 0);

        // Get code fixes for diagnostics at this position
        if (includeCodeFixes)
        {
            await AddCodeFixActionsAsync(document, position, span, allActions);
        }

        // Get code refactorings at this position
        if (includeRefactorings)
        {
            await AddCodeRefactoringActionsAsync(document, span, allActions);
        }

        return allActions;
    }

    private async Task AddCodeRefactoringActionsAsync(
        Document document,
        TextSpan span,
        List<(CodeAction action, string kind)> allActions)
    {
        var refactoringProviders = GetBuiltInCodeRefactoringProviders();

        foreach (var provider in refactoringProviders)
        {
            var context = new CodeRefactoringContext(
                document,
                span,
                action => allActions.Add((action, "refactoring")),
                CancellationToken.None);

            try
            {
                await provider.ComputeRefactoringsAsync(context);
            }
            catch
            {
                // Skip providers that throw
            }
        }
    }

    private async Task AddCodeFixActionsAsync(
        Document document,
        int position,
        TextSpan span,
        List<(CodeAction action, string kind)> allActions)
    {
        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxTree = await document.GetSyntaxTreeAsync();

        if (semanticModel == null || syntaxTree == null)
            return;

        var diagnostics = GetDocumentDiagnostics(semanticModel, syntaxTree)
            .Where(d => d.Location.SourceSpan.IntersectsWith(span) || d.Location.SourceSpan.Contains(position))
            .Distinct()
            .ToList();

        var codeFixProviders = GetBuiltInCodeFixProviders();

        foreach (var diagnostic in diagnostics)
        {
            foreach (var provider in codeFixProviders)
            {
                if (!provider.FixableDiagnosticIds.Contains(diagnostic.Id))
                    continue;

                var context = new CodeFixContext(
                    document,
                    diagnostic,
                    (action, _) => allActions.Add((action, "fix")),
                    CancellationToken.None);

                try
                {
                    await provider.RegisterCodeFixesAsync(context);
                }
                catch
                {
                    // Skip providers that throw
                }
            }
        }
    }

    private List<CodeFixProvider> GetBuiltInCodeFixProviders()
    {
        // Get built-in C# code fix providers from Roslyn
        var assembly = typeof(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree).Assembly;

        return CreateProviderInstances<CodeFixProvider>(assembly).ToList();
    }

    private List<CodeRefactoringProvider> GetBuiltInCodeRefactoringProviders()
    {
        // Get built-in C# code refactoring providers from Roslyn
        var assemblies = new[]
        {
            typeof(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree).Assembly,
            typeof(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxNode).Assembly
        };

        var providers = new List<CodeRefactoringProvider>();

        foreach (var assembly in assemblies)
        {
            providers.AddRange(CreateProviderInstances<CodeRefactoringProvider>(assembly));
        }

        // Also try to load from Features assembly if available
        try
        {
            var featuresAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Microsoft.CodeAnalysis.CSharp.Features");

            if (featuresAssembly != null)
            {
                providers.AddRange(CreateProviderInstances<CodeRefactoringProvider>(featuresAssembly));
            }
        }
        catch
        {
            // Features assembly not available, continue with what we have
        }

        return providers.Distinct().ToList();
    }

    private static IEnumerable<TProvider> CreateProviderInstances<TProvider>(System.Reflection.Assembly assembly)
        where TProvider : class
    {
        var providerType = typeof(TProvider);

        return assembly.GetTypes()
            .Where(t => !t.IsAbstract && providerType.IsAssignableFrom(t))
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Length == 0))
            .Select(CreateProviderInstance<TProvider>)
            .Where(provider => provider != null)
            .Cast<TProvider>();
    }

    private static TProvider? CreateProviderInstance<TProvider>(Type providerType)
        where TProvider : class
    {
        try
        {
            return Activator.CreateInstance(providerType) as TProvider;
        }
        catch
        {
            return null;
        }
    }
}
