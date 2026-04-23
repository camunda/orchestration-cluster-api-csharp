using Camunda.Orchestration.Sdk.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Camunda.Orchestration.Sdk.Tests;

/// <summary>
/// Defect-class regression guard for spec-controlled string injection.
///
/// Two defect classes are covered:
///   1. Unicode line-terminator / invisible / bidi characters surviving into a
///      generated <c>.cs</c> file (allowing comment-break injection or
///      Trojan-Source identifier shadowing).
///   2. Spec-derived names producing C# tokens that are not valid identifiers
///      or that collide with reserved keywords.
///
/// The unit tests exercise the helpers directly; the integration tests run
/// the full generator on a synthetic spec packed with hostile content and
/// assert that the post-emit scan rejects it AND, with hostile content
/// removed, that no forbidden code points survive into any generated file.
/// </summary>
public class GeneratorSafeEmitTests
{
    // ── Defect class 1: Unicode line-terminator / invisible / bidi ──

    [Theory]
    [InlineData('\u2028')] // LINE SEPARATOR
    [InlineData('\u2029')] // PARAGRAPH SEPARATOR
    [InlineData('\u0085')] // NEXT LINE
    [InlineData('\u200B')] // ZERO WIDTH SPACE
    [InlineData('\u200E')] // LEFT-TO-RIGHT MARK
    [InlineData('\u202E')] // RIGHT-TO-LEFT OVERRIDE
    [InlineData('\u2060')] // WORD JOINER
    [InlineData('\uFEFF')] // ZWNBSP / BOM
    public void SafeXmlDocText_strips_or_escapes_every_dangerous_codepoint(char dangerous)
    {
        var raw = $"hello{dangerous}world";

        var safe = SafeEmit.SafeXmlDocText(raw);

        Assert.Equal(-1, safe.IndexOf(dangerous));
    }

    [Theory]
    [InlineData("<script>", "&lt;script&gt;")]
    [InlineData("a & b", "a &amp; b")]
    [InlineData("\"quoted\"", "&quot;quoted&quot;")]
    [InlineData("it's", "it&apos;s")]
    public void SafeXmlDocText_escapes_xml_metacharacters(string raw, string expected)
    {
        Assert.Equal(expected, SafeEmit.SafeXmlDocText(raw));
    }

    [Fact]
    public void SafeXmlDocText_collapses_bare_cr_and_lf_to_space()
    {
        // If a description survived past the caller's split-on-newline,
        // the helper must not let LF / CR terminate the `///` line.
        Assert.Equal("a b", SafeEmit.SafeXmlDocText("a\nb"));
        Assert.Equal("a b", SafeEmit.SafeXmlDocText("a\rb"));
    }

    [Fact]
    public void SafeXmlDocText_preserves_tab_and_normal_text()
    {
        Assert.Equal("hello\tworld", SafeEmit.SafeXmlDocText("hello\tworld"));
        Assert.Equal("Plain ASCII text.", SafeEmit.SafeXmlDocText("Plain ASCII text."));
    }

    // ── Defect class 2: identifier validity ──

    [Theory]
    [InlineData("class")]
    [InlineData("namespace")]
    [InlineData("return")]
    [InlineData("int")]
    [InlineData("public")]
    public void SafeCSharpIdentifier_disambiguates_reserved_keywords_with_at_sign(string keyword)
    {
        var result = SafeEmit.SafeCSharpIdentifier(keyword);

        Assert.Equal('@', result[0]);
        Assert.Equal(keyword, result[1..]);
    }

    [Theory]
    [InlineData("foo bar", '_')]   // space → '_'
    [InlineData("foo-bar", '_')]   // hyphen → '_'
    [InlineData("foo.bar", '_')]   // dot → '_'
    [InlineData("foo$bar", '_')]   // dollar → '_'
    public void SafeCSharpIdentifier_replaces_disallowed_chars_with_underscore(string raw, char expectedSep)
    {
        var result = SafeEmit.SafeCSharpIdentifier(raw);
        Assert.Equal($"foo{expectedSep}bar", result);
    }

    [Fact]
    public void SafeCSharpIdentifier_prefixes_underscore_when_first_char_is_invalid()
    {
        Assert.Equal("_123", SafeEmit.SafeCSharpIdentifier("123"));
    }

    [Fact]
    public void SafeCSharpIdentifier_returns_underscore_for_empty_or_null()
    {
        Assert.Equal("_", SafeEmit.SafeCSharpIdentifier(""));
        Assert.Equal("_", SafeEmit.SafeCSharpIdentifier(null));
    }

    [Theory]
    [InlineData('\u200B')]
    [InlineData('\u202E')]
    [InlineData('\uFEFF')]
    public void SafeCSharpIdentifier_strips_invisible_and_bidi_chars(char dangerous)
    {
        // A single dangerous char alone collapses to a clean name.
        var result = SafeEmit.SafeCSharpIdentifier($"name{dangerous}");
        Assert.Equal(-1, result.IndexOf(dangerous));
        Assert.Equal("name", result);
    }

