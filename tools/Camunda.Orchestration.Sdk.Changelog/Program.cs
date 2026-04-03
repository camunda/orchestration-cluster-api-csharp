namespace Camunda.Orchestration.Sdk.Changelog;

/// <summary>
/// CLI entry point for the C# SDK breaking changes detection tool.
///
///   dotnet run --project tools/Camunda.Orchestration.Sdk.Changelog -- \
///     --old snapshots/stable-8.9/ \
///     --new src/Camunda.Orchestration.Sdk/Generated/ \
///     --old-version stable/8.9 --new-version main
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("-h") || args.Contains("--help"))
        {
            PrintUsage();
            return 0;
        }

        var oldPath = GetArg(args, "--old");
        var newPath = GetArg(args, "--new");
        var oldVersion = GetArg(args, "--old-version") ?? "old";
        var newVersion = GetArg(args, "--new-version") ?? "new";
        var format = GetArg(args, "--format") ?? "markdown";
        var output = GetArg(args, "--output");

        if (oldPath == null || newPath == null)
        {
            Console.Error.WriteLine("Error: --old and --new are required.");
            Console.Error.WriteLine();
            PrintUsage();
            return 2;
        }

        var oldFiles = ResolveFiles(oldPath);
        var newFiles = ResolveFiles(newPath);

        if (oldFiles.Length == 0)
        {
            Console.Error.WriteLine($"Error: no .cs files found at '{oldPath}'");
            return 2;
        }
        if (newFiles.Length == 0)
        {
            Console.Error.WriteLine($"Error: no .cs files found at '{newPath}'");
            return 2;
        }

        Console.Error.WriteLine($"Parsing old ({oldVersion}): {oldFiles.Length} file(s)");
        var oldSurface = Parser.ParseFiles(oldFiles);
        Console.Error.WriteLine($"  {oldSurface.Classes.Count} classes, {oldSurface.Enums.Count} enums, {oldSurface.Structs.Count} structs, {oldSurface.ClientMethods.Count} methods");

        Console.Error.WriteLine($"Parsing new ({newVersion}): {newFiles.Length} file(s)");
        var newSurface = Parser.ParseFiles(newFiles);
        Console.Error.WriteLine($"  {newSurface.Classes.Count} classes, {newSurface.Enums.Count} enums, {newSurface.Structs.Count} structs, {newSurface.ClientMethods.Count} methods");

        Console.Error.WriteLine("Diffing...");
        var diff = Differ.Diff(oldSurface, newSurface, oldVersion, newVersion);

        var report = format.ToLowerInvariant() switch
        {
            "json" => Reporter.GenerateJson(diff),
            _ => Reporter.GenerateMarkdown(diff),
        };

        if (output != null)
        {
            var dir = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(output, report);
            Console.Error.WriteLine($"Report written to {output}");
        }
        else
        {
            Console.Write(report);
        }

        Console.Error.WriteLine($"Done: {diff.Breaking} breaking, {diff.Warnings} warning, {diff.Additive} additive, {diff.Info} info — {diff.Total} total");

        // Exit 1 if breaking changes found (for CI gating)
        return diff.Breaking > 0 ? 1 : 0;
    }

    private static string[] ResolveFiles(string path)
    {
        if (File.Exists(path) && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return [path];

        if (Directory.Exists(path))
            return Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories);

        return [];
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
                return args[i + 1];
        }
        return null;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
            C# SDK Breaking Changes Detector

            Usage:
              dotnet run --project tools/Camunda.Orchestration.Sdk.Changelog -- [options]

            Required:
              --old <path>          Path to old generated .cs file(s) or directory
              --new <path>          Path to new generated .cs file(s) or directory

            Options:
              --old-version <name>  Label for old version (default: "old")
              --new-version <name>  Label for new version (default: "new")
              --format <fmt>        Output format: markdown (default) or json
              --output <path>       Write report to file (default: stdout)
              -h, --help            Show this help

            Exit codes:
              0  No breaking changes
              1  Breaking changes detected
              2  Usage error

            Examples:
              # Compare two snapshot directories
              dotnet run --project tools/Camunda.Orchestration.Sdk.Changelog -- \
                --old snapshots/stable-8.9/ \
                --new src/Camunda.Orchestration.Sdk/Generated/ \
                --old-version stable/8.9 --new-version main

              # JSON output to file
              dotnet run --project tools/Camunda.Orchestration.Sdk.Changelog -- \
                --old snapshots/stable-8.9/ \
                --new src/Camunda.Orchestration.Sdk/Generated/ \
                --format json --output report.json
            """);
    }
}
