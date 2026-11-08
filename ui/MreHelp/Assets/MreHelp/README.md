# Исходники игрового меню MRE

Оболочка повторяет согласованный макет `ui/preview/menu-shell`: горизонтальная навигация, настоящий логотип, графитовые поверхности, зелёный акцент, прозрачный фон вне окна. Noto Sans Regular и SemiBold встроены в bundle из `Assets/MreFonts`; у SemiBold используется семейство **Noto Sans SemiBold**, синтетический Bold отключён. PNG-иконки и маски скруглений — собственные ресурсы из `Assets/MreMenuArt/shell`.

`MreHelpBuilder.Generate` генерирует prefab, `BuildWindows` собирает Windows64 bundle, `RenderPreview` рендерит фактический bundle в шести состояниях и сохраняет главное изображение. Версия Unity — 2022.3.62f3. Canvas использует ConstantPixelSize: шрифт не уменьшается до нечитаемого размера на 1366×768; панели растягиваются по якорям. Проверены рендеры 1920×1080 и 1366×768, другие разрешения требуют отдельной проверки.

EffectAsset: ID48625, GUID6e4a31b49c874f428a622e1a68e4d203, key14825, address Effect, schema5. Вкладки Home,Help,Team,Events,Stats,Links,Settings,BattlePass,Rewards,Reports; кнопки `MenuTab{вкладка}`, подсветка `MenuSelected{вкладка}`, страницы `MenuPage{вкладка}`. Служебные кнопки: MenuPrevious, MenuNext, MenuNotifications, MenuNotificationsClose, MenuClose. Уведомления: MenuNotificationsPanel и MenuNotificationsSelected.

Главная содержит профиль и серверные сведения, которые отправляет HelpUi/HomeData на соединение владельца. Остальные девять страниц пока пустые. Четыре быстрые кнопки MenuHome{Help,Team,Events,Settings} только переключают вкладки. Семь текстов MenuHome{PlayerName,SteamId,Group,GroupCount,ServerName,Online,Map} обновляются на игровом потоке каждые пять секунд; данные других модулей не требуются. Разделитель у колокольчика удалён. `/menu` открывает Home, `/help`/`/mrehelp` — Help. Игровая логика находится в `src/MreHelp/HelpUi.cs`. Сборка и рендеры не доказывают клиентскую доставку кликов; штатное Escape-меню игры сервером не перехватывается. Не устанавливайте второй локальный экземпляр этого EffectAsset поверх Workshop — возможен конфликт GUID.

Полный протокол: `docs/qa-runs/2026-11-08-menu-shell.md`; инструкции: `docs/HELP_UI.md`.
