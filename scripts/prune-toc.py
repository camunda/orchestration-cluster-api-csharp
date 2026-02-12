#!/usr/bin/env python3
"""
Post-process the DocFX-generated toc.json to keep method sub-items
only for CamundaClient, removing member sub-items from all other types.

This allows CamundaClient methods to appear in the left-hand sidebar TOC
while keeping model classes, config classes, etc. flat (no property clutter).
"""

import json
import sys

KEEP_MEMBERS_FOR = {"Camunda.Client.CamundaClient"}


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
        # Types we want expanded → keep their members
        elif uid in KEEP_MEMBERS_FOR:
            pass
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
