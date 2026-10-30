# Установленная локальная бета MRE

Состояние: **30.10.2026**. Это отчет о фактической установке и проверках; игровой сервер еще не принят для публичного платного запуска.

## Состав

| Компонент | Установлено и подтверждено |
|---|---|
| U3DS | Официальный SteamCMD app 1110390, build 25613089; SteamCMD завершил проверку файлов |
| Мир | PEI, Easy, PvP, MAX4, 24 слота |
| Режим | Закрытая локальная бета `+LanServer/MRE`, пароль в `secrets/mre.local.json` |
| Фреймворк | LDM из поставки U3DS |
| HOME | MoreHomes 1.10.3, Harmony 2.2.2, 1/2/3 кровати для default/vip/elite |
| TPA | Собственный MIT-форк Teleportation MRE 1.8.3.2 |
| Голосование и обслуживание | Собственный MIT MreEssentials 0.1.0 |
| Workshop | Оригинальные More Farming 1232252658 и William's Vehicles 3485061773 |

Наборы, денежная экономика, VAULT, платные права и GameStores-модуль сейчас не включены. Группы `supporter/vip/elite` подготовлены без участников и без админских полномочий; факт существования группы не означает работающую продажу или истечение срока.

HOME/TPA настроены на задержку 15 секунд и cooldown 300 секунд. Combat — 60, raid — 180 секунд в пределах возможностей Teleportation. Платные роли не обходят эти ограничения. Точный объем исправлений — в [README форка](../src/Teleportation.MRE/README.md).

## Объем

После загрузки обоих модов каталог `runtime/server` занимал **2 059 799 987 байт**, примерно **2,06 ГБ / 1,92 ГиБ**. Это измерение без SteamCMD, исходников и отдельных резервных копий; логи и мир продолжат расти. Чистый depot U3DS: 1 863 680 749 байт; загрузка Steam — 1 219 217 984 байта.

More Farming: 189 097 784 байта; William's Vehicles: 4 452 282 байта. Сервер добавил 667 и 129 mod-assets. Регистрация assets не доказывает работу каждого рецепта и транспорта.

Для обновлений и истории копий оставляем запас 20–30 ГБ. Игровые файлы и Workshop не загружаются в GitHub.

## Установка с нуля

Нужны Windows x64, Windows PowerShell 5.1 и установленный .NET SDK для компиляции собственных плагинов. Русские PowerShell-скрипты сохранены UTF-8 с BOM.

```powershell
.\scripts\Install-MreServer.ps1 -Validate
.\scripts\Initialize-MreServer.ps1
.\src\Teleportation.MRE\Build.ps1
.\scripts\Install-MrePlugins.ps1 -Apply
.\scripts\Build-MreEssentials.ps1
Copy-Item .\runtime\builds\MreEssentials\MreEssentials.dll .\runtime\server\Servers\MRE\Rocket\Plugins\
.\scripts\Start-MreServer.ps1 -WaitForReady -TimeoutSeconds 240
.\scripts\Stop-MreServer.ps1
.\scripts\Set-MreWorkshop.ps1 -Apply -IncludeVehicles
.\scripts\Start-MreServer.ps1 -WaitForReady -TimeoutSeconds 240
```

Первый запуск создает WorkshopDownloadConfig.json. Установщик плагинов проверяет закрепленные SHA256 и лицензии, сборщик Teleportation создает исправленную DLL. Версии и хеши — в [plugins.lock.json](../plugins.lock.json), снимок сборки — в [release-manifest.csv](../release-manifest.csv).

`Initialize-MreServer.ps1` по умолчанию не заменяет существующие Commands.dat, Config.txt и права. Для применения базовых шаблонов использовать `-UpdateConfiguration` только после остановки и копии; прежние файлы сохраняются рядом. Существующие группы и участники не заменяются. Настройки плагинов сохраняются по умолчанию; их обновление требует `Install-MrePlugins.ps1 -Apply -UpdateConfiguration`.

