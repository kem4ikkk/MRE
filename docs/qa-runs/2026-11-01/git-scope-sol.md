# Read-only аудит Git перед объединением работ, 01.11.2026

**Исторический контекст:** аудит выполнялся для прежнего узкого коммита HUD. Позднее владелец отдельно попросил зафиксировать весь проект; ограничения списка ниже относятся к прежней задаче и не описывают состав последующего общего коммита.

Индекс на момент проверки пуст: git diff --cached --stat не вывел изменений.52 tracked файла изменены, дополнительные файлы untracked. Ничего не добавлялось в индекс, commit/stash/reset не выполнялись. Владение файлами нельзя определить по Git: ниже тематическая классификация diff, а не доказательство авторства. Состояние меняется параллельно; перед staging нужно повторить diff.

## Цельный набор текущих UI/TPA/HOME/immunity/kits/safes

Можно рассматривать целиком после завершения нового HUD review:

- src/MreHud/**, config/plugins/MreHud/**, scripts/Build-MreHud.ps1, Build-MreHudAssets.ps1, Install-MreHud.ps1; ui/preview/**, ui/MreHud/Assets/Editor/**, ui/MreHud/Packages/**, ui/MreHud/ProjectSettings/** и README/.gitignore. Generated Assets/MreHud/** игнорируются осознанно: воспроизводятся Builder; runtime DLL/bundle в коммит не входят.
- src/MreImmunity/**, config/plugins/MreImmunity/**, scripts/Build-MreImmunity.ps1, Install-MreImmunity.ps1, docs/IMMUNITY.md.
- src/Teleportation.MRE/** (текущий diff добавляет tracked requests,HOME,key edges,cooldown,tests/version), src/MoreHomes.MRE/**, config/plugins/Teleportation/**, config/plugins/MoreHomes/**, scripts/Build-MreUi.ps1,Install-MreUi.ps1,Install-MrePlugins.ps1,plugins.lock.json.
- src/MreKits/**,config/plugins/MreKits/**,scripts/Build-MreKits.ps1,docs/KITS.md. Но Commands.cs уже зависит от src/MreShared/MreChat.cs,см. ниже.
- config/workshop.json: единственная добавка Hypnotic Lockers2922152196;docs/SAFES.md.
- docs/UI_DESIGN.md,UI_VERIFICATION.md;qa-runs только выбранные HUD/home/vote/kits/immunity/safes протоколы. Не захватывать весь каталог автоматически: inventory-sort-sol.md относится к отдельной недоставленной сортировке.
- .gitignore: единственный новый hunk про ui/MreHud Library/Temp/Logs/UserSettings полностью относится к HUD.

## Реальный overlap и выбор hunks

MreEssentialsPlugin.cs объединяет сразу три темы:

1. VoteUI: поля voteInitiatorName,lastVoteUi,voteUiVisibleUntilUtc и методы GetVoteUiSnapshot/FinishVoteUi (текущие строки20–40); очистка snapshot в Unload; запись инициатора при начале; FinishVoteUi canceled/expired/passed вместо session=null; MinimumPlayers<1. Связанные целые файлы VoteUiSnapshot.cs и VoteSession.cs (eligibility/voted getters+solo).
2. MaxSkills: pendingSkills/initialSkillsApplied/nextSkillsRefreshUtc; connect/disconnect/revive subscriptions,Update refresh; ApplyMaxSkills command/report; MaxSkillsPolicy.cs,PlayerMaxSkillsTarget.cs. Это отдельная тема, не нужна HUD snapshot.
3. Общий chat: using MreShared и все UnturnedChat.Say→MreChat.Say. Это функциональная зависимость нового kits и прочих модулей, но тематически шире HUD.

Для узкого UI-коммита нужен точный partial patch MreEssentialsPlugin.cs: только п.1, оставляя maxskills и общий chat вне индекса; целиком VoteUiSnapshot.cs/VoteSession.cs; в MreEssentialsConfiguration.cs только MinimumPlayers=1, без MaxSkillsEnabled; в checks/Program.cs solo vote hunk вначале, без CheckMaxSkills/diagnostics/ApplyMaxSkills; не включать RuntimeDiagnostics.cs,LocalControlBridge.cs,AssemblyInfo description maxskills,MaxSkillsPolicy/PlayerMaxSkillsTarget. Это ручная сборка состояния индекса, которое обязательно отдельно скомпилировать. Обычный выбор whole hunk затронет соседние темы в Load/Unload/HandleVote.

Практически безопаснее отдельный предварительный dependency-коммит общего chat/skills, если эти изменения авторизованы владельцем: src/MreShared/MreChat.cs,src/MreChatStyle/**,Build-MreChatStyle.ps1,подключение MreChat.cs в Build-MreEssentials/Flight/Interface/InventoryAudit/Moderation и chat replacements в соответствующих src. Затем цельный UI-коммит не требует обратного редактирования переплетённых строк. Без подтверждения scope эти unrelated модули не включать.

scripts/Install-MreFeatures.ps1: смешаны VaultOnly,KitsOnly,ChatOnly в одних строках и condition guards; tests/InstallMreFeatures.Tests.ps1 аналогично содержит fixture всех трёх+тесты. Для узкого KitsOnly нужна новая partial версия согласованного кода и tests; staging одного существующего hunk безопасного разделения не даёт. Не брать целиком с целью «только HUD»,пока scope vault/chat не подтверждён.

config/RocketPermissions.xml: единственный diff добавляет mre.vault,не kits/HUD/safes. Отложить вместе с src/MreVault/**,Build-MreVault.ps1,docs/VAULT_TEST_PLAN.md. Виртуальное хранилище и Workshop-сейфы — разные изменения.

README.md,docs/LOCAL_INSTALLATION.md,planning/STATUS.md,docs/IMPLEMENTATION_PLAN.md,config/plugins/README.md объединяют UI,старые статусы,skills/пароль/vault/Workshop. Перед коммитом обновить итоговые заявления по новому запросу HUD; не фиксировать целиком устаревшее «bundle не собран/Unity отсутствует/штатный HUD сохранён» как текущий итог. Исторические блоки должны явно иметь дату. Отдельный парольный hunk README не относится к новому UI,но является уже авторизованной установкой; включать только при широком коммите инфраструктуры.

## Отложить вне текущего набора

src/MreInventorySort/**,Build-MreInventorySort.ps1,docs/INVENTORY_SORT.md,qa inventory-sort;src/MreVault/**/Vault permission;непроверенные assets/design/** иserver-chat*.png; .vscode/**;docs/BRANCH_INTEGRATION.md без отдельного просмотра. AdminObservation/StaffCommandHandler иRuntimeDiagnostics содержат дополнительные admin/skills изменения и требуют отдельного scope review. Не использовать git add . или широкие каталоги docs/src/scripts без списка.

Рекомендуемое русское название цельного UI-коммита после финального review: «Добавить HUD MRE, интерфейс HOME/TPA, названия магазинов и локальный режим иммунитета». Описание должно отдельно назвать cooldown80/100,delay5,голосование,наборы,Workshop-сейфы,резервные копии и границу in-game unverified. Это предложение,commit не создан.

## Уточнение по авторству и узкому UI scope

Координатор подтвердил, что MaxSkills/MreShared/Vault принадлежат другой сессии, а HEAD4291ec7 также создан ею. Поэтому предварительный широкий dependency-коммит **не предлагается к исполнению без согласования автора**. Для действительно только UI нужно отдельное состояние отHEAD со строго перенесёнными voteUI изменениями;это ещё не подготовленный и не собранный commit. Текущая успешная сборка рабочего дерева не подтверждает сборку такого partial состояния. Kits имеет реальную compile dependency на чужой MreShared:из узкого UI scope его следует исключить либо отдельно согласовать конкретную зависимость;нельзя обещать полноценный воспроизводимый UI+Kits commit без неё. README Vault integration hunk и permission mre.vault исключить. Последний повторный status--porcelain подтвердил пустой индекс;никто не staging в момент проверки. Во время финального commit нужно повторить проверку конкурентного индекса.

## Final snapshot review перед коммитом

Независимая проверка runtime/git-scope-terra/vote-clean-20261101/vote-only.patch: точно совпадает с нормализованным HEAD→snapshot diff пяти файлов MreEssentialsPlugin.cs,MreEssentialsConfiguration.cs,VoteSession.cs,VoteUiSnapshot.cs,checks/Program.cs. Нет MreShared/MreChat/MaxSkills. Повторно выполнены snapshot checks:46Essentials+76TPA/HOME+23HUD —PASS. Собственные production-сборки snapshot заново не запускались;ранее полученныеcompile manifests и отсутствиеforeign include вcompile rsp просмотрены.

Четыреcandidate файла сохраняют границу HEAD:TeleportationXMLCombatDuration60 иemptyicon,MoreHomesXMLMaxHomes1 иemptyicon,TeleportationConfiguration.cs upstreamicon default безновогоchat,HomeChecks не навязывает2дома. Изменения cooldown100/80,delay5,free movement иplugin keys относятся кUI. ДополнительныйMessageColorgray→white вTeleportationXML не является необходимой UI-зависимостью:узкий whitelist лучше оставитьgray,еслиавторство не подтверждено.

Минимальный технический whitelist UI-коммита:

- Пять файлов ровно изvote-only.patch,нерабочие mixedверсии.
- src/MreHud/**,config/plugins/MreHud/**,scripts/Build-MreHud.ps1,Build-MreHudAssets.ps1,Install-MreHud.ps1.
- src/Teleportation.MRE/**сдвумяcandidateзаменамиConfiguration.cs иTests/HomeChecks.cs;src/MoreHomes.MRE/**.
- config/plugins/Teleportation/** иMoreHomes/**,ноXMLровноcandidateверсии (желательноgray вместоwhiteвTeleportation).
- scripts/Build-MreUi.ps1,Install-MreUi.ps1,Install-MrePlugins.ps1,plugins.lock.json;ui/** сдействующимиignoreдляgeneratedAssets/Library/Temp;HUD-hunk.gitignore.
- ТолькоисправленныеUI_DESIGN/UI_VERIFICATION ивыбранныеUIQAпротоколы,неширокиеREADME/STATUS/LOCAL_INSTALLATION. СледитьзаQAссылкаминаисключённыефайлы.

Не включатьBuild-MreEssentials.ps1 израбочегодерева (он добавляетчужойMreShared;HEADсобираетчистыйsnapshot),MaxSkillsиdiagnostics,CommandVotechatdiff,AssemblyInfoсMaxSkillsdescription,Build*adminchatdiff,Install-MreFeatures/testsсVault/Kits/Chat,kit/shared/vault/READMEmixed. Immunity иWorkshop-safes технически независимы;дляминимальногоUIscope вынести в отдельные последующие явноименованныеизменения,есликоммит должен включать их—добавитьтолькоMreImmunitysource/config/build/install/LICENSE/docsиSAFES/configworkshop,безVault.

Передфиксацией найденыдваdocblocker: UI_VERIFICATION.mdстрока60пишетMaxHomes2 иtestactualXML,ноcandidate1;UI_DESIGN.mdстрока73содержитбуквальныевопросительныезнакииустаревшийy84. Исправитьдокументыилиисключитьэтистроки. Последнийcachedname-only пуст. Staging/commitне выполнялись;проверитьполученныйиндекссноваиз-заconcurrentсессииипровестиdiff--checkнавыбранномсостоянии.
