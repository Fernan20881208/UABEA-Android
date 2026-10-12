#!/usr/bin/env python3
"""Assert and tap actual Android UI nodes captured by uiautomator dump."""
import argparse
import re
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument("xml")
parser.add_argument("operation", choices=["assert", "point"])
parser.add_argument("text")
args = parser.parse_args()

root = ET.parse(args.xml).getroot()
nodes = [node for node in root.iter("node")
         if node.get("text", "") == args.text or node.get("content-desc", "") == args.text]
if not nodes:
    raise SystemExit(f"UI node missing: {args.text!r}")
if args.operation == "assert":
    print(f"UI OK: {args.text}")
else:
    bounds = nodes[0].get("bounds", "")
    match = re.fullmatch(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]", bounds)
    if not match:
        raise SystemExit(f"Invalid UI node bounds: {bounds}")
    x1, y1, x2, y2 = map(int, match.groups())
    if x1 >= x2 or y1 >= y2:
        raise SystemExit(f"UI node has no clickable area: {bounds}")
    print(f"{(x1 + x2) // 2} {(y1 + y2) // 2}")
