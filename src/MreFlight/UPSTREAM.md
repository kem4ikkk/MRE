# Происхождение MreFlight

MreFlight — отдельный GPL-3.0-only плагин, производный от механики FlightSession в [ShimmysAdminTools](https://github.com/ShimmyMySherbet/ShimmysAdminTools). Автор исходного проекта — ShimmyMySherbet и его участники. Изменения MRE — MRE contributors, 2026.

- Зафиксированная версия: `v1.7.2a`, prerelease от 24.04.2023.
- Commit: `e74012c42e5cece1932493303ef2bf7f2310623c`.
- Основной исходный файл: [Behaviors/FlightSession.cs](https://github.com/ShimmyMySherbet/ShimmysAdminTools/blob/e74012c42e5cece1932493303ef2bf7f2310623c/ShimmysAdminTools/Behaviors/FlightSession.cs).
- Дополнительно изучены `Commands/AFlyCommand.cs`, `Commands/AFlySpeedCommands.cs` и `Models/UnturnedKey.cs` того же commit.
- Лицензия сохранена в `LICENSE`; SHA256 исходного LICENSE: `3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986`.
- SHA256 архива исходного commit: `76a6e585d4d3a5f91a721bad6477c3fb6c8fdf07576005efb98cd99116850fd2`.

Сохранен принцип физического полета через штатные серверные множители: прыжок меняет направление гравитации на подъем; спринт при взгляде вниз дает спуск; без этих действий используется нулевая гравитация. Это перемещение персонажа, а не свободной камеры.

В fork выделен только полет. Переписаны управление сессией, проверка прав и текущего игрока, восстановление прежних gravity/speed/jump при выключении, смерти, выходе, выгрузке и отзыве прав. Ограничены скорости, отвергаются NaN/Infinity. Убраны plugin hotkeys, чтобы не занимать Б/Ю у TPA. Remote updater и прочие инструменты исходного проекта в состав MreFlight не входят. Реализованы идемпотентные `afly on/off` и отдельный публичный API.

Файлы MreFlight и тесты в этом каталоге распространяются с GPL-3.0-only. Код не переносится в MIT-модули MRE. Исходники и сценарий сборки `scripts/Build-MreFlight.ps1` составляют соответствующий исходный код этого fork; SDK игры отдельно устанавливается из U3DS и в репозиторий не включается.
