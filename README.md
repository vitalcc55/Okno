# Okno

Okno — это Windows-native MCP runtime для локальной desktop automation под Windows 11. Проект строится как product-ready `STDIO` local process для агентных сценариев `observe -> act -> verify`, где приоритетом являются надёжность, проверяемость и предсказуемое поведение, а не максимальное покрытие фич с первого дня.

Внутренний runtime теперь разделён на отдельные проекты по ответственности, а единый source of truth для MCP tools закреплён в `ToolNames` + `ToolContractManifest`.

## Что делает проект

- перечисляет и выбирает окна Windows;
- поддерживает привязку текущей сессии к окну;
- даёт MCP tool contract для агентного вызова;
- сохраняет структурированные диагностические артефакты;
- предоставляет локальный control plane для build/test/smoke/investigation.

Текущий вертикальный срез уже подтверждает реальный flow:

1. `initialize`
2. `tools/list`
3. `okno.health`
4. `windows.list_windows`
5. `windows.attach_window`
6. `okno.session_state`

## Стек

- `C# / .NET 8`
- `ModelContextProtocol` C# SDK
- `xUnit`
- `PowerShell` scripts для локального control plane

## Transport policy

- Product-ready target сейчас только `STDIO` local process.
- HTTP/URL transport не является текущим рабочим режимом.
- HTTP будет рассматриваться только после готового и стабилизированного `STDIO`.

## Быстрый старт

### Требования

- Windows 11
- .NET SDK `8.0.401` или совместимый по `global.json`
- PowerShell

### Установка после скачивания

1. Клонировать или распаковать репозиторий.
2. Перейти в корень проекта.
3. Выполнить:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/bootstrap.ps1
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -ExecutionPolicy Bypass -File scripts/test.ps1
powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1
```

### One-command local CI

```powershell
powershell -ExecutionPolicy Bypass -File scripts/ci.ps1
```

### Обновление generated docs из manifest

```powershell
powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1
```

## Где читать дальше

- [docs/product/index.md](docs/product/index.md)
- [docs/architecture/index.md](docs/architecture/index.md)
- [docs/generated/commands.md](docs/generated/commands.md)
- [docs/generated/project-interfaces.md](docs/generated/project-interfaces.md)
- [docs/runbooks/investigation.md](docs/runbooks/investigation.md)
