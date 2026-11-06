#!/usr/bin/env python3
"""Проверка локального пакета MreTrade без публикации и доступа к сети."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

GUID = "a986933995df4d29816efcc37f87b220"
EXPECTED_DAT = [f"GUID {GUID}", "Type Effect", "ID 48622",
                "Asset_Bundle_Version 6", "Exclude_From_Master_Bundle"]
FILES = {"package.json", "Content/Object.meta", "Content/Bundles/MreTrade/MreTrade.dat",
         "Content/Bundles/MreTrade/MreTrade.unity3d"}


def check(package: Path) -> None:
    if not package.is_dir():
        raise ValueError("Папка пакета отсутствует")
    actual = {p.relative_to(package).as_posix() for p in package.rglob("*") if p.is_file()}
    if actual != FILES:
        raise ValueError(f"Неверный состав пакета: {actual ^ FILES}")
    receipt = json.loads((package / "package.json").read_text(encoding="utf-8-sig"))
    expected = {"effect_id": 48622, "effect_guid": GUID, "unity_version": "2022.3.62f3",
                "target": "StandaloneWindows64", "compatibility_version": 6,
                "tags": ["Object", "Effect"], "published": False, "in_game_verified": False}
    for key, value in expected.items():
        if key not in receipt or type(receipt[key]) is not type(value) or receipt[key] != value:
            raise ValueError(f"Неверное поле package.json: {key}")
    if (package / "Content/Object.meta").read_bytes() != b"\0":
        raise ValueError("Object.meta должен содержать один нулевой байт")
    base = package / "Content/Bundles/MreTrade"
    for filename, key in (("MreTrade.dat", "dat_sha256"), ("MreTrade.unity3d", "bundle_sha256")):
        data = (base / filename).read_bytes()
        if receipt.get(key) != hashlib.sha256(data).hexdigest():
            raise ValueError(f"SHA-256 не совпадает: {filename}")
        if filename.endswith(".dat"):
            if [line for line in data.decode("utf-8-sig").splitlines() if line.strip()] != EXPECTED_DAT:
                raise ValueError("Неверное описание dat")
        elif not data.startswith(b"UnityFS\0"):
            raise ValueError("Неверный заголовок UnityFS")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path, help="Путь к папке пакета")
    args = parser.parse_args()
    try:
        check(args.package)
    except (OSError, ValueError, UnicodeError) as exc:
        print(f"ОШИБКА: {exc}", file=sys.stderr)
        return 1
    print(f"Пакет проверен: {args.package}; публикация и игровая проверка не выполнены")
    return 0


if __name__ == "__main__":
    sys.exit(main())