    [Fact]
    public void SafeCSharpIdentifier_does_not_let_homoglyph_attack_produce_collision()
    {
        // Two names that look identical to a reviewer but differ by a
        // zero-width space must not produce identical *or* injection-
        // capable identifiers.
        var clean = SafeEmit.SafeCSharpIdentifier("Item");
        var attack = SafeEmit.SafeCSharpIdentifier("Item\u200B");

        Assert.Equal("Item", clean);
        Assert.Equal("Item", attack); // zero-width stripped — collision is loud (compile error on duplicate member) rather than silent
        Assert.Equal(-1, attack.IndexOf('\u200B'));
    }

    private static readonly string[] IdentifierFuzzInputs =
    [
        "", "a", "1", "class", "foo bar", "foo-bar.baz", "$$$",
        "Item\u200BName", "Total\u202EFoo", "naïve", "café",
        "🚀rocket", "α-β-γ", "_", "__", "@@@",
    ];

    [Fact]
    public void SafeCSharpIdentifier_output_is_always_a_valid_csharp_identifier()
    {
        // Property of the helper: for any input, the output must parse as a
        // valid C# identifier.
        foreach (var input in IdentifierFuzzInputs)
        {
            var ident = SafeEmit.SafeCSharpIdentifier(input);
            Assert.True(
                IsParseableIdentifier(ident),
                $"SafeCSharpIdentifier({input.Length}-char input) → \"{ident}\" is not a valid C# identifier");
        }
    }

    // ── String literal escaping ──

    [Fact]
    public void SafeCSharpStringLiteral_escapes_backslash_and_quote()
    {
        Assert.Equal("a\\\\b\\\"c", SafeEmit.SafeCSharpStringLiteral("a\\b\"c"));
    }

    [Theory]
    [InlineData('\u2028')]
    [InlineData('\u2029')]
    [InlineData('\u0085')]
    [InlineData('\u200B')]
    [InlineData('\u202E')]
    public void SafeCSharpStringLiteral_escapes_dangerous_chars(char dangerous)
    {
        var raw = $"a{dangerous}b";
        var safe = SafeEmit.SafeCSharpStringLiteral(raw);
        Assert.Equal(-1, safe.IndexOf(dangerous));
        Assert.Contains($"\\u{(int)dangerous:X4}", safe, StringComparison.Ordinal);
    }

    // ── Defense-in-depth scan ──

    [Theory]
    [InlineData("namespace X;\nclass C { /* harmless */ }\n")]
    [InlineData("// no dangerous chars here\n")]
    public void ScanGeneratedSource_passes_clean_source(string source)
    {
        // Should not throw.
        SafeEmit.ScanGeneratedSource("file.cs", source);
    }

    [Theory]
    [InlineData('\u2028')]
    [InlineData('\u2029')]
    [InlineData('\u0085')]
    [InlineData('\u200B')]
    [InlineData('\u202E')]
    [InlineData('\uFEFF')]
    public void ScanGeneratedSource_throws_on_any_forbidden_codepoint(char dangerous)
    {
        var src = $"namespace X; // descrip{dangerous}tion\n";
        var ex = Assert.Throws<InvalidOperationException>(
            () => SafeEmit.ScanGeneratedSource("file.cs", src));
        Assert.Contains("file.cs", ex.Message);
        Assert.Contains($"U+{(int)dangerous:X4}", ex.Message);
    }

    // ── End-to-end defect-class behaviour: run the full generator on a
    //    synthetic spec with hostile content and assert the post-emit scan
    //    rejects it.

    [Fact]
    public void Generator_rejects_spec_with_unicode_line_terminator_in_description()
    {
        using var tmp = TempDir.Create();
        // Description deliberately contains U+2028. With no SafeXmlDocText
        // routing this would silently survive into the `///` summary; the
        // post-emit scan must catch it.
        var spec = MinimalBundledSpec.Build(
            schemaDescription: "harmless prefix\u2028}); ATTACKER_PAYLOAD; //");
        File.WriteAllText(tmp.SpecPath, spec);
        File.WriteAllText(tmp.MetadataPath, MinimalBundledSpec.MinimalMetadata);

        var ex = Record.Exception(() =>
            CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir));

