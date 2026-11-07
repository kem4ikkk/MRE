# Fixture постоянной идентичности построек

Это изолированный тестовый Rocket-плагин. Он создаёт в пустом мире `MREDecayTest` две соседние берёзовые конструкции и два MRE Safe100, сохраняет мир и останавливает сервер. При втором запуске сверяет `Kind:InstanceId`, asset, владельца и координаты всех четырёх объектов, удаляет старое поколение и сохраняет мир. При третьем запуске требует пустой мир, повторно ставит те же assets в те же позиции и завершается ошибкой, если любой `Kind:InstanceId` совпал со старым.

Плагин находит сейф по GUID `bf233036b60d403c836deb8ac0f03292`. Поэтому тестовая карта должна содержать исходный asset, его `.dat` и Unity bundle. `Prepare.ps1` требует оба пути и сам вызывает `scripts/Build-MreDecaySafeTestMap.ps1`, затем копирует его `MREDecayTest` в новый TEMP экземпляр U3DS. Подготовленный `Commands.dat` выбирает `Map MREDecayTest`. Скрипт проверяет ID/GUID/type Safe100 и сверяет SHA-256 Bundle при подготовке и перед запуском.

## Подготовка и запуск

Сначала собери временную коллекцию из собственных MRE ассетов:

```powershell
$package = Join-Path $env:TEMP ('mre-decay-client-package-' + [guid]::NewGuid().ToString('N'))
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-MreDecaySafeClientPackage.ps1 -WorkDirectory $package
$collection = Join-Path $package 'collection'
```

`MapSource` должен указывать на отдельную распакованную карту с `Level.dat` и без `Bundles`. Укажи полный путь:

```powershell
$fixture = Join-Path $env:TEMP ('mre-decay-ground-identity-' + [guid]::NewGuid().ToString('N'))
$mapSource = 'D:\путь\к\распакованной\карте'
powershell -NoProfile -ExecutionPolicy Bypass -File tests/MreDecayIdentityFixture/Prepare.ps1 -Collection $collection -MapSource $mapSource -WorkDirectory $fixture
powershell -NoProfile -ExecutionPolicy Bypass -File tests/MreDecayIdentityFixture/Run.ps1 -WorkDirectory $fixture
```

Если запуск завершился после успешно сохранённого удаления и штатного выключения второй фазы, продолжи только третью фазу:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/MreDecayIdentityFixture/Run.ps1 -WorkDirectory $fixture -ResumeRecreate
```

`-ResumeRecreate` требует исходный `ground-identity-state.txt`, отсутствие `ground-identity-state.txt.verified` и оба сохранённых лога предыдущих фаз. Лог `01-create-server.log` должен подтверждать `CREATE_PASS`, `CREATE_SAVE_REQUESTED` и штатный `Quit game: server shutdown`; `02-reload-delete-server.log` — `RELOAD_ID_PASS`, `DELETE_SAVE_REQUESTED` и штатное выключение. В обоих логах не должно быть `FAILED`/`REFUSED`. Режим сохраняет проверки путей, процессов, портов, хешей и карты, пропуская create/reload. Сам плагин в третьей фазе всё равно проверяет пустой мир и требует новые `Kind:InstanceId`, отличные от удалённого поколения.

`Prepare.ps1` собирает карту и плагин только в новом каталоге TEMP. `Run.ps1` запускает только скопированный экземпляр, предварительно проверяет process paths, свободные UDP-порты, manifest и отсутствие reparse points. При ошибке чтения списка процессов, неизвестном `ExecutablePath`, занятом порте или недоступной проверке пути запуск прекращается. Скрипты не устанавливают файлы в `runtime/server` и не используют рабочий мир.

Compile-only проверка плагина:

```powershell
dotnet build tests/MreDecayIdentityFixture/MreDecayIdentityFixture.csproj --configuration Release
```

Проверенный обычный `save`/restart доказывает лишь поведение сохранённого ID на этом цикле и не доказывает crash durability, транзакционное восстановление, защиту от отката или готовность production decay backend.

## Текущее состояние

08.11.2026 fixture запущена из свежей TEMP U3DS-копии Steam build `26491864`; SHA-256 игровых и Rocket DLL совпали с проектным runtime. Первый запуск создал две Structure и два MRE Safe100 с уникальными `Kind:InstanceId`, сохранил мир и штатно выключился (`CREATE_PASS`). После перезапуска все четыре записи совпали по ID, asset, владельцу, группе, координатам и здоровью (`RELOAD_ID_PASS`); исходное поколение удалено, сохранение и штатное выключение подтверждены. После временного fail-closed отказа из-за постороннего процесса запуск продолжен флагом `-ResumeRecreate`, который требует подтверждённые логи первых двух фаз.

Третья фаза подтвердила пустой мир и новое поколение: старые IDs были `structure:1,2` и `barricade:1,2`, новые — `structure:3,4` и `barricade:3,4` (`DELETE_RECREATE_PASS`). Все три сервера завершились штатно; `.verified` receipt создан. Логи и контрольные суммы оставлены только в TEMP-каталоге теста. Новое поколение после третьего сохранения повторно не загружалось. Проверялись только четыре объекта на конкретной сборке U3DS, без содержимого хранилищ или массовой нагрузки. Результат подтверждает обычный save/restart и смену поколения ID, но не crash durability, checkpoint anchor/reconcile, rollback resistance, транзакционное восстановление или готовность production decay backend.
