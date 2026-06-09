using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DotNetLensMcp.Tests;

/// <summary>
/// Tests for quality analysis tools: find_large_classes, get_public_api_surface,
/// get_operators, find_obsolete_usage, get_call_graph, find_async_violations,
/// find_disposable_misuse, find_event_subscribers, find_naming_violations, find_god_objects.
/// </summary>
public class QualityAnalysisTests : RoslynServiceTestBase
{
    #region FindLargeClasses

    [Fact]
    public async Task FindLargeClasses_ReturnsSuccessWithResults()
    {
        var result = await Service.FindLargeClassesAsync();

        AssertSuccess(result);
        var data = GetData(result);
        data["classes"].Should().NotBeNull();
        data["memberCountThreshold"].Should().NotBeNull();
    }

    [Fact]
    public async Task FindLargeClasses_RoslynServiceIsLarge()
    {
        // RoslynService is a known large type — should appear with default thresholds
        var result = await Service.FindLargeClassesAsync(memberCountThreshold: 10, lineCountThreshold: 100);

        AssertSuccess(result);
        var data = GetData(result);
        var classes = data["classes"] as JArray;
        classes.Should().NotBeNull();

        var names = classes!.Select(c => c["typeName"]?.Value<string>() ?? "").ToList();
        names.Should().Contain(n => n.Contains("RoslynService"));
    }

    [Fact]
    public async Task FindLargeClasses_HighThreshold_ReturnsEmpty()
    {
        // Thresholds far beyond any type in the solution
        var result = await Service.FindLargeClassesAsync(
            memberCountThreshold: 100_000,
            lineCountThreshold: 100_000);

        AssertSuccess(result);
        var data = GetData(result);
        var classes = data["classes"] as JArray;
        classes!.Count.Should().Be(0);
    }

    [Fact]
    public async Task FindLargeClasses_SortedByMemberCountDescending()
    {
        var result = await Service.FindLargeClassesAsync(memberCountThreshold: 5, lineCountThreshold: 50);

        AssertSuccess(result);
        var data = GetData(result);
        var classes = (data["classes"] as JArray)!;

        if (classes.Count > 1)
        {
            var counts = classes.Select(c => c["memberCount"]?.Value<int>() ?? 0).ToList();
            counts.Should().BeInDescendingOrder("results should be sorted by member count");
        }
    }

    #endregion

    #region GetPublicApiSurface

    [Fact]
    public async Task GetPublicApiSurface_ReturnsSuccess()
    {
        var result = await Service.GetPublicApiSurfaceAsync();

        AssertSuccess(result);
        var data = GetData(result);
        data["projects"].Should().NotBeNull();
    }

    [Fact]
    public async Task GetPublicApiSurface_ContainsDotNetLensMcpProject()
    {
        var result = await Service.GetPublicApiSurfaceAsync();

        AssertSuccess(result);
        var data = GetData(result);
        var projects = data["projects"] as JArray;
        projects.Should().NotBeNull();
        projects!.Count.Should().BeGreaterThan(0);

        var projectNames = projects.Select(p => p["project"]?.Value<string>() ?? "").ToList();
        projectNames.Should().Contain(n => n.Contains("DotNetLensMcp"));
    }

    [Fact]
    public async Task GetPublicApiSurface_IncludeInternal_ReturnsMoreItems()
    {
        var publicOnly = await Service.GetPublicApiSurfaceAsync(includeInternal: false);
        var withInternal = await Service.GetPublicApiSurfaceAsync(includeInternal: true);

        AssertSuccess(publicOnly);
        AssertSuccess(withInternal);

        var publicCount = JObject.FromObject(publicOnly)["returnedCount"]?.Value<int>() ?? 0;
        var internalCount = JObject.FromObject(withInternal)["returnedCount"]?.Value<int>() ?? 0;

        internalCount.Should().BeGreaterThanOrEqualTo(publicCount,
            "including internal members should return at least as many results");
    }

