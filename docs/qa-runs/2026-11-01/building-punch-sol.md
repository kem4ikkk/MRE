# Наблюдатель удара по неуязвимой постройке — Sol review

01.11.2026. Независимая проверка после Terra. Production, сервер и мир не изменялись.

Найдена проблема высокой важности: заявленный fallback при отсутствии Harmony не гарантирован. `MreHudPlugin.Load` без внешнего catch создаёт PunchInputObserver и вызывает Start. Тип содержит Harmony-поле, а Start статически обращается к Harmony/HarmonyMethod. Разрешение этой зависимости возможно при JIT до входа во внутренний try. Точный production `PunchInputObserver.cs` проверен изолированным .NET 8 probe с fake игровыми типами и отсутствующим runtime 0Harmony: получен внешний FileNotFoundException для 0Harmony 2.2.2.0, внутренний catch не сработал. Probe: `runtime/ui-sol-building/missing-harmony/`. Это доказательство на CLR; Mono сервера с удалением библиотеки не запускался.

Связанная проблема установки: `Build-MreHud.ps1` проверяет наличие Harmony и записывает harmony_sha256, но `Install-MreHud.ps1` не читает этот hash и не проверяет библиотеку. При установке на другом сервере возможны отсутствующая/несовместимая dependency и отказ всего HUD, включая damage-event. Рекомендуется предпроверка библиотеки/хеша до Apply и отделённая от Harmony граница вызова с внешним catch в Load. Для гарантии JIT-изоляции helper не должен встраиваться; типы Harmony должны оставаться за этой границей.

Остальное проверено:

- SHA256 установленной Assembly-CSharp.dll равен зафиксированному VerifiedGameHash `15666f1e20afc32a01a5510eb71eef2512a6eb4064ee628b806ee333ab4e4660`.
- Cecil-чтение установленного бинарника подтверждает overload `InputInfo PlayerInput.getInput(bool,ERaycastInfoUsage)`, а PlayerEquipment.punch вызывает именно его. Обёртка вызывает трёхпараметрический overload с nullable Vector3=null. Метка NoInlining у обёртки отсутствует. Патч этой маленькой обёртки имеет риск обхода из-за inlining при другом порядке JIT/reload; воспроизведение на Mono не выполнено. Надёжнее патч реализации с тремя аргументами и фильтром отсутствующего override.
- [PlayerInput SDK](https://github.com/SmartlyDressedGames/U3-SDK/blob/main/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs), проверено 01.11.2026: getInput проверяет usage и делает forward/backward occlusion check при doOcclusionCheck=true. Postfix принимает только сервер, true и Punch, non-null result; не извлекает очередь второй раз. Это серверная проверка клиентского InputInfo, а не новый независимый серверный raycast.
- [PlayerEquipment SDK](https://github.com/SmartlyDressedGames/U3-SDK/blob/main/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerEquipment.cs) дополнительно проверяет squared range≤36. Наблюдатель повторяет этот предел и фильтры type/tag, инициатора, живого игрока и пустой руки. NaN/Infinity отсекаются.
- Ветка hook включается только когда !(canBeDamaged && isVulnerable); уязвимые постройки остаются на прежних damage-events. Barricade/structure получают реальные managed health/asset.health; объекты, не являющиеся ими, отбрасываются.
- Postfix не меняет __result, InputInfo, damage, allow, сетевые команды игры; меняются только View/таймер UI. Priority.Last не гарантирует порядок после произвольного стороннего postfix той же приоритетности, но standalone конфликт не найден.
- Harmony owner уникален в проекте: mre.hud.punch-input.v1. Dispose снимает только owner patches с target, обнуляет active до unpatch; Load/Unload вызывают Start/Dispose по одному разу. Обычный punch идёт в игровом потоке; явной проверки thread id нет, сторонние off-thread вызовы getInput не поддержаны отдельно.
- Изолированная полная компиляция MreHud C#7.3 по установленным DLL и Harmony прошла: `runtime/ui-sol-building/MreHud-punch.dll`, `punch.rsp`.
- 23 проверки чистой логики прошли. Добавленная проверка охватывает range/type/NaN/Infinity, но не фактическую установку Harmony patch, LOS, server event и отсутствие Harmony. Последнее проверено отдельным probe выше.

Не проверено в игре: Mono patch/unpatch/reload, вызов postfix настоящим punch, неуязвимые сейфы/двери/structure, LOS и дальность реального клиента, GUI и сохранность других Harmony patches. До исправления dependency fallback и игровой приёмки этот review не подтверждает готовность полного hook. Сортировка данным отчётом не покрывается.

## Повторная проверка исправления

01.11.2026: Terra исправил зависимость и перенёс patch на фактическую трёхпараметрическую реализацию. Ранее описанная высокая проблема закрыта в исходном коде; новых блокеров при повторной проверке не найдено. Игровая приёмка остаётся необходимой.

- Load теперь ловит исключение вокруг StartPunchObserver; поле имеет тип IDisposable. Cecil-чтение заново собранной DLL подтверждает `StartPunchObserver` с `NoInlining`. Harmony-типы остаются внутри отдельно вызываемого observer.
- Missing-Harmony probe обновлён: точные тела production Load и StartPunchObserver извлечены из исходного файла (изменена только доступность Load для вызова), с production PunchInputObserver и fake окружением SDG/Rocket. Без runtime 0Harmony внешний production catch зарегистрировал FileNotFoundException, затем Load завершился, обе damage-event подписки остались. Результат PASS. Это тест реальной границы исходного кода на CLR, не запуск полноценного Rocket/Mono без библиотеки.
- Patch выбирает точный `getInput(bool, ERaycastInfoUsage, Vector3?)`, return InputInfo; установленный игровой бинарник содержит эту сигнатуру. Postfix принимает __2 и отбрасывает non-null rayOriginOverride. Это устраняет ранее отмеченную зависимость от inlining маленькой обёртки. Остальные фильтры/LOS/range и отсутствие записи в input/result сохранены.
- Полная компиляция MreHud заново прошла, 23 проверки чистой логики повторно PASS. Cecil подтвердил nullable Vector3 параметр AfterInput и тип IDisposable поля.
- Install-MreHud теперь проверяет формат harmony_sha256 и staged библиотеку до dry-run; установленную runtime библиотеку — перед Apply и до копирования. Staged/runtime SHA256 в текущей среде совпадают: `0de0118c8f1d4408de389ca33b46d2ff7778f3a8541b430cae729ec913d899c7`. Настоящий dry-run PASS. В изолированной копии installer с fixture в runtime/ui-sol-building/install-fixture проверены отказ при отсутствующей staged Harmony и отказ Apply при отсутствующей installed Harmony; оба PASS до копирования плагина. Реальный Apply не запускался.
- Уточнение: dry-run проверяет staged Harmony, но установленную runtime проверяет только Apply. Это соответствует текущей модели preflight; dry-run нельзя трактовать как полную проверку runtime установки.

Вердикт после исправления: исходный hook и безопасный fallback прошли независимую проверку кода/сборки/изолированных тестов. Реальный punch по неуязвимой постройке, patch/unpatch на Mono, перезагрузка плагина и GUI не проверены. Сервер, production и мир этой проверкой не изменялись.
