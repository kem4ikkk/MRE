# HUD export — независимая проверка GPT-6.1 Sol

01.11.2026, около 02:58 МСК. Проверка read-only; сервер и игровые файлы не изменялись.

Фактический Unity export подтверждён: `runtime/ui-builds/MreHud-unity-20261101-025150-704.log` содержит Generate/BuildWindows, отчёт сборки и завершение return code 0. `bundle-build.json`: Unity 2022.3.62f3, StandaloneWindows64, prefab_address_verified=true, in_game_verified=false. Builder загружает собранный prefab по адресу Effect, проверяет Canvas и контракт имён, включая BuildingFill01…20. Это проверка структуры prefab, не его отображения в игре.

Bundle 29457 байт: SHA256 `eccb4ed5ae33c6c26582dce1045f3253ddb7c755d9a60ab66fe90d2ffd7629c8` совпадает у результата экспорта, установленного server `runtime/server/Servers/MRE/Bundles/MreHud/MreHud.unity3d` и клиента `D:/steam/steamapps/common/Unturned/Bundles/MreHud/MreHud.unity3d`.

.dat 82 байта: SHA256 `bd2a056e35ff0be88bb951f372f91447d83d044c5a330a87aca29612ff83a1c5` одинаков на сервере/клиенте и в экспорте. Контракт GUID 4f11731bb53e4a55b588b97c2c2f61b5, Type Effect, ID 48621, Asset_Bundle_Version 6 совпадает. Отдельных SDG server/client hashes файлов возле MreHud нет; SHA256 manifest не заменяет этот механизм. Проверка SDK/бинарного сравнения SDG asset hash при подключении не выполнена.

Сервер после перезапуска координатором: PID 4764, новый log от 31.10 23:52 UTC. Он добавляет Per-Server Bundles в asset search, загружает MreHud 1.1 и сообщает AssetsVerified=True. Runtime конфигурация Enabled=true, AssetsVerified=true, HideNativeHud=false; исходная конфигурация сохраняет AssetsVerified=false. Ошибок MreHud в просмотренном логе нет. Пока игроков нет, ветка Render и успешное получение EffectAsset не подтверждаются отсутствием ошибок. Перед включением скрытия штатного HUD нужна клиентская приёмка.

Ограничения подключения: собственного Workshop item для HUD нет (`workshop_file_id=null` в исходном manifest); локальная установка не обеспечивает автоматическую доставку другим компьютерам. Другим игрокам нужна та же версия ассетов или последующая штатная публикация/доставка. Лог сообщает Steam readiness Current/OK и выдаёт Server Code; A2S и это сообщение не доказывают внешний вход. GSLT не используется. На этом этапе проверена только локальная копия клиента; реальное удалённое подключение не выполнено.

Граница GUI-теста: Unity запускался в batchmode, bundle структурно прочитан Builder. Клиентский Client.log завершён до нового экспорта (выход 31.10 23:47 UTC); нового входа с этими ассетами в нём нет. Проверки браузерного preview из предыдущего отчёта не являются GUI-тестом Unity/Unturned. Не проверены реальные показатели, hit building, TPA/HOME, vote, получение сетевых UI сообщений и экранные разрешения в игре.

Конкретная проблема документации: `ui/MreHud/effect-manifest.json` всё ещё содержит client_bundle_status=requires_unity_editor_build и список panels без building. При этом фактический bundle уже собран; статус исходного manifest устарел. Основные README/UI_DESIGN также следует сверить с новым этапом экспорта.

Следующий шаг зависит от запуска клиента после установки ассетов, входа на сервер и ручной игровой приёмки; для друзей — от доставки того же bundle. Отдельные ошибки parsing duplicate AdditionalTransparentSections у William’s Vehicles и предупреждение rclockers server hashes относятся к сторонним модам и остаются в логе.
