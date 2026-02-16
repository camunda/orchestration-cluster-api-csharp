#!/usr/bin/env python3
"""
Convert DocFX ManagedReference YAML metadata into Docusaurus-compatible
markdown pages for the C# SDK API reference.

Usage:
    python3 scripts/generate-docusaurus-md.py

Input:
    docs/api/*.yml          – DocFX metadata (produced by `docfx metadata`)
    docs/overwrite/*.md     – DocFX overwrite files (uid → example mapping)
    docs/examples/*.cs      – Compilable C# example files with region tags

Output:
    docs-md/api-reference/  – Markdown pages + sidebar.js
"""

from __future__ import annotations

import glob
import json
import os
import re
import sys
import textwrap
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import yaml

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

REPO_ROOT = Path(__file__).resolve().parent.parent
README_PATH = REPO_ROOT / "README.md"
API_DIR = REPO_ROOT / "docs" / "api"
OVERWRITE_DIR = REPO_ROOT / "docs" / "overwrite"
EXAMPLES_DIR = REPO_ROOT / "docs" / "examples"
OUTPUT_DIR = REPO_ROOT / "docs-md" / "api-reference"
LANDING_DIR = REPO_ROOT / "docs-md"

# ---------------------------------------------------------------------------
# Frontmatter helper
# ---------------------------------------------------------------------------

FRONTMATTER_TEMPLATE = textwrap.dedent("""\
    ---
    title: "{title}"
    sidebar_label: "{label}"
    mdx:
      format: md
    ---
""")


def frontmatter(title: str, label: str | None = None) -> str:
    return FRONTMATTER_TEMPLATE.format(
        title=_escape_yaml(title),
        label=_escape_yaml(label or title),
    )


def _escape_yaml(s: str) -> str:
    return s.replace('"', '\\"')


# ---------------------------------------------------------------------------
# Technical Preview banner (injected after first H1 on every page)
# ---------------------------------------------------------------------------

TECH_PREVIEW_BANNER = (
    "\n:::caution Technical Preview\n"
    "The C# SDK is a **technical preview** available from Camunda 8.9. "
    "It will become fully supported in Camunda 8.10. "
    "Its API surface may change in future releases without following semver.\n"
    ":::\n"
)


def inject_tech_preview_banner(content: str) -> str:
    """Insert the Technical Preview banner after the first H1 heading."""
    m = re.search(r"^#\s+.+$", content, re.MULTILINE)
    if m:
        pos = m.end()
        return content[:pos] + "\n" + TECH_PREVIEW_BANNER + content[pos:]
    return content


# ---------------------------------------------------------------------------
# Link rewriting: docs.camunda.io → relative
# ---------------------------------------------------------------------------

DEPLOYMENT_DEPTH = 3  # apis-tools/csharp-sdk/api-reference

_URL_PATH_OVERRIDES: dict[str, str] = {
    "camunda-api-rest": "orchestration-cluster-api-rest",
}

_DOCS_LINK_RE = re.compile(
    r"\[([^\]]*)\]\(https?://docs\.camunda\.io/docs/(?:next/)?(.*?)\)"
)


def _rewrite_match(m: re.Match) -> str:  # type: ignore[type-arg]
    text = m.group(1)
    url_path = m.group(2).rstrip("/")
    for old, new in _URL_PATH_OVERRIDES.items():
        url_path = url_path.replace(old, new)
    prefix = "../" * DEPLOYMENT_DEPTH
    return f"[{text}]({prefix}{url_path}.md)"


def rewrite_camunda_docs_links(content: str) -> str:
    return _DOCS_LINK_RE.sub(_rewrite_match, content)


# ---------------------------------------------------------------------------
# YAML loading (strip DocFX header)
# ---------------------------------------------------------------------------


def load_docfx_yaml(path: Path) -> dict:
    text = path.read_text(encoding="utf-8")
    # Strip the ### YamlMime:ManagedReference line
    if text.startswith("###"):
        text = text[text.index("\n") + 1 :]
    return yaml.safe_load(text) or {}


