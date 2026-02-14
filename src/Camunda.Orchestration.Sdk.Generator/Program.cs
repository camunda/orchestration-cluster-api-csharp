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

        var specPath = Path.Combine(repoRoot, "external-spec", "bundled", "rest-api.bundle.json");
        var metadataPath = Path.Combine(repoRoot, "external-spec", "bundled", "spec-metadata.json");
        var outputDir = Path.Combine(repoRoot, "src", "Camunda.Orchestration.Sdk", "Generated");

        // Allow overriding spec path via CLI args or env
        if (args.Length > 0 && File.Exists(args[0]))
            specPath = args[0];
        else if (Environment.GetEnvironmentVariable("CAMUNDA_SDK_SPEC_PATH") is { } envSpec && File.Exists(envSpec))
            specPath = envSpec;

        if (!File.Exists(specPath))
        {
            Console.Error.WriteLine($"[generator] Bundled spec not found at {specPath}");
            Console.Error.WriteLine("[generator] Run the spec fetch/bundle step first (see scripts/bundle-spec.sh)");
            return 1;
        }

        if (!File.Exists(metadataPath))
        {
            Console.Error.WriteLine($"[generator] Spec metadata not found at {metadataPath}");
            Console.Error.WriteLine("[generator] Run the spec bundle step first (see scripts/bundle-spec.sh)");
            return 1;
        }

        try
        {
            CSharpClientGenerator.Generate(specPath, metadataPath, outputDir);
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
