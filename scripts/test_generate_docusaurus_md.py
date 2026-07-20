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
