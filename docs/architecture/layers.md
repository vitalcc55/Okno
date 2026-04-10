# Слои и границы

| Слой | Ответственность | Текущий статус | Граница |
| --- | --- | --- | --- |
| `src/WinBridge.Server` | MCP host, `STDIO` transport, tool surface | Реализуется в bootstrap | Не знает деталей Win32/UIA beyond service interfaces |
| `src/WinBridge.Runtime` | composition root и DI wiring | Реализуется в bootstrap | Не держит domain/service implementation внутри себя |
| `src/WinBridge.Runtime.Contracts` | DTO и runtime wire contracts | Реализуется в bootstrap | Не содержит host/IO logic |
| `src/WinBridge.Runtime.Tooling` | tool names, manifest, exporter | Реализуется в bootstrap | Один source of truth для tool contract |
| `src/WinBridge.Runtime.Diagnostics` | audit, evidence, tool boundary execution | Реализуется в bootstrap | Не знает MCP transport |
| `src/WinBridge.Runtime.Session` | session state и attach semantics | Реализуется в bootstrap | Не тянет diagnostics config внутрь domain logic |
| `src/WinBridge.Runtime.Windows.Display` | monitor inventory, monitor identity, monitor lookup | Реализуется в display/activation slice | Не знает MCP transport и не управляет окнами |
| `src/WinBridge.Runtime.Windows.Shell` | Enum/find/focus/activate top-level windows | Реализуется в bootstrap | Не владеет monitor inventory и не включает UIA/capture/input/clipboard/wait |
| `src/WinBridge.Runtime.Windows.Capture` | window/desktop monitor capture, PNG encoding, capture artifacts | Реализован первый observe slice | `WGC` как основной path, native fallback без смены MCP contract и без hidden restore |
| `src/WinBridge.Runtime.Windows.UIA` + hosting/worker companion | public `windows.uia_snapshot`, UIA runtime/evidence path, isolated worker boundary | Реализован semantic observe slice | Public MCP handler опирается на runtime service, а worker sidecar ограничивает timeout/process-failure boundary |
| `src/WinBridge.Runtime.Waiting` | public `windows.wait`, runtime evidence path, polling wait orchestration | Реализован shipped wait slice | Public MCP handler опирается на canonical runtime service и не держит legacy deferred stub |
| future capability projects | `Input` / `Clipboard` interfaces | Подготовлены как seams | Без fake implementations до реальной потребности |
| `Diagnostics artifacts` | JSONL audit, summary, smoke evidence | Реализуется в bootstrap | Артефакты воспроизводимы через scripts |

## Инварианты bootstrap

- `observe -> attach -> verify contract` должен быть доступен без UIA.
- `IWindowManager` остаётся только shell-window interface и не раздувается новыми capability-методами.
- `IMonitorManager` остаётся единственным source of truth для monitor inventory и public `monitorId`.
- Все side effects на ОС должны быть явными и отличимыми от read-only tools.
- Stub-tools допустимы только если честно возвращают `unsupported`/`deferred`, а не имитируют успех.
- Для каждого deferred public tool integration coverage обязана доказывать одно из двух состояний: либо tool не публикуется как callable, либо invocation возвращает честный structured `unsupported/deferred` path без generic transport/invocation error.
