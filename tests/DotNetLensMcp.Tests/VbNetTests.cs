using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DotNetLensMcp.Tests;

/// <summary>
/// Integration tests verifying VB.NET support: semantic tools work, syntax tools gracefully decline.
/// Loads standalone C#, standalone VB.NET, and mixed C#/VB.NET fixture solutions.
/// </summary>
public class VbNetTests : IAsyncLifetime
{
    private RoslynService _service = null!;
    private string _testSolutionsDir = null!;
    private string _vbSampleDir = null!;
    private string _testServiceVbPath = null!;
    private string _interfaceVbPath = null!;

    public async Task InitializeAsync()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var solutionDir = currentDir;
        while (solutionDir != null && !File.Exists(Path.Combine(solutionDir, "DotNetLensMcp.sln")))
            solutionDir = Directory.GetParent(solutionDir)?.FullName;
        if (solutionDir == null)
            throw new InvalidOperationException("Could not find DotNetLensMcp.sln");

        _testSolutionsDir = Path.Combine(solutionDir, "tests", "DotNetLensMcp.Tests", "TestSolutions");
        _vbSampleDir = Path.Combine(_testSolutionsDir, "VbNetSample");
        _testServiceVbPath = Path.Combine(_vbSampleDir, "TestService.vb");
        _interfaceVbPath = Path.Combine(_vbSampleDir, "ITestInterface.vb");

        _service = new RoslynService();
        var result = await _service.LoadSolutionAsync(Path.Combine(_vbSampleDir, "VbNetSample.sln"));
        var json = JObject.FromObject(result);
        json["success"]?.Value<bool>().Should().BeTrue(
            $"VbNetSample.sln should load successfully. Error: {json["error"]}");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private void AssertSuccess(object response)
    {
        var json = JObject.FromObject(response);
        json["success"]?.Value<bool>().Should().BeTrue(
            $"Expected success but got: {json["error"]}");
    }

    private void AssertError(object response, string? errorCodeContains = null)
    {
        var json = JObject.FromObject(response);
        json["success"]?.Value<bool>().Should().BeFalse("Expected an error response");
        if (errorCodeContains != null)
            json["error"]?["code"]?.Value<string>().Should().Contain(errorCodeContains);
    }

    private JToken GetData(object response)
    {
        AssertSuccess(response);
        return JObject.FromObject(response)["data"]!;
    }

    private (int line, int col) FindSymbolPosition(string filePath, string searchText)
    {
        var lines = File.ReadAllLines(filePath);
        for (var i = 0; i < lines.Length; i++)
        {
            var col = lines[i].IndexOf(searchText, StringComparison.Ordinal);
            if (col >= 0) return (i, col);
        }
        throw new InvalidOperationException($"'{searchText}' not found in {filePath}");
    }

    [Fact]
    public async Task FindReferences_VbSymbol_ReturnsResults()
    {
        var (line, col) = FindSymbolPosition(_testServiceVbPath, "TestService");
        var result = await _service.FindReferencesAsync(_testServiceVbPath, line, col);
        AssertSuccess(result);
        var data = GetData(result);
        data["symbolName"]?.Value<string>().Should().Be("TestService");
    }