    [Fact]
    public async Task GetPublicApiSurface_ProjectFilter_LimitsToOneProject()
    {
        var result = await Service.GetPublicApiSurfaceAsync(projectFilter: "DotNetLensMcp");

        AssertSuccess(result);
        var data = GetData(result);
        var projects = data["projects"] as JArray;
        projects.Should().NotBeNull();
        foreach (var project in projects!)
            project["project"]?.Value<string>().Should().Contain("DotNetLensMcp");
    }

    #endregion

    #region GetOperators

    [Fact]
    public async Task GetOperators_OnTypeWithNoOperators_ReturnsEmptyList()
    {
        var result = await Service.GetOperatorsAsync("RoslynService");

        AssertSuccess(result);
        var data = GetData(result);
        data["operators"].Should().NotBeNull();
        (data["operators"] as JArray)!.Count.Should().Be(0);
    }

    [Fact]
    public async Task GetOperators_UnknownType_ReturnsError()
    {
        var result = await Service.GetOperatorsAsync("NonExistentType99999");

        var json = JObject.FromObject(result);
        json["success"]?.Value<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task GetOperators_TypeName_Populated()
    {
        var result = await Service.GetOperatorsAsync("RoslynService");

        AssertSuccess(result);
        var data = GetData(result);
        data["typeName"]?.Value<string>().Should().NotBeNullOrEmpty();
    }

    #endregion

    #region FindObsoleteUsage

    [Fact]
    public async Task FindObsoleteUsage_ReturnsSuccess()
    {
        var result = await Service.FindObsoleteUsageAsync();

        AssertSuccess(result);
        var data = GetData(result);
        data["usages"].Should().NotBeNull();
    }

    [Fact]
    public async Task FindObsoleteUsage_ResultsHaveExpectedShape()
    {
        var result = await Service.FindObsoleteUsageAsync(maxResults: 10);

        AssertSuccess(result);
        var data = GetData(result);
        var usages = data["usages"] as JArray;
        usages.Should().NotBeNull();

        foreach (var usage in usages!)
        {
            usage["obsoleteSymbol"].Should().NotBeNull();
            usage["filePath"].Should().NotBeNull();
            usage["line"].Should().NotBeNull();
        }
    }

    [Fact]
    public async Task FindObsoleteUsage_MaxResults_IsRespected()
    {
        var result = await Service.FindObsoleteUsageAsync(maxResults: 3);

        AssertSuccess(result);
        var data = GetData(result);
        var usages = data["usages"] as JArray;
        usages!.Count.Should().BeLessThanOrEqualTo(3);
    }

    #endregion

    #region GetCallGraph

    [Fact]
    public async Task GetCallGraph_Callees_ReturnsGraphStructure()
    {
        var result = await Service.GetCallGraphAsync(
            typeName: "RoslynService",
            methodName: "GetHealthCheckAsync",
            depth: 2,
            direction: "callees");

        AssertSuccess(result);
        var data = GetData(result);
        data["nodes"].Should().NotBeNull();
        data["edges"].Should().NotBeNull();
        data["root"].Should().NotBeNull();
        data["direction"]?.Value<string>().Should().Be("callees");
    }

    [Fact]
    public async Task GetCallGraph_Callers_ReturnsGraphStructure()
    {
        var result = await Service.GetCallGraphAsync(
            typeName: "RoslynService",
            methodName: "EnsureSolutionLoaded",
            depth: 1,
            direction: "callers");

        AssertSuccess(result);
        var data = GetData(result);
        data["nodes"].Should().NotBeNull();
        data["edges"].Should().NotBeNull();
        data["direction"]?.Value<string>().Should().Be("callers");

        // EnsureSolutionLoaded is called from many places
        var nodes = data["nodes"] as JArray;
        nodes!.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task GetCallGraph_UnknownMethod_ReturnsError()
    {
        var result = await Service.GetCallGraphAsync(
            typeName: "RoslynService",
            methodName: "NonExistentMethod99999",
            depth: 2,
            direction: "callees");

        var json = JObject.FromObject(result);
        json["success"]?.Value<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task GetCallGraph_DepthOne_LimitsTraversal()
    {
        var depth1 = await Service.GetCallGraphAsync(
            typeName: "RoslynService",
            methodName: "LoadSolutionAsync",
            depth: 1,
            direction: "callees");

        var depth3 = await Service.GetCallGraphAsync(
            typeName: "RoslynService",
            methodName: "LoadSolutionAsync",
            depth: 3,
            direction: "callees");

        var nodes1 = (GetData(depth1)["nodes"] as JArray)!.Count;
        var nodes3 = (GetData(depth3)["nodes"] as JArray)!.Count;
        nodes3.Should().BeGreaterThanOrEqualTo(nodes1,
            "deeper traversal should find at least as many nodes");
    }

    #endregion

    #region FindAsyncViolations

    [Fact]
    public async Task FindAsyncViolations_ReturnsSuccess()
    {
        var result = await Service.FindAsyncViolationsAsync();

        AssertSuccess(result);
        var data = GetData(result);
        data["violations"].Should().NotBeNull();
    }

    [Fact]
    public async Task FindAsyncViolations_ViolationsHaveExpectedShape()
    {
        var result = await Service.FindAsyncViolationsAsync(maxResults: 10);

        AssertSuccess(result);
        var data = GetData(result);
        var violations = data["violations"] as JArray;
        violations.Should().NotBeNull();

        foreach (var v in violations!)
        {
            v["violationType"].Should().NotBeNull();
            v["filePath"].Should().NotBeNull();
            v["line"].Should().NotBeNull();
        }
    }

    [Fact]
    public async Task FindAsyncViolations_MaxResults_IsRespected()
    {
        var result = await Service.FindAsyncViolationsAsync(maxResults: 5);

        AssertSuccess(result);
        var data = GetData(result);
        (data["violations"] as JArray)!.Count.Should().BeLessThanOrEqualTo(5);
    }

    #endregion

    #region FindDisposableMisuse

    [Fact]
    public async Task FindDisposableMisuse_ReturnsSuccess()
    {
        var result = await Service.FindDisposableMisuseAsync();

        AssertSuccess(result);
        var data = GetData(result);
        data["violations"].Should().NotBeNull();
    }

    [Fact]
    public async Task FindDisposableMisuse_ViolationsHaveExpectedShape()
    {
        var result = await Service.FindDisposableMisuseAsync(maxResults: 10);

        AssertSuccess(result);
        var data = GetData(result);
        var violations = data["violations"] as JArray;
        violations.Should().NotBeNull();

        foreach (var v in violations!)
        {
            v["violationType"].Should().NotBeNull();
            v["filePath"].Should().NotBeNull();
            v["line"].Should().NotBeNull();
        }
    }

    [Fact]
    public async Task FindDisposableMisuse_MaxResults_IsRespected()
    {
        var result = await Service.FindDisposableMisuseAsync(maxResults: 2);

        AssertSuccess(result);
        (GetData(result)["violations"] as JArray)!.Count.Should().BeLessThanOrEqualTo(2);
    }

    #endregion

    #region FindEventSubscribers

    [Fact]
    public async Task FindEventSubscribers_NoFilter_ReturnsSuccess()
    {
        var result = await Service.FindEventSubscribersAsync();

        AssertSuccess(result);
        var data = GetData(result);
        data["subscribers"].Should().NotBeNull();
        data["filter"].Should().NotBeNull();
    }

    [Fact]
    public async Task FindEventSubscribers_FilterByNonExistentEvent_ReturnsEmpty()
    {
        var result = await Service.FindEventSubscribersAsync(
            typeName: "NonExistentClass99999",
            eventName: "NonExistentEvent");

        AssertSuccess(result);
        var data = GetData(result);
        (data["subscribers"] as JArray)!.Count.Should().Be(0);
    }

    [Fact]
    public async Task FindEventSubscribers_MaxResults_IsRespected()
    {
        var result = await Service.FindEventSubscribersAsync(maxResults: 1);

        AssertSuccess(result);
        (GetData(result)["subscribers"] as JArray)!.Count.Should().BeLessThanOrEqualTo(1);
    }

    #endregion

    #region FindNamingViolations

    [Fact]
    public async Task FindNamingViolations_ReturnsSuccess()
    {
        var result = await Service.FindNamingViolationsAsync();

        AssertSuccess(result);
        var data = GetData(result);
        data["violations"].Should().NotBeNull();
        data["summary"].Should().NotBeNull();
    }

    [Fact]
    public async Task FindNamingViolations_ViolationsHaveExpectedShape()
    {
        var result = await Service.FindNamingViolationsAsync(maxResults: 10);

        AssertSuccess(result);
        var violations = GetData(result)["violations"] as JArray;
        violations.Should().NotBeNull();

        foreach (var v in violations!)
        {
            v["violationType"].Should().NotBeNull();
            v["description"].Should().NotBeNull();
            v["symbolName"].Should().NotBeNull();
        }
    }

    [Fact]
    public async Task FindNamingViolations_MaxResults_IsRespected()
    {
        var result = await Service.FindNamingViolationsAsync(maxResults: 3);

        AssertSuccess(result);
        (GetData(result)["violations"] as JArray)!.Count.Should().BeLessThanOrEqualTo(3);
    }

    #endregion

    #region FindGodObjects

    [Fact]
    public async Task FindGodObjects_ReturnsSuccess()
    {
        var result = await Service.FindGodObjectsAsync();

        AssertSuccess(result);
        var data = GetData(result);
        data["godObjects"].Should().NotBeNull();
        data["thresholds"].Should().NotBeNull();
    }

    [Fact]
    public async Task FindGodObjects_LowThresholds_FindsRoslynService()
    {
        // RoslynService references many namespaces — must appear as a god object
        var result = await Service.FindGodObjectsAsync(
            memberThreshold: 5,
            lineThreshold: 50,
            outgoingNamespaceThreshold: 2);

        AssertSuccess(result);
        var data = GetData(result);
        var objects = data["godObjects"] as JArray;
        objects.Should().NotBeNull();

        var names = objects!.Select(o => o["typeName"]?.Value<string>() ?? "").ToList();
        names.Should().Contain(n => n.Contains("RoslynService"),
            "RoslynService references many namespaces and should qualify as a god object");
    }

    [Fact]
    public async Task FindGodObjects_HighThresholds_ReturnsEmpty()
    {
        var result = await Service.FindGodObjectsAsync(
            memberThreshold: 100_000,
            lineThreshold: 100_000,
            outgoingNamespaceThreshold: 100_000);

        AssertSuccess(result);
        (GetData(result)["godObjects"] as JArray)!.Count.Should().Be(0);
    }

    [Fact]
    public async Task FindGodObjects_ResultsHaveExpectedShape()
    {
        var result = await Service.FindGodObjectsAsync(
            memberThreshold: 5,
            lineThreshold: 50,
            outgoingNamespaceThreshold: 2);

        AssertSuccess(result);
        var objects = GetData(result)["godObjects"] as JArray;

        foreach (var obj in objects!)
        {
            obj["typeName"].Should().NotBeNull();
            obj["memberCount"].Should().NotBeNull();
            obj["outgoingNamespaceCount"].Should().NotBeNull();
            obj["outgoingNamespaces"].Should().NotBeNull();
        }
    }

    #endregion
}

/// <summary>
/// VB.NET-specific tests for the quality analysis tools.
/// Verifies that VB-compatible tools work on .vb files and C#-only tools skip VB projects silently.
/// </summary>
public class QualityAnalysisVbNetTests : IAsyncLifetime
{
    private RoslynService _service = null!;

    public async Task InitializeAsync()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var solutionDir = currentDir;
        while (solutionDir != null && !File.Exists(Path.Combine(solutionDir, "DotNetLensMcp.sln")))
            solutionDir = Directory.GetParent(solutionDir)?.FullName;
        if (solutionDir == null)
            throw new InvalidOperationException("Could not find DotNetLensMcp.sln");

        var vbSampleSln = Path.Combine(solutionDir, "tests", "DotNetLensMcp.Tests",
            "TestSolutions", "VbNetSample", "VbNetSample.sln");

        _service = new RoslynService();
        var result = await _service.LoadSolutionAsync(vbSampleSln);
        JObject.FromObject(result)["success"]?.Value<bool>().Should()
            .BeTrue("VbNetSample.sln should load successfully");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private void AssertSuccess(object response) =>
        JObject.FromObject(response)["success"]?.Value<bool>().Should()
            .BeTrue($"Expected success but got: {JObject.FromObject(response)["error"]}");

    private JToken GetData(object response)
    {
        AssertSuccess(response);
        return JObject.FromObject(response)["data"]!;
    }

    [Fact]
    public async Task FindLargeClasses_VbProject_ReturnsSuccessWithEmptyOrSmallList()
    {
        // VbNetSample is tiny — may return empty, but must not error
        var result = await _service.FindLargeClassesAsync(memberCountThreshold: 1, lineCountThreshold: 1);
        AssertSuccess(result);
        GetData(result)["classes"].Should().NotBeNull();
    }

    [Fact]
    public async Task GetPublicApiSurface_VbProject_ReturnsSuccess()
    {
        var result = await _service.GetPublicApiSurfaceAsync();
        AssertSuccess(result);
        var projects = GetData(result)["projects"] as JArray;
        projects.Should().NotBeNull();
        projects!.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FindObsoleteUsage_VbProject_ReturnsSuccess()
    {
        var result = await _service.FindObsoleteUsageAsync();
        AssertSuccess(result);
        GetData(result)["usages"].Should().NotBeNull();
    }

    [Fact]
    public async Task FindAsyncViolations_VbProject_ReturnsSuccess()
    {
        // VbNetSample has no async methods — should return empty violations, not an error
        var result = await _service.FindAsyncViolationsAsync();
        AssertSuccess(result);
        GetData(result)["violations"].Should().NotBeNull();
    }

    [Fact]
    public async Task FindDisposableMisuse_VbProject_ReturnsSuccess()
    {
        var result = await _service.FindDisposableMisuseAsync();
        AssertSuccess(result);
        GetData(result)["violations"].Should().NotBeNull();
    }

    [Fact]
    public async Task FindEventSubscribers_VbProject_ReturnsSuccess()
    {
        var result = await _service.FindEventSubscribersAsync();
        AssertSuccess(result);
        GetData(result)["subscribers"].Should().NotBeNull();
    }

    [Fact]
    public async Task FindNamingViolations_VbProject_ReturnsSuccessWithNoViolations()
    {
        // C# only — VB projects are skipped silently, so violations list must be empty
        var result = await _service.FindNamingViolationsAsync();
        AssertSuccess(result);
        (GetData(result)["violations"] as JArray)!.Count.Should().Be(0,
            "VB projects should be skipped silently by find_naming_violations");
    }

    [Fact]
    public async Task FindGodObjects_VbProject_ReturnsSuccessWithNoResults()
    {
        // C# only — VB projects are skipped silently
        var result = await _service.FindGodObjectsAsync(
            memberThreshold: 1,
            lineThreshold: 1,
            outgoingNamespaceThreshold: 1);
        AssertSuccess(result);
        (GetData(result)["godObjects"] as JArray)!.Count.Should().Be(0,
            "VB projects should be skipped silently by find_god_objects");
    }
}