# ---------------------------------------------------------------------------
# Example extraction
# ---------------------------------------------------------------------------


def parse_region_tags(cs_path: Path) -> dict[str, str]:
    """Extract region-tagged code blocks from a .cs file.

    Region tags use the pattern ``// <RegionName>`` ... ``// </RegionName>``.
    Returns {RegionName: code_block}.
    """
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


def load_overwrite_examples(
    overwrite_dir: Path, examples_dir: Path
) -> dict[str, str]:
    """Parse DocFX overwrite .md files and resolve code references.

    Returns {uid: code_example_string}.
    """
    # Pre-load all region tags from all example .cs files
    all_regions: dict[str, str] = {}
    for cs_file in examples_dir.glob("*.cs"):
        all_regions.update(parse_region_tags(cs_file))

    # Parse overwrite files: each section starts with --- uid: ...\nexample:\n- *content\n---
    uid_to_example: dict[str, str] = {}
    for md_file in overwrite_dir.glob("*.md"):
        text = md_file.read_text(encoding="utf-8")
        # Split on --- blocks
        sections = re.split(r"^---\s*$", text, flags=re.MULTILINE)
        current_uid = None
        for section in sections:
            section = section.strip()
            if not section:
                continue
            # Check for uid: line
            uid_match = re.search(r"^uid:\s*(.+)$", section, re.MULTILINE)
            if uid_match:
                current_uid = uid_match.group(1).strip()
                continue
            # Check for code reference
            code_ref = re.search(
                r"\[!code-csharp\[\]\(\.\.\/examples\/(\S+)#(\w+)\)\]", section
            )
            if code_ref and current_uid:
                region = code_ref.group(2)
                if region in all_regions:
                    uid_to_example[current_uid] = all_regions[region]
                current_uid = None
    return uid_to_example


# ---------------------------------------------------------------------------
# Data model
# ---------------------------------------------------------------------------


@dataclass
class TypeItem:
    uid: str = ""
    name: str = ""
    full_name: str = ""
    type_kind: str = ""  # Class, Struct, Enum, Interface
    summary: str = ""
    remarks: str = ""
    syntax_content: str = ""
    namespace: str = ""
    parent: str = ""
    inheritance: list[str] = field(default_factory=list)
    implements: list[str] = field(default_factory=list)
    children_uids: list[str] = field(default_factory=list)
    members: list[MemberItem] = field(default_factory=list)
    example: str = ""


@dataclass
class MemberItem:
    uid: str = ""
    name: str = ""
    type_kind: str = ""  # Method, Property, Constructor, Field
    summary: str = ""
    syntax_content: str = ""
    parameters: list[dict[str, str]] = field(default_factory=list)
    return_type: str = ""
    return_description: str = ""
    example: str = ""


def _short_type(full: str) -> str:
    """Shorten a fully-qualified type name: keep only the last segment."""
    if not full:
        return full
    # Handle generic syntax {T} → <T>
    full = re.sub(r"\{([^}]+)\}", lambda m: f"<{_short_type(m.group(1))}>", full)
    parts = full.rsplit(".", 1)
    return parts[-1] if len(parts) > 1 else full


def _clean_summary(s: str | None) -> str:
    if not s:
        return ""
    # Remove XML doc artifacts like <xref uid="..."/>
    s = re.sub(r"<xref\s+href=\"[^\"]*\"\s*data-throw-if-not-resolved=\"false\"\s*></xref>", "", s)
    s = re.sub(r"</?[a-z][^>]*>", "", s)
    return s.strip()


# ---------------------------------------------------------------------------
# Load all types
# ---------------------------------------------------------------------------


