# Группы игроков и персонала MRE

Дата решения: 08.11.2026. Это проектная схема MRE. Она описывает желаемое распределение доступов и не подтверждает фактическую игровую приёмку.

## Игроки

| Группа | Состояние в проекте | Подтверждённые игровые права |
|---|---|---|
| Игрок (`default`) | Базовая Rocket-группа | Все базовые команды; до 2 сохранённых домов через `/home` |
| VIP (`vip`) | Есть в шаблоне | До 3 сохранённых домов через `/home` |
| Premium (`premium`) | Новый уровень; наследует VIP | До 4 сохранённых домов через `/home` |
| Elite (`elite`) | Есть в шаблоне | До 5 сохранённых домов через `/home` |

VIP → Premium → Elite — проектная лестница удобства: каждый уровень добавляет один сохранённый дом; задержка HOME, cooldown и блокировки боя/рейда одинаковы для всех. Группы — не подтверждение подключённых товаров или оплат. Не добавлять им административные, иммунитетные или обходящие PvP/raid block права. VIP/Premium/Elite не должны наследовать staff-группы.

## Персонал и разрешения

Иерархия: `helper` → `moderator` → `senior_moderator` → `mre_admin` → `head_admin` → `mre_owner`. В Rocket каждая следующая группа наследует права родительской. Сотрудника следует добавлять только в его старшую группу, а не дублировать во все группы ниже. `mre_owner` резервируется для владельца, указанного в локальных настройках.

| Уровень | Команды и действия, доступные по Rocket permissions |
|---|---|
| **Helper** | `/staff list`, `profile`, `history`, `note`, `warn`, `commend`; просмотр `/staff help` показывает разрешённые действия. Имеет только просмотр `/admin players` и не получает целевые административные действия. Требуются `mre.staff` и соответствующий `mre.staff.<action>`. |
| **Moderator** | Всё Helper, плюс `/staff kick` и `/staff mute`. |
| **Senior Moderator** | Всё Moderator, плюс `/staff ban`, `unban`, `unmute`, `revoke`, `logs`. |
| **Admin** | Всё Senior Moderator, плюс стандартные действия: `/admin players`, `tp`, `bring`, `heal`, `kick`, `save`, `day`, `night`, `fly`/`afly`/`aflyspeed`/`aflyvspeed`/`freecam`, `spectate`, `god`, `vanish`, `stats`, `freeze`, `owner`; `/inspectinventory`, `/previewdeath`. Высокорисковые и изменяющие объекты действия из колонки Head Admin закрыты. |
| **Head Admin** | Всё Admin, плюс `/give`, `/veh`, `/airdrop`, `/admin kill`, `/admin remove`, `/admin workzone`, `/admin door`, `/admin storage`, `/restoredeath`. Назначение — старшая staff-роль и управление составом вне игрового API. |
| **Владелец (`mre_owner`)** | Наследует весь набор Главного админа; собственных MRE nodes не добавляет. Назначается только владельцу сервера. Отдельный native `Owner` Unturned даёт встроенный обход Rocket permission checks. |

Распределение — начальная политика MRE, заданная в `config/RocketPermissions.xml`. Если node отсутствует в актуальном исходнике, его нельзя считать доступом только на основании этой таблицы. Команды, наказания и компенсации требуют проверки в игре; перечисление в документе не является результатом теста.

### Техническая карта nodes

Действия `/admin` и InventoryAudit требуют `mre.admin` и собственный node. Все staff-группы получают `mre.admin`, а также могут открыть панель через `mre.staff`; само открытие не даёт дополнительных прав. Панель показывает только вкладки и кнопки, разрешённые точными nodes, и повторно проверяет право при каждом callback. Staff-действия отдельно требуют `mre.staff` и `mre.staff.<action>`, без `mre.admin`. `MreAdminTools` дополнительно проверяет `mre.admin.give` или `mre.admin.veh`; `/airdrop` отдельно проверяет `mre.airdrop.admin`.

| Действие | Node |
|---|---|
| Игроки, телепорт, перенос к себе, лечение, исключение | `mre.admin.players`, `.tp`, `.bring`, `.heal`, `.kick` |
| Сохранение, день, ночь | `mre.admin.save`, `.day`, `.night` |
| Полёт/freecam, наблюдение | `mre.admin.fly`, `.spectate` |
| God, Vanish, Workzone, статистика | `mre.admin.god`, `.vanish`, `.workzone`, `.stats` |
| Дверь/контейнер по прицелу | `mre.admin.door`, `.storage` |
| Заморозка, убийство, удаление постройки, владелец | `mre.admin.freeze`, `.kill`, `.remove`, `.owner` |
| Объявление события | `mre.admin.announce` |
| Выдать предмет, вызвать транспорт | `mre.admin.give`, `mre.admin.veh` |
| Ручной аирдроп | `mre.airdrop.admin` |
| Осмотр инвентаря, история смертей, компенсация | `mre.inventory.inspect`, `mre.inventory.deaths`, `mre.inventory.restore` |
| Персонал: список, карточка, история, заметка, предупреждение, благодарность | `mre.staff` + `mre.staff.list/profile/history/note/warn/commend` |
| Персонал: kick, mute, ban, логи, отмена записи, unban, unmute | `mre.staff` + одноимённый `mre.staff.kick/mute/ban/logs/revoke/unban/unmute` |

