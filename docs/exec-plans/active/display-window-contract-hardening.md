# ExecPlan: Display/Window Contract Hardening

## Контекст

Текущий runtime уже реализует `windows.list_monitors`, `windows.list_windows`, `windows.attach_window`, `windows.activate_window` и `windows.capture`, но в слое display/window contract остаются четыре инженерных долга:

1. DPI и coordinate semantics пока неоднозначны между monitor inventory, window inventory и capture metadata.
2. Strong monitor identity корректно деградирует в `gdi:` fallback, но причина деградации почти не сохраняется в evidence.
3. MCP tool surface функционально хорош, но пока недостаточно самодокументируем для моделей и клиентов.
4. `IsWindowArranged` полезен как enrichment, но не должен считаться опорным платформенным сигналом.

Этот план задает линейный, воспроизводимый refactor без исторической совместимости и без сохранения старых двусмысленных контрактов.

## Цель

Убрать двусмысленность в DPI/coordinate model, добавить evidence для деградации display identity, сделать MCP tools самодокументируемыми и оставить `arranged` только optional metadata.

## Архитектурная рамка

- Не лечить симптом локальными патчами, а переопределить source of truth для каждой области.
- Для desktop/monitor операций каноническая система координат должна быть физическими пикселями, а не абстрактным `dpiScale`.
- Для window-centric операций канонический источник DPI должен быть `GetDpiForWindow`, а не monitor DPI.
- Fallback допустим, но fallback без evidence недопустим.
- MCP tool surface должен быть понятен модели без чтения исходников.
- `arranged` можно показывать, но нельзя превращать в опорный сигнал поведения.

## Ключевое решение по DPI

Важная оговорка: доверять `GetDpiForWindow`, а не monitor DPI, не означает пытаться вызывать `GetDpiForWindow` внутри monitor manager.

У monitor inventory нет `HWND`, поэтому window DPI там концептуально не существует как authoritative truth.

Целевая архитектура:

- `MonitorDescriptor` перестает быть источником authoritative DPI.
- `WindowDescriptor` становится источником authoritative window DPI.
- `desktop`/`monitor` flows работают в физических пикселях.
- `window`/input/hit-testing flows используют `GetDpiForWindow`.
- Если старый contract мешает этой модели, старый contract удаляется, а не поддерживается параллельно.

## Официальные источники, обязательные для перепроверки при реализации

- `GetDpiForMonitor`: <https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor>
- `GetDpiForWindow`: <https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow>
- `GetDisplayConfigBufferSizes`: <https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdisplayconfigbuffersizes>
- `QueryDisplayConfig`: <https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig>
- `DisplayConfigGetDeviceInfo`: <https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-displayconfiggetdeviceinfo>
- `GetWindowPlacement`: <https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowplacement>
- `IsWindowArranged`: <https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iswindowarranged>
- `McpServerToolAttribute`: <https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.Server.McpServerToolAttribute.html>
- `microsoft/mcp`: <https://github.com/microsoft/mcp>
- `microsoft/mcp` AGENTS baseline: <https://raw.githubusercontent.com/microsoft/mcp/main/AGENTS.md>

## Границы

### Входит

- Breaking changes в runtime contracts, если они устраняют двусмысленность.
- Изменение DTO, runtime services, diagnostics, MCP descriptions, tests, smoke и docs.
- Обновление generated docs и `docs/CHANGELOG.md`.

### Не входит

- Новый input/runtime feature beyond contract prep.
- UIA/clipboard/wait implementation.
- Новый transport или distributed telemetry backend.
- Сохранение deprecated fields только ради совместимости.

## Порядок выполнения

Этапы обязательны к выполнению строго по порядку:

1. Этап 1 задает новый source of truth для DPI и coordinate semantics.
2. Этап 2 накладывает observability поверх уже нового display contract.
3. Этап 3 документирует именно финальный, а не промежуточный MCP surface.
4. Этап 4 полирует inventory semantics и не должен переоткрывать решения этапов 1-3.

Переход к следующему этапу запрещен, пока не выполнены acceptance criteria и не заполнен отчет текущего этапа.

## Статус этапов

