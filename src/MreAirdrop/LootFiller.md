# Добор аирдропов

Локальные ассеты `runtime/server/Bundles/Items` проверены 02.11.2026; новые оружейные предметы дополнительно сверены 04.11.2026. У каждого ID ниже в соответствующем `.dat` указаны `Size_X 1` и `Size_Y 1`, название взято из соседнего `English.dat`. При запуске остаётся необходима проверка `Assets.find` и размеров: наличие файла не подтверждает загрузку ассета.

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
| 151, 152, 1007, 1008 | Tactical Laser, Tactical Light, Adaptive Chambering, Rangefinder | Tacticals |
| 299, 1001, 134, 19, 128, 1383, 1486 | Штатные прицелы соответствующего оружия | Sights |
| 150, 1190 | Military Muzzle, Ranger Muzzle | Barrels |
| 298 | Grizzly Magazine | Magazines/Grizzly_5 |
| 20 | Timberwolf Magazine | Magazines/Timberwolf_6 |
| 1003 | Matamorez Magazine | Magazines/Matamorez_17 |
| 1384 | Ekho Magazine | Magazines/Ekho_7 |

Оружейный пул использует только оружейные предметы размером 1×1: циклически повторяются Tactical Laser 151, Tactical Light 152, Adaptive Chambering 1007 и Rangefinder 1008. У всех восьми видов оружия в пяти базовых комплектах локальные ассеты содержат `Hook_Tactical`. По одному разу добавляются совместимые магазины 297→298, 18→20, 1000→1003, 1382→1384; штатные прицелы 297→299, 1000→1001, 132→134, 18→19, 126→128, 1382→1383, 1484→1486; дульные устройства 132→150 и 126→1190. Наличие оружия определяется по базовому набору; калибры дульных устройств и магазинов, штатные прицелы и точки крепления сверены с локальными `.dat`. Произвольные изменённые наборы требуют отдельной проверки совместимости общих тактических обвесов. Повторение четырёх обвесов позволяет заполнить свободные ячейки, но их количество и баланс требуют игровой проверки.

Строительный пул содержит материалы; рейдовый — расходники и средства отвлечения без дополнительной взрывчатки. Пулы циклически повторяются со случайным начальным сдвигом.

`Launch` проверяет все возможные предметы добора через `Assets.find` и их размер до появления самолёта. Пул включён в сборку плагина и заполняет оставшиеся клетки с учётом ограничений контейнера.

Изолированные тесты: `dotnet run --project src/MreAirdrop/Tests/Filler/Filler.Tests.csproj`. Размещение и заполнение в настоящем ящике не проверены; игровая приёмка необходима.
