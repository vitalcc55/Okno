# Observe/Capture Slice

## Зачем существует этот документ

Этот документ фиксирует первый реально полезный observe-loop в `Okno`:

`windows.list_windows -> windows.attach_window -> windows.capture`

Начиная с display/activation hardening этот observe-loop дополняется двумя важными правилами:

- explicit desktop target выбирается через `windows.list_monitors` + `monitorId`;
- minimизированное окно сначала проходит через `windows.activate_window`, а не через hidden restore внутри `windows.capture`.

Его цель проста: агент должен надёжно увидеть нужное окно, получить новый визуальный контекст и только потом переходить к более хрупким шагам вроде `click`, `type` или `wait`.

Документ описывает:

- что именно реализовано сейчас;
- как работает текущий runtime path;
- почему выбраны именно такие инженерные решения;
- на какие внешние источники мы опирались и что именно из них взяли.

## Что реализовано

На текущем этапе `observe/capture` slice опирается на четыре публичных capability:

- `windows.list_monitors` — перечисляет captureable desktop view targets и их stable source/view identity;
- `windows.list_windows` — перечисляет видимые top-level окна и их базовые metadata;
- `windows.attach_window` — делает выбранное окно текущим session target;
- `windows.capture` — возвращает снимок окна или monitor в виде MCP tool result с image block, structured metadata и локальным PNG artifact.

Текущая реализация живёт в:

- `src/WinBridge.Server/Tools/WindowTools.cs`
- `src/WinBridge.Runtime.Windows.Capture/GraphicsCaptureService.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`
- `scripts/smoke.ps1`

## Как это работает

### 1. Session остаётся source of truth

`windows.capture` не создаёт отдельную модель выбора окна. Для `scope="window"` порядок такой:

1. если передан `hwnd`, он имеет приоритет;
2. иначе используется attached window из session;
3. если нет ни `hwnd`, ни attached window, tool возвращает tool-level error.

Это держит observe-loop коротким и предсказуемым: attach уже задаёт рабочий контекст, capture только читает его.

Для `scope="desktop"` порядок сейчас такой:

1. если передан `monitorId`, выбирается именно он;
2. иначе если есть explicit или attached window, выбирается monitor этого окна;
3. если target window нет, берётся именно primary monitor через monitor inventory;
4. если attached window устарело или live окно с тем же `HWND` больше не совпадает с attached snapshot по ключевым metadata, runtime нормализует его в `no target` и тоже уходит в primary monitor, а не в tool error.

При этом attached identity теперь читается так:

- `HWND` остаётся быстрым candidate key;
- `ProcessId`, `ThreadId` и `ClassName` используются как OS-backed identity signals против reuse-case;
- `Title` больше не является обязательным identity-критерием и остаётся display metadata;
- если `HWND` совпал и OS-backed identity сигналы совместимы, attached окно считается тем же live target и для `window`, и для `desktop`.

### 2. MCP result ориентирован на реальный клиентский use-case

`windows.capture` возвращает:

- `structuredContent` с каноническими metadata;
- `text` block с сериализованным JSON для совместимости;
- `image/png` block с base64 PNG;
- `isError=true`, если target не найден или capture не удалось выполнить.

В metadata сейчас входят:

- `scope`
- `targetKind`
- `hwnd`
- `coordinateSpace`
- `effectiveDpi` для `window` capture
- `dpiScale` как derived convenience field только там, где у runtime есть authoritative window DPI
- `monitorId`
- `monitorFriendlyName`
- `monitorGdiDeviceName`
- `title`
- `processName`
- `bounds`
- `pixelWidth`
- `pixelHeight`
- `capturedAtUtc`
- `artifactPath`
- `mimeType`
- `byteSize`
- `sessionRunId`

Metadata строятся не из исходного pre-capture target snapshot, а из authoritative target snapshot после завершения capture path. Это важно для двух случаев:

- после `Recreate` runtime публикует уже обновлённые `bounds` и связанные target fields, согласованные с финальным PNG;
- перед desktop `GDI` fallback runtime заново резолвит текущий monitor target, чтобы не возвращать успешный screenshot по устаревшей topology.
- для успешного `desktop` WGC capture runtime не делает silent retarget на другой monitor: monitor identity остаётся от того target, по которому был создан `GraphicsCaptureItem`, а refreshed topology используется только если она подтверждает тот же monitor.

Аннотации tool тоже теперь выровнены с фактическим поведением:

- `ReadOnly = false`, потому что успешный capture пишет локальный PNG artifact;
- `Idempotent = false`, потому что повторный capture создаёт новый artifact и может вернуть другой observation;
- `OpenWorld = true`, потому что tool читает live state внешней desktop session.

### 3. Артефакт сохраняется локально всегда

Даже при успешном inline image response runtime пишет PNG в:

`artifacts/diagnostics/<run_id>/captures/`

Это нужно не для “резервного вывода”, а для доказуемого investigation path:

- smoke может проверить, что файл реально создан;
- человек может открыть конкретный PNG без повторного прогона;
- audit/summary и capture artifact оказываются в одном diagnostics run directory.

### 4. Backend выбран как WGC-first

Основной path:

1. `Windows.Graphics.Capture`
2. `CreateForWindow` или `CreateForMonitor`
3. `Direct3D11CaptureFramePool`
4. `GraphicsCaptureSession.StartCapture()`
5. `FrameArrived -> TryGetNextFrame()`
6. проверка `Direct3D11CaptureFrame.ContentSize` против текущего размера frame pool
7. при первом size drift — `Direct3D11CaptureFramePool.Recreate(...)` и ожидание следующего frame
8. только после size stabilization — `SoftwareBitmap.CreateCopyFromSurfaceAsync`
9. PNG encoding через `BitmapEncoder`

То есть первый path максимально windows-native и не изобретает собственный графический pipeline поверх shell или внешних утилит.

### 5. Fallback policy разделена по scope

На практике оказалось, что target-specific `Windows.Graphics.Capture` path может:

- не дать frame вовремя;
- не сработать для конкретной цели;
- быть нестабильным как единственный backend для первого observe-loop.

Поэтому fallback policy сейчас разделена так:

- для `scope="desktop"` `GDI` screen copy через `Graphics.CopyFromScreen` допустим и как fallback после timeout/native ошибки, и как прямой backend, если `Windows.Graphics.Capture` недоступен в текущей сессии;
- для `scope="desktop"` туда же относится persistent geometry mismatch: если после single `Recreate` `ContentSize` всё ещё не стабилизировался, runtime считает WGC acquisition недостоверным и уходит в тот же desktop fallback;
- для `scope="window"` screen-copy fallback больше не используется, потому что он может вернуть чужие экранные пиксели и нарушить semantics конкретного `HWND`;
- для `scope="window"` size drift после single `Recreate` считается честной tool-level ошибкой, а не поводом подменять window semantics screen-copy fallback'ом;
- для minimизированного окна runtime возвращает tool-level error и просит сначала вызвать `windows.activate_window`.

Важно:

- desktop fallback не меняет публичный MCP contract;
- desktop fallback остаётся Windows-native;
- window path теперь предпочитает честный `isError=true`, если достоверный window-specific capture невозможен.
- runtime process должен быть DPI-aware до старта MCP host, иначе `physical_pixels` contract для window bounds и capture metadata недостоверен.

## Почему реализовано именно так

### Маленькая публичная surface-area

Мы сознательно не добавляли отдельный `windows.observe`.

Причина: в текущем продукте уже есть хороший минимальный цикл из трёх tools. Новый high-level tool на этом этапе только размыл бы контракт и усложнил smoke/tests/docs.

### `CallToolResult`, а не произвольный DTO

Для `windows.capture` нужен именно mixed result:

- текст;
- структурированные metadata;
- картинка.

Обычный DTO для этого неудобен. `CallToolResult` позволил сохранить wire-shape близким к MCP spec без ручного обходного формата.

### Артефакт рядом с audit

Из-за `STDIO` transport runtime не может “болтать в stdout” чем угодно. Поэтому локальные артефакты становятся важной частью operating model, а не случайной отладочной опцией.

### WGC-first, но не любой ценой

`Windows.Graphics.Capture` остался главным направлением, потому что это корректный windows-native путь для window/monitor capture.

Но первый observe-loop оценивался не по архитектурной чистоте, а по способности реально и повторяемо вернуть **достоверный** снимок в `STDIO` MCP runtime. Поэтому desktop fallback допустим, а window fallback через screen copy — нет.

### Harness тоже должен опираться на runtime semantics

`smoke.ps1` и `McpProtocolSmokeTests` теперь не выбирают candidate window по собственным геометрическим порогам. Вместо этого harness ищет первое окно, которое сам runtime может последовательно:

1. `attach`;
2. отразить в `session_state`;
3. успешно `capture` без `isError=true`.

Это важно, потому что evidence layer должен подтверждать контракт runtime, а не вводить отдельную параллельную модель “валидного окна”.

## Что пока не входит

Этот slice пока не решает:

- OCR;
- UIA summary;
- region capture;
- multi-monitor stitched desktop capture;
- capture diff / visual compare;
- richer monitor selection contract beyond `windows.list_monitors` + `monitorId`.
- offscreen window-specific fallback beyond `Windows.Graphics.Capture`.

То есть это не полный visual subsystem, а первый рабочий observe foundation.

При этом `region capture` остаётся осмысленным следующим узким follow-up внутри capture family:

- он полезен для verify-after-action и для дешёвого visual proof на небольшом фрагменте;
- он может стать мостом к будущему OCR fallback, не делая OCR primary mode;
- его не нужно смешивать с broad browser-aware или vision-heavy subsystem раньше shipped action core.

## Внешние источники и что именно из них взято

### 1. MCP Tools specification

Источник:

- [MCP Tools, spec 2025-06-18](https://modelcontextprotocol.io/specification/2025-06-18/server/tools)

Что взяли:

- форму `tools/list` / `tools/call`;
- `content` как список content blocks;
- `image` block с `data` и `mimeType`;
- `structuredContent` как канонический structured payload;
- правило совместимости: structured result полезно дублировать в `text` block.

### 2. MCP Transports specification

Источник:

- [MCP Transports, spec 2025-06-18](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports)

Что взяли:

- `STDIO` как product-ready transport;
- требование не писать в `stdout` ничего кроме валидных MCP messages;
- идею, что обычные логи должны уходить вне transport payload.

Именно отсюда следует текущий выбор diagnostics artifacts вместо обычного console logging.

### 3. MCP C# SDK

Источник:

- [McpServerToolAttribute, MCP C# SDK](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.Server.McpServerToolAttribute.html)

Что взяли:

- `ReadOnly`;
- `Idempotent`;
- `OpenWorld`;
- `UseStructuredContent`.

Это напрямую легло в annotations для `windows.capture`.

### 4. Windows Graphics Capture overview

Источник:

- [Screen capture - UWP applications](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture)

Что взяли:

- `Direct3D11CaptureFramePool`;
- `GraphicsCaptureSession`;
- `StartCapture`;
- `FrameArrived`;
- `ContentSize` как authoritative размер кадра на момент рендера;
- `Recreate` как канонический ответ на size drift frame pool;
- захват через `B8G8R8A8`;
- общую форму capture pipeline для первого native backend.

### 5. Win32 interop для window/monitor capture item

Источники:

- [IGraphicsCaptureItemInterop::CreateForWindow](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)
- [IGraphicsCaptureItemInterop::CreateForMonitor](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createformonitor)

Что взяли:

- сам interop entry point для `HWND`;
- отдельный interop entry point для `HMONITOR`;
- понимание, что desktop path в этом этапе лучше строить как monitor capture, а не как stitched virtual desktop.

### 6. SoftwareBitmap from Direct3D surface

Источник:

- [Create, edit, and save bitmap images - section about `SoftwareBitmap.CreateCopyFromSurfaceAsync`](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/imaging)

Что взяли:

- прямой переход от `IDirect3DSurface` к `SoftwareBitmap`;
- идею держать отдельную копию image data перед PNG encoding;
- дальнейший шаг через `BitmapEncoder`.

### 7. Native screen-copy fallback

Источник:

- [Graphics.CopyFromScreen Method](https://learn.microsoft.com/ru-ru/dotnet/api/system.drawing.graphics.copyfromscreen?view=windowsdesktop-10.0&viewFallbackFrom=dotnet-plat-ext-8.0)

Что взяли:

- сам native Windows desktop API path для bit-block transfer;
- минимальный desktop-only fallback semantics без смены внешнего MCP контракта.

## Итог

Этот observe/capture slice важен не потому, что он “умеет делать скриншоты”, а потому что он впервые делает `Okno` пригодным для короткого цикла:

1. увидеть окно;
2. выбрать его;
3. прикрепиться;
4. получить новый визуальный state;
5. опереться на него дальше.

Именно на этом фундаменте уже имеет смысл строить `UIA`, `wait/verify`, `click/type` и richer observe model.