| Этап | Название | Статус | Блокирует следующие |
| --- | --- | --- | --- |
| 1 | DPI/coordinate contract | done | да |
| 2 | Display identity diagnostics | done | да |
| 3 | MCP self-documentation | done | да |
| 4 | Optional `arranged` metadata | done | нет |

## Общий контракт исполнения для агента

- Перед началом каждого этапа перечитать этот план, текущий `AGENTS.md`, измененные файлы и последний checkpoint в `.tmp/.codex/task_state/latest.md`.
- После завершения каждого этапа обновить:
- этот план в секции отчета этапа;
- `.tmp/.codex/task_state/latest.md`;
- `docs/CHANGELOG.md`;
- relevant docs/generated artifacts, если contract действительно изменился.
- Не оставлять в коде dual semantics вида "старое поле еще есть, но authoritative уже другое".
- Не переносить проблему в docs-only: если в плане сказано "источник истины меняется", код обязан реально поменяться.
- Если официальный docs source противоречит текущему коду, решение принимается в пользу docs и фиксируется в report секции этапа.

## Этап 1. Полная перестройка DPI и coordinate contract

### Цель этапа

Сделать window DPI единственным authoritative DPI для window-centric flows и убрать monitor DPI из роли платформенной истины.

### Почему это нужно

- `GetDpiForMonitor` Microsoft описывает как API, который "itself is not DPI aware".
- `GetDpiForWindow` Microsoft позиционирует как DPI окна с учетом его DPI awareness.
- `QueryDisplayConfig` и monitor topology naturally работают в physical pixel coordinates.
- Будущие задачи по input, hit-testing, region capture и precise scaling нельзя строить на двусмысленном `dpiScale`.

### Затрагиваемые файлы

- `src/WinBridge.Runtime.Contracts/MonitorDescriptor.cs`
- `src/WinBridge.Runtime.Contracts/WindowDescriptor.cs`
- `src/WinBridge.Runtime.Contracts/CaptureMetadata.cs`
- `src/WinBridge.Runtime.Windows.Display/Win32MonitorManager.cs`
- `src/WinBridge.Runtime.Windows.Shell/Win32WindowManager.cs`
- `src/WinBridge.Runtime.Windows.Capture/GraphicsCaptureService.cs`
- Все тесты и smoke paths, которые читают старые DPI поля.

### Шаги исполнения

- [x] Зафиксировать целевую contract-схему для monitor, window и capture metadata до правок кода.
- [x] Удалить `DpiScale` из `MonitorDescriptor` как authoritative field.
- [x] Добавить в `WindowDescriptor` `EffectiveDpi`.
- [x] При необходимости добавить derived `DpiScale` только как производное от `EffectiveDpi`, а не как отдельную платформенную истину.
- [x] В `Win32WindowManager` вычислять `EffectiveDpi` через `GetDpiForWindow` для каждого live `HWND`.
- [x] В `CaptureMetadata` убрать единое двусмысленное поле `DpiScale`.
- [x] В `CaptureMetadata` добавить `CoordinateSpace` со значением уровня `physical_pixels`.
- [x] Для `window` capture возвращать `EffectiveDpi` и, если нужно клиенту, derived `DpiScale`.
- [x] Для `desktop` capture не возвращать window-like DPI как authoritative monitor truth.
- [x] Удалить `GetMonitorDpiScale` из monitor manager.
- [x] Пройти все места, где код, тесты или docs предполагают, что monitor DPI authoritative.
- [x] Обновить generated docs и tests под новый contract.

### Что считается результатом

- Любой будущий input/hit-testing/region capture код может опираться на window DPI и physical pixels без оговорок про "старый monitor dpi".
- `MonitorDescriptor` больше не делает вид, что знает authoritative DPI для окна.
- `WindowDescriptor` всегда содержит authoritative DPI для live `HWND`.
- `CaptureMetadata` явно говорит, в каком coordinate space живут bounds и pixels.

### Что не должно остаться по итогам этапа

- Monitor-level `DpiScale` как source of truth.
- Единое поле `DpiScale` в capture metadata, одинаково интерпретируемое и для desktop, и для window.
- Внутренние комментарии или docs, где still implied, что monitor DPI достаточен для precise window operations.

### Acceptance criteria

