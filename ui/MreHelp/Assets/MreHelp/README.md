# MRE `/menu` и `/help` — Unity UI

Автономный Unity-проект меню MRE. Используется Unity **2022.3.62f3**, uGUI package **1.0.0**, Windows Standalone AssetBundle и reference canvas 1920×1080. Шрифт PT Sans включён по лицензии OFL.

Контракт EffectAsset: ID **48625**, GUID **6e4a31b4-9c87-4f42-8a62-2e1a68e4d203**, key **14825**, prefab address **Effect**. Верхние кнопки: `MenuTopMain`, `MenuTopProgress`, `MenuTopSettings`; разделы: `MenuSectionInformation`, `MenuSectionGroup`, `MenuSectionCommands`, `MenuSectionLinks`, `MenuSectionRewards`, `MenuSectionTop`; закрытие: `MenuClose`. Десять строк: `MenuRow01..10`, заголовки `MenuRowTitle01..10`, описания `MenuRowDescription01..10`; перелистывание `MenuCommandsPrevious`/`MenuCommandsNext`, номер страницы `MenuPage`, заголовок окна `MenuTitle`, подпись `MenuSubtitle`, нижняя строка `MenuFooter`.

Сборка подходящим локальным Unity Editor:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Build-MreHelpAssets.ps1
```

Вывод bundle и dat находится в `ui/MreHelp/Build/Windows/MreHelp/`, а `effect-manifest.json` — рядом, в `ui/MreHelp/Build/Windows/`. Параметр `-RenderPreview` рисует `Build/MreHelp-preview.png`. Скрипт сам не устанавливает, не упаковывает и не публикует ресурсы.

В prefab записан пример текста для Unity preview. На сервере заполняется явный каталог команд MRE; команда показывается, если она зарегистрирована и у игрока есть право. Это не автоматический список всех Rocket-команд. Клиенту нужен тот же EffectAsset. Сервер показывает текстовую подсказку, если сам не нашёл asset; он не может надёжно определить, загрузил ли его клиент. Опубликованный Workshop-пакет MRE HUD пока не содержит bundle меню; сначала соберите локальный объединённый пакет и проверьте его на чистом клиенте и сервере. Не помещайте эффект в общий каталог `Bundles`: UI MRE доставляется отдельным Workshop-пакетом, чтобы избежать конфликтов GUID.

Сборка prefab и просмотр PNG не подтверждают доставку кликов, масштабирование разрешения, закрытие клавиатурой, поддержку платформ кроме Windows или распространение Workshop. Это требует отдельной игровой приёмки.
