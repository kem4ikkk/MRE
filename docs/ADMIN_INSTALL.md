# Установка MRE AdminTools и AdminMenu

## Состав и текущий статус

`MreAdminMenu` — внутриигровая панель административных действий. Launcher доступен по `mre.admin` или `mre.staff`; вкладки и кнопки фильтруются по отдельным permission nodes, а typed API каждого сервиса повторно проверяет actor и права. Идентификаторы клиентского Effect: ID `48626`, key `14826`, GUID `bc15863fb9c24a69a36bb733456cc3b2`.

Ролевое распределение staff-команд приведено в [матрице групп](STAFF_GROUPS.md). Nodes `/give` и `/veh` назначены только `head_admin`; единственная разрешённая дочерняя группа — `mre_owner`. Установщик проверяет уникальность group IDs без учёта регистра, `DefaultGroup=default`, wildcard permissions и отсутствие head-only nodes в других группах. Сотрудников добавляют в Rocket-группы, не в native Admin.

Серверные DLL собираются проектными build scripts. Unity bundle пока не собран: Unity остановилась с `No valid Unity Editor license found`. Preview установщика показывает имеющиеся DLL/manifest, проверяет текущие XML и печатает JSON план, не создавая и не меняя файлы.

## Preview

Из корня репозитория запустите:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/mre-admin/Install-MreAdmin.ps1
```

Preview ожидает увидеть карту `runtime/server/Servers/MRE/Rocket/Commands.config.xml` и Rocket permissions того же экземпляра. Для текущей карты он намерен отключить ровно native mapping `Rocket.Unturned.Commands.UnturnedCommands/give`, добавить/включить High mappings `MreAdminTools.CommandGive/give` и `MreAdminTools.CommandVehicle/veh`, оставить `/vehicle` без изменений и закрепить `mre.admin.give`/`mre.admin.veh` только за `head_admin`. Включённый неизвестный mapping команды `/give` или конфликт `/veh` прекращает preview с ошибкой.

Для Apply требуются семь DLL и manifests в `runtime/builds/`: `MreAdminTools`, `MreAdminMenu`, `MreInterface`, `MreFlight`, `MreModeration`, `MreInventoryAudit`, `MreAirdrop`. Установщик сверяет DLL SHA256, SHA256 перечисленных исходников и шесть MRE references в manifest `MreAdminMenu`. Отсутствующий артефакт или несовпадение закрывает Apply.

## Условие Apply: клиентский Effect

`-Apply` сейчас должен завершаться отказом. Он разрешается только после того, как предоставлены bundle и `.dat`, согласован способ доставки клиентам и это зафиксировано в manifest. Формат manifest (положить рядом с файлами):

```json
{
  "effect_id": 48626,
  "effect_key": 14826,
  "guid": "bc15863fb9c24a69a36bb733456cc3b2",
  "bundle_file": "MreAdmin.bundle",
  "dat_file": "MreAdmin.dat",
  "bundle_sha256": "<64 hex chars>",
  "dat_sha256": "<64 hex chars>",
  "client_delivery": "<согласованный способ доставки>",
  "owner_approved": true
}
```

`owner_approved` означает, что владелец согласовал описанный способ клиентской доставки; это ручная отметка, а не цифровая подпись. Установщик проверяет ID/key/GUID, наличие файлов и SHA256, сохраняет Effect hashes и способ доставки в receipt. Сам Effect package установщик не размещает и не публикует: его путь интеграции должен быть отдельно согласован до Apply. Создавать этот manifest заранее или подставлять фиктивные значения нельзя.

После наличия согласованного manifest и всех семи build-артефактов проверьте preview с ним. Затем при остановленном сервере:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/mre-admin/Install-MreAdmin.ps1 -EffectManifest "D:\path\to\mre-admin-effect.json" -Apply
```

Скрипт требует эксклюзивный `runtime/.mre-maintenance.lock`, повторно проверяет остановку MRE и hashes семи staged DLL. В `runtime/backups/mre-admin-<UTC stamp>/before/` сохраняются прежние версии DLL и Rocket XML; рядом пишется `install-receipt.json`. При ошибке установки скрипт пытается восстановить каждый target и пишет `install-failure-recovery.json`; если компенсация неполна, оставляет snapshots для ручного восстановления. Установщик не меняет `/vehicle`, default/donor permissions и назначения членов групп.

`config/RocketPermissions.xml` — эталон проекта, а `config/plugins/permissions.json` — справочник, не загружаемый сервером автоматически. `scripts/Apply-MreSettings.ps1 -Apply` синхронизирует управляемые группы с эталоном, сохраняет их `<Members>` и перед изменением создаёт резервную копию. Назначенный владелец перемещается в `mre_owner`, наследует `head_admin` и отдельно остаётся native Owner по `Commands.dat`; этот статус обходит Rocket permissions. Текущую runtime-конфигурацию перед установкой нужно сверить с эталоном: preview сообщит об ошибке, если в ней нет `head_admin` и `mre_owner`.

## Откат

После остановки сервера используйте путь к receipt успешной установки:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/mre-admin/Rollback-MreAdmin.ps1 -Receipt "D:\path\to\runtime\backups\mre-admin-<stamp>\install-receipt.json"
```

Откат принимает receipt только из `runtime/backups`, требует остановленный сервер и maintenance lock, ограничивает пути целями внутри экземпляра MRE и проверяет after hashes и SHA256 резервных копий. Если целевой файл отсутствует или изменён, rollback прекращается без записи. При успехе восстанавливаются все семь DLL и Rocket XML. Перед восстановлением создаются snapshots; при ошибке скрипт пытается вернуть файлы и receipt к состоянию до отката.

## Осталось перед установкой

- Получить Unity bundle и `.dat` с проверенными идентификаторами.
- Согласовать и реально обеспечить доставку Effect клиентам; одного серверного plugin DLL недостаточно для UI.
- Собрать MreInterface, MreFlight, MreAdminTools, MreModeration, MreInventoryAudit, MreAirdrop и затем MreAdminMenu; проверить DLL, source и reference hashes.
- Выполнить preview, затем после всех условий выполнить Apply при выключенном MRE.
- После установки вручную проверить запуск панели, ролевые ограничения всех групп, `/give` и `/veh` только у Head Admin, неизменность `/vehicle`, выход/переподключение и корректное закрытие UI. Эти проверки не выполнялись установщиком.
