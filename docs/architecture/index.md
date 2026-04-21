# Архитектура Okno

## Текущий baseline

`Okno` строится как локальный `STDIO` MCP runtime для Windows 11, ориентированный на агентные сценарии `observe -> act -> verify`. Внутренний codename и project layout пока остаются `WinBridge`, но product-facing документация и runtime identity уже выровнены на `Okno`.

Product-ready delivery target на текущем этапе только один: `STDIO` как локальный процесс. HTTP/URL transport осознанно не входит в текущий контур и будет рассматриваться только после стабилизации `STDIO`.

## Основные слои

- `Host boundary`: MCP server, transport, tool registration, capability negotiation.
- `Runtime composition root`: тонкий `WinBridge.Runtime`, который собирает DI и не держит service implementation внутри себя.
- `Tooling`: `ToolNames` + `ToolContractManifest` + exporter как единый source of truth для tool contract.
- `Runtime guard layer`: conservative reporting-first readiness baseline для `okno.health`, который публикует domain/capability snapshot и не вводит hidden enforcement для уже shipped observe tools.
- `Runtime services`: session, diagnostics и shell-window logic в отдельных проектах.
- `Windows integration`: `Windows.Shell`, `Windows.Capture`, shipped `windows.uia_snapshot`, shipped `windows.wait` и click-first `windows.input` уже реализованы сейчас; broad input actions и `windows.clipboard_*` остаются следующими seams.
- `Diagnostics`: structured audit artifacts, human-readable summary, smoke harness, runbooks.

## Где смотреть дальше

- [capability-design-policy.md](capability-design-policy.md)
- [computer-use-win-surface.md](computer-use-win-surface.md)
- [layers.md](layers.md)
- [openai-computer-use-interop.md](openai-computer-use-interop.md)
- [observe-capture.md](observe-capture.md)
- [observability.md](observability.md)
- [reference-research-policy.md](reference-research-policy.md)
- [engineering-principles.md](engineering-principles.md)
- [adr-0001-stack-choice.md](adr-0001-stack-choice.md)
- [../product/index.md](../product/index.md)
