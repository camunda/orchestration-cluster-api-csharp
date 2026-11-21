using Camunda.Orchestration.Sdk.Generator;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Defect-class guard: the generator must never pair a spec with metadata that was
/// not produced for it. Metadata-derived flags (default-tenant injection, eventual
/// consistency, ...) default to false for a missing operation entry, so a mismatched
/// pair silently generates wrong code. Covers both halves of the defense: input
/// resolution (a spec override never falls back to the repository metadata) and the
/// coverage check (every spec operation must have a metadata entry).
/// </summary>
public sealed class GeneratorSpecMetadataPairingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gen-pair-" + Guid.NewGuid().ToString("N"));

    public GeneratorSpecMetadataPairingTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "repo", "external-spec", "bundled"));
        Directory.CreateDirectory(Path.Combine(_root, "other"));
        File.WriteAllText(RepoSpec, "{}");
        File.WriteAllText(RepoMetadata, "{}");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string RepoRoot => Path.Combine(_root, "repo");
    private string RepoSpec => Path.Combine(RepoRoot, "external-spec", "bundled", "rest-api.bundle.json");
    private string RepoMetadata => Path.Combine(RepoRoot, "external-spec", "bundled", "spec-metadata.json");
    private string OtherSpec => Path.Combine(_root, "other", "spec.json");
    private string OtherMetadata => Path.Combine(_root, "other", "spec-metadata.json");

    [Fact]
    public void DefaultsToRepositoryPair()
    {
        var (spec, meta, error) = Program.ResolveInputs(RepoRoot, [], null, null);
        Assert.Null(error);
        Assert.Equal(RepoSpec, spec);
        Assert.Equal(Path.GetFullPath(RepoMetadata), Path.GetFullPath(meta!));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SpecOverrideWithoutMatchingMetadataIsRejected(bool viaArgs)
    {
        File.WriteAllText(OtherSpec, "{}");
        var (_, meta, error) = viaArgs
            ? Program.ResolveInputs(RepoRoot, [OtherSpec], null, null)
            : Program.ResolveInputs(RepoRoot, [], OtherSpec, null);
        Assert.Null(meta);
        Assert.Contains("matching metadata", error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SpecOverrideUsesSiblingMetadata(bool viaArgs)
    {
        File.WriteAllText(OtherSpec, "{}");
        File.WriteAllText(OtherMetadata, "{}");
        var (spec, meta, error) = viaArgs
            ? Program.ResolveInputs(RepoRoot, [OtherSpec], null, null)
            : Program.ResolveInputs(RepoRoot, [], OtherSpec, null);
        Assert.Null(error);
        Assert.Equal(OtherSpec, spec);
        Assert.Equal(Path.GetFullPath(OtherMetadata), Path.GetFullPath(meta!));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitMetadataOverrideIsUsed(bool viaArgs)
    {
        File.WriteAllText(OtherSpec, "{}");
        var explicitMeta = Path.Combine(_root, "other", "custom-meta.json");
        File.WriteAllText(explicitMeta, "{}");
        var (_, meta, error) = viaArgs
            ? Program.ResolveInputs(RepoRoot, [OtherSpec, explicitMeta], null, null)
            : Program.ResolveInputs(RepoRoot, [], OtherSpec, explicitMeta);
        Assert.Null(error);
        Assert.Equal(explicitMeta, meta);
    }

    [Fact]
    public void MissingSpecOverrideDoesNotFallBackToRepositorySpec()
    {
        var (spec, _, error) = Program.ResolveInputs(RepoRoot, [OtherSpec], null, null);
        Assert.Null(spec);
        Assert.Contains("Spec not found", error);
    }

    private const string TwoOpSpec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "T", "version": "1" },
          "paths": {
            "/a": { "get": { "operationId": "getA", "responses": { "204": { "description": "ok" } } } },
            "/b": { "get": { "operationId": "getB", "responses": { "204": { "description": "ok" } } } }
          },
          "components": { "schemas": {} }
        }
        """;

    private static string Metadata(params string[] ops) =>
        "{\"specHash\":\"sha256:" + new string('0', 64) + "\",\"semanticKeys\":[],\"unions\":[],\"operations\":["
        + string.Join(",", ops.Select(o => $"{{\"operationId\":\"{o}\"}}")) + "]}";

    [Fact]
    public void StrictGenerationRejectsMetadataMissingAnOperation()
    {
        File.WriteAllText(OtherSpec, TwoOpSpec);
        File.WriteAllText(OtherMetadata, Metadata("getA"));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CSharpClientGenerator.Generate(OtherSpec, OtherMetadata, Path.Combine(_root, "out"), requireOperationMetadata: true));
        Assert.Contains("getB", ex.Message);
        Assert.DoesNotContain("getA,", ex.Message);
    }

    [Fact]
    public void StrictGenerationAcceptsCoveringMetadata()
    {
        File.WriteAllText(OtherSpec, TwoOpSpec);
        File.WriteAllText(OtherMetadata, Metadata("getA", "getB"));
        CSharpClientGenerator.Generate(OtherSpec, OtherMetadata, Path.Combine(_root, "out"), requireOperationMetadata: true);
    }
}
