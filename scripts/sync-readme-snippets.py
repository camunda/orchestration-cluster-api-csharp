#!/usr/bin/env python3
"""
Synchronize code snippets in README.md from compilable example files.

Replaces code blocks between ``<!-- snippet:RegionName -->`` markers in
README.md with the corresponding region-tagged code from docs/examples/*.cs.

Usage:
    python3 scripts/sync-readme-snippets.py          # update README.md in-place
    python3 scripts/sync-readme-snippets.py --check   # CI mode: exit 1 if out of sync

Region tags in .cs files use ``// <RegionName>`` ... ``// </RegionName>``.
Markers in README.md use ``<!-- snippet:RegionName -->`` before a fenced
code block, which ends at the next ````` `` ``` ``````.  The script replaces
everything between the marker and the closing fence (inclusive of both
fences) with freshly extracted content.

Composite regions: ``<!-- snippet:A+B+C -->`` concatenates multiple regions
separated by blank lines.
"""

from __future__ import annotations

import re
import sys
import textwrap
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parent.parent
README_PATH = REPO_ROOT / "README.md"
EXAMPLES_DIR = REPO_ROOT / "docs" / "examples"


# ---------------------------------------------------------------------------
# Region extraction (same logic as generate-docusaurus-md.py)
# ---------------------------------------------------------------------------

def parse_region_tags(cs_path: Path) -> dict[str, str]:
    """Extract region-tagged code blocks from a .cs file."""
    text = cs_path.read_text(encoding="utf-8")
    regions: dict[str, str] = {}
    current_tag: str | None = None
    lines: list[str] = []
    for line in text.splitlines():
        stripped = line.strip()
        m_open = re.match(r"^//\s*<(\w+)>\s*$", stripped)
        m_close = re.match(r"^//\s*</(\w+)>\s*$", stripped)
        if m_open:
            current_tag = m_open.group(1)
            lines = []
        elif m_close and current_tag == m_close.group(1):
            regions[current_tag] = textwrap.dedent("\n".join(lines))
            current_tag = None
            lines = []
        elif current_tag is not None:
            lines.append(line)
    return regions


def load_all_regions() -> dict[str, str]:
    """Load regions from all .cs files under docs/examples/."""
    all_regions: dict[str, str] = {}
    for cs_file in sorted(EXAMPLES_DIR.glob("*.cs")):
        all_regions.update(parse_region_tags(cs_file))
    return all_regions


# ---------------------------------------------------------------------------
# README rewriting
# ---------------------------------------------------------------------------

# Matches: <!-- snippet:RegionName --> or <!-- snippet:Region1+Region2 -->
SNIPPET_MARKER = re.compile(r"^<!--\s*snippet:([\w+]+)\s*-->$")


def resolve_region(name: str, regions: dict[str, str]) -> str | None:
    """Resolve a region name, supporting ``A+B`` composite syntax."""
    if "+" not in name:
        return regions.get(name)
    parts = name.split("+")
    resolved = [regions.get(p) for p in parts]
    if any(r is None for r in resolved):
        return None
    return "\n\n".join(r for r in resolved if r)


def sync_readme(regions: dict[str, str], *, check: bool = False) -> bool:
    """Replace snippet-marked code blocks in README.md.

    Returns True if the file was (or would be) changed.
    """
    readme_text = README_PATH.read_text(encoding="utf-8")
    lines = readme_text.splitlines(keepends=True)

    out: list[str] = []
    i = 0
    changed = False
    missing: list[str] = []

    while i < len(lines):
        line = lines[i].rstrip("\n")
        m = SNIPPET_MARKER.match(line.strip())

        if not m:
            out.append(lines[i])
            i += 1
            continue

        region_name = m.group(1)
        content = resolve_region(region_name, regions)

        if content is None:
            missing.append(region_name)
            out.append(lines[i])
            i += 1
            continue

        # Keep the marker line
        out.append(lines[i])
        i += 1

        # Skip whitespace between marker and opening fence
        while i < len(lines) and lines[i].strip() == "":
            out.append(lines[i])
            i += 1

        # Expect opening fence
        if i >= len(lines) or not lines[i].strip().startswith("```"):
            print(f"WARNING: snippet:{region_name} — expected ``` after marker, skipping")
            continue

        fence_lang = lines[i].strip()  # e.g. ```csharp

        # Find closing fence
        close_idx = i + 1
        while close_idx < len(lines) and lines[close_idx].strip() != "```":
            close_idx += 1

        if close_idx >= len(lines):
            print(f"WARNING: snippet:{region_name} — no closing ``` found, skipping")
            out.append(lines[i])
            i += 1
            continue

        # Build replacement block
        new_block = fence_lang + "\n" + content + "\n```\n"
        old_block = "".join(lines[i : close_idx + 1])

        if old_block != new_block:
            changed = True

        out.append(new_block)
        i = close_idx + 1

    if missing:
        print(f"ERROR: missing regions: {', '.join(missing)}", file=sys.stderr)
        return True  # treat as failure

    new_text = "".join(out)

    if check:
        if changed:
            print("README.md is out of sync with example snippets. Run:")
            print("  python3 scripts/sync-readme-snippets.py")
        return changed

    if changed:
        README_PATH.write_text(new_text, encoding="utf-8", newline="")
        print(f"README.md updated ({sum(1 for l in out if SNIPPET_MARKER.match(l.strip()))} snippets synced)")
    else:
        print("README.md is already up to date")

    return changed


def main() -> None:
    check = "--check" in sys.argv
    regions = load_all_regions()
    print(f"Loaded {len(regions)} regions from docs/examples/*.cs")

    changed = sync_readme(regions, check=check)

    if check and changed:
        sys.exit(1)


if __name__ == "__main__":
    main()
