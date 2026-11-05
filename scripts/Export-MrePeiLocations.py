#!/usr/bin/env python3
"""Read PEI Level.hierarchy and emit validated location points as JSON to stdout."""
import hashlib
import json
import math
import re
import sys
from pathlib import Path

TOKEN = re.compile(r'\s*("(?:[^"\\]|\\.)*"|[{}\[\]])')
TYPE = "SDG.Unturned.LocationDevkitNode"


def parse(text):
    tokens = []
    offset = 0
    while offset < len(text):
        match = TOKEN.match(text, offset)
        if not match:
            if not text[offset:].strip():
                break
            raise ValueError(f"unexpected input at offset {offset}")
        tokens.append(match.group(1))
        offset = match.end()
    index = 0

    def value():
        nonlocal index
        if index >= len(tokens):
            raise ValueError("unexpected end")
        token = tokens[index]
        index += 1
        if token == "{":
            return mapping("}")
        if token == "[":
            items = []
            while index < len(tokens) and tokens[index] != "]":
                items.append(value())
            if index == len(tokens):
                raise ValueError("unclosed array")
            index += 1
            return items
        if not token.startswith('"'):
            raise ValueError("expected value")
        return json.loads(token)

    def mapping(end=None):
        nonlocal index
        result = {}
        while index < len(tokens) and tokens[index] != end:
            key = value()
            if not isinstance(key, str) or key in result:
                raise ValueError(f"invalid or duplicate key {key!r}")
            result[key] = value()
        if end:
            if index == len(tokens):
                raise ValueError("unclosed object")
            index += 1
        return result

    return mapping()


def extract(data):
    items = data.get("Items")
    if not isinstance(items, list):
        raise ValueError("missing Items array")
    output, ids, names, positions = [], set(), set(), set()
    for entry in items:
        if not isinstance(entry, dict) or not isinstance(entry.get("Type"), str):
            raise ValueError("invalid hierarchy item")
        if entry["Type"].split(",", 1)[0].strip() != TYPE:
            continue
        item = entry.get("Item")
        if not isinstance(item, dict):
            raise ValueError("location missing Item")
        instance = entry.get("Instance_ID")
        name, visible, position = item.get("LocationName"), item.get("IsVisibleOnMap"), item.get("Position")
        if not isinstance(instance, str) or not instance.isdecimal() or int(instance) <= 0 or int(instance) in ids:
            raise ValueError("invalid or duplicate location instance ID")
        if not isinstance(name, str) or not name.strip() or name.casefold() in names:
            raise ValueError("invalid or duplicate location name")
        if visible not in ("true", "false") or not isinstance(position, dict) or set(position) != {"X", "Y", "Z"}:
            raise ValueError("invalid visibility or position")
        coords = [float(position[axis]) for axis in ("X", "Y", "Z")]
        if not all(math.isfinite(x) for x in coords):
            raise ValueError("nonfinite coordinate")
        if tuple(coords) in positions:
            raise ValueError("duplicate location position")
        ids.add(int(instance))
        names.add(name.casefold())
        positions.add(tuple(coords))
        output.append({"instance_id": int(instance), "name": name, "x": coords[0], "y": coords[1], "z": coords[2], "visible_on_map": visible == "true"})
    if not output:
        raise ValueError("no location nodes found")
    return output


def cartography(data):
    matches = [e for e in data.get('Items', []) if isinstance(e, dict) and isinstance(e.get('Type'), str) and e['Type'].split(',', 1)[0].strip() == 'SDG.Unturned.CartographyVolume']
    if len(matches) != 1:
        raise ValueError('expected exactly one CartographyVolume')
    entry = matches[0]
    item = entry.get('Item')
    if not isinstance(item, dict) or item.get('Shape') != 'Box':
        raise ValueError('unsupported cartography shape')
    result = {'instance_id': entry.get('Instance_ID'), 'shape': 'Box'}
    if not isinstance(result['instance_id'], str) or not result['instance_id'].isdecimal() or int(result['instance_id']) <= 0:
        raise ValueError('invalid cartography instance ID')
    for key, axes in [('Position', 'XYZ'), ('Rotation', 'XYZW'), ('Scale', 'XYZ')]:
        value = item.get(key)
        if not isinstance(value, dict) or set(value) != set(axes) or not all(isinstance(value[a], str) for a in axes):
            raise ValueError('invalid cartography ' + key)
        if not all(math.isfinite(float(value[a])) for a in axes):
            raise ValueError('nonfinite cartography ' + key)
        result[key.lower()] = {a.lower(): value[a] for a in axes}
    if tuple(float(result['rotation'][a]) for a in 'xyzw') != (0, 0, 0, 1):
        raise ValueError('unsupported cartography rotation')
    if any(float(result['scale'][a]) <= 0 for a in 'xyz'):
        raise ValueError('invalid cartography scale')
    canonical = json.dumps(result, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode('utf-8')
    result['sha256'] = hashlib.sha256(canonical).hexdigest()
    return result


def main():
    path = Path(sys.argv[1]) if len(sys.argv) == 2 else Path(__file__).resolve().parent.parent / "runtime/server/Maps/PEI/Level.hierarchy"
    if len(sys.argv) > 2:
        raise ValueError("usage: Export-MrePeiLocations.py [Level.hierarchy]")
    raw = path.read_bytes()
    parsed = parse(raw.decode("utf-8-sig"))
    locations = extract(parsed)
    print(json.dumps({"source_sha256": hashlib.sha256(raw).hexdigest(), "cartography": cartography(parsed), "location_count": len(locations), "visible_count": sum(p["visible_on_map"] for p in locations), "locations": locations}, ensure_ascii=False, indent=2, allow_nan=False))


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, TypeError, KeyError) as exc:
        print(f"export failed: {exc}", file=sys.stderr)
        sys.exit(1)
