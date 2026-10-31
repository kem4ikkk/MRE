"""Необязательный Discord-бот MRE. Запуск выполняется владельцем после настройки."""
from __future__ import annotations

import asyncio
import json
import os
from pathlib import Path

import discord
from discord import app_commands

from profiles import ProfileRepository, authorized, history_text, names_text, profile_text


ROOT = Path(__file__).resolve().parents[2]


def main() -> None:
    config_path = ROOT / "secrets" / "mre-moderation-bot.json"
    config = json.loads(config_path.read_text(encoding="utf-8-sig"))
    token = os.environ.get("MRE_DISCORD_TOKEN") or config.get("BotToken")
    guild_id = int(config["GuildId"])
    staff_roles = {int(value) for value in config.get("StaffRoleIds", [])}
    if not token or not guild_id:
        raise ValueError("Заполните приватную конфигурацию Discord-бота.")
    directory = (ROOT / config["ProfileDirectory"]).resolve(strict=True)
    allowed_root = (ROOT / "runtime/server/Servers/MRE/Rocket/Plugins/MreModeration").resolve(strict=True)
    if not directory.is_relative_to(allowed_root):
        raise ValueError("Каталог профилей должен находиться внутри MreModeration.")
    repository = ProfileRepository(directory)
    guild_object = discord.Object(id=guild_id)

    class MreClient(discord.Client):
        async def setup_hook(self) -> None:
            # Только команды указанной гильдии; глобальные команды не регистрируются.
            await tree.sync(guild=guild_object)

    client = MreClient(intents=discord.Intents.none(), allowed_mentions=discord.AllowedMentions.none())
    tree = app_commands.CommandTree(client)

    async def respond(interaction: discord.Interaction, steam_id: str, kind: str) -> None:
        roles = {role.id for role in getattr(interaction.user, "roles", [])}
        administrator = bool(getattr(getattr(interaction.user, "guild_permissions", None), "administrator", False))
        if not authorized(interaction.guild_id, guild_id, administrator, roles, staff_roles):
            await interaction.response.send_message("Нет доступа к модерации MRE.", ephemeral=True)
            return
        await interaction.response.defer(ephemeral=True, thinking=True)
        try:
            profile = await asyncio.to_thread(repository.read, steam_id)
            render = {"profile": profile_text, "history": history_text, "names": names_text}[kind]
            content = discord.utils.escape_mentions(discord.utils.escape_markdown(render(profile)))[:1900]
            await interaction.followup.send(content, ephemeral=True, allowed_mentions=discord.AllowedMentions.none())
        except FileNotFoundError:
            await interaction.followup.send("Такого сохраненного профиля пока нет.", ephemeral=True)
        except (ValueError, KeyError, TypeError, OSError):
            await interaction.followup.send("Не удалось прочитать профиль. Проверьте SteamID и журнал сервера.", ephemeral=True)

    @tree.command(name="mre_profile", description="Карточка игрока MRE", guild=guild_object)
    async def profile_command(interaction: discord.Interaction, steam_id: str) -> None:
        await respond(interaction, steam_id, "profile")

    @tree.command(name="mre_history", description="Последние действия модерации MRE", guild=guild_object)
    async def history_command(interaction: discord.Interaction, steam_id: str) -> None:
        await respond(interaction, steam_id, "history")

    @tree.command(name="mre_names", description="История имен игрока MRE", guild=guild_object)
    async def names_command(interaction: discord.Interaction, steam_id: str) -> None:
        await respond(interaction, steam_id, "names")

    client.run(token, log_level=None)


if __name__ == "__main__":
    main()
