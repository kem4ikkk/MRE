"""Чтение ограниченных профилей MRE. Этот модуль ничего не записывает на сервер."""
from __future__ import annotations

import json
import os
import re
from pathlib import Path
from typing import Any


def open_shared(path: Path):
    """Windows: чтение не мешает атомарной замене JSON игровым сервером."""
    if os.name != "nt":
        return path.open("r", encoding="utf-8-sig")
    import ctypes
    import msvcrt
    from ctypes import wintypes
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateFileW.argtypes = (wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                                   wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE)
    kernel.CreateFileW.restype = wintypes.HANDLE
    handle = kernel.CreateFileW(str(path), 0x80000000, 7, None, 3, 0x80, None)
    if handle == ctypes.c_void_p(-1).value:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        descriptor = msvcrt.open_osfhandle(handle, os.O_RDONLY)
    except Exception:
        kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
        kernel.CloseHandle(handle)
        raise
    return open(descriptor, "r", encoding="utf-8-sig", closefd=True)


def authorized(guild: int | None, configured_guild: int, administrator: bool,
               member_roles: set[int], staff_roles: set[int]) -> bool:
    return bool(guild and guild == configured_guild
                and (administrator or member_roles.intersection(staff_roles)))


class ProfileRepository:
    def __init__(self, directory: Path):
        self.directory = directory.resolve(strict=True)

    def read(self, steam_id: str) -> dict[str, Any]:
        if not re.fullmatch(r"7656119[0-9]{10}", steam_id):
            raise ValueError("Нужен корректный SteamID64.")
        path = (self.directory / f"{steam_id}.json").resolve(strict=True)
        if path.parent != self.directory or path.stat().st_size > 4 * 1024 * 1024:
            raise ValueError("Некорректный файл профиля.")
        with open_shared(path) as source:
            contents = source.read(4 * 1024 * 1024 + 1)
        if len(contents) > 4 * 1024 * 1024:
            raise ValueError("Слишком большой профиль.")
        profile = json.loads(contents)
        if profile.get("schema") != 1 or str(profile.get("steam_id")) != steam_id:
            raise ValueError("Несовместимый профиль MRE.")
        if not isinstance(profile.get("names"), list) or not isinstance(profile.get("records"), list):
            raise ValueError("Поврежденный профиль.")
        # Не возвращаем сообщения чата, команды или произвольные дополнительные поля.
        return {key: profile.get(key) for key in (
            "steam_id", "first_seen", "last_seen", "play_seconds", "names", "records"
        )}


def profile_text(profile: dict[str, Any]) -> str:
    names = profile["names"]
    name = str(names[0].get("name", "Неизвестно")) if names else "Неизвестно"
    seconds = max(0, int(profile.get("play_seconds") or 0))
    return (f"{name}\nSteamID: {profile['steam_id']}\n"
            f"Первый вход UTC: {profile.get('first_seen', '—')}\n"
            f"Последний вход UTC: {profile.get('last_seen', '—')}\n"
            f"Учтенное время игры: {seconds // 3600} ч {(seconds // 60) % 60} мин\n"
            f"Записей модерации: {len(profile['records'])}")


def history_text(profile: dict[str, Any], count: int = 10) -> str:
    records = profile["records"][:max(1, min(count, 10))]
    return "\n".join(
        f"{str(row.get('at', ''))[:19]} UTC | {row.get('kind', '')} | "
        f"{row.get('status', '')} | {str(row.get('reason', ''))[:160]}"
        + (" | отменено" if row.get("revoked_by") else "")
        for row in records
    ) or "История пуста."


def names_text(profile: dict[str, Any]) -> str:
    return "\n".join(f"{str(row.get('at', ''))[:19]} UTC | {row.get('name', '')}"
                     for row in profile["names"][:20]) or "История имен пуста."