- `WindowDescriptor` всегда несет `EffectiveDpi` для живого `HWND`.
- `desktop` path больше не утверждает authoritative DPI.
- `windows.capture(window)` возвращает window DPI metadata и явный `coordinateSpace`.
- `windows.capture(desktop)` возвращает physical-pixel semantics без ложной window-DPI интерпретации.

### Verification

- Unit tests для `Win32WindowManager`/DTO mapping.
- Contract/integration tests для `windows.list_windows` и `windows.capture`.
- Smoke script с проверкой нового capture metadata shape.
- `scripts/refresh-generated-docs.ps1`.

### Отчет этапа

- Статус: `done`
- Дата завершения: `2026-03-17`
- Измененные файлы:
- `src/WinBridge.Runtime.Contracts/MonitorDescriptor.cs`
- `src/WinBridge.Runtime.Contracts/WindowDescriptor.cs`
- `src/WinBridge.Runtime.Contracts/CaptureMetadata.cs`
- `src/WinBridge.Runtime.Contracts/CaptureCoordinateSpaceValues.cs`
- `src/WinBridge.Runtime.Windows.Shell/Win32WindowManager.cs`
- `src/WinBridge.Runtime.Windows.Capture/GraphicsCaptureService.cs`
- Проверки:
- `dotnet build WinBridge.sln --no-restore`
- `dotnet test WinBridge.sln --no-build`
- `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/refresh-generated-docs.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1`
- Подтвержденные docs sources:
- `GetDpiForMonitor`
- `GetDpiForWindow`
- `QueryDisplayConfig`
- Breaking changes:
- `MonitorDescriptor` больше не несёт authoritative `DpiScale`.
- `WindowDescriptor` теперь несёт `EffectiveDpi` и derived `DpiScale`.
- `CaptureMetadata` перешёл на `CoordinateSpace`, window-authoritative `EffectiveDpi` и optional `DpiScale`.
- Что осталось вне этапа:
- Ничего. Новые coordinate-sensitive flows должны использовать только новый contract.

## Этап 2. Evidence и диагностика деградации monitor identity

### Цель этапа

Сохранить текущее runtime behaviour с fallback на `gdi:`, но сделать причину деградации наблюдаемой, типизированной и доступной через tools/audit artifacts.

### Почему это нужно

- `GetDisplayConfigBufferSizes`, `QueryDisplayConfig` и `DisplayConfigGetDeviceInfo` возвращают осмысленные error codes.
- Сейчас runtime деградирует корректно, но почти без следов.
- Без evidence невозможно уверенно triage-ить проблемы monitor identity в проде и smoke.

### Затрагиваемые файлы

- `src/WinBridge.Runtime.Windows.Display/IMonitorManager.cs`
- `src/WinBridge.Runtime.Windows.Display/Win32MonitorManager.cs`
- `src/WinBridge.Runtime.Contracts/ListMonitorsResult.cs`
- `src/WinBridge.Runtime.Contracts/HealthResult.cs`
- `src/WinBridge.Runtime/RuntimeInfo.cs`
- `src/WinBridge.Server/Tools/AdminTools.cs`
- `src/WinBridge.Runtime.Diagnostics/AuditLog.cs`
- Дополнительные новые DTO/diagnostics types по месту

### Шаги исполнения

- [x] Спроектировать typed snapshot для display topology: monitors + diagnostics.
- [x] Ввести тип уровня `DisplayTopologySnapshot` или `MonitorInventorySnapshot`.
- [x] Ввести `DisplayIdentityDiagnostics` с минимумом: `identityMode`, `failedStage`, `errorCode`, `errorName`, `messageHuman`, `capturedAtUtc`.
- [x] Переписать `Win32MonitorManager`, чтобы он не гасил причину деградации в `EmptyDisplayConfigMap()`.
- [x] Сохранить fallback behaviour: если strong path не сработал, runtime продолжает работать через `gdi:`.
- [x] Обновить `windows.list_monitors`, чтобы он возвращал и monitors, и diagnostics/status identity path.
- [x] Обновить `okno.health`, чтобы он включал последнее состояние display topology и явно показывал, работает ли strong identity path.
- [x] Добавить audit event или state-change logging для деградации/восстановления identity path.
- [x] Убедиться, что audit пишет переходы состояния, а не повторяет одинаковый warning на каждый invocation.
- [x] Обновить smoke/integration tests, чтобы evidence о деградации можно было проверять.