    [Fact]
    public async Task GoToDefinition_VbSymbol_ReturnsLocation()
    {
        var (line, col) = FindSymbolPosition(_testServiceVbPath, "DoWork");
        var result = await _service.GoToDefinitionAsync(_testServiceVbPath, line, col);
        AssertSuccess(result);
        var data = GetData(result);
        data["filePath"]?.Value<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task FindImplementations_VbInterface_ReturnsTestService()
    {
        var (line, col) = FindSymbolPosition(_interfaceVbPath, "ITestInterface");
        var result = await _service.FindImplementationsAsync(_interfaceVbPath, line, col);
        AssertSuccess(result);
        var data = GetData(result);
        var impls = data["implementations"] as JArray;
        impls.Should().NotBeNull();
        impls!.Any(i => i["name"]?.Value<string>()?.Contains("TestService") == true)
            .Should().BeTrue("TestService should implement ITestInterface");
    }

    [Fact]
    public async Task FindCallers_VbMethod_ReturnsResults()
    {
        var (line, col) = FindSymbolPosition(_testServiceVbPath, "DoWork");
        var result = await _service.FindCallersAsync(_testServiceVbPath, line, col);
        AssertSuccess(result);
    }

    [Fact]
    public async Task GetMethodOverloads_VbMethod_ReturnsResults()
    {
        var (line, col) = FindSymbolPosition(_testServiceVbPath, "DoWork");
        var result = await _service.GetMethodOverloadsAsync(_testServiceVbPath, line, col);
        AssertSuccess(result);
        var data = GetData(result);
        data["methodName"]?.Value<string>().Should().Be("DoWork");
    }

    [Fact]
    public async Task GetTypeMembers_VbClass_ReturnsMemberList()
    {
        var result = await _service.GetTypeMembersAsync("TestService");
        AssertSuccess(result);
        var data = GetData(result);
        var members = data["members"] as JArray;
        members.Should().NotBeNull();
        var memberNames = members!.Select(m => m["name"]?.Value<string>()).ToList();
        memberNames.Should().Contain("DoWork");
        memberNames.Should().Contain("GetValue");
        memberNames.Should().Contain("Helper");
        memberNames.Should().Contain("_value");
    }

    [Fact]
    public async Task GetTypeHierarchy_VbClass_ReturnsHierarchy()
    {
        var (line, col) = FindSymbolPosition(_testServiceVbPath, "TestService");
        var result = await _service.GetTypeHierarchyAsync(_testServiceVbPath, line, col);
        AssertSuccess(result);
        var data = GetData(result);
        var interfaces = data["interfaces"] as JArray;
        interfaces.Should().NotBeNull();
        interfaces!.Any(i => i["name"]?.Value<string>()?.Contains("ITestInterface") == true)
            .Should().BeTrue("TestService implements ITestInterface");
    }

    [Fact]
    public async Task SearchSymbols_VbNamespace_FindsTestService()
    {
        var result = await _service.SearchSymbolsAsync("TestService", kind: null, maxResults: 10);
        AssertSuccess(result);
        var data = GetData(result);
        var results = data["results"] as JArray;
        results.Should().NotBeNull();
        results!.Any(r => r["name"]?.Value<string>() == "TestService").Should().BeTrue();
    }

    [Fact]
    public async Task GetDiagnostics_VbFile_ReturnsWithoutCrash()
    {
        var result = await _service.GetDiagnosticsAsync(
            filePath: _testServiceVbPath,
            projectPath: null,
            severity: null,
            includeHidden: false);
        AssertSuccess(result);
    }

    [Fact]
    public async Task SyncDocuments_VbFile_AcceptedWithoutError()
    {
        var result = await _service.SyncDocumentsAsync(new List<string> { _testServiceVbPath });
        AssertSuccess(result);
    }

    [Fact]
    public async Task ExtractMethod_VbFile_ReturnsNotSupportedError()
    {
        var result = await _service.ExtractMethodAsync(
            _testServiceVbPath,
            startLine: 8,
            endLine: 10,
            methodName: "ExtractedMethod");
        AssertError(result, "VB_NOT_SUPPORTED");
    }

    [Fact]
    public async Task GetContainingMember_VbFile_ReturnsMember()
    {
        var (line, col) = FindSymbolPosition(_testServiceVbPath, "Console.WriteLine");
        var result = await _service.GetContainingMemberAsync(_testServiceVbPath, line, col);
        AssertSuccess(result);
        var data = GetData(result);
        data["memberName"]?.Value<string>().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetOutgoingCalls_VbMethod_ReturnsResults()
    {
        var (line, col) = FindSymbolPosition(_testServiceVbPath, "DoWork");
        var result = await _service.GetOutgoingCallsAsync(_testServiceVbPath, line, col);
        AssertSuccess(result);
        var data = GetData(result);
        data["calls"].Should().NotBeNull();
    }

    [Fact]
    public async Task GetMethodSource_VbMethod_ReturnsNotSupportedError()
    {
        var result = await _service.GetMethodSourceAsync("TestService", "DoWork");
        AssertError(result, "VB_NOT_SUPPORTED");
    }

    [Fact]
    public async Task GetComplexityMetrics_VbFile_ReturnsMetrics()
    {
        var result = await _service.GetComplexityMetricsAsync(_testServiceVbPath);
        AssertSuccess(result);
        var data = GetData(result);
        data["methods"].Should().NotBeNull();
    }

    [Fact]
    public async Task GetCodeActionsAtPosition_VbFile_ReturnsNotSupportedError()
    {
        var (line, col) = FindSymbolPosition(_testServiceVbPath, "DoWork");
        var result = await _service.GetCodeActionsAtPositionAsync(_testServiceVbPath, line, col);
        AssertError(result, "VB_NOT_SUPPORTED");
    }

    [Fact]
    public async Task LoadSolution_CsLanguage_LoadsCsProject()
    {
        var csSlnPath = Path.Combine(_testSolutionsDir, "CsSample", "CsSample.sln");
        var service = new RoslynService();
        var loadResult = await service.LoadSolutionAsync(csSlnPath);
        var loadJson = JObject.FromObject(loadResult);
        loadJson["success"]?.Value<bool>().Should().BeTrue(
            $"CsSample.sln should load. Error: {loadJson["error"]}");

        var structureResult = await service.GetProjectStructureAsync(
            includeReferences: false,
            includeDocuments: false,
            summaryOnly: true);
        var data = JObject.FromObject(structureResult)["data"]!;
        var projects = data["projects"] as JArray;
        projects.Should().NotBeNull();
        projects!.Any(p => p["name"]?.Value<string>() == "CsSample")
            .Should().BeTrue("CsSample project should be in the C# solution");
    }

    [Fact]
    public async Task LoadSolution_MixedLanguage_LoadsBothProjects()
    {
        var mixedSlnPath = Path.Combine(_testSolutionsDir, "MixedSample", "MixedSample.sln");
        var service = new RoslynService();
        var loadResult = await service.LoadSolutionAsync(mixedSlnPath);
        var loadJson = JObject.FromObject(loadResult);
        loadJson["success"]?.Value<bool>().Should().BeTrue(
            $"MixedSample.sln should load. Error: {loadJson["error"]}");

        var structureResult = await service.GetProjectStructureAsync(
            includeReferences: false,
            includeDocuments: false,
            summaryOnly: true);
        var data = JObject.FromObject(structureResult)["data"]!;
        var projects = data["projects"] as JArray;
        projects.Should().NotBeNull();
        projects!.Any(p => p["name"]?.Value<string>() == "CsSample")
            .Should().BeTrue("CsSample project should be in the mixed solution");
        projects!.Any(p => p["name"]?.Value<string>() == "VbNetSample")
            .Should().BeTrue("VbNetSample project should be in the mixed solution");
    }

    [Fact]
    public async Task GetContainingMember_MixedLanguage_CsFileReturnsSuccess()
    {
        var mixedSlnPath = Path.Combine(_testSolutionsDir, "MixedSample", "MixedSample.sln");
        var csFilePath = Path.Combine(_testSolutionsDir, "MixedSample", "CsSample", "Class1.cs");
        var service = new RoslynService();
        var loadResult = await service.LoadSolutionAsync(mixedSlnPath);
        AssertSuccess(loadResult);

        var (line, col) = FindSymbolPosition(csFilePath, "Greet");
        var result = await service.GetContainingMemberAsync(csFilePath, line, col);

        AssertSuccess(result);
        var data = GetData(result);
        data["memberName"]?.Value<string>().Should().Be("Greet");
    }

    [Fact]
    public async Task AnalyzeDataFlow_VbFile_ReturnsResults()
    {
        var result = await _service.AnalyzeDataFlowAsync(_testServiceVbPath, startLine: 21, endLine: 25);
        AssertSuccess(result);
        var data = GetData(result);
        data["readInside"].Should().NotBeNull();
    }

    [Fact]
    public async Task AnalyzeControlFlow_VbFile_ReturnsResults()
    {
        var result = await _service.AnalyzeControlFlowAsync(_testServiceVbPath, startLine: 21, endLine: 25);
        AssertSuccess(result);
    }

    [Fact]
    public async Task GetDiRegistrations_VbSolution_ReturnsResults()
    {
        var result = await _service.GetDiRegistrationsAsync(projectName: null);
        AssertSuccess(result);
    }

    [Fact]
    public async Task FindReflectionUsage_VbSolution_ReturnsResults()
    {
        var result = await _service.FindReflectionUsageAsync();
        AssertSuccess(result);
        var data = GetData(result);
        data["usages"].Should().NotBeNull();
    }

    [Fact]
    public async Task FindCircularDependencies_NamespaceLevel_VbSolution_ReturnsResults()
    {
        var result = await _service.FindCircularDependenciesAsync("namespace");
        AssertSuccess(result);
        var data = GetData(result);
        data["level"]?.Value<string>().Should().Be("namespace");
    }

    [Fact]
    public async Task GetFileOverview_VbFile_ReturnsTypeDeclarations()
    {
        var result = await _service.GetFileOverviewAsync(_testServiceVbPath);
        AssertSuccess(result);
        var data = GetData(result);
        var types = data["typeDeclarations"] as JArray;
        types.Should().NotBeNull();
        types!.Any(t => t["name"]?.Value<string>() == "TestService").Should().BeTrue(
            "TestService class should appear in the type declarations");
    }

    [Fact]
    public async Task AnalyzeMethod_VbMethod_WithOutgoingCalls_ReturnsResults()
    {
        var result = await _service.AnalyzeMethodAsync("TestService", "DoWork", includeOutgoingCalls: true);
        AssertSuccess(result);
        var data = GetData(result);
        data["outgoingCalls"].Should().NotBeNull();
    }

    [Fact]
    public async Task FormatDocumentBatch_VbProject_ReturnsSuccess()
    {
        var result = await _service.FormatDocumentBatchAsync(projectName: "VbNetSample", preview: true);
        AssertSuccess(result);
    }

    [Fact]
    public async Task ValidateCode_ValidVbSnippet_ReportsCompiles()
    {
        var result = await _service.ValidateCodeAsync(
            code: "Dim x As Integer = 42",
            language: "vbnet");
        AssertSuccess(result);
        var data = GetData(result);
        data["compiles"]?.Value<bool>().Should().BeTrue("valid VB snippet should compile");
        (data["errors"] as JArray)!.Count.Should().Be(0);
    }

    [Fact]
    public async Task ValidateCode_InvalidVbSnippet_ReportsErrors()
    {
        // Reference to an undefined type — guaranteed compile error even with Option Strict Off
        var result = await _service.ValidateCodeAsync(
            code: "Dim x As UndefinedType123 = Nothing",
            language: "vbnet");
        AssertSuccess(result);
        var data = GetData(result);
        data["compiles"]?.Value<bool>().Should().BeFalse("undefined type should not compile");
        (data["errors"] as JArray)!.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ValidateCode_WithVbContextFile_UsesExistingImports()
    {
        var result = await _service.ValidateCodeAsync(
            code: "Console.WriteLine(\"hello\")",
            contextFilePath: _testServiceVbPath);
        AssertSuccess(result);
        var data = GetData(result);
        data["compiles"].Should().NotBeNull();
    }
}
