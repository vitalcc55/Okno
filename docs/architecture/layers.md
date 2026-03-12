# Слои и границы

| Слой | Ответственность | Текущий статус | Граница |
| --- | --- | --- | --- |
| `src/WinBridge.Server` | MCP host, `STDIO` transport, tool surface | Реализуется в bootstrap | Не знает деталей Win32/UIA beyond service interfaces |
| `src/WinBridge.Runtime` | composition root и DI wiring | Реализуется в bootstrap | Не держит domain/service implementation внутри себя |
| `src/WinBridge.Runtime.Contracts` | DTO и runtime wire contracts | Реализуется в bootstrap | Не содержит host/IO logic |
| `src/WinBridge.Runtime.Tooling` | tool names, manifest, exporter | Реализуется в bootstrap | Один source of truth для tool contract |
| `src/WinBridge.Runtime.Diagnostics` | audit, evidence, tool boundary execution | Реализуется в bootstrap | Не знает MCP transport |
| `src/WinBridge.Runtime.Session` | session state и attach semantics | Реализуется в bootstrap | Не тянет diagnostics config внутрь domain logic |
| `src/WinBridge.Runtime.Windows.Shell` | Enum/find/focus top-level windows | Реализуется в bootstrap | Не включает UIA/capture/input/clipboard/wait |
| future capability projects | `UIA` / `Capture` / `Input` / `Clipboard` / `Waiting` interfaces | Подготовлены как seams | Без fake implementations до реальной потребности |
| `Diagnostics artifacts` | JSONL audit, summary, smoke evidence | Реализуется в bootstrap | Артефакты воспроизводимы через scripts |

## Инварианты bootstrap

- `observe -> attach -> verify contract` должен быть доступен без UIA и capture.
- `IWindowManager` остаётся только shell-window interface и не раздувается новыми capability-методами.
- Все side effects на ОС должны быть явными и отличимыми от read-only tools.
- Stub-tools допустимы только если честно возвращают `unsupported`/`deferred`, а не имитируют успех.
