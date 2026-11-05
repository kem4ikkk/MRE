# Навыки дизайна интерфейсов для Codex

Проверено 05.11.2026. В локальный профиль Codex `C:\Users\kemal\.codex\skills` установлены бесплатные навыки из двух открытых репозиториев. Это инструкции для агента, а не плагины Rocket/LDM и не компоненты сервера Unturned. Установка не меняет `runtime/server`, игровой мир и права игроков.

| Источник | Установленные навыки | Лицензия и проверенный исходник |
|---|---|---|
| [jakubkrehel/skills](https://github.com/jakubkrehel/skills) | `better-accessibility`, `better-colors`, `better-interface`, `better-layout`, `better-typography`, `better-ui`, `better-writing`, `break`, `build-design`, `explain-interface`, `interface-review`, `state-machine`, `teardown`, `variant` | [MIT](https://github.com/jakubkrehel/skills/blob/main/LICENSE); HEAD при проверке: [`60bf140`](https://github.com/jakubkrehel/skills/commit/60bf140e43c98b248e6594487384ec967e89d98a) |
| [oil-oil/oil-ui](https://github.com/oil-oil/oil-ui) | `oil-ui` 0.32.0 | [MIT](https://github.com/oil-oil/oil-ui/blob/main/LICENSE); HEAD при проверке: [`0d64a00`](https://github.com/oil-oil/oil-ui/commit/0d64a00f3a5c45bf7a30d35bded9b7c0fdff2128) |

Официальная команда установки для нового профиля Codex: `npx skills add jakubkrehel/skills` и `npx skills add oil-oil/oil-ui`. После установки навыки доступны в следующем обращении к агенту. Для дополнительных сценариев `oil-ui` доступен Python 3.11; обязательных пакетов для текущего проекта нет. Платный Oil UI Pro не приобретался.

## Применение в MRE

- Для браузерного [предпросмотра HUD](../ui/preview/README.md) можно вызывать `oil-ui` для поиска и сравнения направлений, `better-interface` для общего ревью, а профильные навыки — для цвета, текста, типографики, расположения и доступности. Навыки `break`, `state-machine`, `variant` и `interface-review` запускаются по явному запросу пользователя, как указано в их инструкциях.
- Игровой HUD собирается в [Unity](../ui/MreHud/README.md) и связан с серверным [контрактом](UI_DESIGN.md). Результат работы навыков можно использовать как дизайн-предложение; изменения prefab, bundle и поведения в игре требуют обычной сборки и отдельной игровой проверки по [протоколу](UI_VERIFICATION.md).
- Текущую палитру MRE, русские тексты, назначение клавиш и требования проекта брать из [UI_DESIGN.md](UI_DESIGN.md). Чужие фирменные интерфейсы и ресурсы не копировать.

Проверка установки: у всех 15 каталогов есть `SKILL.md` и `LICENSE`. На этой задаче дизайн, сборка Unity и игровая приёмка не выполнялись. Если появится конкретная задача на редизайн, следующим шагом будет выбор экрана и проверка результата в браузере либо игре.
