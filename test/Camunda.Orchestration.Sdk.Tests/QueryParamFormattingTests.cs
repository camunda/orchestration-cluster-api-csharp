using System.Globalization;
using System.Net;
using System.Reflection;
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

        // Matches Uri.EscapeDataString(<expr>.ToString()!) — the pre-fix pattern for both
        // query-parameter (queryParts.Add) and path-parameter (interpolated) serialization.
        var bareToString = new Regex(@"Uri\.EscapeDataString\([^)]*\.ToString\(\)!\)");
        var matches = bareToString.Matches(generated);

        Assert.True(
            matches.Count == 0,
            $"Found {matches.Count} parameter(s) serialized via bare .ToString(): " +
            string.Join(" | ", matches.Take(5).Select(m => m.Value)));
    }

    /// <summary>
    /// Numeric branded keys (<c>ICamundaLongKey</c>, e.g. <c>BackupId</c>) are not
    /// <see cref="IFormattable"/>, so they hit <c>FormatParam</c>'s generic fallback. That fallback
    /// must format the underlying <c>long</c> with <see cref="CultureInfo.InvariantCulture"/>;
    /// otherwise a hostile ambient culture (e.g. fa-IR, whose negative sign is U+2212) can leak a
    /// non-ASCII representation onto the wire. Class-scoped: covers every <c>ICamundaLongKey</c>
    /// via the centralized helper, not just <c>BackupId</c>.
    /// </summary>
    [Fact]
    public void FormatParam_FormatsNumericBrandedKeys_Invariantly()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");

            // Negative value exercises the culture-sensitive negative sign (fa-IR uses U+2212).
            var key = BackupId.AssumeExists(-1234567L);
            var formatted = InvokeFormatParam(key);

            Assert.Equal("-1234567", formatted);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task DeleteHistoryBackup_SerializesNumericKeyInvariantly()
    {
        _handler.Enqueue(HttpStatusCode.NoContent, "");

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");

            using var client = CreateClient();
            await client.DeleteHistoryBackupAsync(BackupId.AssumeExists(-42L));

            var path = Uri.UnescapeDataString(_handler.Requests.Single().RequestUri!.AbsolutePath);

            Assert.Contains("/backups/history/-42", path);
            Assert.DoesNotContain("\u2212", path);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static string InvokeFormatParam(object value)
    {
        var method = typeof(CamundaClient).GetMethod(
            "FormatParam",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, [value])!;
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

    public void Dispose() => _handler.Dispose();
}
