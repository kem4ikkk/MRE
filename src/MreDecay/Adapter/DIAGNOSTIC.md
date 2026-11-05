# Изолированная диагностика, 05.11.2026

`DiagnosticPlugin` по умолчанию выключен. Для одного вызова на потоке Unity нужны одновременно `Enabled=true`, переменная `MRE_DECAY_ISOLATED_TEST=YES`, точный `Provider.serverID=MREDecayDiagnostic` и исполняемый файл под TEMP. Список GUID сейфов должен быть непустым и целиком разрешаться в `ItemStorageAsset`; при наличии игроков или неполных наземных регионах сводка не выводится. Записываются только общие количества. Список охватывает наземные регионы, но не транспорт; это не полный снимок мира для расчёта. Генераторы классифицируются по типу ассета, состояние питания не оценивается.

Сборка: `./src/MreDecay/Adapter/Build-Diagnostic.ps1 -OutputDirectory "$env:TEMP/mre-decay-new-build"` (каталог не должен существовать). Выходной DLL — `bin/Release/netstandard2.1/Diagnostic.dll`. Rocket использует каталог конфигурации по имени сборки: `Rocket/Plugins/Diagnostic/Diagnostic.configuration.xml`. Не устанавливать на рабочий сервер.

Фактическая проверка в отдельной копии U3DS под TEMP, новый LAN-экземпляр `MREDecayDiagnostic`, порт 27980, пустой PEI: после загрузки Rocket создал конфигурацию с `Enabled=false` и ничего не выводил. При следующем запуске с явной конфигурацией и переменной окружения журнал содержит:

```text
Loading level: 100%
Diagnostic >> [MRE decay diagnostic] Наземные регионы: structures=0 barricades=0 configured_safes=0 generators=0
```

SHA-256 полного второго серверного журнала: `A92EB8CB23578CC2776A34B1AA9C6BB9F2CF33AC80EC462B7696AA1DA78801E4`; DLL `07CD318469BA43CD5BBC858B9A6B3AAC46B0C16F5D3CE24542E5347A779899B4`. Команда остановки через консоль не подтвердилась; оба PID этих пустых тестовых запусков завершены принудительно после проверки пути.

Отдельный `TestFixture.cs` содержит намеренное создание объектов, поэтому **не входит в сборку Diagnostic и никогда не должен устанавливаться на рабочий сервер**. Он требует другой экземпляр `MREDecayFixture` под TEMP, `MRE_DECAY_FIXTURE=YES`, пустой наблюдаемый мир и отсутствие игроков. Одноразовый LAN-экземпляр на порту 27990 создал тестовые пол, сейф и генератор, повторно вызвал `ReadOnlyWorld` и штатно завершился. Очищенная выдержка:

```text
[MRE fixture] classifier id=1764 category=HousingPlanner
[MRE fixture] classifier id=51 category=BuildingPart
[MRE fixture] classifier id=57 category=BuildingPart
[MRE fixture] classifier id=378 category=BuildingPart
[MRE fixture] classifier id=16 category=Other
[MRE fixture] classifier id=328 category=Other
[MRE fixture] structures=1 safes=1 generators=1
Finished quitting
Quit game: server shutdown
```

SHA-256 полного журнала fixture: `33EABB221DCE42E4BE27277308E4CB82B1708BA303CC7B9E53D4936A2EE485AE`; DLL fixture `092291EA812FCAFE20601565A04391FFA08D3FDCBCB3BB5331361CF3EC958BFB`; исходника fixture `588828E71C2E9BF4D09470897FF4B1DECBC14E4A9724197D950E17F65752963D`. Полные журналы хранятся только в TEMP и содержат служебные данные сервера. Fixture собран с `ReadOnlyWorld.cs`, `HeldItemClassifier.cs`, `HintPolicy.cs` и DLL установленного U3DS; одно предупреждение — устаревшая перегрузка `Assets.find(EAssetType)`.

Положительный контроль подтверждает наблюдение только трёх созданных тестовых объектов, а не устойчивость ID после рестарта, полноту транспортных регионов, параллельные изменения, работу для игроков или целостность реального мира. Контроль не проверял damage, repair, raid, сохранение и восстановление; гниение не включено.

После указанного запуска код наблюдения дополнен проверкой целостности записей ObservationAudit: диагностическая сборка пересобрана, а отдельные проверки валидных и ошибочных записей пройдены в 	ests/MreDecayEngine. Саму fixture после усиления проверки полей и изменения набора файлов повторно на сервере не запускали; приведённые выше журнал и хеш относятся к прежнему исходнику. Для её сборки теперь нужны также ObjectObservation.cs и ObservationAudit.cs. Эти проверки не подтверждают полноту снимка или пригодность к записи мира.
