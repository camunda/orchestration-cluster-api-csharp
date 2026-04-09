#!/usr/bin/env python3
"""
Post-process the DocFX-generated toc.json to keep method sub-items
only for CamundaClient, removing member sub-items from all other types.

For CamundaClient, flatten the category groupings (Constructors, Fields,
Properties, Methods) so that methods appear directly under CamundaClient
in alphabetical order — matching the JS and Python SDK docs layout.
"""

import json
import sys

KEEP_MEMBERS_FOR = {"Camunda.Orchestration.Sdk.CamundaClient"}


def prune(items):
    if not items:
        return
    for item in items:
        uid = item.get("topicUid", "")
        children = item.get("items")
        if not children:
            continue
        # Namespace → recurse into child types
        if item.get("type") == "Namespace":
            prune(children)
        # Types we want expanded → flatten category groups into a single
        # alphabetical list of methods (remove Constructors/Fields/Properties/Methods nesting)
        elif uid in KEEP_MEMBERS_FOR:
            flat = []
            for category in children:
                for member in category.get("items", []):
                    flat.append(member)
            flat.sort(key=lambda m: m.get("name", ""))
            item["items"] = flat
        # Everything else → strip member sub-items
        else:
            del item["items"]
            item["leaf"] = True


def main():
    toc_path = sys.argv[1] if len(sys.argv) > 1 else "docs/_site/api/toc.json"

    with open(toc_path, "r") as f:
        data = json.load(f)

    root = data.get("items", data) if isinstance(data, dict) else data
    prune(root)

    with open(toc_path, "w") as f:
        json.dump(data, f, separators=(",", ":"))


if __name__ == "__main__":
    main()