def load_all_types(
    api_dir: Path, examples: dict[str, str]
) -> list[TypeItem]:
    types: list[TypeItem] = []
    yml_files = sorted(api_dir.glob("*.yml"))

    for yml_path in yml_files:
        if yml_path.name == "toc.yml":
            continue
        # Namespace-only files (e.g., Camunda.Orchestration.Sdk.yml)
        data = load_docfx_yaml(yml_path)
        items = data.get("items", [])
        if not items:
            continue
        first = items[0]
        kind = first.get("type", "")
        if kind == "Namespace":
            continue

        ti = TypeItem(
            uid=first.get("uid", ""),
            name=first.get("name", ""),
            full_name=first.get("fullName", ""),
            type_kind=kind,
            summary=_clean_summary(first.get("summary")),
            remarks=_clean_summary(first.get("remarks")),
            syntax_content=first.get("syntax", {}).get("content", ""),
            namespace=first.get("namespace", ""),
            parent=first.get("parent", ""),
            inheritance=[
                _short_type(i)
                for i in first.get("inheritance", [])
                if not i.startswith("System.")
            ],
            implements=[
                _short_type(i)
                for i in first.get("implements", [])
                if not i.startswith("System.")
            ],
            children_uids=first.get("children", []),
        )

        # Parse members (items after the first)
        for item in items[1:]:
            mi = MemberItem(
                uid=item.get("uid", ""),
                name=item.get("name", ""),
                type_kind=item.get("type", ""),
                summary=_clean_summary(item.get("summary")),
                syntax_content=item.get("syntax", {}).get("content", ""),
                parameters=[
                    {"name": p.get("id", ""), "type": _short_type(p.get("type", "")), "description": _clean_summary(p.get("description"))}
                    for p in item.get("syntax", {}).get("parameters", [])
                ],
                return_type=_short_type(
                    item.get("syntax", {}).get("return", {}).get("type", "")
                    if isinstance(item.get("syntax", {}).get("return"), dict)
                    else ""
                ),
                return_description=_clean_summary(
                    item.get("syntax", {}).get("return", {}).get("description", "")
                    if isinstance(item.get("syntax", {}).get("return"), dict)
                    else ""
                ),
                example=examples.get(item.get("uid", ""), ""),
            )
            ti.members.append(mi)

        types.append(ti)
    return types


# ---------------------------------------------------------------------------
# Classify types into pages
# ---------------------------------------------------------------------------


def classify_types(
    types: list[TypeItem],
) -> dict[str, list[TypeItem]]:
    """Bucket types into page categories."""
    pages: dict[str, list[TypeItem]] = {
        "camunda-client": [],
        "configuration": [],
        "runtime": [],
        "models": [],
        "enums": [],
        "keys": [],
    }

    CONFIGURATION_TYPES = {
        "CamundaOptions",
        "CamundaConfig",
        "ConfigurationHydrator",
        "AuthConfig",
        "BasicAuthConfig",
        "OAuthConfig",
        "OAuthRetryConfig",
        "HttpRetryConfig",
        "BackpressureConfig",
        "EventualConfig",
        "ValidationConfig",
        "ValidationMode",
        "AuthStrategy",
        "JobWorkerConfig",
        "ConfigErrorCode",
        "ConfigErrorDetail",
    }

    for t in types:
        ns = t.namespace or t.parent or ""
        name = t.name

        if name in ("CamundaClient", "Camunda", "CamundaServiceCollectionExtensions"):
            pages["camunda-client"].append(t)
        elif name in CONFIGURATION_TYPES:
            pages["configuration"].append(t)
        elif t.type_kind == "Enum" and name not in CONFIGURATION_TYPES:
            pages["enums"].append(t)
        elif t.type_kind == "Struct" and name.endswith("Key"):
            pages["keys"].append(t)
        elif "Runtime" in ns:
            pages["runtime"].append(t)
        else:
            pages["models"].append(t)

    return pages


# ---------------------------------------------------------------------------
# Markdown generation helpers
# ---------------------------------------------------------------------------


def _md_signature(syntax: str) -> str:
    """Render a C# signature as a fenced code block."""
    if not syntax:
        return ""
    # Remove attributes like [JsonPropertyName("...")]
    clean = re.sub(r"^\[.*?\]\n?", "", syntax, flags=re.MULTILINE).strip()
    return f"```csharp\n{clean}\n```\n"


