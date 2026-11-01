# Независимая проверка HUD: GPT-6.1 Sol

Дата: 31.10.2026, Europe/Moscow. Проверка выполнена после завершения реализации Terra. Production-код и работающий сервер ревьюер не менял.

## Замечания

**P2, исправлено Terra и перепроверено:** `ui/MreHud/Assets/Editor/MreHudBuilder.cs:190,245–249`: у VoteInitiator размер 11, но best-fit задавал минимум 12 и максимум 11. Диапазон был инвертирован. Теперь минимум `Math.Min(10, size)`, максимум `size`; исходник прочитан повторно, API-компиляция повторена успешно. Фактическая отрисовка длинного имени требует Editor/клиента.

Других подтверждённых блокирующих дефектов при выполненных проверках не обнаружено.

## Выполнено

- `scripts/Build-MreHud.ps1`: exit 0, без предупреждений; установленные игровые и Rocket/Teleportation/MreEssentials DLL использованы как ссылки. DLL на сервер не устанавливалась.
- `dotnet run --project src/MreHud/Checks/MreHud.Checks.csproj`: 20 PASS — форматирование, границы шкал/патронов, безопасные имена и vote snapshot.
- `node --check ui/preview/app.js`: exit 0.
- Edge headless через существующий Playwright: 15 PASS; полный отсчёт TPA/HOME, движение, отмена, истечение, выстрел/перезарядка, HP, голосование и результат 8 секунд; 1366×768, 390×844, 844×390, отсутствие JS-ошибок. Сценарии и результаты: `runtime/ui-sol-review/review.cjs`, `result.json`; скриншоты сохранены там же. Скриншот 1920×1080 дополнительно просмотрен визуально: центр свободен, числовые показатели и кнопки доступны.
- Дополнительный самостоятельный браузерный сценарий `runtime/ui-sol-review/extra.cjs`: Comma внутри input не принимает TPA; Comma при HOME сохраняет HOME; Period отменяет HOME — PASS.
- Итоговый Unity builder с исправлением best-fit скомпилирован Roslyn против настоящих Unity runtime/uGUI DLL установленного клиента: exit 0. Только UnityEditor API заменены существующим shape-стабом, скопированным в `runtime/ui-sol-review/EditorApiShape.cs`; команды экспорта не выполнялись. Это проверка C# и runtime API, не настоящая Editor-сборка.
- Сопоставлены серверные text/visibility имена с builder, включая 20/10 сегментов. Prefab скрыт до первой передачи значений, имена уникальны и обязательные сегменты проверяются генератором.
- Read-only review lifecycle: отправка только изменённых text/visibility; native HUD отключается только при Enabled+AssetsVerified и существующем EffectAsset ID/GUID; unload/exception восстанавливают изменённые флаги, clear по GUID; смерть скрывает HudContent; вид транспорта и native status icons не отключаются. Игровое поведение этими наблюдениями не измерялось.
- `scripts/Build-MreHudAssets.ps1`: проверены lock, свежесть результата, manifest version/ID/GUID/platform, SHA256, UnityFS и .dat; реально выполнен отрицательный сценарий с отсутствующим Editor — ожидаемый отказ без создания bundle.

## Осталось

Unity Editor не запускался, prefab/bundle не экспортирован, Workshop-доставка и реальный клиентский HUD не проверены. Нужны Editor 2022.3.62f3 и клиентская приёмка: кириллица/переносы длинных строк, масштаб и наложения, разные бинды Plugin Keys, native HUD при unload/ошибке, реальные gun state и Workshop-магазины, PvP/raid, сеть и нагрузка 24 игроков. Browser/mock/API compilation не заменяют эти проверки; AssetsVerified остаётся false до клиентской приёмки.
