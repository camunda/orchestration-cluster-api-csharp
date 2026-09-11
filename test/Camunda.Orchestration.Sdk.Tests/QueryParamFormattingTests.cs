using System.Net;
using System.Text.RegularExpressions;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Regression tests for query/path parameter serialization on the wire.
///
/// Bug: generated operations serialized parameter values with a bare, culture-sensitive
/// <c>.ToString()</c>. For <see cref="DateTimeOffset"/>/<see cref="DateOnly"/> this produced
/// non-ISO-8601 values (e.g. <c>01/06/2026 00:00:00 +00:00</c>) and for <see cref="bool"/> it
/// produced <c>True</c>/<c>False</c> — both rejected by the server (INVALID_ARGUMENT).
///
/// See https://github.com/camunda/orchestration-cluster-api-csharp/issues/433.
/// </summary>
public sealed class QueryParamFormattingTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler = new();

    private CamundaClient CreateClient()
    {
        return new CamundaClient(new CamundaOptions
        {
            Config = new Dictionary<string, string>
            {
                ["CAMUNDA_REST_ADDRESS"] = "https://mock.local",
            },
            HttpMessageHandler = _handler,
        });
    }

    [Fact]
    public async Task GetUsageMetrics_SerializesDatesAsIso8601()
    {
        _handler.Enqueue(HttpStatusCode.OK, "{}");

        var startTime = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var endTime = new DateTimeOffset(2027, 5, 31, 23, 59, 59, TimeSpan.Zero);

        using var client = CreateClient();
        await client.GetUsageMetricsAsync(startTime, endTime);

        var uri = _handler.Requests.Single().RequestUri!;
        var query = Uri.UnescapeDataString(uri.Query);

        // ISO 8601 round-trip: no locale slashes, must contain the 'T' date/time separator.
        Assert.Contains("startTime=2026-06-01T00:00:00", query);
        Assert.Contains("endTime=2027-05-31T23:59:59", query);
        Assert.DoesNotContain("01/06/2026", query);
    }

    [Fact]
    public async Task GetUsageMetrics_SerializesBoolAsLowercase()
    {
        _handler.Enqueue(HttpStatusCode.OK, "{}");

        var startTime = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var endTime = new DateTimeOffset(2027, 5, 31, 23, 59, 59, TimeSpan.Zero);

        using var client = CreateClient();
        await client.GetUsageMetricsAsync(startTime, endTime, withTenants: true);

        var query = Uri.UnescapeDataString(_handler.Requests.Single().RequestUri!.Query);

        Assert.Contains("withTenants=true", query);
        Assert.DoesNotContain("withTenants=True", query);
    }

    /// <summary>
    /// Class-scoped guard: no generated operation may serialize any parameter through a bare,
    /// culture-sensitive <c>.ToString()</c>. All query/path parameter values must be routed through
    /// the invariant <c>FormatParam</c> helper. This catches the whole defect class, not just the
    /// specific <c>getUsageMetrics</c> instance from the issue.
    /// </summary>
    [Fact]
    public void GeneratedClient_DoesNotSerializeParamsWithBareToString()
    {
        var generated = ReadGeneratedClientSource();

        // Matches Uri.EscapeDataString(<expr>.ToString()) — the pre-fix pattern for both
        // query-parameter (queryParts.Add) and path-parameter (interpolated) serialization.
        // The trailing null-forgiving operator is optional so a regression that drops the `!`
        // (e.g. Uri.EscapeDataString(value.ToString())) is caught just the same.
        var bareToString = new Regex(@"Uri\.EscapeDataString\([^)]*\.ToString\(\)!?\)");
        var matches = bareToString.Matches(generated);

        Assert.True(
            matches.Count == 0,
            $"Found {matches.Count} parameter(s) serialized via bare .ToString(): " +
            string.Join(" | ", matches.Take(5).Select(m => m.Value)));
    }

    private static string ReadGeneratedClientSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Camunda.Orchestration.Sdk.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(
            dir!.FullName,
            "src",
            "Camunda.Orchestration.Sdk",
            "Generated",
            "CamundaClient.Generated.cs");

        Assert.True(File.Exists(path), $"Generated client not found at {path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Locks in the wire format for <see cref="DateOnly"/> parameters: an explicit ISO 8601
    /// date (<c>yyyy-MM-dd</c>), independent of the runtime's support for the <c>"O"</c>
    /// round-trip specifier on <see cref="DateOnly"/>. Exercised directly against the private
    /// <c>FormatParam</c> helper since no generated operation currently exposes a
    /// <see cref="DateOnly"/> parameter.
    /// </summary>
    [Fact]
    public void FormatParam_SerializesDateOnlyAsIso8601Date()
    {
        var formatParam = typeof(CamundaClient).GetMethod(
            "FormatParam",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(formatParam);

        var result = (string)formatParam!.Invoke(null, new object[] { new DateOnly(2026, 1, 6) })!;

        Assert.Equal("2026-01-06", result);
    }

    public void Dispose() => _handler.Dispose();
}