def _md_params_table(params: list[dict[str, str]]) -> str:
    if not params:
        return ""
    lines = ["| Parameter | Type | Description |", "| --- | --- | --- |"]
    for p in params:
        desc = p.get("description", "").replace("\n", " ")
        lines.append(f"| `{p['name']}` | `{p['type']}` | {desc} |")
    return "\n".join(lines) + "\n"


def _md_properties_table(members: list[MemberItem]) -> str:
    props = [m for m in members if m.type_kind == "Property"]
    if not props:
        return ""
    lines = [
        "| Property | Type | Description |",
        "| --- | --- | --- |",
    ]
    for p in props:
        summary = p.summary.replace("\n", " ")
        lines.append(f"| `{p.name}` | `{p.return_type}` | {summary} |")
    return "\n".join(lines) + "\n"


def _md_example(code: str) -> str:
    if not code:
        return ""
    code = textwrap.dedent(code).strip()
    return f"\n**Example**\n\n```csharp\n{code}\n```\n"


# ---------------------------------------------------------------------------
# Page generators
# ---------------------------------------------------------------------------


def generate_index(pages: dict[str, list[TypeItem]]) -> str:
    """Generate the API Reference landing page."""
    out = frontmatter("C# SDK API Reference", "Overview")
    out += "\n# C# SDK API Reference\n\n"
    out += "Auto-generated from the Camunda C# SDK source code.\n\n"
    out += "## Sections\n\n"
    sections = [
        ("CamundaClient", "camunda-client.md", "Main client class with all API methods"),
        ("Configuration", "configuration.md", "SDK configuration, authentication, and options"),
        ("Runtime", "runtime.md", "Runtime infrastructure: job workers, backpressure, polling, errors"),
        ("Models", "models.md", "Request and response model classes"),
        ("Enums", "enums.md", "Enumeration types"),
        ("Keys", "keys.md", "Strongly-typed domain key types"),
    ]
    for title, link, desc in sections:
        count = len(pages.get(link.replace(".md", ""), []))
        out += f"- [{title}]({link}) — {desc} ({count} types)\n"
    return out


def generate_camunda_client(types: list[TypeItem]) -> str:
    """Generate the CamundaClient page."""
    out = frontmatter("CamundaClient", "CamundaClient")
    out += "\n# CamundaClient\n\n"

    # Find the CamundaClient type
    client_type = next((t for t in types if t.name == "CamundaClient"), None)
    camunda_class = next((t for t in types if t.name == "Camunda"), None)
    extensions = next(
        (t for t in types if t.name == "CamundaServiceCollectionExtensions"), None
    )

    # Factory method
    if camunda_class:
        out += "## Creating a Client\n\n"
        out += (camunda_class.summary or "Static factory for creating CamundaClient instances.") + "\n\n"
        for m in camunda_class.members:
            if m.type_kind == "Method":
                out += _md_signature(m.syntax_content)
                if m.summary:
                    out += m.summary + "\n\n"
                if m.parameters:
                    out += _md_params_table(m.parameters) + "\n"
                if m.example:
                    out += _md_example(m.example)

    # DI extensions
    if extensions:
        out += "## Dependency Injection\n\n"
        out += (extensions.summary or "Extension methods for Microsoft.Extensions.DependencyInjection.") + "\n\n"
        for m in extensions.members:
            if m.type_kind == "Method":
                out += f"### {m.name}\n\n"
                out += _md_signature(m.syntax_content)
                if m.summary:
                    out += m.summary + "\n\n"
                if m.parameters:
                    out += _md_params_table(m.parameters) + "\n"

    if not client_type:
        return out

    out += f"\n## Overview\n\n{client_type.summary}\n\n"
    out += _md_signature(client_type.syntax_content)

    # Constructor
    ctors = [m for m in client_type.members if m.type_kind == "Constructor"]
    for ctor in ctors:
        out += "\n## Constructor\n\n"
        out += _md_signature(ctor.syntax_content)
        if ctor.summary:
            out += ctor.summary + "\n\n"
        if ctor.parameters:
            out += _md_params_table(ctor.parameters) + "\n"

    # Properties
    props = [m for m in client_type.members if m.type_kind == "Property"]
    if props:
        out += "\n## Properties\n\n"
        out += _md_properties_table(client_type.members) + "\n"

    # Methods — group by domain
    methods = [m for m in client_type.members if m.type_kind == "Method"]
    if methods:
        out += "\n## Methods\n\n"

        # Group methods by prefix
        groups: dict[str, list[MemberItem]] = {}
        for m in methods:
            # Derive group from method name
            group = _method_group(m.name)
            groups.setdefault(group, []).append(m)

        for group_name, group_methods in groups.items():
            out += f"\n### {group_name}\n\n"
            for m in group_methods:
                out += f"#### {m.name}\n\n"
                out += _md_signature(m.syntax_content)
                if m.summary:
                    out += m.summary + "\n\n"
                if m.parameters:
                    out += _md_params_table(m.parameters) + "\n"
                if m.return_type:
                    ret_desc = f" — {m.return_description}" if m.return_description else ""
                    out += f"**Returns:** `{m.return_type}`{ret_desc}\n\n"
                if m.example:
                    out += _md_example(m.example)

    return out