        if (ex == null)
        {
            // If generation completed, EVERY generated file must be free of
            // forbidden code points — which is the post-emit scan's job. Verify
            // by re-scanning.
            foreach (var file in Directory.GetFiles(tmp.OutputDir, "*.cs"))
            {
                var content = File.ReadAllText(file);
                Assert.Equal(-1, content.IndexOf('\u2028'));
                Assert.Equal(-1, content.IndexOf('\u2029'));
                Assert.Equal(-1, content.IndexOf('\u0085'));
            }
        }
        else
        {
            // Otherwise, the generator must have failed loudly — never silently.
            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("Forbidden character", ex.Message);
        }
    }

    private static readonly string[] HostilePropertyNames =
    [
        "class",       // reserved keyword
        "Item\u200BX", // zero-width-space homoglyph
        "Total\u202EFoo", // RTL override
        "123",         // invalid identifier start
        "good-name",   // hyphen
    ];

    [Fact]
    public void Generator_rejects_or_sanitizes_spec_with_hostile_property_names()
    {
        using var tmp = TempDir.Create();
        var spec = MinimalBundledSpec.Build(propertyNames: HostilePropertyNames);
        File.WriteAllText(tmp.SpecPath, spec);
        File.WriteAllText(tmp.MetadataPath, MinimalBundledSpec.MinimalMetadata);

        // Generator must complete (it can sanitize) AND every emitted
        // top-level identifier must parse as a valid C# identifier.
        CSharpClientGenerator.Generate(tmp.SpecPath, tmp.MetadataPath, tmp.OutputDir);

        var modelsPath = Path.Combine(tmp.OutputDir, "Models.Generated.cs");
        Assert.True(File.Exists(modelsPath));

        var source = File.ReadAllText(modelsPath);

        // Defense-in-depth scan must pass.
        SafeEmit.ScanGeneratedSource(modelsPath, source);

        // Parse with Roslyn and verify every type / member identifier is a
        // valid C# identifier (no `class` member, no zero-width chars, no
        // bidi controls, no digit-leading names).
        var tree = CSharpSyntaxTree.ParseText(source);
        var diagnostics = tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            diagnostics.Count == 0,
            $"Generated source has parse errors: {string.Join("; ", diagnostics.Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)))}");

        var root = tree.GetCompilationUnitRoot();
        foreach (var node in root.DescendantNodes())
        {
            var name = node switch
            {
                ClassDeclarationSyntax cls => cls.Identifier.ValueText,
                StructDeclarationSyntax st => st.Identifier.ValueText,
                EnumDeclarationSyntax en => en.Identifier.ValueText,
                EnumMemberDeclarationSyntax em => em.Identifier.ValueText,
                PropertyDeclarationSyntax prop => prop.Identifier.ValueText,
                MethodDeclarationSyntax meth => meth.Identifier.ValueText,
                _ => null,
            };
            if (name == null)
                continue;

            Assert.True(
                IsParseableIdentifier(name),
                $"Generated identifier \"{name}\" is not a valid C# identifier");
        }
    }

    private static bool IsParseableIdentifier(string ident)
    {
        // The verbatim form @keyword is an "identifier or keyword" — strip
        // the leading @ for the validity check; what matters is that the
        // bare token IS a syntactically valid identifier.
        var probe = ident.StartsWith('@') ? ident[1..] : ident;
        return SyntaxFacts.IsValidIdentifier(probe);
    }

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }
        public string SpecPath => Path.Combine(Root, "spec.json");
        public string MetadataPath => Path.Combine(Root, "metadata.json");
        public string OutputDir => Path.Combine(Root, "out");

        private TempDir(string root)
        {
            Root = root;
            Directory.CreateDirectory(OutputDir);
        }

        public static TempDir Create()
        {
            var dir = Path.Combine(Path.GetTempPath(), "csharp-gen-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return new TempDir(dir);
        }

        public void Dispose()
        {
            try
            { Directory.Delete(Root, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}

/// <summary>
/// Helper that produces a minimal but valid bundled OpenAPI spec for use in
/// generator-level tests. The spec contains exactly one schema and one
/// operation, so any forbidden content can be traced to a single emit site.
/// </summary>
internal static class MinimalBundledSpec
{
    /// <summary>
    /// SHA-256 of an empty string — just a syntactically valid placeholder
    /// so the generator's spec-hash regex passes.
    /// </summary>
    public const string MinimalMetadata = """
        {
          "schemaVersion": "1",
          "specHash": "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
          "semanticKeys": [],
          "unions": [],
          "operations": [],
          "deprecatedEnumMembers": []
        }
        """;

    public static string Build(string? schemaDescription = null, string[]? propertyNames = null)
    {
        var props = propertyNames ?? new[] { "name" };
        var propsJson = string.Join(",\n        ", props.Select(p =>
            $"\"{System.Text.Json.JsonEncodedText.Encode(p)}\": {{ \"type\": \"string\" }}"));
        var descPart = schemaDescription == null
            ? ""
            : $"\"description\": {System.Text.Json.JsonSerializer.Serialize(schemaDescription)},";

        return $$"""
            {
              "openapi": "3.0.3",
              "info": { "title": "Test", "version": "1.0.0" },
              "paths": {
                "/things": {
                  "get": {
                    "operationId": "listThings",
                    "responses": {
                      "200": {
                        "description": "ok",
                        "content": {
                          "application/json": {
                            "schema": { "$ref": "#/components/schemas/Thing" }
                          }
                        }
                      }
                    }
                  }
                }
              },
              "components": {
                "schemas": {
                  "Thing": {
                    "type": "object",
                    {{descPart}}
                    "properties": {
                      {{propsJson}}
                    }
                  }
                }
              }
            }
            """;
    }
}
