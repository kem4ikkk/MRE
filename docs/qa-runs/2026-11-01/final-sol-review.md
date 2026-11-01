# Итоговая независимая проверка Sol

01.11.2026, Europe/Moscow. Production, сервер и мир этой проверкой не изменялись. Временные проекты и результаты находятся в `runtime/final-sol-review/`.

## Исправление core/vanilla versus legacy — принято для установки

`MreHudBuilder.BuildWindows` создаёт `.dat` с `Exclude_From_Master_Bundle`, без `Master_Bundle_Override`. В [официальном Assets.cs](https://github.com/SmartlyDressedGames/U3-SDK/blob/main/Assets/Runtime/Assembly-CSharp/Unturned/Bundles/Assets.cs) после поиска master bundle флаг обнуляет выбранный master bundle; override имеет приоритет. [AssetsWorker.cs](https://github.com/SmartlyDressedGames/U3-SDK/blob/main/Assets/Runtime/Assembly-CSharp/Unturned/Bundles/AssetsWorker.cs) читает и хеширует сам dat. Источники прочитаны 01.11.2026. Не следует приписывать выбор bundle AssetsWorker: ветка находится в Assets.LoadFile.

Независимое Cecil-чтение ОБОИХ установленных Assembly-CSharp.dll подтверждает точную ветку `LoadFile(AssetsWorker.AssetDefinition)`: IL_030f string flag, IL_0314 ContainsKey, IL_0319 conditional branch, IL_031b ldnull, IL_031c stloc masterBundle. Server SHA256 `15666f1e20afc32a01a5510eb71eef2512a6eb4064ee628b806ee333ab4e4660`, client `d122e0b615322e660aceaf65e4f68850a2c9aedd233b92d6028e35f0c80db0df`. Это подтверждает влияние флага в фактических версиях, несмотря на разные hashes DLL.

Builder применяет ForceRebuildAssetBundle/StrictMode, загружает созданный bundle, проверяет Canvas по адресу Effect и обязательные имена. Скрипт проверяет время создания, контракт manifest, UnityFS, SHA256 и dat, отклоняет Master_Bundle_Override. Лог `MreHud-unity-20261101-031508-240.log` содержит успешное завершение экспорта. Лицензионное предупреждение в начале лога не оборвало экспорт.

Bundle SHA256 `eccb4ed5ae33c6c26582dce1045f3253ddb7c755d9a60ab66fe90d2ffd7629c8` совпадает у сборки и установленного клиента. Он не новый: изменение dat не требует изменения prefab bundle. Manifest честно сохраняет `in_game_verified=false`.

При первоначальном чтении клиентский dat не содержал флага. После установки координатором повторное read-only сравнение показало одинаковый dat у build/server/client: SHA256 `078651487458b3e1cccd5ce03078f1f4838dbaef3ee17306ca0682c4b5bd6798`. Доказана коррекция ветки загрузчика; реальный вход после полного перезапуска клиента/сервера, GUI и отсутствие следующей ошибки остаются игровой приёмкой.

## Сортировка — включение заблокировано

Повторно запущены фактические SortCore.cs/NativeSortSurface.cs с существующими fake границами: **28 PASS**. Оригинальный тестовый проект не изменялся; отдельный проект `runtime/final-sol-review/Sort.Tests.csproj`. Это проверяет транзакцию и откат, но не реальную сеть.

Два существенных ограничения остаются в исходниках/UX на момент проверки:

1. `/sort storage` требует открытый storage, но ванильный чат открывается при PlayerLifeUI.active, а dashboard/storage используют другой экран. Закрытие dashboard закрывает storage. Текущая команда не доказана как доступный способ сортировки открытого контейнера. Публичный API сам по себе кнопкой не является.
2. PlayerInventory.ReceiveDragItem/ReceiveSwapItem/ReceiveDropItem адресуют предметы координатами без версии. Соответствующие Send* используют ENetReliability.Unreliable. Синхронная транзакция не устраняет уже отправленные старые запросы: после перестановки они могут выбрать другой jar. Cooldown и серверный active-set этого не исправляют. Возможное неправильное действие с вещью нельзя принимать за безопасный UX; фактический дюп/потеря этим review не воспроизведены.

`Enabled=false` остаётся правильным состоянием. До включения нужны продуманный способ вызова, протокол/ограничения конкурентного ввода и реальные сценарии delayed/reordered drag/swap/drop на тестовой копии с двумя клиентами. Ни 28 PASS, ни отсутствие new Item/removeItem не доказывают сетевую безопасность.

## Harmony observer — код принят, игровая приёмка открыта

Повторная проверка согласуется с `building-punch-sol.md`: поле IDisposable, внешний catch в Load, NoInlining у StartPunchObserver; Harmony-типы за этой границей. Patch выбирает трёхпараметрический getInput(bool, ERaycastInfoUsage, Vector3?), отбрасывает ненулевой rayOriginOverride и не пишет __result. Hash gate относится к установленному серверу.

Выполнены заново: полная компиляция фактических исходников C#7.3 по установленным DLL (`MreHud-reviewed.dll`); **23 HUD checks PASS**; missing-Harmony probe с production observer и скопированными production Load/helper — **PASS**, внешний catch сохранил обе damage-event подписки. Probe работает на CLR .NET 8 с fake игровыми границами; Mono не запускался. `Install-MreHud.ps1` dry-run — PASS. Installer проверяет staged Harmony hash до dry-run и installed Harmony до Apply/копирования; dry-run не проверяет установленную библиотеку полностью.

Реальный punch, Mono patch/unpatch/reload, LOS, неуязвимые шкафы/двери/structures и GUI не проверены. Новый blocker в коде observer не найден.

## Сейфы — установка подтверждена, эксплуатация не принята

Прочитаны исходные Workshop dat: 36501/36502/36503/36504, сетки 10×10/15/20/25, health 600/900/1200/1500, Locked. config/workshop.json и установленный WorkshopDownloadConfig содержат оригинальный 2922152196. Server_MRE.log подтверждает загрузку rclockers.masterbundle и добавление четырёх ассетов; также присутствует предупреждение об отсутствии server hashes.

[Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=2922152196), прочитан 01.11.2026: автор RageLess5, описание разрешает серверное использование и запрещает re-upload; четыре IDs совпадают. HTML также содержит шаблонные сообщения removal/incompatibility; их наличие без API и игрового испытания не доказывает фактическую несовместимость. docs/SAFES.md эту неоднозначность уже оговаривает. Original bundle не изменялся данным review.

Готовность зависит от установки/открытия реальным клиентом, owner/group/stranger доступа, save→restart→restore, разрушения/выпадения и наполненных рецептов. Четвёртый вариант не скрыт: требуется решение владельца; сортировку на действующих сейфах пока не включать. Наличие backup/A2S не заменяет эти проверки.