def _method_group(name: str) -> str:
    """Derive a logical group name from a method name."""
    GROUP_PREFIXES = [
        ("ProcessInstance", "Process Instances"),
        ("ProcessDefinition", "Process Definitions"),
        ("Job", "Jobs"),
        ("UserTask", "User Tasks"),
        ("DecisionDefinition", "Decision Definitions"),
        ("DecisionRequirements", "Decision Requirements"),
        ("DecisionInstance", "Decision Instances"),
        ("Decision", "Decisions"),
        ("Incident", "Incidents"),
        ("Variable", "Variables"),
        ("FlowNode", "Flow Nodes"),
        ("Element", "Elements"),
        ("Message", "Messages"),
        ("Signal", "Signals"),
        ("Resource", "Resources"),
        ("Deploy", "Deployments"),
        ("Topology", "Cluster"),
        ("Authentication", "Cluster"),
        ("Clock", "Cluster"),
        ("Backpressure", "Cluster"),
        ("License", "Cluster"),
        ("Worker", "Job Workers"),
        ("RunWorkers", "Job Workers"),
        ("Tenant", "Tenants"),
        ("Role", "Roles"),
        ("Group", "Groups"),
        ("Mapping", "Mappings"),
        ("Authorization", "Authorizations"),
        ("AuditLog", "Audit Logs"),
        ("BatchOperation", "Batch Operations"),
        ("DocumentLink", "Documents"),
        ("Document", "Documents"),
        ("AdHocSubProcess", "Ad Hoc Sub-Processes"),
    ]
    # Strip Async suffix for matching
    base = re.sub(r"Async$", "", name)
    for prefix, group in GROUP_PREFIXES:
        if prefix in base:
            return group
    return "Other"


def generate_configuration(types: list[TypeItem]) -> str:
    """Generate configuration page."""
    out = frontmatter("Configuration", "Configuration")
    out += "\n# Configuration\n\n"
    out += "Configuration and authentication types for the Camunda C# SDK.\n\n"

    # Order: CamundaOptions, CamundaConfig, ConfigurationHydrator, Auth*, then rest
    priority = [
        "CamundaOptions", "CamundaConfig", "ConfigurationHydrator",
        "AuthConfig", "AuthStrategy", "BasicAuthConfig", "OAuthConfig",
        "OAuthRetryConfig", "HttpRetryConfig", "BackpressureConfig",
        "EventualConfig", "ValidationConfig", "ValidationMode",
        "JobWorkerConfig",
    ]
    ordered = sorted(types, key=lambda t: (
        priority.index(t.name) if t.name in priority else 999,
        t.name,
    ))

    for t in ordered:
        out += f"\n## {t.name}\n\n"
        if t.summary:
            out += t.summary + "\n\n"
        out += _md_signature(t.syntax_content)

        if t.type_kind == "Enum":
            out += _md_enum_values(t) + "\n"
        else:
            props = _md_properties_table(t.members)
            if props:
                out += "\n### Properties\n\n" + props + "\n"

    return out


