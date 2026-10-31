import json
import os
import tempfile
import unittest
from pathlib import Path

from profiles import ProfileRepository, authorized, history_text, names_text, profile_text, open_shared


class ProfileTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.steam_id = "76561198000000001"
        self.profile = {"schema": 1, "steam_id": self.steam_id, "first_seen": "2026-10-30T10:00:00Z",
                        "last_seen": "2026-10-30T11:00:00Z", "play_seconds": 3600,
                        "names": [{"name": "Игрок", "at": "2026-10-30T10:00:00Z"}],
                        "records": [{"kind": "warn", "reason": "Проверка", "status": "recorded", "at": "2026-10-30T10:00:00Z"}],
                        "messages": [{"text": "приватный чат"}], "private_key": "секрет"}
        self.write()
        self.repository = ProfileRepository(self.root)

    def write(self):
        (self.root / f"{self.steam_id}.json").write_text(json.dumps(self.profile), encoding="utf-8")

    def test_no_unintended_private_fields(self):
        result = self.repository.read(self.steam_id)
        self.assertNotIn("messages", result)
        self.assertNotIn("private_key", result)
        self.assertIn("Игрок", profile_text(result))
        self.assertIn("warn", history_text(result))
        self.assertIn("Игрок", names_text(result))

    def test_path_traversal_rejected(self):
        with self.assertRaises(ValueError):
            self.repository.read("../config")

    def test_different_profile_id_rejected(self):
        self.profile["steam_id"] = "76561198000000002"
        self.write()
        with self.assertRaises(ValueError):
            self.repository.read(self.steam_id)

    def test_unknown_schema_rejected(self):
        self.profile["schema"] = 2
        self.write()
        with self.assertRaises(ValueError):
            self.repository.read(self.steam_id)

    def test_staff_access_is_guild_specific(self):
        self.assertTrue(authorized(1, 1, False, {9}, {9}))
        self.assertTrue(authorized(1, 1, True, set(), set()))
        self.assertFalse(authorized(2, 1, True, {9}, {9}))
        self.assertFalse(authorized(None, 1, True, {9}, {9}))
        self.assertFalse(authorized(1, 1, False, set(), {9}))

    def test_server_can_replace_profile_during_bot_read(self):
        target = self.root / f"{self.steam_id}.json"
        replacement = self.root / "replacement.json"
        replacement.write_text('{"new":true}', encoding="utf-8")
        with open_shared(target) as old:
            if os.name == "nt":
                # Игровой сервер использует File.Replace/ReplaceFileW, а не MoveFileEx.
                import ctypes
                from ctypes import wintypes
                kernel = ctypes.WinDLL("kernel32", use_last_error=True)
                kernel.ReplaceFileW.argtypes = (wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.LPCWSTR,
                                                wintypes.DWORD, wintypes.LPVOID, wintypes.LPVOID)
                kernel.ReplaceFileW.restype = wintypes.BOOL
                if not kernel.ReplaceFileW(str(target), str(replacement), None, 0, None, None):
                    raise ctypes.WinError(ctypes.get_last_error())
            else:
                os.replace(replacement, target)
            self.assertEqual(json.loads(old.read())["steam_id"], self.steam_id)
        self.assertTrue(json.loads(target.read_text(encoding="utf-8"))["new"])


if __name__ == "__main__":
    unittest.main()
