using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DotNetLensMcp.Tests;

/// <summary>
/// Tests for ILSpy-backed decompilation tools:
/// roslyn_inspect_external_assembly and roslyn_peek_il.
/// </summary>
/// <remarks>
/// Uses FluentAssertions as the target assembly because it is a direct test-project
/// dependency, so FindAssemblySymbolAsync will always resolve it when the test
/// solution is loaded.
/// </remarks>
public class DecompilationTests : RoslynServiceTestBase
{
    private const string KnownAssembly  = "FluentAssertions";
    private const string KnownNamespace = "FluentAssertions";

    // Static extension class — has many non-abstract public methods with IL bodies.
    private const string KnownMethod = "FluentAssertions.AssertionExtensions.Should";

    #region InspectExternalAssembly — summary mode

    [Fact]
    public async Task InspectExternalAssembly_Summary_ReturnsSuccess()
    {
        var result = await Service.InspectExternalAssemblyAsync(KnownAssembly);

        AssertSuccess(result);
    }

    [Fact]
    public async Task InspectExternalAssembly_Summary_AssemblyInfoPresent()
    {
        var result = await Service.InspectExternalAssemblyAsync(KnownAssembly);

        var data = GetData(result);
        data["mode"]?.Value<string>().Should().Be("summary");
        data["name"]?.Value<string>().Should().Be(KnownAssembly);
        data["version"].Should().NotBeNull();
    }

    [Fact]
    public async Task InspectExternalAssembly_Summary_NamespaceTreeIsNonEmpty()
    {
        var result = await Service.InspectExternalAssemblyAsync(KnownAssembly);

        var tree = GetData(result)["namespaceTree"] as JArray;
        tree.Should().NotBeNullOrEmpty("FluentAssertions exposes many public types");
    }

    [Fact]
    public async Task InspectExternalAssembly_Summary_NamespaceTreeHasExpectedShape()
    {
        var result = await Service.InspectExternalAssemblyAsync(KnownAssembly);

        var tree = GetData(result)["namespaceTree"] as JArray;
        tree.Should().NotBeNull();

        foreach (var entry in tree!)
        {
            entry["namespace"].Should().NotBeNull();
            entry["typeCount"].Should().NotBeNull();
            entry["publicTypes"].Should().NotBeNull();
        }
    }

    #endregion

    #region InspectExternalAssembly — namespace mode

    [Fact]
    public async Task InspectExternalAssembly_Namespace_ReturnsTypes()
    {
        var result = await Service.InspectExternalAssemblyAsync(
            KnownAssembly, mode: "namespace", namespaceFilter: KnownNamespace);

        AssertSuccess(result);
        var types = GetData(result)["types"] as JArray;
        types.Should().NotBeNullOrEmpty($"'{KnownNamespace}' contains public types");
    }

    [Fact]
    public async Task InspectExternalAssembly_Namespace_TypesHaveExpectedShape()
    {
        var result = await Service.InspectExternalAssemblyAsync(
            KnownAssembly, mode: "namespace", namespaceFilter: KnownNamespace);

        var types = GetData(result)["types"] as JArray;
        types.Should().NotBeNull();

        foreach (var t in types!)
        {
            t["kind"].Should().NotBeNull();
            t["fullName"].Should().NotBeNull();
            t["members"].Should().NotBeNull();
        }
    }

    [Fact]
    public async Task InspectExternalAssembly_Namespace_MissingFilter_ReturnsError()
    {
        var result = await Service.InspectExternalAssemblyAsync(
            KnownAssembly, mode: "namespace", namespaceFilter: null);

        AssertError(result, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task InspectExternalAssembly_Namespace_UnknownNamespace_ReturnsError()
    {
        var result = await Service.InspectExternalAssemblyAsync(
            KnownAssembly, mode: "namespace", namespaceFilter: "NoSuchNamespace.DoesNotExist");

        AssertError(result, "SYMBOL_NOT_FOUND");
    }

    #endregion

    #region InspectExternalAssembly — error cases

    [Fact]
    public async Task InspectExternalAssembly_UnknownAssembly_ReturnsError()
    {
        var result = await Service.InspectExternalAssemblyAsync("NonExistentAssembly99999");

        AssertError(result, "SYMBOL_NOT_FOUND");
    }

    [Fact]
    public async Task InspectExternalAssembly_UnknownMode_ReturnsError()
    {
        var result = await Service.InspectExternalAssemblyAsync(KnownAssembly, mode: "invalid_mode");

        AssertError(result, "INVALID_ARGUMENT");
    }

    #endregion

    #region PeekIl

    [Fact]
    public async Task PeekIl_KnownMethod_ReturnsSuccess()
    {
        var result = await Service.PeekIlAsync(KnownMethod);

        AssertSuccess(result);
    }

    [Fact]
    public async Task PeekIl_KnownMethod_IlTextContainsMethodDirective()
    {
        var result = await Service.PeekIlAsync(KnownMethod);

        var il = GetData(result)["il"]?.Value<string>();
        il.Should().NotBeNullOrWhiteSpace();
        il.Should().Contain(".method", "disassembled IL must include a .method directive");
    }

    [Fact]
    public async Task PeekIl_KnownMethod_ResultHasAssemblyAndMethodInfo()
    {
        var result = await Service.PeekIlAsync(KnownMethod);

        var data = GetData(result);
        data["assemblyName"]?.Value<string>().Should().Be(KnownAssembly);
        data["assemblyVersion"].Should().NotBeNull();
        data["methodFullName"].Should().NotBeNull();
    }

    [Fact]
    public async Task PeekIl_UnknownMethod_ReturnsError()
    {
        var result = await Service.PeekIlAsync("NoSuchNamespace.NoSuchType.NoSuchMethod");

        AssertError(result, "SYMBOL_NOT_FOUND");
    }

    #endregion
}