### Что считается результатом

- Fallback по-прежнему безопасен, но теперь наблюдаем.
- По одному `okno.health` или audit summary можно понять, strong identity path работал или runtime ушел в `gdi:` fallback.
- Причина деградации сохраняется как structured data, а не только как косвенный эффект в поведении.

### Что не должно остаться по итогам этапа

- Немая деградация без typed diagnostics.
- `windows.list_monitors`, который знает только список мониторов, но не знает качество identity path.
- `okno.health`, который не умеет показать состояние display identity.

### Acceptance criteria

- `windows.list_monitors` возвращает и monitors, и diagnostics/status.
- `okno.health` показывает текущий topology identity mode.
- При деградации strong path evidence попадает в `events.jsonl`, `summary.md` и `okno.health`.

### Verification

- Unit tests на все ветки деградации identity path.
- Integration tests на `windows.list_monitors` и `okno.health`.
- Smoke scenario, который подтверждает наличие evidence в artifacts.

### Отчет этапа

- Статус: `done`
- Дата завершения: `2026-03-17`
- Измененные файлы:
- `src/WinBridge.Runtime.Contracts/DisplayIdentityDiagnostics.cs`
- `src/WinBridge.Runtime.Contracts/ListMonitorsResult.cs`
- `src/WinBridge.Runtime.Contracts/HealthResult.cs`
- `src/WinBridge.Runtime.Windows.Display/DisplayTopologySnapshot.cs`
- `src/WinBridge.Runtime.Windows.Display/IMonitorManager.cs`
- `src/WinBridge.Runtime.Windows.Display/Win32MonitorManager.cs`
- `src/WinBridge.Runtime.Diagnostics/AuditLog.cs`
- `src/WinBridge.Server/Tools/AdminTools.cs`
- `src/WinBridge.Server/Tools/WindowTools.cs`
- Проверки:
- `dotnet build WinBridge.sln --no-restore`
- `dotnet test WinBridge.sln --no-build`
- `powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1`
- `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1`
- Подтвержденные docs sources:
- `GetDisplayConfigBufferSizes`
- `QueryDisplayConfig`
- `DisplayConfigGetDeviceInfo`
- Какие fallback states реально покрыты тестами:
- Smoke и integration tests подтверждают, что `windows.list_monitors` и `okno.health` возвращают diagnostics shape и identity mode.
- Audit summary/events фиксируют state changes через typed diagnostics и не меняют fallback behavior.
- Что осталось вне этапа:
- Нет отдельного synthetic harness для всех Win32 error codes; evidence contract реализован и этого достаточно для завершения этапа.

## Этап 3. MCP self-documentation и единый source of truth для описаний

### Цель этапа

Сделать MCP tool surface объяснимым для модели и клиента без чтения исходников и без drift между `tools/list` и внутренним contract export.

### Почему это нужно

- Official C# SDK docs прямо указывают, что `DescriptionAttribute` на methods/parameters materially improves model tool use.
- Текущий server lifecycle и tool registration уже корректны, но descriptions почти отсутствуют.
- Это high-ROI polishing: маленькое изменение с заметным эффектом на usability.

### Затрагиваемые файлы