Верхняя таблица распределяет эти nodes по ролям. `/give`, `/veh`, `/airdrop`, `/admin kill`, `/admin remove`, `/admin workzone`, `/admin door`, `/admin storage` и `/restoredeath` зарезервированы за Head Admin как высокорисковые или изменяющие мир действия. Компенсация `/restoredeath` не возвращает исходное состояние мира и не должна выдаваться обычной модерации.

`/admin spectate` временно включает God и Vanish на время наблюдения; отдельной permission node для скрытия этого побочного режима нет. Для ban/mute текущие permissions не ограничивают длительность и не предотвращают наказание старшего ранга младшим; пока код не поддерживает эти ограничения, закрепите их в правилах и контролируйте журнал вручную.

## Rocket-группа и native Admin — разные вещи

Rocket-группы — уровни доступа MRE. Native Admin — встроенный статус Unturned. LDM Rocket сам возвращает разрешение для `player.IsAdmin`, а несколько текущих плагинов дополнительно пропускают отдельные Rocket-node проверки для `actor.IsAdmin` (`MreInterface`, `MreFlight`, `MreAdminTools`, `MreInventoryAudit`, `MreModeration`, `MreAirdrop`). Поэтому присвоение сотруднику native Admin фактически отменяет разграничение, описанное выше; одного изменения наших плагинов для закрытия обхода недостаточно.

**Не добавляйте Helpers, Moderators, Senior Moderators, Admins и Head Admins в список native Admin Unturned.** Назначайте им только Rocket-группу. Native Admin оставляйте только доверенному владельцу/оператору, понимающему, что он получает полный обход permission checks.

## Применение к локальному серверу

`config/RocketPermissions.xml` — шаблон проекта. Его изменение само по себе не меняет действующую группу на сервере; фактический файл — `runtime/server/Servers/MRE/Rocket/Permissions.config.xml`. В runtime могут находиться существующие участники, в том числе локальная запись владельца. Не заменяйте этот файл новым шаблоном целиком.

В текущем runtime `DefaultMaxHomes=2`, но старые ранговые лимиты остаются VIP=2 и Elite=3, Premium ещё не настроен; группа `mre_owner` тоже пока не установлена. Шаблоны задают новую лестницу 2/3/4/5 и владельца. `Install-MreFeatures.ps1 -Apply` обновляет HOME лимиты вместе с установкой функций, а `Apply-MreSettings.ps1 -Apply` синхронизирует Rocket-группы и переводит SteamID владельца в `mre_owner`; оба изменения требуют остановки и резервной копии.

1. Остановите локальный сервер штатным `scripts/Stop-MreServer.ps1`.
2. Скопируйте runtime `Permissions.config.xml` и `Commands.dat` в резервную копию вне Git. Не добавляйте в репозиторий список участников.
3. Выполните `scripts/Apply-MreSettings.ps1 -WhatIf` и прочтите план. Если он верен, выполните `scripts/Apply-MreSettings.ps1 -Apply`; скрипт сохраняет собственную резервную копию, сохраняет `<Members>` групп и синхронизирует managed группы с шаблоном. Проверьте уникальность `<Id>`, `ParentGroup` и отсутствие staff nodes у донатных групп.
4. Сравните `Permissions.config.xml` и `Commands.dat` до/после: ни один игрок не должен неожиданно получить staff/native роль; локальные записи должны сохраниться, владелец должен состоять только в `mre_owner` среди staff-групп и оставаться Owner в `Commands.dat`.
5. Запустите сервер штатно и проверьте загрузку permissions в журнале. Войдите тестовыми учётными записями каждой Rocket-роли и проверьте разрешённую и запрещённую команду; отдельно проверьте, что native Admin остаётся полным обходом. Это план проверки, пока не отмеченный как выполненный.
6. При ошибке XML или неправильном доступе остановите сервер, восстановите сохранённые `Permissions.config.xml` и `Commands.dat`, затем повторите перенос.

Не назначайте группы через выдачу товара или автоматическое восстановление trust-score: админские роли назначаются вручную из доверенного локального административного контекста. Магазин и автодонат не должны уметь выдавать permissions персонала.

## Источники реализации

- [Админ-команды и их nodes](INTERFACE.md)
- [Выдача предметов и транспорта](ADMIN_TOOLS.md)
- [Игровая панель](ADMIN_UI.md)
- `src/MreModeration/Models.cs`, `Commands.cs` — список `/staff` actions и отдельная проверка permission для каждого действия
- `src/MreInventoryAudit/Commands.cs`, `MreInventoryAuditPlugin.cs` — inspect/deaths/restore и серверная авторизация
- `src/MreAirdrop/CommandAirdrop.cs`, `MreAirdropPlugin.cs` — `mre.airdrop.admin` и native Admin bypass
- `config/plugins/MoreHomes/MoreHomes.configuration.xml` — лимиты HOME для `morehomes.vip`, `morehomes.premium` и `morehomes.elite`
- [LDM RocketPermissionsHelper.cs](https://raw.githubusercontent.com/SmartlyDressedGames/Legally-Distinct-Missile/master/Rocket/Rocket.Core/Permissions/RocketPermissionsHelper.cs) — проверен 08.11.2026: реализация обходит permissions для `IsAdmin`, добавляет default/parent groups, упорядочивает группы по Priority и поддерживает отрицательные nodes. Priority определяет порядок применения permissions, но сам не создаёт границу безопасности.

