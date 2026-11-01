# Независимая проверка HUD постройки — GPT-6.1 Sol

Дата: 31.10.2026. Реализация: GPT-6.1 Terra. Проверка выполнена отдельно; production-файлы не изменялись, сервер не перезапускался.

Результат: блокирующих дефектов в добавленном коде не найдено. Это приёмка серверного исходного кода и браузерного примера; готовность интерфейса в Unturned не подтверждена.

Проверено:

- C# 7.3: весь MreHud собран отдельной командой csc по установленным игровым/Rocket/Teleportation/MreEssentials DLL. Выход: `runtime/ui-sol-building/MreHud.dll`, response file там же. Код возврата 0.
- 22 существующих проверки чистой логики пройдены через изолированный проект `runtime/ui-sol-building/Checks.csproj`. Две проверки относятся к building hit: фильтр живого игрока с пустой рукой, отложенное чтение HP и граница пяти секунд. Это не тест реальных damage events.
- 10 браузерных проверок пройдены в Edge/Playwright: начальное скрытие, название/пример HP, одновременные TPA и vote, обновление таймера повторным ударом, скрытие спустя пять секунд, попадание карточки в экран 1920×1080, 1366×768, 390×844, 844×390, отсутствие ошибок JavaScript. Результат и снимки: `runtime/ui-sol-building/`. Снимок 1920×1080 просмотрен вручную.
- Статический review: отдельные barricade/structure handlers подписаны в Load и сняты в Unload; `EDamageOrigin.Punch`, alive и `equipment.asset == null` проверяются; damage/allow не меняются. Инициатор определяется по CSteamID; чужие карточки не изменяются. Последний удар заменяет состояние. Отписка Teleportation при смене Instance сохранена.
- HP считывается в следующем Update после завершения синхронного damage-request. Closure держит обычный объект Barricade/Structure с полем health, а не Transform/Drop. Уничтожение уроном устанавливает HP=0 до удаления модели. Смерть очищает состояние в Render; выход удаляет View при очередном обновлении; выгрузка освобождает словарь. Отдельных таймеров и подписок на каждый удар нет.
- Late join: View создаётся при очередном обновлении; удар до этого обновления игнорируется (максимум RefreshIntervalSeconds). Новому игроку не передаётся чужое прежнее состояние. Это проверка кода, не двух клиентов.
- Исключение при Render приводит к существующему восстановлению штатного HUD и отключению custom HUD для данного View. Damage handlers не имеют собственной защиты try/catch; штатные операции не обнаруживают очевидного throw, но искусственные ошибки сторонних плагинов не воспроизводились.
- Builder: BuildingPanel/BuildingName/BuildingHealth совпадают с сервером, Bar создаёт 20 сегментов BuildingFill01…20, ValidateNames проверяет наличие/уникальность. Панель 280×90 под server/combat/raid и отдельно от TPA/vote. ID 48621/key 14821/GUID не менялись. Реальное исполнение Builder и экспорт в эту проверку не входят.

Подтверждение SDK, проверено 31.10.2026:

- [PlayerEquipment](https://github.com/SmartlyDressedGames/U3-SDK/blob/main/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerEquipment.cs): серверный удар вызывает DamageTool только при `canBeDamaged && isVulnerable`, передаёт инициатора и `EDamageOrigin.Punch`. Неуязвимые и не получающие урон от кулака постройки карточку не вызовут; ограничение корректно описано в UI_DESIGN.
- [BarricadeManager](https://github.com/SmartlyDressedGames/U3-SDK/blob/main/Assets/Runtime/Assembly-CSharp/Unturned/Managers/BarricadeManager.cs) и [StructureManager](https://github.com/SmartlyDressedGames/U3-SDK/blob/main/Assets/Runtime/Assembly-CSharp/Unturned/Managers/StructureManager.cs): request вызывается перед проверкой allow/числа урона и askDamage. Отмена другим обработчиком оставляет HP прежним и не мешает карточке.
- [Barricade](https://github.com/SmartlyDressedGames/U3-SDK/blob/main/Assets/Runtime/Assembly-CSharp/Unturned/Inventory/Barricade.cs) и [Structure](https://github.com/SmartlyDressedGames/U3-SDK/blob/main/Assets/Runtime/Assembly-CSharp/Unturned/Inventory/Structure.cs): обычные managed-классы, health обнуляется при смертельном уроне. Сравнение с SDK не является декомпиляцией установленного бинарника; бинарная совместимость подтверждена сборкой.

Оставшиеся ограничения: если объект убран подбором/администратором без изменения health, closure может показывать последние HP до истечения пяти секунд. Это не исключение и не утечка Unity reference, но наличие постройки повторно не проверяется. Предпросмотр использует фиксированные 996/1000, не игровой источник; его полоса плавная, в Unity — 20 сегментов.

Не выполнено: удар настоящего клиента по structure/barricade/двери/хранилищу; отмена урона другим плагином в игре; смертельный удар; подбор/ремонт во время показа; смерть, выход и повторный вход; reload плагина; получение bundle клиентом; реальное расположение/шрифт/передача видимости. Следующий шаг зависит от экспорта Unity, доставки bundle и игровой приёмки. Результат отдельного экспорта координатора следует фиксировать отдельно.
