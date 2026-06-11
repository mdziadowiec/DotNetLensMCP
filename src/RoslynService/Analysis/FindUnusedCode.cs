using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public async Task<object> FindUnusedCodeAsync(
        string? projectName,
        bool includePrivate,
        bool includeInternal,
        string? symbolKindFilter = null,
        int? maxResults = null)
    {
        EnsureSolutionLoaded();

        using var cts = CreateTimeoutCts();
        var unusedSymbols = new List<object>();
        var maxResultsToReturn = maxResults ?? 50; // Default to 50 to prevent huge outputs

        var projectsToAnalyze = string.IsNullOrEmpty(projectName)
            ? _solution!.Projects
            : _solution!.Projects.Where(p => p.Name == projectName);

        // Track counts by kind for summary
        var countByKind = new Dictionary<string, int>();

        foreach (var project in projectsToAnalyze)
        {
            if (unusedSymbols.Count >= maxResultsToReturn)
                break; // Stop analyzing if we hit the limit

            var compilation = await GetProjectCompilationAsync(project);
            if (compilation == null) continue;

            // Check if we should analyze types
            var shouldAnalyzeTypes = string.IsNullOrEmpty(symbolKindFilter) ||
                                     symbolKindFilter.Equals("Class", StringComparison.OrdinalIgnoreCase) ||
                                     symbolKindFilter.Equals("Interface", StringComparison.OrdinalIgnoreCase) ||
                                     symbolKindFilter.Equals("Struct", StringComparison.OrdinalIgnoreCase) ||
                                     symbolKindFilter.Equals("Type", StringComparison.OrdinalIgnoreCase);

            if (shouldAnalyzeTypes)
            {
                // Get all named type symbols (classes, interfaces, structs, enums)
                var allTypes = compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type)
                    .OfType<INamedTypeSymbol>();

                foreach (var typeSymbol in allTypes)
                {
                    if (unusedSymbols.Count >= maxResultsToReturn)
                        break;

                    // Skip compiler-generated, extern, and types not in source
                    if (typeSymbol.IsImplicitlyDeclared ||
                        typeSymbol.IsExtern ||
                        !typeSymbol.Locations.Any(loc => loc.IsInSource))
                        continue;

                    // Filter by accessibility
                    if (!includePrivate && typeSymbol.DeclaredAccessibility == Accessibility.Private)
                        continue;
                    if (!includeInternal && typeSymbol.DeclaredAccessibility == Accessibility.Internal)
                        continue;

                    // Skip classes that implement framework interfaces (likely used via DI or framework)
                    if (ImplementsFrameworkInterface(typeSymbol))
                        continue;

                    // Skip classes with framework attributes (controllers, hosted services, etc.)
                    if (HasFrameworkAttribute(typeSymbol))
                        continue;

                    // Find references to this type
                    var references = await SymbolFinder.FindReferencesAsync(typeSymbol, _solution!, cancellationToken: cts.Token);
                    var referenceCount = references.SelectMany(r => r.Locations).Count();

                    // For types, also check if any members are referenced
                    // This handles static classes where the class itself isn't referenced
                    // but its static methods/properties are called
                    var hasReferencedMembers = false;
                    if (referenceCount <= 1) // Type itself has no references
                    {
                        // Check if any public/internal members are referenced
                        foreach (var member in typeSymbol.GetMembers())
                        {
                            // Skip constructors, compiler-generated, and special members
                            if (member.IsImplicitlyDeclared ||
                                member is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor })
                                continue;

                            var memberRefs = await SymbolFinder.FindReferencesAsync(member, _solution!, cancellationToken: cts.Token);
                            var memberRefCount = memberRefs.SelectMany(r => r.Locations).Count();

                            if (memberRefCount > 0) // Member is referenced
                            {
                                hasReferencedMembers = true;
                                break; // No need to check other members
                            }
                        }
                    }

                    // SymbolFinder.FindReferencesAsync returns only usage sites (not the declaration).
                    // A count of 0 means truly unreferenced; 1 means used exactly once (not unused).
                    if (referenceCount == 0 && !hasReferencedMembers)
                    {
                        var location = typeSymbol.Locations.FirstOrDefault(loc => loc.IsInSource);
                        if (location != null)
                        {
                            var lineSpan = location.GetLineSpan();
                            var kind = typeSymbol.TypeKind.ToString();

                            countByKind[kind] = countByKind.GetValueOrDefault(kind) + 1;

                            unusedSymbols.Add(new
                            {
                                name = typeSymbol.Name,
                                fullyQualifiedName = typeSymbol.ToDisplayString(),
                                kind,
                                accessibility = typeSymbol.DeclaredAccessibility.ToString(),
                                filePath = FormatPath(lineSpan.Path),
                                line = lineSpan.StartLinePosition.Line,
                                column = lineSpan.StartLinePosition.Character
                            });
                        }
                    }
                }
            }

            // Check if we should analyze members
            var shouldAnalyzeMembers = string.IsNullOrEmpty(symbolKindFilter) ||
                                       symbolKindFilter.Equals("Method", StringComparison.OrdinalIgnoreCase) ||
                                       symbolKindFilter.Equals("Property", StringComparison.OrdinalIgnoreCase) ||
                                       symbolKindFilter.Equals("Field", StringComparison.OrdinalIgnoreCase) ||
                                       symbolKindFilter.Equals("Member", StringComparison.OrdinalIgnoreCase);

            if (shouldAnalyzeMembers && unusedSymbols.Count < maxResultsToReturn)
            {
                // Also check methods, properties, and fields
                var allMembers = compilation.GetSymbolsWithName(_ => true, SymbolFilter.Member);

                foreach (var member in allMembers)
                {
                    if (unusedSymbols.Count >= maxResultsToReturn)
                        break;

                    if (member is not (IMethodSymbol or IPropertySymbol or IFieldSymbol))
                        continue;

                    // Skip compiler-generated, extern, and symbols not in source
                    if (member.IsImplicitlyDeclared ||
                        member.IsExtern ||
                        !member.Locations.Any(loc => loc.IsInSource))
                        continue;

                    // Skip special methods (constructors, operators, etc.)
                    if (member is IMethodSymbol method &&
                        (method.MethodKind != MethodKind.Ordinary || method.IsOverride || method.IsVirtual))
                        continue;

                    // Filter by accessibility
                    if (!includePrivate && member.DeclaredAccessibility == Accessibility.Private)
                        continue;
                    if (!includeInternal && member.DeclaredAccessibility == Accessibility.Internal)
                        continue;

                    // Find references
                    var references = await SymbolFinder.FindReferencesAsync(member, _solution!, cancellationToken: cts.Token);
                    var referenceCount = references.SelectMany(r => r.Locations).Count();

                    if (referenceCount == 0)
                    {
                        var location = member.Locations.FirstOrDefault(loc => loc.IsInSource);
                        if (location != null)
                        {
                            var lineSpan = location.GetLineSpan();
                            var kind = member.Kind.ToString();

                            countByKind[kind] = countByKind.GetValueOrDefault(kind) + 1;

                            unusedSymbols.Add(new
                            {
                                name = member.Name,
                                fullyQualifiedName = member.ToDisplayString(),
                                kind,
                                accessibility = member.DeclaredAccessibility.ToString(),
                                containingType = member.ContainingType?.ToDisplayString(),
                                filePath = FormatPath(lineSpan.Path),
                                line = lineSpan.StartLinePosition.Line,
                                column = lineSpan.StartLinePosition.Character
                            });
                        }
                    }
                }
            }
        }

        return CreateSuccessResponse(
            data: new
            {
                projectName = projectName ?? "All projects",
                countByKind,
                unusedSymbols = unusedSymbols.ToList()
            },
            suggestedNextTools: unusedSymbols.Count > 0
                ? new[] { "find_references to verify symbol is truly unused", "rename_symbol or delete unused code" }
                : new[] { "No unused code found - codebase is clean" },
            totalCount: unusedSymbols.Count,
            returnedCount: unusedSymbols.Count
        );
    }
}
