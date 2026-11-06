# MreTrade: проверка загрузки изолированного стенда

Дата: 7 ноября 2026 года (Europe/Moscow).

## Что проверено

- Создан отдельный экземпляр `runtime/trade-stage/server` на порту `27115`; RCON настроен выключенным, соседний порт `27116` зарезервирован под RCON.
- При подготовке не копировались `Servers/MRE/Level`, `Players`, Rocket permissions, конфиги и журналы основного экземпляра. Стенд загрузил чистый PEI.
- Установлены только `MreTrade`, совместимая с ним сборка `Teleportation.MRE` и `0Harmony`; полный набор остальных плагинов MRE не копировался.
- `MreTrade.dll` собран из release-ветки `release/mre-trade-review` и файлов игры из локального U3DS; SHA-256 DLL `38f143dcbb94385d89b83902aed527c8f07f819130b9c86f2521fa17ddb971bb` совпал с `build-manifest.json`.
- Workshop bundle заново собран 7 ноября из Unity-проекта этой release-ветки (Unity 2022.3.62f3); SHA-256 `29d4ccd4c449f69067fa0d8a598c287ad397ff08f0d9578128fa7ebe5e716444`. Сборка подтвердила свежесть prefab/generator; статическая проверка нового локального Workshop-пакета пройдена.
- Подготовлено отдельное превью 512×512 JPEG (44 345 байт); это иллюстрация макета, не игровой скриншот.
- На старте Rocket зарегистрировал зависимость `MreTrade` и команду `/trade`; `MreTrade` и `Teleportation` загрузились. Создана конфигурация с `ExperimentalTradeEnabled=false`, `UiEnabled=false`, `AssetsVerified=false`.
- Стенд штатно выгружен через Ctrl+C; Rocket записал `[unloading] MreTrade`. Основной сервер не останавливался и его файлы не изменялись.

## Что эта проверка не доказывает

- Обмен не запускался; UI отключён, bundle не доставлялся и не проверялся клиентом.
- Нет проверки второго клиента, фактических callbacks UI, предметов, PvP/raid guard, аварийного восстановления и восстановления из backup.
- Прежняя глобальная блокировка после `.done` остаётся намеренным ограничением. Автоматически обходить её на основании одного штатного завершения пока небезопасно.

Результат: **проверка загрузки серверной DLL на отдельном экземпляре пройдена; игровая приёмка обмена не пройдена/не выполнялась**.

## Подготовка Workshop-публикации

- Свежий пакет повторно собран скриптом `tools/trade-workshop/Build-MreTradeWorkshopPackage.ps1`; проверка `tests/CheckMreTradeWorkshopPackage.py` прошла, включая хеши DLL-ресурсов, структуру `Content` и JPEG-превью 512×512.
- `tests/CheckMreTradeUi.py` прошёл. `dotnet run --project src/MreTrade/RecoveryChecks/MreTrade.RecoveryChecks.csproj` и `dotnet run --project src/MreTrade/DeliveryChecks/MreTrade.DeliveryChecks.csproj` прошли все встроенные проверки восстановления и выдачи.
- Проверка браузерного превью `tests/CheckMreTradePreview.cjs` не запущена: в окружении отсутствует npm-пакет Playwright. Игровой клиент и двухсторонний обмен не проверялись.
- Workshop-публикация пока не выполнена: в доступном наборе инструментов нет управления интерфейсом Unturned/Steam Workshop. Workshop ID не создан; основной сервер не изменён.