## Подключение и команды

На этом компьютере: IP **127.0.0.1**, порт **27015**. Пароль — свойство `JoinPassword` локального `secrets/mre.local.json`. Для друзей текущий Server Code находится в `runtime/server/Logs/Server_MRE.log`. Без GSLT код меняется после перезапуска; доступ из чужой сети еще не проверен.

Игровые команды: `/home`, `/homes`, `/renamehome`, `/destroyhome`, `/tpa`, `/vote day`, `/vote yes`, `/vote status`, `/cv`. HOME привязан к кроватям, а не к произвольной точке `/sethome`.

Голосование: только ночью, минимум два игрока, 60% от зафиксированного состава участников, длительность 60 секунд, cooldown 300 секунд. Право — `mre.vote`. Это собственная реализация MRE; точное поведение REDLINE `/vote day` не подтверждено.

```powershell
.\scripts\Test-MreServer.ps1
.\scripts\Send-MreServerCommand.ps1 -Command Diagnostics
.\scripts\Send-MreServerCommand.ps1 -Command Save
.\scripts\Stop-MreServer.ps1
.\scripts\Backup-MreServer.ps1 -Name MRE-before-change
```

Диагностика — `runtime/control/MRE/diagnostics.json`. Локальный мост принимает фиксированный список команд, не слушает сетевой порт и защищен NTFS-правами владельца Windows, SYSTEM и администраторов. Игрок не получает доступ к нему через чат. RCON LDM отключен.

## Выполненные проверки

| Проверка | Результат |
|---|---|
| Официальный U3DS | SteamCMD `Success! App '1110390' fully installed` |
| Повторные запуски | PEI загружалась до `Loading level: 100%` без Workshop, с More Farming, затем с обоими модами |
| Сохранение мира | После рестарта загружены 16 сохраненных машин и счетчик дней |
| A2S_INFO | Получены PEI, 24 слота, пароль; это сетевой запрос, не вход клиента |
| Runtime-настройки | EASY, PEI, max_players=24, group_cap=4 |
| MoreHomes | Найдены три Harmony-патча нужного владельца и шестипараметрический private API кроватей |
| Управление | Save/Diagnostics/Players/Day/Night подтверждены; Shutdown завершил процесс с сохранением |
| Автосохранение | В журнале длительного запуска есть успешные сохранения MreEssentials с интервалом около 300 секунд |
| Повторная настройка | Хеши Commands.dat, Config.txt и Permissions.config.xml остались прежними |
| Реальная копия | Создан и проверен ZIP остановленного экземпляра, 49 файлов до добавления Workshop |
| Реальное восстановление | Предпросмотр и применение прошли; файл-проба после даты копии исчез из восстановленного экземпляра, прежний каталог сохранен отдельно, мир повторно запущен |
| Изолированные проверки | MreEssentials — 42, Teleportation MRE — 20; проверка кода с подменами API, не игра двумя клиентами |

На старте Harmony пишет `Unable to resolve type "HarmonySharedState" for`, после чего регистрирует три нужных патча. Диагностика подтвердила регистрацию; действие при работе игрока с кроватью остается отдельным тестом.

В локальном A2S-ответе VAC=false и строка версии 1.0.0.0. Это не приемка защищенного публичного сервера и не номер установленного depot. Номер сборки взят из appmanifest Steam. Публичный Internet-режим, GSLT, античит и внешний вход проверяются отдельно.

## Что не проверено и не подключено

Чистый клиент, два настоящих игрока, пятый участник группы, кровати, PvP/рейд в Unity, голосование в игре, рецепты, спавн транспорта и нагрузка 12/24. VAULT отсутствует. Существующий raid block не является общей защитой всех хранилищ и не охватывает радиус/защищающихся.

Автодонат: подтверждены официальные команды и схема конфигурации, но магазина владельца, ключей и проверенных платежей нет. Порядок — [AUTODONATE.md](AUTODONATE.md). Хостинг не куплен; деньги не списывались.
