# Диагностика SaveManager в TEMP

Это test-only Rocket plugin. Он не меняет объекты мира и не делает decay/repair. Если все guard checks пройдены, plugin один раз подписывается на `SaveManager.onPreSave` и `onPostSave`, фиксирует hash `Structures.dat` и `Barricades.dat` до и после callback-ов, вызывает штатную команду `save`, записывает возврат команды и через 5 секунд запрашивает штатный `shutdown`. Каждая запись содержит номер, `Stopwatch.GetTimestamp()`, managed thread ID, фазу и hash файлов. Marker-файл flush-ится на каждом событии.

## Подтверждённые API

Reflection по `Assembly-CSharp.dll` из TEMP U3DS build `26491864` показал:

- `SDG.Unturned.SaveManager.onPreSave` и `onPostSave` — поля типа `SDG.Unturned.SaveHandler`;
- `SaveHandler.Invoke()` имеет сигнатуру `void Invoke()`;
- `SaveManager.save()` имеет сигнатуру `void save()`;
- `Commander.execute(Steamworks.CSteamID, string)` имеет сигнатуру `bool`.

Это подтверждает наличие API у этой сборки, но не устанавливает порядок записи мира относительно callbacks, их поток, долговечность файлов или транзакционность.

## Сборка

`ServerRoot` должен указывать на отдельную U3DS копию внутри TEMP с `steamapps/appmanifest_1110390.acf`, App ID `1110390`, `StateFlags=4`, `UpdateResult=0` и build ID `26491864`. Скрипт сверяет hash executable и игровых/Rocket DLL, компилирует против этих DLL и пишет результат только в новый `WorkDirectory` внутри TEMP. Существующий каталог, путь вне TEMP, reparse point, иной build или неполная установка приводят к отказу.

```powershell
$server = Join-Path $env:TEMP 'mre-decay-u3ds-current-...\u3ds'
$build = Join-Path $env:TEMP ('mre-decay-save-hook-build-' + [guid]::NewGuid().ToString('N'))
powershell -NoProfile -ExecutionPolicy Bypass -File tests/MreDecaySaveHookFixture/Build.ps1 -ServerRoot $server -WorkDirectory $build
```

Результат: `$build/package/Rocket/Plugins/MreDecaySaveHookFixture/MreDecaySaveHookFixture.dll` и `fixture-manifest.txt`. Скрипт только собирает пакет; он не устанавливает DLL, не подготавливает/не запускает сервер и не пишет в `runtime/server`.

## Guard contract для будущего TEMP-прогона

Plugin выключен по умолчанию. Для диагностического прогона конфигурация должна содержать `Enabled=true`, `ServerId=MREDecaySaveHookFixture` и случайный непустой GUID `RunToken` в формате `N`; запуск требует, чтобы env `MRE_DECAY_SAVE_HOOK_FIXTURE` точно совпадал с этим token. Plugin также требует stage receipt с этим token, ID и build `26491864`, совпадающий `Provider.serverID`, executable и `Servers/MREDecaySaveHookFixture/Server` под TEMP, загруженный сервер, подтверждённо пустой `Provider.clients`, полное read-only наблюдение без построек и устройств, отсутствие прежнего marker-файла и `Level.isLoaded`. Во время штатной остановки callback hooks остаются подключены до `Unload`; при таймауте они снимаются.

**Запуск не подготовлен и не выполнялся.** Отдельный TEMP staging script должен создать новый instance, записать новый одноразовый token в конфигурацию и stage receipt, проверить свободные порты, process paths и пустой мир. Этого staging script пока нет. Нельзя отключать process/path guard или устанавливать DLL в основной `runtime/server`.

## Снимок после штатной остановки

После завершения процесса отдельный postprocessor сверяет TEMP build manifest и hashes девяти ключевых DLL с серверной копией, проверяет отсутствие процесса этого экземпляра, логовый marker `Quit game: server shutdown`, маркеры ручной команды и наличие обеих save-файлов. Он записывает итоговые hashes только в новый report directory внутри TEMP:

```powershell
$report = Join-Path $env:TEMP ('mre-decay-save-hook-report-' + [guid]::NewGuid().ToString('N'))
powershell -NoProfile -ExecutionPolicy Bypass -File tests/MreDecaySaveHookFixture/Collect-AfterShutdown.ps1 -ServerRoot $serverInstance -ServerLogPath $capturedServerLog -BuildManifestPath (Join-Path $build 'package/fixture-manifest.txt') -ReportDirectory $report
```

Отчёт содержит счётчики callback-ов и hash файлов после штатного shutdown. Hash-значения внутри callback-ов — только наблюдения. Финальный snapshot не доказывает crash durability, atomicity, rollback resistance, согласование plugin-state с миром или production recovery.

## Текущее состояние

API-сигнатуры прочитаны по TEMP build `26491864`; PowerShell parser и compile-only build против точных TEMP DLL прошли без предупреждений/ошибок. Сервер не запускался. Следующий шаг — независимое ревью Sol 6.1 и отдельный guarded TEMP staging script. Настоящий save-hook прогон зависит от безопасного чтения process paths и получения пустого изолированного мира.
