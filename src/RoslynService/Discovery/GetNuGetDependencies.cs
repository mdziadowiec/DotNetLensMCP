using Microsoft.CodeAnalysis;

namespace DotNetLensMcp;

public partial class RoslynService
{
    public Task<object> GetNuGetDependenciesAsync(string? projectName = null)
    {
        EnsureSolutionLoaded();

        var projectDependencies = new List<object>();
        var projects = projectName != null
            ? _solution!.Projects.Where(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase))
            : _solution!.Projects;

        foreach (var project in projects)
        {
            var packages = new List<object>();
            var projectFilePath = project.FilePath;

            if (projectFilePath != null && File.Exists(projectFilePath))
            {
                try
                {
                    var csprojContent = File.ReadAllText(projectFilePath);
                    var doc = System.Xml.Linq.XDocument.Parse(csprojContent);
                    var packageRefs = doc.Descendants("PackageReference");

                    foreach (var pkgRef in packageRefs)
                    {
                        var name = pkgRef.Attribute("Include")?.Value;
                        var version = pkgRef.Attribute("Version")?.Value ?? pkgRef.Element("Version")?.Value;
                        var privateAssets = pkgRef.Attribute("PrivateAssets")?.Value ?? pkgRef.Element("PrivateAssets")?.Value;
                        var excludeAssets = pkgRef.Attribute("ExcludeAssets")?.Value ?? pkgRef.Element("ExcludeAssets")?.Value;

                        if (name != null)
                        {
                            packages.Add(new
                            {
                                packageName = name,
                                version = version ?? "unknown",
                                privateAssets = privateAssets,
                                excludeAssets = excludeAssets
                            });
                        }
                    }
                }
                catch
                {
                    // Failed to parse csproj, skip
                }
            }

            projectDependencies.Add(new
            {
                projectName = project.Name,
                projectPath = FormatPath(project.FilePath ?? ""),
                packages
            });
        }

        return Task.FromResult(CreateSuccessResponse(
            data: new { projects = projectDependencies },
            suggestedNextTools: new[] { "get_project_structure", "dependency_graph" },
            totalCount: projectDependencies.Count,
            returnedCount: projectDependencies.Count
        ));
    }
}
