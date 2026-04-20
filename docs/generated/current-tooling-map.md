# Current Tooling Map

## Канонические инструменты

| Категория | Инструмент | Где задан |
| --- | --- | --- |
| SDK selection | `global.json` | `/global.json` |
| Build defaults | `Directory.Build.props` | `/Directory.Build.props` |
| Package versions | Central Package Management | `/Directory.Packages.props` |
| Build/test CLI | `dotnet` | solution-wide |
| MCP SDK | `ModelContextProtocol 1.1.0` | `src/WinBridge.Server`, `tests/WinBridge.Server.IntegrationTests` |
| Tool contract source | `ToolNames` + `ToolContractManifest` | `src/WinBridge.Runtime.Tooling` |
| Test framework | `xUnit` | `tests/*` |
| Runtime diagnostics | JSONL + summary files | `src/WinBridge.Runtime.Diagnostics/*` |
| Dev wrappers | PowerShell scripts | `/scripts` |

## Mechanical guardrails

- `Nullable=enable`
- `TreatWarningsAsErrors=true`
- `EnableNETAnalyzers=true`
- `AnalysisLevel=latest-recommended`

## Что уже автоматизировано

- restore
- build
- unit/integration test
- generated tool contract export
- MCP smoke over stdio
- latest artifact investigation

## Что пока только задокументировано

- clipboard и broad input action milestones из текущего roadmap
- расширение observability до OTel exporter
- HTTP transport после product-ready `STDIO`
