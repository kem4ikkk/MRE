# Контракт MreInterface для отдельного UI

Версия сборки: `MreInterface 0.2.1.0`, пространство имён `MreInterface`. Это серверный API; клиентские сообщения никогда не определяют, от чьего имени выполняется действие. UI должен получать `UnturnedPlayer` из серверного события нажатия, а не из переданного SteamID.

## Доступ к плагину

`MreInterfacePlugin.Instance` равен `null` до загрузки и после выгрузки. Все методы вызываются на главном игровом потоке. При несовпадении потока API выбрасывает `InvalidOperationException`. Подписки UI должны сниматься при выгрузке UI-плагина; отсутствие UI не мешает серверной команде `/admin`.

| Метод | Результат |
|---|---|
| `HasAdminPermission(UnturnedPlayer actor, string permission)` | Подключён ли actor и имеет ли право |
| `CanExecuteAdmin(UnturnedPlayer actor, string action)` | Проверка `mre.admin` и `mre.admin.<action>` |
| `ExecuteAdmin(UnturnedPlayer actor, string action, ulong targetId = 0)` | Повторная проверка прав и актуальной цели, выполнение; русская строка результата |
| `GetRecentKills()` | Массив до 5 записей, новые первыми |
| `GetRecentDamage(UnturnedPlayer viewer)` | До 4 нанесённых viewer ударов, новые первыми; чужой SteamID этот метод не принимает |

Допустимые действия перечислены в `AdminPolicy.Actions`, включая `players`, `tp`, `bring`, `heal`, `kick`, `save`, `day`, `night`, `fly`, `freecam`, `spectate`, `god`, `vanish`, `remove`, `owner`, `workzone`, `stats`, `door`, `storage`, `freeze`, `kill`. Для `tp/bring/kick`, `spectate`, `freeze`, `kill` нужна цель; `heal` с targetId=0 лечит actor. Деструктивные `remove` и `kill` сначала создают предпросмотр: UI использует `PreviewRemoval`/`ConfirmRemoval` или `PreviewKill`/`ConfirmKill`, передавая одноразовый токен только от того же actor. Нативный запрет телепортации возвращается как отказ. Права проверяются при каждом исполнении, даже если UI открылся раньше изменения прав. Произвольных команд консоли и назначения полномочий здесь нет.

`event Func<UnturnedPlayer,bool> AdminMenuRequested`: после авторизации `/admin` без аргументов вызывает подписчиков на игровом потоке. Подписчик возвращает `true`, если панель открыта; при отсутствии успешного подписчика выводится текстовая справка. Исключение одного подписчика не мешает другим. Отписывайте обработчик при выгрузке UI. `/admin help` всегда выводит текст. При отправке кнопок actor извлекается из серверного callback, а цель определяется отдельно из выбранного SteamID.

Дополнительные методы: `RunBodyFlight`, `SetFreecamPermission`, `StartObservation`, `StopObservation`, `SetStaffMode`, `SetWorkzonePermission`, `SetStatsPermission`, `ToggleAimedDoor`, `OpenAimedStorage`, `SetFreeze`. Точный синтаксис и ограничения — в [INTERFACE.md](INTERFACE.md). Наблюдение следует телом рядом с игроком и не является видом из его глаз; открытый storage — настоящий контейнер, изменение его содержимого реально.

`InspectOwnership(actor)` отдельно проверяет `mre.admin.owner` и возвращает `OwnershipInfo`: Kind, AssetId, InstanceId, OwnerId, GroupId, IsBed, BedClaimantId. Цель — наземная постройка по прицелу на расстоянии до 6 м; транспортные объекты не поддерживаются. Для кровати BedClaimantId — владелец привязки, который может отличаться от владельца размещения. Возврат `null` означает, что подходящая цель не найдена.

## Модерация, инвентарь и полёт

`MreModeration.MreModerationPlugin.Instance` предоставляет `GetStaffActions`, `GetProfile`, `GetMyTrustscore`, `SearchProfiles`, `SearchHistory`, `SearchMessages`, `ApplyModeration`, `RevokeRecord`, `RevokePunishment` и события `ProfileChanged`, `StaffActionRecorded`. Методы выполняются на главном потоке и проверяют actor и права заново. Фильтры/пагинация возвращают не более 50 строк; operationId для повторяемого действия создаётся один раз на стороне UI и передаётся без изменения при повторной отправке. Состав DTO и настройки — в [MODERATION.md](MODERATION.md).

`MreInventoryAudit.MreInventoryAuditPlugin.Instance`: `Inspect`, `GetDeaths`, `PrepareRestore`, `ConfirmRestore`, события `DeathCaptured`, `InventoryRestored`; точные поля и порядок подтверждения — в [INVENTORY_AUDIT.md](INVENTORY_AUDIT.md). UI не выполняет выдачу сам. `MreFlight.MreFlightPlugin.Instance` предоставляет `SetFlight`, `IsFlying`, `SetSpeed`, `GetSpeed`; происхождение и управление — в [FLIGHT.md](FLIGHT.md).

## События

`event Action<CombatRecord> KillRecorded` — смерть игрока. `event Action<CombatRecord> DamageRecorded` — подтверждённый урон игроку. Обработчики выполняются на игровом потоке; исключение одного подписчика записывается в журнал и не отменяет остальные подписки. Обработчик должен быстро обновить состояние HUD. Персональный индикатор урона отправляется только игроку с `AttackerId` из записи; не следует рассылать историю всем игрокам.

Поля `CombatRecord` доступны для чтения, setter имеет `internal`:

| Поле | Тип/значение |
|---|---|
| `AtUtc` | `DateTime`, UTC; перевод в Europe/Moscow делает UI при необходимости |
| `AttackerId`, `VictimId` | `ulong`, SteamID64; 0/системный источник возможен у окружения |
| `AttackerName`, `VictimName` | Имена в момент события; экранировать rich text при выводе |
| `Damage` | `byte`, фактически снятые HP, ограничены прежним запасом; для KillRecorded равно 0 |
| `Cause`, `Limb` | Строковые имена нативных `EDeathCause` и `ELimb` |
| `WeaponId` | `ushort`, экипированный предмет атакующего; 0 при отсутствии/окружении |
| `IsKill` | `true` только у события смерти |

Ограничения расчёта и перечень непокрытых целей приведены в [INTERFACE.md](INTERFACE.md). Один смертельный удар может породить оба события; это разные данные, их не надо суммировать. При выгрузке/перезапуске история теряется. События не являются журналом экономики или основанием для начисления наград.

## TPA и HOME

TPA предоставляется отдельным `Teleportation.dll` (пространство имён `RestoreMonarchy.Teleportation`). Его актуальные публичные события необходимо сверить с исходниками `src/Teleportation.MRE/Plugin`, потому что отдельная UI-сессия параллельно расширяет контракт. MreInterface не перехватывает Б/Ю и не отправляет эффекты TPA. HOME обслуживает MoreHomes.