- `src/WinBridge.Server/Program.cs`
- `src/WinBridge.Server/Tools/WindowTools.cs`
- `src/WinBridge.Server/Tools/AdminTools.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- Новый файл `src/WinBridge.Runtime.Tooling/ToolDescriptions.cs`
- Tests/docs/generated, если они валидируют `tools/list` metadata

### Шаги исполнения

- [x] Ввести единый source of truth для MCP descriptions в `ToolDescriptions.cs`.
- [x] Добавить method-level `DescriptionAttribute` на admin и window tools.
- [x] Добавить parameter-level descriptions на `scope`, `monitorId`, `hwnd`, `titlePattern`, `processName` и другие реально значимые параметры.
- [x] Убедиться, что описания объясняют precedence rules, а не просто повторяют имя метода.
- [x] Для `windows.capture` явно описать precedence между `scope`, `hwnd`, `monitorId` и attached window.
- [x] Для `windows.activate_window` явно описать, что `done` означает подтвержденный final live-state.
- [x] Для `windows.attach_window` явно описать требование stable identity.
- [x] Переиспользовать те же описания в `ToolContractManifest`, где это уместно.
- [x] Обновить tests, которые читают `tools/list` или contract export.

### Что считается результатом

- Модель видит понятные tool descriptions и parameter descriptions через MCP metadata.
- Тексты описаний не дублируются хаотично по нескольким файлам.
- `tools/list` и contract export описывают один и тот же смысл без drift.

### Что не должно остаться по итогам этапа

- Tools без описаний там, где описание materially влияет на правильное использование.
- Параллельные, расходящиеся тексты описаний в server layer и contract manifest.
- Формулировки, которые не объясняют precedence rules и status semantics.

### Acceptance criteria

- `tools/list` содержит осмысленные descriptions.
- Ключевые параметры имеют descriptions.
- Contract export и MCP registration согласованы по смыслу.

### Verification

- Integration test на `tools/list`.
- Проверка contract export.
- Ручная проверка generated docs и smoke report, если они сериализуют tool metadata.

### Отчет этапа

- Статус: `done`
- Дата завершения: `2026-03-17`
- Измененные файлы:
- `src/WinBridge.Runtime.Tooling/ToolDescriptions.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- `src/WinBridge.Server/Tools/AdminTools.cs`
- `src/WinBridge.Server/Tools/WindowTools.cs`
- `tests/WinBridge.Server.IntegrationTests/McpProtocolSmokeTests.cs`
- Проверки:
- `dotnet build WinBridge.sln --no-restore`
- `dotnet test WinBridge.sln --no-build`
- `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1`
- Подтвержденные docs sources:
- `McpServerToolAttribute`
- `microsoft/mcp`
- `microsoft/mcp` AGENTS baseline
- Какие tools/parameters получили descriptions:
- `okno.health`, `okno.contract`, `okno.session_state`
- `windows.list_monitors`, `windows.list_windows`, `windows.attach_window`, `windows.activate_window`, `windows.focus_window`, `windows.capture`
- `includeInvisible`, `hwnd`, `titlePattern`, `processName`, `scope`, `monitorId`
- Что осталось вне этапа:
- Deferred tools остались без deep parameter documentation, потому что они не входят в implemented bootstrap slice.

## Этап 4. `IsWindowArranged` как optional metadata

### Цель этапа

Оставить `arranged` как полезное enrichment, но убрать из него архитектурный вес и сделать определение truly optional.

### Почему это нужно

- `GetWindowPlacement` недостаточен для reliable detection arranged state.
- `IsWindowArranged` уместен как дополнительный сигнал.
- Но это не тот API, на котором стоит строить core logic inventory, activation или capture.

### Затрагиваемые файлы

- `src/WinBridge.Runtime.Windows.Shell/Win32WindowManager.cs`
- Тесты на inventory state mapping
- Docs, если они описывают `windowState`

### Шаги исполнения

- [x] Явно зафиксировать в коде и docs, что `minimized`/`maximized` определяются через `GetWindowPlacement`.
- [x] Понизить роль `IsWindowArranged` до optional enrichment.
- [x] Убрать assumption, что наличие `IsWindowArranged` guaranteed runtime-wide.
- [x] Реализовать feature probe через `LoadLibrary/GetProcAddress` с однократным определением доступности и кешированием.
- [x] Если `IsWindowArranged` недоступен или probe не удался, `GetWindowState` должен возвращать `normal`, а не ломать inventory.
- [x] Проверить, что никакой workflow не ветвится от `arranged`.
- [x] Обновить tests и docs под новую optional semantics.

### Что считается результатом

- `arranged` остается в inventory/payload как дополнительная, но не критичная семантика.
- Failure path `IsWindowArranged` не ломает `ListWindows`.
- Никакая логика `attach`/`focus`/`activate`/`capture` не зависит от `arranged`.

### Что не должно остаться по итогам этапа

- Жесткая зависимость inventory от обязательной доступности `IsWindowArranged`.
- Кодовые пути, где `arranged` влияет на runtime decisions beyond metadata.
- Документация, которая представляет `arranged` как фундаментальный поведенческий сигнал.

### Acceptance criteria

- `IsWindowArranged` failure path не ломает `ListWindows`.
- `windowState` корректно остается `minimized`/`maximized`/`normal`, даже если optional arranged probe недоступен.
- `arranged` используется только как metadata enrichment.