def generate_runtime(types: list[TypeItem]) -> str:
    """Generate runtime page."""
    out = frontmatter("Runtime", "Runtime")
    out += "\n# Runtime\n\n"
    out += "Runtime infrastructure types: job workers, backpressure management, "
    out += "eventual consistency polling, error handling, and key serialization.\n\n"

    # Group: interfaces first, then classes, then exceptions, then structs, enums
    def sort_key(t: TypeItem) -> tuple:
        kind_order = {"Interface": 0, "Class": 1, "Struct": 2, "Enum": 3}
        is_exception = 1 if "Exception" in t.name else 0
        return (kind_order.get(t.type_kind, 9), is_exception, t.name)

    for t in sorted(types, key=sort_key):
        out += f"\n## {t.name}\n\n"
        kind_tag = t.type_kind.lower()
        if "Exception" in t.name:
            kind_tag = "exception"
        out += f"*{kind_tag}*\n\n"
        if t.summary:
            out += t.summary + "\n\n"
        out += _md_signature(t.syntax_content)

        if t.type_kind == "Enum":
            out += _md_enum_values(t) + "\n"
        else:
            props = _md_properties_table(t.members)
            if props:
                out += "\n### Properties\n\n" + props + "\n"

            methods = [m for m in t.members if m.type_kind == "Method"]
            if methods:
                out += "\n### Methods\n\n"
                for m in methods:
                    out += f"#### {m.name}\n\n"
                    out += _md_signature(m.syntax_content)
                    if m.summary:
                        out += m.summary + "\n\n"
                    if m.parameters:
                        out += _md_params_table(m.parameters) + "\n"
                    if m.return_type:
                        ret_desc = f" — {m.return_description}" if m.return_description else ""
                        out += f"**Returns:** `{m.return_type}`{ret_desc}\n\n"

    return out


def generate_models(types: list[TypeItem]) -> str:
    """Generate models page."""
    out = frontmatter("Models", "Models")
    out += "\n# Models\n\n"
    out += f"Request and response model classes ({len(types)} types).\n\n"

    # TOC at top
    out += "## Quick Reference\n\n"
    for t in sorted(types, key=lambda t: t.name):
        anchor = t.name.lower()
        summary = t.summary.split(".")[0] if t.summary else ""
        out += f"- [{t.name}](#{anchor})"
        if summary:
            out += f" — {summary}"
        out += "\n"
    out += "\n---\n\n"

    for t in sorted(types, key=lambda t: t.name):
        out += f"\n## {t.name}\n\n"
        if t.summary:
            out += t.summary + "\n\n"
        out += _md_signature(t.syntax_content)
        props = _md_properties_table(t.members)
        if props:
            out += "\n" + props + "\n"

    return out


def _md_enum_values(t: TypeItem) -> str:
    """Render enum fields as a table."""
    fields = [m for m in t.members if m.type_kind == "Field"]
    if not fields:
        return ""
    lines = ["| Value | Description |", "| --- | --- |"]
    for f in fields:
        summary = f.summary.replace("\n", " ") if f.summary else ""
        lines.append(f"| `{f.name}` | {summary} |")
    return "\n".join(lines) + "\n"


def generate_enums(types: list[TypeItem]) -> str:
    """Generate enums page."""
    out = frontmatter("Enums", "Enums")
    out += "\n# Enums\n\n"
    out += f"Enumeration types ({len(types)} enums).\n\n"

    for t in sorted(types, key=lambda t: t.name):
        out += f"\n## {t.name}\n\n"
        if t.summary:
            out += t.summary + "\n\n"
        out += _md_enum_values(t) + "\n"

    return out


