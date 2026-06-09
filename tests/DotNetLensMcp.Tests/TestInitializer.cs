using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;

namespace DotNetLensMcp.Tests;

internal static class TestInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (MSBuildLocator.IsRegistered)
            return;

        var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
        if (instances.Count > 0)
        {
            MSBuildLocator.RegisterInstance(instances.MaxBy(i => i.Version)!);
            return;
        }

        // Fallback for environments where standard VS/SDK discovery fails (e.g. CI or
        // machines with only .NET 10+ SDK and no Visual Studio). Find dotnet root via
        // DOTNET_HOST_PATH (always set by the dotnet test host), then pick the latest
        // SDK subfolder that contains MSBuild.dll and its sibling assemblies.
        var dotnetHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var dotnetRoot = dotnetHostPath is not null
            ? Path.GetDirectoryName(dotnetHostPath)
            : FindDotNetOnPath();

        if (dotnetRoot is not null)
        {
            var sdkDir = Path.Combine(dotnetRoot, "sdk");
            var sdkMsbuild = Directory.Exists(sdkDir)
                ? Directory.GetDirectories(sdkDir)
                    .Where(d => File.Exists(Path.Combine(d, "MSBuild.dll"))
                             && File.Exists(Path.Combine(d, "Microsoft.Build.Framework.dll")))
                    .OrderByDescending(d => d)
                    .FirstOrDefault()
                : null;

            if (sdkMsbuild is not null)
            {
                MSBuildLocator.RegisterMSBuildPath(sdkMsbuild);
                return;
            }
        }

        MSBuildLocator.RegisterDefaults();
    }

    private static string? FindDotNetOnPath()
    {
        var exe = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        return Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator)
            .Select(dir => Path.Combine(dir, exe))
            .FirstOrDefault(File.Exists) is { } found
            ? Path.GetDirectoryName(found)
            : null;
    }
}
