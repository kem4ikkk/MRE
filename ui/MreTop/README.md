# MRE /top: исходный Unity prefab

Unity 2022.3.62f3; Windows AssetBundle; адрес prefab — `Effect`. Временные ID `48624`, key `14824`, GUID `a3484011ca53436e92008ee933211fb5`: уникальность среди всех ресурсов Workshop не проверена.

Кнопки: `TopClose`, `TopCategoryKills`, `TopCategoryDeaths`, `TopCategoryKd`, `TopCategoryZombies`, `TopCategoryTime`. Индикаторы выбора: `TopSelectedKills`, `TopSelectedDeaths`, `TopSelectedKd`, `TopSelectedZombies`, `TopSelectedTime`. Текст: `TopTitle`, `TopPeriod`, `TopStatus`, `TopRank00`…`TopRank09`, `TopName00`…`TopName09`, `TopValue00`…`TopValue09`. Строки `TopRow00`…`TopRow09` и индикаторы изначально скрыты. Сервер должен передавать реальные данные, очищать имена игроков, проверять нажатия и управлять Modal и закрытием окна. Интеграции с сервером пока нет.

Сборка: Unity с аргументами `-batchmode -quit -projectPath <ui/MreTop> -executeMethod MreTopBuilder.BuildWindows -logFile <log>`. Результат: `runtime/ui-builds/MreTop/Windows/MreTop/`. Генератор повторно открывает bundle и проверяет контракт prefab. Игровое отображение, управление клавиатурой, масштаб и совместимость с другими окнами не проверены. Пакет не установлен и не опубликован.