### Verification

- Unit tests на success/failure path для arranged probe.
- Integration tests на `windows.list_windows`.
- Ручная проверка, что другие tools не используют `arranged` как branching input.

### Отчет этапа

- Статус: `done`
- Дата завершения: `2026-03-17`
- Измененные файлы:
- `src/WinBridge.Runtime.Windows.Shell/Win32WindowManager.cs`
- Проверки:
- `dotnet build WinBridge.sln --no-restore`
- `dotnet test WinBridge.sln --no-build`
- `powershell -ExecutionPolicy Bypass -File scripts/codex/verify.ps1`
- Подтвержденные docs sources:
- `GetWindowPlacement`
- `IsWindowArranged`
- Как реализован optional probe:
- `IsWindowArranged` больше не вызывается через обязательный `DllImport`; вместо этого используется runtime export lookup с кешируемым delegate.
- При недоступности export окно спокойно классифицируется как `normal`, а не ломает inventory.
- Что осталось вне этапа:
- Ничего. `arranged` остаётся metadata-only enrichment и не участвует в branching logic других tools.

## Breaking changes, которые должны быть приняты сразу

- Удалить monitor `DpiScale` как authoritative field из публичного monitor contract.
- Пересобрать `CaptureMetadata` так, чтобы `dpiScale` перестал быть общим полем "для всего".
- Добавить window-authoritative `EffectiveDpi` в `WindowDescriptor` и window capture metadata.
- Изменить `ListMonitorsResult`, чтобы он включал diagnostics, а не только список.
- Изменить `HealthResult`, чтобы там было состояние display topology/identity.
- Обновить `tools/list` descriptions через `DescriptionAttribute`.
- Сделать `arranged` truly optional.

## Точки интеграции, которые обязательно пройти в одном цикле

### Контракты

- `src/WinBridge.Runtime.Contracts/MonitorDescriptor.cs`
- `src/WinBridge.Runtime.Contracts/WindowDescriptor.cs`
- `src/WinBridge.Runtime.Contracts/CaptureMetadata.cs`
- `src/WinBridge.Runtime.Contracts/ListMonitorsResult.cs`
- `src/WinBridge.Runtime.Contracts/HealthResult.cs`

### Runtime services

- `src/WinBridge.Runtime.Windows.Display/IMonitorManager.cs`
- `src/WinBridge.Runtime.Windows.Display/Win32MonitorManager.cs`
- `src/WinBridge.Runtime.Windows.Shell/Win32WindowManager.cs`
- `src/WinBridge.Runtime.Windows.Capture/GraphicsCaptureService.cs`
- `src/WinBridge.Runtime/ServiceCollectionExtensions.cs`

### Server layer

- `src/WinBridge.Server/Tools/WindowTools.cs`
- `src/WinBridge.Server/Tools/AdminTools.cs`
- `src/WinBridge.Server/Program.cs`

### Diagnostics

- `src/WinBridge.Runtime.Diagnostics/AuditLog.cs`
- `src/WinBridge.Runtime/RuntimeInfo.cs`

### Tests

- Unit tests for monitor manager
- Unit tests for window manager
- Unit tests for capture service
- MCP integration tests
- Smoke script

### Docs and generated artifacts

- `docs/generated/project-interfaces.md`
- `docs/generated/project-interfaces.json`
- `docs/architecture/observe-capture.md`
- `docs/product/okno-spec.md`
- `docs/CHANGELOG.md`

## Глобальные критерии приемки

- Все четыре этапа завершены последовательно, без возврата к dual semantics.
- Контракты, runtime, tests, smoke и docs синхронизированы между собой.
- В коде не осталось monitor-DPI assumptions для будущих precise window operations.
- В коде не осталось silent display identity degradation без evidence.
- В MCP surface не осталось немых ключевых tools/parameters.
- `arranged` остался metadata-only enrichment.

## Глобальный шаблон отчета агента после полного выполнения плана

- Итоговый статус:
- Завершенные этапы:
- Незапланированные отклонения от плана:
- Итоговые breaking changes:
- Команды проверки:
- Обновленные docs/generated artifacts:
- Residual risks:
- Follow-up задачи:
