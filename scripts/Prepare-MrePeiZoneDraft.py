#!/usr/bin/env python3
"""Вывести черновик вручную набросанных зон PEI. Не применять к серверу."""
import hashlib
import importlib.util
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MAP = ROOT / "runtime/server/Maps/PEI"
EXPECTED_HIERARCHY = "46bc3e3a869346e009b7f6d0fd3ca10a769ab4b2777922e405a442ed8b4054bd"
EXPECTED_CHART = "757c5a947a3e36a0f17b10a0f1c18013d3e096a7f3dd9675e99b36a0af1623d5"

# Примерные пиксельные границы по виду карты; не игровая или пользовательская приёмка.
# Координаты намеренно не являются игровым конфигом и требуют визуальной проверки.
PIXELS = {
    "Summerside Military Base": [(430, 155), (585, 155), (585, 380), (430, 380)],
    "Belfast Airport": [(1560, 160), (1825, 160), (1825, 480), (1560, 480)],
    "Stratford": [(785, 230), (1015, 230), (1015, 480), (785, 480)],
    "Alberton": [(320, 705), (565, 705), (565, 930), (320, 930)],
    "Wiltshire Farm": [(435, 1415), (575, 1415), (575, 1570), (435, 1570)],
    "Fernwood Farm": [(660, 1235), (790, 1235), (790, 1400), (660, 1400)],
    "Wellington Farm": [(1185, 1260), (1320, 1260), (1320, 1430), (1185, 1430)],
    "Charlottetown": [(890, 1300), (1085, 1300), (1085, 1560), (890, 1560)],
    "Montague": [(1150, 960), (1390, 960), (1390, 1140), (1150, 1140)],
    "Kensington Campground": [(1460, 1325), (1740, 1325), (1740, 1580), (1460, 1580)],
    "Confederation Bridge": [(0, 510), (260, 510), (260, 620), (0, 620)],
    "O'Leary Prison": [(615, 815), (810, 815), (810, 1085), (615, 1085)],
    "Souris Campground": [(1410, 715), (1550, 715), (1550, 915), (1410, 915)],
    "Cape Rock": [(1010, 35), (1300, 35), (1300, 220), (1010, 220)],
    "Holman Isle": [(85, 1300), (380, 1300), (380, 1890), (85, 1890)],
    "Oulton's Isle": [(1050, 1670), (1270, 1670), (1270, 1890), (1050, 1890)],
    "Courtin Isle": [(1670, 710), (1880, 710), (1880, 980), (1670, 980)],
    "Liberation Bridge": [(1560, 1130), (1700, 1130), (1700, 1320), (1560, 1320)],
    "Taylor Beach": [(950, 765), (1145, 765), (1145, 1015), (950, 1015)],
    "Pirate Cove": [(580, 1650), (790, 1650), (790, 1870), (580, 1870)],
    "Tignish Farm": [(1450, 1620), (1620, 1620), (1620, 1845), (1450, 1845)],
}


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    exporter_path = ROOT / "scripts/Export-MrePeiLocations.py"
    spec = importlib.util.spec_from_file_location("pei_locations", exporter_path)
    exporter = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(exporter)
    raw = (MAP / "Level.hierarchy").read_bytes()
    chart = (MAP / "Chart.png").read_bytes()
    hierarchy_hash = hashlib.sha256(raw).hexdigest()
    chart_hash = hashlib.sha256(chart).hexdigest()
    if hierarchy_hash != EXPECTED_HIERARCHY or chart_hash != EXPECTED_CHART:
        raise ValueError("PEI source changed; this manually drawn draft must be redone")
    parsed = exporter.parse(raw.decode("utf-8-sig"))
    locations = exporter.extract(parsed)
    volume = exporter.cartography(parsed)
    if {x["name"] for x in locations if x["visible_on_map"]} != set(PIXELS):
        raise ValueError("map locations do not match manually drafted polygons")
    position, scale = volume["position"], volume["scale"]
    width, height = 1923, 1927
    zones = []
    for loc in locations:
        if not loc["visible_on_map"]:
            continue
        vertices = []
        for px, py in PIXELS[loc["name"]]:
            if not (0 <= px <= width and 0 <= py <= height):
                raise ValueError("draft pixel outside chart")
            vertices.append({"x": round((px / width - 0.5) * float(scale["x"]) + float(position["x"]), 3),
                             "z": round((0.5 - py / height) * float(scale["z"]) + float(position["z"]), 3)})
        zones.append({"instance_id": loc["instance_id"], "name": loc["name"], "vertices": vertices})
    print(json.dumps({"status": "DRAFT_NOT_FOR_AUTOMATIC_APPLICATION", "source_sha256": hierarchy_hash,
                      "chart_sha256": chart_hash, "cartography": volume,
                      "note": "Примерные прямоугольники по изображению, НЕ игровые границы; нужна полная ручная проверка.",
                      "zones": zones}, ensure_ascii=False, indent=2, allow_nan=False))


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError) as exc:
        print(f"draft preparation failed: {exc}", file=sys.stderr)
        sys.exit(1)
