namespace Camunda.Orchestration.Sdk.Generator;

/// <summary>
/// CLI entry point for the C# SDK generator.
/// Mirrors the JS pipeline: fetch spec → bundle → generate models & client.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Console.Error.WriteLine("[generator] Cannot find repository root (looked for Camunda.Orchestration.Sdk.sln)");
            return 1;
        }

        var outputDir = Path.Combine(repoRoot, "src", "Camunda.Orchestration.Sdk", "Generated");
        var (specPath, metadataPath, error) = ResolveInputs(
            repoRoot, args,
            Environment.GetEnvironmentVariable("CAMUNDA_SDK_SPEC_PATH"),
            Environment.GetEnvironmentVariable("CAMUNDA_SDK_METADATA_PATH"));
        if (error != null)
        {
            Console.Error.WriteLine($"[generator] {error}");
            Console.Error.WriteLine("[generator] Run the spec fetch/bundle step first (see scripts/bundle-spec.sh)");
            return 1;
        }

        try
        {
            CSharpClientGenerator.Generate(specPath!, metadataPath!, outputDir, requireOperationMetadata: true);
            Console.WriteLine("[generator] Generation completed successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[generator] Failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    /// <summary>
    /// Resolve the spec and its metadata as a pair. A spec override (first argument or
    /// CAMUNDA_SDK_SPEC_PATH) never falls back to the repository's metadata: the metadata
    /// comes from the second argument / CAMUNDA_SDK_METADATA_PATH, or else the
    /// spec-metadata.json next to the overriding spec. An override path that does not
    /// exist is an error rather than a silent fallback to the default inputs.
    /// </summary>
    internal static (string? SpecPath, string? MetadataPath, string? Error) ResolveInputs(
        string repoRoot, string[] args, string? envSpec, string? envMetadata)
    {
        var specOverride = args.Length > 0 ? args[0] : NullIfEmpty(envSpec);
        var metadataOverride = args.Length > 1 ? args[1] : NullIfEmpty(envMetadata);

        var specPath = specOverride
            ?? Path.Combine(repoRoot, "external-spec", "bundled", "rest-api.bundle.json");
        if (!File.Exists(specPath))
            return (null, null, $"Spec not found at {specPath}");

        var metadataPath = metadataOverride
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(specPath))!, "spec-metadata.json");
        if (!File.Exists(metadataPath))
            return (null, null, specOverride != null && metadataOverride == null
                ? $"Spec metadata not found at {metadataPath}. A spec override needs its matching metadata: "
                  + "place spec-metadata.json next to the spec, or pass CAMUNDA_SDK_METADATA_PATH / a second argument."
                : $"Spec metadata not found at {metadataPath}");

        return (specPath, metadataPath, null);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Camunda.Orchestration.Sdk.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
