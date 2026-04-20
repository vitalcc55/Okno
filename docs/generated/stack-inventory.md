# Stack Inventory

> Generated file. Refreshed by `scripts/refresh-generated-docs.ps1`.

## Текущий срез репозитория

`Okno` больше не пустой репозиторий со спецификацией без реализации. Текущий runtime уже закрепляет boring baseline для Windows-native MCP.

## Языки и рантаймы

- `C# 12 / .NET 8` через SDK-style projects.
- `PowerShell` для локального control plane и smoke/investigation workflows.
- `Markdown` для durable repo memory.

## Текущее дерево подсистем

| Подсистема | Путь | Стек | Статус |
| --- | --- | --- | --- |
| Runtime composition root | `src/WinBridge.Runtime` | `net8.0-windows10.0.19041.0` | Работает |
| Runtime contracts | `src/WinBridge.Runtime.Contracts` | `net8.0-windows10.0.19041.0` | Работает |
| Runtime tooling | `src/WinBridge.Runtime.Tooling` | `net8.0-windows10.0.19041.0` | Работает |
| Runtime diagnostics | `src/WinBridge.Runtime.Diagnostics` | `net8.0-windows10.0.19041.0` | Работает |
| Runtime session | `src/WinBridge.Runtime.Session` | `net8.0-windows10.0.19041.0` | Работает |
| Windows shell | `src/WinBridge.Runtime.Windows.Shell` | `net8.0-windows10.0.19041.0` | Работает |
| Windows UIA slice | `src/WinBridge.Runtime.Windows.UIA, src/WinBridge.Runtime.Windows.UIA.Hosting, src/WinBridge.Runtime.Windows.UIA.Worker` | `net8.0-windows10.0.19041.0` | Работает |
| Public wait slice | `src/WinBridge.Runtime.Waiting` | `net8.0-windows10.0.19041.0` | Работает |
| Input and clipboard capability seams | `src/WinBridge.Runtime.Windows.Input, src/WinBridge.Runtime.Windows.Clipboard` | `net8.0-windows10.0.19041.0` | Input click-first работает; clipboard deferred |
| MCP host | `src/WinBridge.Server` | `net8.0-windows10.0.19041.0, ModelContextProtocol 1.1.0` | Работает |
| Unit tests | `tests/WinBridge.Runtime.Tests` | `xUnit` | Работает |
| Integration smoke | `tests/WinBridge.Server.IntegrationTests` | `xUnit + raw stdio JSON-RPC` | Работает |
| Dev control plane | `scripts/*.ps1` | `PowerShell` | Работает |
| Repo memory | `AGENTS.md, docs/` | `Markdown` | Работает |

## Package/build/tooling map

- `global.json` -> SDK 8.0.401
- Central package versions: `Directory.Packages.props`
- Build/analyzer baseline: `Directory.Build.props`
- Formatting/style baseline: `.editorconfig`
- Package manager: NuGet via `dotnet`
- Tool contract source of truth: `ToolNames` + `ToolContractManifest`

## Осознанно отсутствует в bootstrap

- Docker/Compose/devcontainer
- HTTP transport как рабочий delivery mode
- clipboard production implementation and broad input actions beyond click-first
- external observability backend