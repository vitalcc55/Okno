# AGENTS.md

## Карта проекта

- `Okno` — продуктовое имя проекта. Внутренний codename и namespace-слой пока остаются `WinBridge`.
- Source of truth по продукту больше не лежит в корне: используй [docs/product/index.md](docs/product/index.md), [docs/product/okno-spec.md](docs/product/okno-spec.md), [docs/product/okno-roadmap.md](docs/product/okno-roadmap.md), [docs/product/okno-vision.md](docs/product/okno-vision.md).
- Операционный source of truth для bootstrap: [docs/exec-plans/active/bootstrap-harness.md](docs/exec-plans/active/bootstrap-harness.md), [docs/generated/commands.md](docs/generated/commands.md), [docs/generated/project-interfaces.md](docs/generated/project-interfaces.md), [docs/generated/stack-research.md](docs/generated/stack-research.md), `docs/bootstrap/bootstrap-status.json`.
- Source of truth для tool contract: `src/WinBridge.Runtime.Tooling/ToolNames.cs` + `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`.

## Структура

- `src/WinBridge.Runtime` — thin composition root и DI wiring.
- `src/WinBridge.Runtime.Contracts` — DTO/records и runtime wire contracts.
- `src/WinBridge.Runtime.Tooling` — tool names, manifest, exporter.
- `src/WinBridge.Runtime.Diagnostics` — audit, evidence, tool execution boundary.
- `src/WinBridge.Runtime.Session` — session state и attach semantics.
- `src/WinBridge.Runtime.Windows.Shell` — top-level shell/window capability (`list/find/focus`).
- `src/WinBridge.Runtime.Windows.UIA` / `Capture` / `Input` / `Clipboard` / `Waiting` — будущие capability seams без implementation.
- `src/WinBridge.Server` — MCP host, tool registration, transport boundary.
- `tests/WinBridge.Runtime.Tests` — unit и structural checks.
- `tests/WinBridge.Server.IntegrationTests` — stdio/MCP smoke и integration checks.
- `scripts/` — единые entry points для bootstrap, verify, smoke, local CI и runbooks.
- `docs/product` — продуктовый source of truth (`vision`, `v1 spec`, `roadmap`).
- `docs/architecture` — фактическая архитектура и observability-модель.
- `docs/generated` — инвентаризации стека, команд, интерфейсов и test matrix.
- `docs/bootstrap/bootstrap-status.json` — generated bootstrap status; не редактировать вручную.
- `artifacts/diagnostics` и `artifacts/smoke` — локальные evidence packs; не коммитятся, но считаются каноническим investigation path.

## Guardrails

- `STDOUT` зарезервирован под MCP transport; любые diagnostics и human logs должны идти в файлы артефактов или в `stderr`.
- Новые MCP tools нельзя добавлять вручную в нескольких местах: сначала `ToolNames`, затем `ToolContractManifest`, затем export/docs/smoke/tests.
- `docs/generated/*` и `docs/bootstrap/bootstrap-status.json` могут обновляться автоматически после `refresh-generated-docs.ps1` и `ci.ps1`. Если они изменились без ручной правки, это ожидаемое generated behavior, а не неожиданный user diff.
- Для V1 не подменять GUI-слой shell-автоматизацией; shell допустим только для repo operations, test harness и локальных dev-команд.
- Любая новая нетривиальная задача должна обновлять ExecPlan и соответствующие generated docs по факту проверок, а не по догадке.
- Если меняется tool contract или observability schema, синхронизируй [docs/generated/project-interfaces.md](docs/generated/project-interfaces.md) и [docs/architecture/observability.md](docs/architecture/observability.md) в том же цикле.
- Verification-first loop уже нормализован: `scripts/bootstrap.ps1` -> `scripts/build.ps1` -> `scripts/test.ps1` -> `scripts/refresh-generated-docs.ps1` -> `scripts/smoke.ps1`; для полного локального контура используй `scripts/ci.ps1`.
