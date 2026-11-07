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
BASE_FILES = {"package.json", "Content/Object.meta", "Content/Bundles/MreTrade/MreTrade.dat",
              "Content/Bundles/MreTrade/MreTrade.unity3d"}
PUBLISHED_FILES = {"MreTrade-preview.jpg", "published-item-id.txt"}


def check(package: Path) -> None:
    if not package.is_dir():
        raise ValueError("Папка пакета отсутствует")
    actual = {p.relative_to(package).as_posix() for p in package.rglob("*") if p.is_file()}
    if not BASE_FILES.issubset(actual):
        raise ValueError(f"В пакете отсутствуют обязательные файлы: {BASE_FILES - actual}")
    receipt = json.loads((package / "package.json").read_text(encoding="utf-8-sig"))
    expected = {"effect_id": 48622, "schema_version": 2,
                "confirmation_protocol": "nonce-input-v1", "input_fields": ["TradeConfirmToken"],
                "effect_guid": GUID, "unity_version": "2022.3.62f3",
                "target": "StandaloneWindows64", "compatibility_version": 6,
                "tags": ["Object", "Effect"], "in_game_verified": False}
    for key, value in expected.items():
        if key not in receipt or type(receipt[key]) is not type(value) or receipt[key] != value:
            raise ValueError(f"Неверное поле package.json: {key}")
    if type(receipt.get("published")) is not bool:
        raise ValueError("Поле published должно иметь логический тип")
    extras = actual - BASE_FILES
    prepared_update = receipt.get("upload_status") == "prepared"
    if receipt["published"] or prepared_update:
        if extras != PUBLISHED_FILES:
            raise ValueError(f"Пакет обновления должен содержать превью и ID: {extras ^ PUBLISHED_FILES}")
        item_id = (package / "published-item-id.txt").read_text(encoding="ascii").strip()
        if not item_id.isdecimal() or int(item_id) != receipt.get("workshop_file_id"):
            raise ValueError("Workshop ID в receipt и published-item-id.txt не совпадают")
        preview = (package / "MreTrade-preview.jpg").read_bytes()
        if receipt.get("preview_sha256") != hashlib.sha256(preview).hexdigest():
            raise ValueError("SHA-256 превью не совпадает с receipt")
        if receipt.get("preview_dimensions") != "512x512":
            raise ValueError("Размер превью в receipt не соответствует ожидаемому 512x512")
    elif extras:
        raise ValueError(f"Неопубликованный пакет содержит посторонние файлы: {extras}")
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


def check_downloaded_payload(package: Path, payload: Path) -> None:
    receipt = json.loads((package / "package.json").read_text(encoding="utf-8-sig"))
    if payload.name != str(receipt.get("workshop_file_id")):
        raise ValueError("Каталог загрузки должен называться Workshop ID из receipt")
    expected = {"Object.meta", "Bundles/MreTrade/MreTrade.dat", "Bundles/MreTrade/MreTrade.unity3d"}
    actual = {p.relative_to(payload).as_posix() for p in payload.rglob("*") if p.is_file()}
    if actual != expected:
        raise ValueError(f"Неверный состав загруженного Workshop-пакета: {actual ^ expected}")
    if (payload / "Object.meta").read_bytes() != b"\0":
        raise ValueError("Загруженный Object.meta должен содержать один нулевой байт")
    for relative, key in (("Bundles/MreTrade/MreTrade.dat", "dat_sha256"),
                          ("Bundles/MreTrade/MreTrade.unity3d", "bundle_sha256")):
        if hashlib.sha256((payload / relative).read_bytes()).hexdigest() != receipt.get(key):
            raise ValueError(f"Хеш загруженного файла не совпадает с опубликованным receipt: {relative}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path, help="Путь к папке пакета")
    parser.add_argument("--downloaded-payload", type=Path,
                        help="Распакованный SteamCMD payload для сравнения с receipt")
    args = parser.parse_args()
    try:
        check(args.package)
        if args.downloaded_payload:
            check_downloaded_payload(args.package, args.downloaded_payload)
    except (OSError, ValueError, UnicodeError) as exc:
        print(f"ОШИБКА: {exc}", file=sys.stderr)
        return 1
    receipt = json.loads((args.package / "package.json").read_text(encoding="utf-8-sig"))
    state = ("подготовленный пакет обновления; отправка в Workshop и игровая проверка не подтверждены"
             if receipt.get("upload_status") == "prepared" else
             "опубликованный пакет; загрузка клиентом и игровая проверка не подтверждены"
             if receipt["published"] else
             "собранный пакет; публикация и игровая проверка не выполнены")
    if args.downloaded_payload:
        state += "; SteamCMD payload скачан и его хеши совпадают"
    print(f"Пакет проверен: {args.package}; {state}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
