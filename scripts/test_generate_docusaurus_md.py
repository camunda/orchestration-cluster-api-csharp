"""Tests for the doc-rendering fixes in ``scripts/generate-docusaurus-md.py``.

These guard two rendering defects reported on the generated C# API reference
(camunda-docs PR #9409):

* **C#-1** — a DocFX ``<summary>`` carries the short OpenAPI summary on its first
  line and the longer description on the following lines. A single newline
  collapses to a space in rendered HTML, so the title runs into the description
  (``Restore from a backup Restores the cluster...``). ``_format_description``
  must separate the title into its own paragraph.
* **C#-2** — method-name headings contain generic type arguments such as
  ``ConsistencyOptions<Result>?``. MDX treats ``<Result>`` as a JSX element and
  drops it (also corrupting the surrounding text). ``_escape_heading`` must
  backslash-escape the angle brackets so they render literally.

The tests are class-scoped: they assert the transform is correct for *any*
generic heading / any title+body summary, and that the transforms are actually
wired into the emission (``generate_camunda_client``) and parsing
(``load_all_types``) paths — not just that the helpers exist.

Run directly (no pytest required)::

    python scripts/test_generate_docusaurus_md.py

or via pytest.
"""

from __future__ import annotations

import importlib.util
import sys
import tempfile
from pathlib import Path

_SCRIPT = Path(__file__).resolve().parent / "generate-docusaurus-md.py"
_spec = importlib.util.spec_from_file_location("gen_docusaurus_md", _SCRIPT)
assert _spec is not None and _spec.loader is not None
gen = importlib.util.module_from_spec(_spec)
# Register before exec so dataclass forward references (e.g. TypeItem -> MemberItem)
# resolve against the module namespace.
sys.modules["gen_docusaurus_md"] = gen
_spec.loader.exec_module(gen)


# ---------------------------------------------------------------------------
# C#-2 — escape angle brackets in headings
# ---------------------------------------------------------------------------


def test_escape_heading_escapes_generic_args():
    src = "SearchAsync(ConsistencyOptions<Result>?, CancellationToken)"
    out = gen._escape_heading(src)
    assert out == "SearchAsync(ConsistencyOptions\\<Result\\>?, CancellationToken)"
    # No bare angle bracket may survive.
    assert "<" not in out.replace("\\<", "") and ">" not in out.replace("\\>", "")


def test_escape_heading_escapes_every_occurrence():
    src = "Foo<A>(Bar<B>, Baz<C>)"
    out = gen._escape_heading(src)
    assert out.count("\\<") == 3
    assert out.count("\\>") == 3


def test_escape_heading_noop_without_brackets():
    assert gen._escape_heading("PlainMethod(String, Int32)") == "PlainMethod(String, Int32)"


# ---------------------------------------------------------------------------
# C#-1 — separate summary title from description body
# ---------------------------------------------------------------------------


def test_format_description_splits_title_from_body():
    src = "Restore from a backup\nRestores the cluster from a backup."
    assert (
        gen._format_description(src)
        == "Restore from a backup\n\nRestores the cluster from a backup."
    )


def test_format_description_flattens_body_newlines():
    src = "Title line\nbody one\nbody two"
    assert gen._format_description(src) == "Title line\n\nbody one body two"


def test_format_description_single_line_unchanged():
    assert gen._format_description("Just one line.") == "Just one line."


def test_format_description_empty_passthrough():
    assert gen._format_description("") == ""


# ---------------------------------------------------------------------------
# C#-3 — do not flatten structural blocks (over-correction regression)
#
# DocFX summaries carry HTML: fenced code (``<pre><code class="lang-…">``),
# inline code (``<code>``), lists (``<ul><li>``), cross-references
# (``<xref>``) and, on facade methods, ``:::admonition`::: markdown. The
# renderer must convert these to Markdown *without* collapsing them onto a
# single prose line. These guards are class-scoped: they assert the
# transform for *any* code block / list / admonition / xref, not one member.
# ---------------------------------------------------------------------------


def test_clean_summary_renders_code_block_as_fence():
    src = (
        '<example><pre><code class="lang-csharp">var x = 1;\n'
        "var y = 2;</code></pre></example>"
    )
    out = gen._clean_summary(src)
    assert "```csharp" in out
    # Newlines inside the block survive — the block is not flattened.
    assert "var x = 1;\nvar y = 2;" in out
    assert out.rstrip().endswith("```")


def test_clean_summary_unescapes_entities_in_code_block():
    src = '<pre><code class="lang-csharp">Get&lt;decimal&gt;(&quot;amount&quot;)</code></pre>'
    out = gen._clean_summary(src)
    assert 'Get<decimal>("amount")' in out


def test_clean_summary_wraps_inline_code_in_backticks():
    out = gen._clean_summary("Honouring <code>[JsonPropertyName]</code> on members.")
    assert "`[JsonPropertyName]`" in out
    assert "<code>" not in out


def test_clean_summary_renders_list_items():
    src = "<ul><li>Lenient tolerates absent.</li><li>Strict throws.</li></ul>"
    out = gen._clean_summary(src)
    assert "- Lenient tolerates absent." in out
    assert "- Strict throws." in out


def test_clean_summary_collapses_soft_wrapped_list_item():
    src = "<ul><li>Lenient tolerates\nabsent variables.</li></ul>"
    out = gen._clean_summary(src)
    assert "- Lenient tolerates absent variables." in out


def test_clean_summary_resolves_xref_to_backtick_type_name():
    src = (
        'parsed into a <xref href="Camunda.Orchestration.Sdk.VariableMap%601" '
        'data-throw-if-not-resolved="false"></xref>.'
    )
    out = gen._clean_summary(src)
    assert "`VariableMap`" in out
    assert "<xref" not in out
    # No empty gap left where the xref used to be.
    assert "parsed into a  ." not in out


