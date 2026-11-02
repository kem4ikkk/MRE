# Добор аирдропов

02.11.2026 проверены локальные ассеты `runtime/server/Bundles/Items`: у каждого ID ниже в соответствующем `.dat` указаны `Size_X 1` и `Size_Y 1`, название взято из соседнего `English.dat`. При запуске остаётся необходима проверка `Assets.find` и размеров: наличие файла не подтверждает загрузку ассета.

| ID | Название ассета | Каталог внутри `Bundles/Items` |
|---|---|---|
| 394 | Dressing | Medical/Dressing |
| 95 | Bandage | Medical/Bandage |
| 390 | Painkillers | Medical/Painkillers |
| 391 | Vitamins | Medical/Vitamins |
| 392 | Purification Tablets | Medical/Tablets |
| 389 | Antibiotics | Medical/Antibiotics |
| 86 | Energy Bar | Food/Bar_Energy |
| 14 | Bottled Water | Water/Bottled_Water |
| 1346 | Flashbang | Throwables/Flashbang |
| 267 | White Smoke | Throwables/Smoke_White |
| 285 | Metal Bar | Supplies/Bar_Metal |
| 74 | Bricks | Supplies/Bricks |
| 67 | Metal Scrap | Supplies/Scrap_Metal |
| 71 | Nails | Supplies/Nails |
| 65 | Wire | Supplies/Wire |
| 69 | Tape | Supplies/Tape |
| 64 | Rope | Supplies/Rope |
| 66 | Cloth | Supplies/Cloth |
| 70 | Glue | Water/Glue |
| 1926 | Clay | Supplies/Clay |
| 298 | Grizzly Magazine | Magazines/Grizzly_5 |
| 20 | Timberwolf Magazine | Magazines/Timberwolf_6 |
| 1003 | Matamorez Magazine | Magazines/Matamorez_17 |
| 1384 | Ekho Magazine | Magazines/Ekho_7 |

Оружейный пул содержит медицину, воду, еду и средства отвлечения. Если соответствующее оружие присутствует в базовом наборе, добавляется максимум один магазин: Grizzly 297→298, Timberwolf 18→20, Matamorez 1000→1003 и Ekho 1382→1384. Связи `Magazine` и калибры проверены в локальных ассетах. Строительный пул содержит материалы; рейдовый — расходники и средства отвлечения без дополнительной взрывчатки. Пулы циклически повторяются для заполнения ящика со случайным начальным сдвигом; при большом ящике расходники накапливаются. Игровой баланс ещё не проверен.

`Launch` проверяет все возможные предметы добора через `Assets.find` и их размер до появления самолёта. Пул включён в сборку плагина и заполняет оставшиеся клетки с учётом ограничений контейнера.

Изолированные тесты: `dotnet run --project src/MreAirdrop/Tests/Filler/Filler.Tests.csproj`. Размещение и заполнение в настоящем ящике не проверены; игровая приёмка необходима.
