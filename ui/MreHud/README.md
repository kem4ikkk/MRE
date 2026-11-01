# Исходный Unity UI MRE

Это минимальный проект **Unity 2022.3.62f3** с оригинальным uGUI-генератором [MreHudBuilder.cs](Assets/Editor/MreHudBuilder.cs). Он создаёт настоящий `Effect.prefab` для Unturned и собирает Windows asset bundle: здоровье/оружие, TPA/HOME и верхнюю панель `/vote day`. В bundle только стандартные Canvas, CanvasScaler, CanvasGroup, Image и Text; своего клиентского кода и закрытых ресурсов REDLINE нет. Генератор имеет ту же [MIT-лицензию](../../src/MreHud/LICENSE), что и серверный HUD. Встроенный Unity font используется как engine resource, отдельный сторонний шрифт не распространяется.

## Как получить bundle

1. Открыть эту папку в Unity **2022.3.62f3**. Пакет `com.unity.ugui` задаётся в `Packages/manifest.json`.
2. Выбрать **MRE → Создать HUD prefab**. Результат: `Assets/MreHud/Effect.prefab`. Его дочерний `HudContent` по умолчанию скрыт; для визуального просмотра в Editor временно включить объект, для игровой сборки генератор снова скрывает его до получения реальных данных.
3. Выбрать **MRE → Собрать Windows bundle**. Результат: `runtime/ui-builds/MreHud/Windows/MreHud/` относительно корня репозитория.

Воспроизводимая командная сборка при уже установленном и лицензированном Editor:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe' `
  -batchmode -quit -projectPath "$PWD\ui\MreHud" `
  -executeMethod MreHudBuilder.BuildWindows `
  -logFile "$PWD\runtime\ui-builds\MreHud-unity.log"
```

Перед командой создать `runtime/ui-builds`, если его ещё нет. Генератор не требует импорта проприетарной игровой DLL: эффект состоит из штатных Unity UI компонентов. Основной адрес внутри `.unity3d` — **`Effect`**, как требует загрузчик `EffectAsset`. `.dat` содержит GUID, Type Effect, ID, Asset_Bundle_Version 6 и `Exclude_From_Master_Bundle`: флаг обязателен для выбора соседнего legacy bundle при размещении клиента под корневым `core.masterbundle`. По [документации SDG](https://docs.smartlydressedgames.com/en/latest/assets/asset-definitions.html), master bundle выше в иерархии иначе имеет приоритет. `Master_Bundle_Override` не должен присутствовать: он имеет приоритет над исключением. Это Windows-сборка; поддержка остальных клиентских платформ отдельно не проверена.

Для запуска из репозитория также предусмотрен скрипт:

```powershell
.\scripts\Build-MreHudAssets.ps1 -UnityEditorPath 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe'
```

Он запускает Editor в batchmode, сохраняет отдельный журнал и проверяет свежесть результата, версию Unity, платформу, GUID/ID, SHA256, сигнатуру UnityFS и `.dat`. Без Editor скрипт завершится понятной ошибкой; старый bundle не будет выдан за результат нового запуска. Скрипт не активирует лицензию и не включает HUD у игроков. Бесплатный Personal активируется пользователем через Unity Hub под собственным Unity ID; пароль в проект не передаётся.

## Доставка в игру

Папка результата содержит `MreHud.dat` и `MreHud.unity3d`. Она нужна **и серверу, и клиенту**. Локальная серверная папка сама по себе не скачивает bundle на компьютеры игроков.

Для контролируемого теста на этом Windows-компьютере скопировать одну и ту же папку в `runtime/server/Servers/MRE/Bundles/MreHud` и локальные `Unturned/Bundles/MreHud` на выключенных приложениях. Журнал сервера подтверждает поиск ресурсов в `Servers/MRE/Bundles`. В `.dat` обязательна строка `Exclude_From_Master_Bundle`: без неё клиент наследует расположенный выше `core.masterbundle`, а сервер видит legacy bundle, и проверка asset при входе выдаёт ошибку. Сделать резервную копию, если такой каталог уже существует. Временный ручной клиентский каталог удалить после перехода на Workshop, чтобы он не маскировал проверку автоматической доставки.

Для обычного подключения пользователей владелец публикует **собственный** Workshop item с этим UI и добавляет выданный Steam ID в серверный список Workshop. Публикация и её доступность — отдельное действие; фиктивного ID в проекте нет. Чистый клиент должен скачать bundle при подключении. Дальше включить конфигурацию и пройти игровую приёмку по `docs/UI_VERIFICATION.md`.

## Состояние на 01.11.2026

Unity Editor 2022.3.62f3 установлен, владелец активировал Personal. Реальный экспорт прошёл: редактор открыл полученный bundle и проверил prefab `Effect` и имена элементов. После первого игрового скриншота исправлено перекрытие штатных полос и добавлено имя магазина; установленный bundle имеет SHA-256 `6d1425d0c2e92d04e33dc366d4ce729f6a7e57caf4e4098a53742ace32fc5210`. Одинаковые копии размещены на локальном сервере и локальном Windows-клиенте. Первоначальную ошибку legacy/core устранил `Exclude_From_Master_Bundle`; первый вход и показ HUD подтверждены, обновлённое расположение ещё ожидает визуальной проверки. Локальный тестовый конфиг `AssetsVerified=true`, `HideNativeHud=false`. Доставка другим клиентам через Workshop пока не настроена.

## Первичные источники

Проверены 30.10.2026:

- [SDG: Getting Started](https://docs.smartlydressedgames.com/en/stable/about/getting-started.html) — рекомендуемый Unity 2022.3.62f3, необходимость Editor для экспорта.
- [SDG: Asset Definitions](https://github.com/SmartlyDressedGames/Unturned-Docs/blob/latest/assets/asset-definitions.rst) — ID/GUID и legacy `.unity3d`, Asset_Bundle_Version 6.
- [SDG SDK: EffectAsset](https://github.com/SmartlyDressedGames/U3-SDK/blob/main/Assets/Runtime/Assembly-CSharp/Unturned/Bundles/EffectAsset.cs) — prefab загружается под именем `Effect`.
- [SDG SDK: EffectManager](https://github.com/SmartlyDressedGames/U3-SDK/blob/main/Assets/Runtime/Assembly-CSharp/Unturned/Managers/EffectManager.cs) — effect, text, visibility и форматирование горячих клавиш.

## Исправление HUD по игровым снимкам, 01.11.2026

Верхняя карточка PEI/PVP, онлайна и группы удалена; `CombatPanel` и `RaidPanel` сохранены. При `HideNativeHud=true` показатели находятся на y=120, при `false` — на y=260, над штатными полосами. Поле здоровья увеличено до 103×58; Unity TextGenerator проверил значения от 0 до 100. Вероятная причина отсутствия прежнего числа — обрезание текста, однако по снимку она не установлена окончательно.

Одинаковый bundle установлен на локальный сервер и клиент вместе с новой DLL; локальные настройки — `AssetsVerified=true`, `HideNativeHud=true`. Восстановление штатных флагов при ошибке, смерти и выгрузке предусмотрено кодом, а проверка в игре ещё требуется. Автоматическая доставка bundle другим клиентам пока не настроена.

Название установленного магазина показывается отдельно от вместимости. Панель оружия находится на y=28 при `HideNativeHud=true` и на y=380 при `false`; данные синхронизируются в обеих раскладках.
