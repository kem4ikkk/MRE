# MoreHomes MRE 1.10.3.1

Минимальный MIT-форк [Restore Monarchy MoreHomes 1.10.3](https://github.com/RestoreMonarchyPlugins/MoreHomes/tree/d57770a30fc9705b1360eb3adc966c4722eaf40c), commit `d57770a30fc9705b1360eb3adc966c4722eaf40c`. Исходная лицензия и авторство сохранены в [LICENSE](LICENSE). Это изменение MRE, не официальный выпуск Restore Monarchy.

`/home [имя]` запускает пятисекундное перемещение к выбранной собственной кровати и показывает общую с TPA карточку. Двигаться разрешено. Ю (стандартный Plugin Key 1) или `/home cancel` отменяют HOME; подтверждение через Б для HOME не требуется. Если игрок переназначил Plugin Keys в Unturned, действуют его назначения.

Отслеживаемый `HomeTeleportRequest` находится в `Teleportation.MRE`: HUD читает его через `TeleportationPlugin.GetHomeUiRequest(CSteamID)`, а изменения приходят через общий `RequestStateChanged`. HOME имеет приоритет над непринятыми TPA. Одновременно исполняемый HOME и TPA с участием одного игрока запрещены.

Перед началом и непосредственно перед переносом проверяются живой актуальный объект игрока, смерть, combat/raid, активность и владелец **той же зарегистрированной кровати**, транспорт и запрет подземной точки. Точка берётся из актуального `home.LivePosition`, перенос использует безопасный `teleportToLocation`. Если Teleportation отсутствует, HOME не запускается. Отключение, смерть, выгрузка MoreHomes/Teleportation, отмена и исключение API очищают состояние HOME.

Базовый cooldown HOME — **80 секунд**. Он создаётся только после успешной постановки HOME в очередь. Отмена или ошибка его не возвращают; это ограничение MRE против повторного спама. `/tpa cancel` относится только к TPA и не затрагивает HOME/cooldown. Пять секунд одинаковы для всех HOME: устаревшие `DefaultHomeDelay`, `VIPDelays` и `CancelOnMove` сохраняются в конфигурационном классе ради совместимости, но не меняют фиксированный lifecycle MRE. Лимиты домов и HOME cooldown продолжают задаваться прежними настройками и правами; примерные ускорения VIP из upstream убраны из defaults.

Формат `MoreHomesData.json`, storage, `DataService`, модели домов и Harmony patches кроватей сохранены из указанного commit. Файл данных не входит в репозиторий. Имя сборки остаётся **MoreHomes**, версия — **1.10.3.1**. Замена плагина на работающем мире выполняется координатором после согласованной резервной копии и остановки; этот исходник сам не изменяет мир.

## Сборка

```powershell
.\src\Teleportation.MRE\Build.ps1
.\src\MoreHomes.MRE\Build.ps1
```

Нужны установленный .NET SDK/Roslyn, локальные U3DS/Mono/LDM DLL, `Newtonsoft.Json.dll` из U3DS и `0Harmony.dll` из уже проверенного upstream MoreHomes cache. Build не скачивает пакеты и не меняет серверные плагины. Результат: `runtime/plugin-builds/MoreHomes.MRE/MoreHomes.dll` и `build.json` с `patch_id=mre-home-ui-v1`, версией, commit и hashes исходников/зависимостей. Для независимой проверки доступны `-TeleportationAssembly`, `-ReferenceRoot` и `-OutputDirectory` внутри `runtime`.

30.10.2026 сборка по установленным локальным DLL завершилась успешно. Поведенческие результаты и невыполненные сценарии с настоящими кроватями/клиентами записываются в [UI_VERIFICATION.md](../../docs/UI_VERIFICATION.md). Компиляция и harness не подтверждают фактическую отрисовку bundle, безопасность точки кровати в реальной геометрии или сохранение мира после замены плагина.