def generate_keys(types: list[TypeItem]) -> str:
    """Generate keys page."""
    out = frontmatter("Keys", "Keys")
    out += "\n# Key Types\n\n"
    out += "Strongly-typed domain key types provide compile-time safety for entity identifiers. "
    out += "Each key wraps a string value and ensures type-safe API calls.\n\n"

    out += "## Overview\n\n"
    out += "| Key Type | Description |\n"
    out += "| --- | --- |\n"
    for t in sorted(types, key=lambda t: t.name):
        summary = t.summary or ""
        out += f"| `{t.name}` | {summary} |\n"

    out += "\n## Common Methods\n\n"
    out += "All key types share these methods:\n\n"
    out += "| Method | Description |\n"
    out += "| --- | --- |\n"
    out += "| `AssumeExists(string)` | Creates a key from a known-valid string value. |\n"
    out += "| `IsValid(string)` | Validates whether a string is a valid key value. |\n"
    out += "| `Value` | Gets the underlying string value. |\n"
    out += "| `ToString()` | Returns the string representation. |\n"

    out += "\n## Details\n\n"
    for t in sorted(types, key=lambda t: t.name):
        out += f"\n### {t.name}\n\n"
        if t.summary:
            out += t.summary + "\n\n"
        out += _md_signature(t.syntax_content)

    return out


# ---------------------------------------------------------------------------
# Landing page generator (from README.md)
# ---------------------------------------------------------------------------

LANDING_FRONTMATTER = textwrap.dedent("""\
    ---
    id: csharp-sdk
    title: "C# SDK (Technical Preview)"
    sidebar_label: "C# SDK (Technical Preview)"
    mdx:
      format: md
    ---

""")

# Depth for landing page: apis-tools/csharp-sdk/ = 2
_LANDING_PAGE_DEPTH = 2


def _strip_cut_sections(content: str) -> str:
    return re.sub(
        r"<!-- docs:cut:start -->.*?<!-- docs:cut:end -->\n?",
        "",
        content,
        flags=re.DOTALL,
    )


def _strip_badges(content: str) -> str:
    """Remove markdown badge/shield image lines."""
    return re.sub(r"^\[!\[.*?\]\(.*?\)\]\(.*?\)\s*$", "", content, flags=re.MULTILINE)


def _strip_contributing(content: str) -> str:
    """Remove Contributing and License sections (end of README)."""
    return re.sub(r"\n## Contributing\b.*", "", content, flags=re.DOTALL)


def _strip_external_doc_link(content: str) -> str:
    """Remove the 'Full API Documentation available here' line."""
    return re.sub(r"^Full API Documentation available \[.*?\]\(.*?\)\.?\s*$", "", content, flags=re.MULTILINE)


def _clean_empty_lines(content: str) -> str:
    return re.sub(r"\n{4,}", "\n\n\n", content)


def _rewrite_landing_links(content: str) -> str:
    """Rewrite docs.camunda.io links at landing page depth."""
    base_url_re = re.compile(
        r"\[([^\]]*)\]\(https?://docs\.camunda\.io/docs/(?:next/)?(.*?)\)"
    )
    prefix = "../" * _LANDING_PAGE_DEPTH

    def _replace(m: re.Match) -> str:
        text = m.group(1)
        url_path = m.group(2).rstrip("/")
        for old, new in _URL_PATH_OVERRIDES.items():
            url_path = url_path.replace(old, new)
        return f"[{text}]({prefix}{url_path}.md)"

    return base_url_re.sub(_replace, content)


