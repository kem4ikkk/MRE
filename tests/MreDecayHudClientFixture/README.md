# TEST-only: визуальная проверка HUD эффекта

Это отдельный Rocket diagnostic plugin для локальной карты `MREDecayTest`. Он проверяет получение и видимость тестового UI эффекта на двух клиентах. Он не вызывает decay runtime, `GameHintAdapter`, `HintPolicy` или источник зон и не сообщает реальное состояние постройки. Текст содержит явную отметку `ТЕСТ UI`.

Плагин продолжит работу только если одновременно выполнены условия:

- server ID равен `MREDecayHudVisualTest`;
- executable сервера находится внутри TEMP;
- задан process environment marker `MRE_DECAY_HUD_VISUAL_TEST=YES`;
- загружена карта, папка которой называется `MREDecayTest`;
- эффект ID `48623` разрешается в тот же asset с GUID `5d4628c25c73441a89ef5aed4db47713`;
- до подключения клиентов выполнена исходная проверка и в момент показа подключены ровно два клиента.

## Сборка test package

Сначала установи отдельную копию U3DS через SteamCMD в TEMP и подготовь в ней Rocket. Укажи именно её как `ServerRoot`: сборщик запишет в package Steam `buildid` и SHA-256 всех игровых и Rocket DLL, против которых собран fixture. Этот version manifest позже сверяется с серверным источником до копирования. Чтобы добавить собственный эффект и его map bundle, передай collection и исходник отдельной карты:

```powershell
$serverSource = Join-Path $env:TEMP 'mre-u3ds-source'
$packageWork = Join-Path $env:TEMP ('mre-decay-hud-package-' + [guid]::NewGuid().ToString('N'))
$collection = 'D:\путь\к\test-collection'
$mapSource = 'D:\путь\к\чистой-распакованной-карте'
powershell -NoProfile -ExecutionPolicy Bypass -File tests/MreDecayHudClientFixture/Build.ps1 `
  -ServerRoot $serverSource `
  -WorkDirectory $packageWork `
  -Collection $collection `
  -MapSource $mapSource
$package = Join-Path $packageWork 'package'
```

`Collection` можно подготовить существующими `scripts/Build-MreDecaySafeClientPackage.ps1` и Unity 2022.3.62f3. Скрипт карты откажется сливать файлы, если в исходном каталоге уже есть `Bundles`; результат компиляции/копирования останется в TEMP и ничего не устанавливает или запускает.

## Подготовка отдельного тестового сервера

Подготовь новый пустой путь внутри TEMP. Скрипт проверит источник и package на reparse points, Steam appmanifest (`appid=1110390`, `StateFlags=4`, `UpdateResult=0`, buildid; если задан `TargetBuildID`, он должен совпасть), точные SHA-256 девяти игровых/Rocket файлов, точное соответствие набора map-файлов SHA-256 manifest, process paths и UDP порт. Он скопирует серверные файлы, исключив `Servers`, `Logs`, `Maps`, `Sandbox`, `Cloud`, `steamapps`, `appcache`, `config`, `userdata`, затем установит только карту `MREDecayTest` и этот test-only plugin. Сервер останется выключенным.

```powershell
$serverSource = Join-Path $env:TEMP 'mre-u3ds-source'
$serverWork = Join-Path $env:TEMP ('mre-decay-hud-server-' + [guid]::NewGuid().ToString('N'))
powershell -NoProfile -ExecutionPolicy Bypass -File tests/MreDecayHudClientFixture/Prepare-Server.ps1 `
  -ServerRoot $serverSource `
  -PackageDirectory $package `
  -WorkDirectory $serverWork `
  -Port 28195
```

Подготовленный экземпляр находится в `$serverWork/instance`; receipt `prepare-receipt.txt` и `server-SHA256SUMS.txt` фиксируют его TEST_ONLY статус и хеши. Этот скрипт только готовит изолированные файлы в TEMP и не запускает сервер.

## Изолированный двухклиентский сценарий

Состояние на 08.11.2026: package против build `26491864` успешно собран, но `Prepare-Server.ps1` отказал до копирования, потому что process enumeration обнаружил `Unturned.exe` PID `15288` с недоступным `ExecutablePath`. Серверная копия не создана и не запущена; клиентская отрисовка не проверена. Не отключай этот guard. Повтори подготовку из PowerShell с доступом к путям процессов, когда все пути `Unturned.exe` будут доступны.

1. Создать отдельную TEMP-копию U3DS с отдельным миром, портом и server ID `MREDecayHudVisualTest`. В её `Commands.dat` выбрать карту `MREDecayTest`.
2. Заранее поместить одинаковую папку `MREDecayTest` с одинаковыми файлами `Bundles` из package и на этот сервер, и в `Maps` каждого из двух тестовых клиентов. Сверить `MREDecayTest-SHA256SUMS.txt`. Это локальное предварительное копирование, не автоматическая доставка клиентам и не Workshop публикация.
3. Скопировать `package/Rocket/Plugins/MreDecayHudClientFixture/MreDecayHudClientFixture.dll` только в каталог плагинов этой TEMP-копии. Не класть DLL или карту в рабочий runtime/server либо основной клиент.
4. Запустить только отдельный процесс с `MRE_DECAY_HUD_VISUAL_TEST=YES`. Подключить ровно двух клиентов к нему. Скрипт покажет текст `ТЕСТ UI: эффект загружен` на 12 секунд и затем очистит эффект; серверный лог должен содержать `TEST_EFFECT_SENT_TO_TWO_CLIENTS` и `TEST_EFFECT_CLEARED`.
5. На обоих клиентах визуально подтвердить строку снизу, её читаемость/позицию, отсутствие её на обычном HUD и скрытие через 12 секунд. Сохранить версии, хеши и скриншоты, без SteamID. Это остаётся ручной игровой проверкой.

Fixture написан под C# 7.3 и не должен устанавливаться на основной сервер. Compile-only build подтверждает совместимость вызовов с локальными DLL, но не подтверждает client asset delivery или отрисовку. Пока два клиента не прошли шаг 5, gate G3 остаётся незакрытым. Успешная визуальная проверка тоже не включает production-гниение: для рабочего статуса отдельно нужны утверждённые зоны и готовый production source/owner.
