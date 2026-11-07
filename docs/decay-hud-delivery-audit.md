# Аудит подключения подсказки гниения

Состояние на 07.11.2026: код HUD и визуальный Unity bundle подготовлены как изолированный кандидат; в игре подсказка не работает.

## Что уже подтверждено

- `HintPolicy` формирует две строки 1%/2%; `GameHintAdapter` классифицирует удерживаемый предмет, берёт точку попадания луча и обновляет или очищает эффект.
- Заглушки HUD проходят `dotnet run --project tests/MreDecayZoneHud/MreDecayZoneHud.csproj --configuration Release`; сборка исходников против локальных игровых DLL проходит `dotnet build tests/MreDecayZoneHud/GameBuild/GameBuild.csproj --configuration Release`. Это не игровая доставка и не клиентская отрисовка.
- Unity builder создаёт prefab с именами `DecayZonePanel`/`DecayZoneText`, проверяет тестовый bundle повторной загрузкой и пишет `.dat` с Effect ID `48623`, GUID `5d4628c25c73441a89ef5aed4db47713`; manifest задаёт key `14823`. Скрипт `scripts/Build-MreDecayZoneHud.ps1` собирает только в TEMP и помечает результат `TEST_ONLY_NOT_INSTALLED`.
- Изолированный серверный диагностический запуск подтверждал разрешение Effect ID/GUID и копирование полного набора файлов test collection в `Servers/MREClientPackage/Bundles`. Аудит отмечает 10 ошибок разбора William's Vehicles в полном Workshop inventory; коллизии проверены только против реально разрешённых ресурсов конкретных версий.
- В исходниках текущие ключи: MreHud `14821`, MreTrade `14822`, decay HUD `14823`, MreHelp `14825`. Локальный поиск не нашёл другого использования `14823`; это проектная сверка исходников, а не глобальная гарантия.

## Что ещё требуется для показа двум клиентам

1. **Проверить asset и визуальный эффект отдельно от production decay.** Уже есть локальный test-map путь: `ui/MreDecaySafe/LOCAL_CLIENT_TEST.md`, `scripts/Build-MreDecaySafeTestMap.ps1` и серверная диагностика `ui/MreDecaySafe/checks/SafeMapPackageDiagnostic.cs`. В отдельной тестовой карте можно использовать test-only fixture с явно помеченным текстом, не подключая draft-зоны и не выставляя readiness production-механики. Одинаковую карту с `Bundles` заранее подготовить на обеих сторонах: это локальная копия контента, а не автоматическая доставка клиенту.
2. **Подключить отдельный test sender, а не production policy.** `GameHintAdapter` — не Rocket-плагин: production-владельца, который создаёт его с реальным status source, вызывает `Tick()` на server Unity thread, выполняет `Clear()` при `Unload()` и логирует ошибки, нет. `IDecayZoneStatusSource` сообщает только булеву классификацию зоны и не задаёт текст; поэтому test-only показ должен использовать отдельный guarded UI sender с явной пометкой `TEST`, не production `GameHintAdapter`/`HintPolicy` и не `DecayRuntimePlugin`.
3. **Проверить доставку A/B на тестовой карте.** `ui/MreDecaySafe/LOCAL_CLIENT_TEST.md` описывает два варианта: одинаковые файлы карты заранее на сервере и клиентах либо корректный опубликованный Workshop File ID. Документация SDG указывает Workshop File ID как автоматический сетевой путь; серверная `Assets.find`, файлы только в серверном `Bundles` и map loading не доказывают клиентскую доставку.
4. **После независимой проверки visual path подключить production status.** `UnavailableDecayZoneStatusSource` пока всегда запрещает показ. `planning/pei-decay-zones.draft.json` остаётся черновиком, а production источник должен одновременно проверять готовность механики и утверждённую геометрию. Проверка визуального fixture не заменяет эту связь.
5. **Игровая приёмка A и B.** На изолированном PEI-сервере с идентичными локальными пакетами проверить показ и очистку test effect обоими клиентами. После подключения production source отдельно проверить Housing Planner, пол/стену/потолок, выход из зоны и переход границы, отсутствие строительного предмета, отсутствие raycast hit, потерю готовности, переподключение и выгрузку. Проверить UI scale и совместную работу с MRE HUD; подтвердить реальный луч/16 м и список строительных Workshop assets. Записать версии, хеши и наблюдения обоих клиентов.

## Порядок безопасного продолжения

1. Сначала провести независимый test-only visual/client package test на отдельной карте; его можно выполнять независимо от production decay runtime.
2. Параллельно доказать сохранение `Kind:InstanceId` на ordinary save/restart fixture; отдельно продолжать crash/reconcile матрицу и проверку durable transaction.
3. Завершить production source только после verified zones, raid, world snapshot и crash-safe transaction; до этого оставить `UnavailableDecayZoneStatusSource` и runtime fail-closed.
4. Подключить Rocket-владельца production HUD adapter с проверенными zones и очисткой effect на Unity thread, только после готовности runtime prerequisites.
5. Перед отдельным применением на основном сервере провести полную A/B приёмку, резервную копию и проверенный откат.

Рабочий статус появится только после подключения production source/owner и игровой приёмки. Отдельный test-only effect можно показывать и проверять раньше; bundle, тестовый серверный asset mapping и compile/fake checks подтверждают отдельные части, но сами по себе не включают рабочую функцию.