def generate_landing_page(readme_path: Path) -> str:
    content = readme_path.read_text(encoding="utf-8")
    content = _strip_badges(content)
    content = _strip_cut_sections(content)
    content = _strip_external_doc_link(content)
    content = _strip_contributing(content)
    content = _clean_empty_lines(content)

    # Replace the H1 title with a shorter one
    content = re.sub(
        r"^#\s+.*$",
        "# C# SDK (Technical Preview)",
        content,
        count=1,
        flags=re.MULTILINE,
    )

    content = _rewrite_landing_links(content)
    content = inject_tech_preview_banner(content)
    content = content.strip() + "\n"

    # Append API Reference link
    content += (
        "\n## API Reference\n\n"
        "See the [API Reference](api-reference/index.md) for full class "
        "and method documentation.\n"
    )

    return LANDING_FRONTMATTER + content


# ---------------------------------------------------------------------------
# Sidebar generator
# ---------------------------------------------------------------------------


def generate_sidebar() -> str:
    return textwrap.dedent("""\
        // Auto-generated sidebar for C# SDK API Reference
        module.exports = [
          {
            type: "doc",
            id: "apis-tools/csharp-sdk/api-reference/index",
            label: "Overview",
          },
          {
            type: "doc",
            id: "apis-tools/csharp-sdk/api-reference/camunda-client",
            label: "CamundaClient",
          },
          {
            type: "doc",
            id: "apis-tools/csharp-sdk/api-reference/configuration",
            label: "Configuration",
          },
          {
            type: "doc",
            id: "apis-tools/csharp-sdk/api-reference/runtime",
            label: "Runtime",
          },
          {
            type: "doc",
            id: "apis-tools/csharp-sdk/api-reference/models",
            label: "Models",
          },
          {
            type: "doc",
            id: "apis-tools/csharp-sdk/api-reference/enums",
            label: "Enums",
          },
          {
            type: "doc",
            id: "apis-tools/csharp-sdk/api-reference/keys",
            label: "Keys",
          },
        ];
    """)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------


def main() -> None:
    if not API_DIR.exists():
        print(f"ERROR: {API_DIR} not found. Run `docfx metadata` first.", file=sys.stderr)
        sys.exit(1)

    print(f"Loading DocFX YAML from {API_DIR} ...")
    examples = load_overwrite_examples(OVERWRITE_DIR, EXAMPLES_DIR)
    print(f"  Loaded {len(examples)} code examples")
    types = load_all_types(API_DIR, examples)
    print(f"  Loaded {len(types)} types")

    pages = classify_types(types)
    for page, page_types in pages.items():
        print(f"  {page}: {len(page_types)} types")

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    # Generate pages
    generators = {
        "index.md": lambda: generate_index(pages),
        "camunda-client.md": lambda: generate_camunda_client(pages["camunda-client"]),
        "configuration.md": lambda: generate_configuration(pages["configuration"]),
        "runtime.md": lambda: generate_runtime(pages["runtime"]),
        "models.md": lambda: generate_models(pages["models"]),
        "enums.md": lambda: generate_enums(pages["enums"]),
        "keys.md": lambda: generate_keys(pages["keys"]),
    }

    for filename, gen_fn in generators.items():
        content = gen_fn()
        content = rewrite_camunda_docs_links(content)
        content = inject_tech_preview_banner(content)
        out_path = OUTPUT_DIR / filename
        out_path.write_text(content, encoding="utf-8")
        print(f"  Wrote {out_path} ({len(content)} bytes)")

    # Generate sidebar
    sidebar_path = OUTPUT_DIR / "sidebar.js"
    sidebar_path.write_text(generate_sidebar(), encoding="utf-8")
    print(f"  Wrote {sidebar_path}")

    # Generate landing page from README
    if README_PATH.exists():
        LANDING_DIR.mkdir(parents=True, exist_ok=True)
        landing = generate_landing_page(README_PATH)
        landing_path = LANDING_DIR / "csharp-sdk.md"
        landing_path.write_text(landing, encoding="utf-8")
        print(f"  Wrote {landing_path} ({len(landing)} bytes)")
    else:
        print(f"  WARNING: {README_PATH} not found, skipping landing page")

    print("\nDone!")


if __name__ == "__main__":
    main()