def test_clean_summary_resolves_member_xref_to_type_and_member():
    src = (
        '<xref href="Camunda.Orchestration.Sdk.VariableMap%601.Validate" '
        'data-throw-if-not-resolved="false"></xref> throws.'
    )
    out = gen._clean_summary(src)
    assert "`VariableMap.Validate`" in out


def test_clean_summary_resolves_generic_method_xref():
    src = (
        '<xref href="Camunda.Orchestration.Sdk.VariableMap%601.Get%60%601(System.String)" '
        'data-throw-if-not-resolved="false"></xref>'
    )
    out = gen._clean_summary(src)
    assert "`VariableMap.Get`" in out
    assert "(" not in out and "%" not in out


def test_format_description_preserves_code_block():
    src = gen._clean_summary(
        "Fetch data.\n\n"
        '<example><pre><code class="lang-csharp">var a = 1;\n'
        "var b = 2;</code></pre></example>"
    )
    out = gen._format_description(src)
    # The fence and its internal newlines survive reflow (defect class C#-3).
    assert "```csharp\nvar a = 1;\nvar b = 2;\n```" in out


def test_format_description_keeps_admonition_as_independent_block():
    src = (
        "Get resource\n"
        "Returns a deployed resource.\n"
        ":::info\n"
        "This endpoint does not return X\n"
        "or Y.\n"
        ":::"
    )
    out = gen._format_description(src)
    # Title separated into its own paragraph.
    assert out.startswith("Get resource\n\n")
    # Admonition is a standalone block: blank line before the opener, markers
    # on their own lines, body reflowed inside the block.
    assert "\n\n:::info\nThis endpoint does not return X or Y.\n:::" in out


def test_format_description_reflows_prose_but_not_fence():
    src = (
        "This first sentence is deliberately long enough to exceed the title\n"
        "threshold used by the splitter.\n"
        "\n"
        "```csharp\n"
        "var a = 1;\n"
        "var b = 2;\n"
        "```"
    )
    out = gen._format_description(src)
    assert (
        "This first sentence is deliberately long enough to exceed the title "
        "threshold used by the splitter." in out
    )
    assert "```csharp\nvar a = 1;\nvar b = 2;\n```" in out


def test_load_all_types_preserves_code_block_in_summary():
    """End-to-end: a rich HTML summary is not flattened when parsed."""
    yml = (
        "### YamlMime:ManagedReference\n"
        "items:\n"
        "- uid: Test.Foo\n"
        "  name: Foo\n"
        "  fullName: Test.Foo\n"
        "  type: Class\n"
        "  summary: >-\n"
        "    Fetch typed variables.\n"
        "\n"
        "    <example><pre><code class=\"lang-csharp\">var a = 1;\n"
        "\n"
        "    var b = 2;</code></pre></example>\n"
        "  namespace: Test\n"
        "  syntax:\n"
        "    content: public class Foo\n"
    )
    with tempfile.TemporaryDirectory() as d:
        (Path(d) / "Test.Foo.yml").write_text(yml, encoding="utf-8")
        types = gen.load_all_types(Path(d), {})
    assert len(types) == 1
    assert "```csharp" in types[0].summary
    # Code fence is a standalone block, not merged into the prose sentence.
    assert "Fetch typed variables. ```csharp" not in types[0].summary


# ---------------------------------------------------------------------------
# Integration — the transforms are wired into the generator
# ---------------------------------------------------------------------------


def test_generate_camunda_client_escapes_method_headings():
    client = gen.TypeItem(name="CamundaClient", type_kind="Class")
    client.members.append(
        gen.MemberItem(
            name=(
                "SearchProcessDefinitionVariableNamesAsync("
                "ConsistencyOptions<Result>?, CancellationToken)"
            ),
            type_kind="Method",
            summary="Search names\nSearches the names.",
            syntax_content="public Task X();",
        )
    )
    md = gen.generate_camunda_client([client])
    assert (
        "#### SearchProcessDefinitionVariableNamesAsync("
        "ConsistencyOptions\\<Result\\>?, CancellationToken)"
    ) in md
    # The raw (unescaped) generic must not survive in a heading.
    assert (
        "#### SearchProcessDefinitionVariableNamesAsync(ConsistencyOptions<Result>"
        not in md
    )


def test_load_all_types_formats_summary():
    yml = (
        "### YamlMime:ManagedReference\n"
        "items:\n"
        "- uid: Test.Foo\n"
        "  name: Foo\n"
        "  fullName: Test.Foo\n"
        "  type: Class\n"
        "  summary: |\n"
        "    Restore from a backup\n"
        "    Restores the cluster from a backup.\n"
        "  namespace: Test\n"
        "  syntax:\n"
        "    content: public class Foo\n"
    )
    with tempfile.TemporaryDirectory() as d:
        (Path(d) / "Test.Foo.yml").write_text(yml, encoding="utf-8")
        types = gen.load_all_types(Path(d), {})
    assert len(types) == 1
    assert (
        types[0].summary
        == "Restore from a backup\n\nRestores the cluster from a backup."
    )


def _run() -> None:
    fns = [
        v
        for k, v in sorted(globals().items())
        if k.startswith("test_") and callable(v)
    ]
    failed = 0
    for fn in fns:
        try:
            fn()
            print(f"PASS {fn.__name__}")
        except Exception as exc:  # noqa: BLE001 - test runner surfaces all failures
            failed += 1
            print(f"FAIL {fn.__name__}: {type(exc).__name__}: {exc}")
    if failed:
        raise SystemExit(f"\n{failed} of {len(fns)} test(s) failed")
    print(f"\n{len(fns)} passed")


if __name__ == "__main__":
    _run()
