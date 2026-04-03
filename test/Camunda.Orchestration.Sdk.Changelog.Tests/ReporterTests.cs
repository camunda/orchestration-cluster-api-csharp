using Xunit;

namespace Camunda.Orchestration.Sdk.Changelog.Tests;

public class ReporterTests
{
    [Fact]
    public void MarkdownReportContainsSummaryLine()
    {
        var diff = CreateSampleDiff();
        var md = Reporter.GenerateMarkdown(diff);

        Assert.Contains("# API Changelog: v1 → v2", md);
        Assert.Contains("breaking", md);
        Assert.Contains("total changes", md);
    }

    [Fact]
    public void MarkdownReportShowsBreakingSection()
    {
        var diff = CreateSampleDiff();
        var md = Reporter.GenerateMarkdown(diff);

        Assert.Contains("## 🔴 Breaking Changes", md);
        Assert.Contains("`Foo.Bar` removed", md);
    }

    [Fact]
    public void JsonReportIsValidJson()
    {
        var diff = CreateSampleDiff();
        var json = Reporter.GenerateJson(diff);

        Assert.Contains("\"oldVersion\": \"v1\"", json);
        Assert.Contains("\"newVersion\": \"v2\"", json);
        Assert.Contains("\"breaking\": 1", json);
        Assert.Contains("\"kind\": \"PropertyRemoved\"", json);
    }

    [Fact]
    public void EmptyDiffShowsNoChanges()
    {
        var old = Parser.ParseSource("namespace Test;");
        var diff = Differ.Diff(old, old, "v1", "v2");
        var md = Reporter.GenerateMarkdown(diff);

        Assert.Contains("No changes detected.", md);
    }

    private static DiffResult CreateSampleDiff()
    {
        var old = Parser.ParseSource("""
            namespace Test;
            public sealed class Foo
            {
                public string Bar { get; set; } = null!;
            }
            """);
        var @new = Parser.ParseSource("""
            namespace Test;
            public sealed class Foo { }
            """);

        return Differ.Diff(old, @new, "v1", "v2");
    }
}
