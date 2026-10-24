using System.Text.RegularExpressions;
using Camunda.Orchestration.Sdk.Generator;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Defect-class regression guard for how the generator decides that an operation is
/// served on the bare root (<c>useRootServer: true</c>).
///
/// The defect: <c>UsesRootServer</c> was derived from a hard-coded path prefix
/// (<c>/cluster/v2/</c>) rather than from the operation's effective <c>servers</c>
/// metadata. The bundled spec <em>does</em> carry the path-item <c>servers</c> override
/// that drops the document's <c>/v2</c> base, so the prefix check duplicated — and could
/// drift from — an available routing contract: any root-served operation whose path does
/// not start with <c>/cluster/v2/</c> (or any <c>/cluster/v2/*</c> path that stopped being
/// root-served) would be mis-routed. See the review on PR #471.
///
/// This test targets the defect <em>class</em>: routing must be derived from the effective
/// server precedence (operation &gt; path-item &gt; document), comparing the effective
/// server's base path against the document server's base path — not from the path string.
/// </summary>
public class GeneratorRootServerTests
{
    // Document server carries the `/v2` base; the root override drops it. These are the two
    // shapes that appear in the real bundled spec (there as `{schema}://{host}:{port}[/v2]`
    // templates; concrete URLs here keep the fixture self-contained).
    private const string DocServerV2 = "http://localhost:8080/v2";
    private const string RootServer = "http://localhost:8080";

    // operationId -> expected useRootServer. Each case isolates one precedence/derivation rule:
    //   getDocDefault : no override            -> document server (/v2)        -> false
    //   getPathRoot   : path-item -> root      -> path-item wins over document -> true
    //   getOpRoot     : operation -> root      -> operation wins over document -> true
    //   getOpWinsV2   : path-item root, op /v2 -> operation wins over path-item -> false
    //   getPathV2Same : path-item -> /v2       -> override, but same base as doc -> false
    private static readonly (string OpId, string Path, string? PathServer, string? OpServer, bool Expected)[] Cases =
    {
        ("getDocDefault", "/doc-default", null, null, false),
        ("getPathRoot", "/path-root", RootServer, null, true),
        ("getOpRoot", "/op-root", null, RootServer, true),
        ("getOpWinsV2", "/op-wins-v2", RootServer, DocServerV2, false),
        ("getPathV2Same", "/path-v2-same", DocServerV2, null, false),
    };

    private static string ServersBlock(string? url) =>
        url == null ? "" : $"\"servers\": [ {{ \"url\": \"{url}\" }} ],";

    private static string BuildSpec()
    {
        var paths = string.Join(",\n", Cases.Select(c => $$"""
                "{{c.Path}}": {
                  {{ServersBlock(c.PathServer)}}
                  "get": {
                    "operationId": "{{c.OpId}}",
                    {{ServersBlock(c.OpServer)}}
                    "responses": { "200": { "description": "ok" } }
                  }
                }
            """));

        return $$"""
            {
              "openapi": "3.0.3",
              "info": { "title": "Test", "version": "1.0.0" },
              "servers": [ { "url": "{{DocServerV2}}" } ],
              "paths": {
            {{paths}}
              }
            }
            """;
    }

    private static bool UseRootServerOf(string clientSource, string operationId)
    {
        // Each generated call site renders `useRootServer: <bool>, ct), "<operationId>"`.
        var m = Regex.Match(
            clientSource,
            $@"useRootServer:\s*(true|false)\b[^\n]*""{Regex.Escape(operationId)}""");
        Assert.True(m.Success, $"No generated call site found for operationId '{operationId}'.");
        return m.Groups[1].Value == "true";
    }

    [Fact]
    public void Root_server_usage_is_derived_from_effective_server_precedence()
    {
        using var tmp = TempSpecDir.Create();
        File.WriteAllText(tmp.SpecPath, BuildSpec());
        File.WriteAllText(tmp.MetadataPath, MinimalBundledSpec.MinimalMetadata);

        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);
        var client = File.ReadAllText(Path.Combine(tmp.OutputDir, "CamundaClient.Generated.cs"));

        foreach (var (opId, _, _, _, expected) in Cases)
        {
            Assert.True(
                UseRootServerOf(client, opId) == expected,
                $"operationId '{opId}': expected useRootServer={expected} (derived from effective "
                + "server metadata: operation > path-item > document), but the generator emitted the opposite.");
        }
    }

    private sealed class TempSpecDir : IDisposable
    {
        public string Root { get; }
        public string SpecPath => Path.Combine(Root, "spec.json");
        public string MetadataPath => Path.Combine(Root, "metadata.json");
        public string OutputDir => Path.Combine(Root, "out");

        private TempSpecDir(string root)
        {
            Root = root;
            Directory.CreateDirectory(OutputDir);
        }

        public static TempSpecDir Create()
        {
            var dir = Path.Combine(Path.GetTempPath(), "csharp-gen-rootserver-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return new TempSpecDir(dir);
        }

        public void Dispose()
        {
            try
            { Directory.Delete(Root, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}
