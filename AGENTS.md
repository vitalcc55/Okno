# AGENTS.md

## Карта проекта

- `Okno` — продуктовое имя проекта. Внутренний codename и namespace-слой пока остаются `WinBridge`.
- Source of truth по продукту больше не лежит в корне: используй [docs/product/index.md](docs/product/index.md), [docs/product/okno-spec.md](docs/product/okno-spec.md), [docs/product/okno-roadmap.md](docs/product/okno-roadmap.md), [docs/product/okno-vision.md](docs/product/okno-vision.md).
- Source of truth по OpenAI interop: [docs/architecture/openai-computer-use-interop.md](docs/architecture/openai-computer-use-interop.md), [docs/exec-plans/active/openai-computer-use-interop.md](docs/exec-plans/active/openai-computer-use-interop.md).
- Source of truth по policy использования reference repos и official docs: [docs/architecture/reference-research-policy.md](docs/architecture/reference-research-policy.md).
- Операционный source of truth для bootstrap: [docs/exec-plans/active/bootstrap-harness.md](docs/exec-plans/active/bootstrap-harness.md), [docs/generated/commands.md](docs/generated/commands.md), [docs/generated/project-interfaces.md](docs/generated/project-interfaces.md), [docs/generated/stack-research.md](docs/generated/stack-research.md), `docs/bootstrap/bootstrap-status.json`.
- Source of truth для tool contract: `src/WinBridge.Runtime.Tooling/ToolNames.cs` + `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`.

## Структура

- `src/WinBridge.Runtime` — thin composition root и DI wiring.
- `src/WinBridge.Runtime.Contracts` — DTO/records и runtime wire contracts.
- `src/WinBridge.Runtime.Tooling` — tool names, manifest, exporter.
- `src/WinBridge.Runtime.Diagnostics` — audit, evidence, tool execution boundary.
- `src/WinBridge.Runtime.Session` — session state и attach semantics.
- `src/WinBridge.Runtime.Windows.Shell` — top-level shell/window capability (`list/find/focus`).
- `src/WinBridge.Runtime.Windows.Capture` — первый реализованный observe/capture slice (`window` / `desktop` monitor capture), PNG artifacts, `Windows.Graphics.Capture` как основной путь с native fallback.
- `src/WinBridge.Runtime.Windows.UIA` — shipped `windows.uia_snapshot` и future `windows.uia_action`.
- `src/WinBridge.Runtime.Waiting` — shipped `windows.wait`.
- `src/WinBridge.Runtime.Windows.Input` / `Clipboard` — следующие action seams; пока без production implementation.
- `src/WinBridge.Server` — MCP host, tool registration, transport boundary.
- `tests/WinBridge.Runtime.Tests` — unit и structural checks.
- `tests/WinBridge.Server.IntegrationTests` — stdio/MCP smoke и integration checks.
- `scripts/` — единые entry points для bootstrap, verify, smoke, local CI и runbooks.
- `docs/product` — продуктовый source of truth (`vision`, `spec`, `roadmap`).
- `docs/architecture` — фактическая архитектура и observability-модель.
- `docs/generated` — инвентаризации стека, команд, интерфейсов и test matrix.
- `docs/bootstrap/bootstrap-status.json` — generated bootstrap status; не редактировать вручную.
- `references/` — локальный игнорируемый cache reference repos для инженерного сравнения; не является частью shipped surface и не коммитится.
- `artifacts/diagnostics` (включая `captures/`) и `artifacts/smoke` — локальные evidence packs; не коммитятся, но считаются каноническим investigation path.

## Guardrails

- `STDOUT` зарезервирован под MCP transport; любые diagnostics и human logs должны идти в файлы артефактов или в `stderr`.
- Новые MCP tools нельзя добавлять вручную в нескольких местах: сначала `ToolNames`, затем `ToolContractManifest`, затем export/docs/smoke/tests.
- `windows.capture` уже считается реализованным observe tool: при изменениях сохраняй MCP contract `structuredContent + image/png + local capture artifact` и синхронизируй smoke/tests/docs в том же цикле.
- Для новых capability slices (`focus`, `clipboard`, `input`, `wait`, `uia`, будущие observe/action tools) сначала применяй universal policy из [docs/architecture/capability-design-policy.md](docs/architecture/capability-design-policy.md): identity, fallback, false-success, scenario matrix и verification ladder должны быть определены до реализации.
- `docs/generated/*` и `docs/bootstrap/bootstrap-status.json` могут обновляться автоматически после `refresh-generated-docs.ps1` и `ci.ps1`. Если они изменились без ручной правки, это ожидаемое generated behavior, а не неожиданный user diff.
- `references/` использовать как secondary engineering source: сначала internal docs/spec/exec-plan, затем official Microsoft/MCP/OpenAI docs, и только потом reference repos для pattern comparison; reference repos не переопределяют platform semantics и contract honesty `Okno`.
- Для текущего продукта не подменять GUI-слой shell-автоматизацией; shell допустим только для repo operations, test harness и локальных dev-команд.
- Built-in OpenAI `computer use` не считать replacement для `Okno`: current local integration path остаётся repo-local MCP/plugin surface, а future compatibility должна приходить отдельным adapter-слоем без протекания OpenAI-specific contracts в `WinBridge.Runtime` / `WinBridge.Server`.
- Если меняется plugin install surface (`plugins/okno`, launcher, install hint, marketplace/install model), acceptance обязан включать proof для cache-installed copy, restart Codex/new thread и минимум один реальный read-only tool call (`okno.health` или `okno.contract`) уже из fresh-thread materialization path.
- Deferred public tools допустимы только при одном из двух условий: либо tool не публикуется как callable, либо integration tests доказывают честный `unsupported/deferred` invocation path без generic transport/invocation error.
- Для `windows.input` contract-boundary malformed request values должны классифицироваться validator-owned `invalid_request`: не оставляй ожидаемые user/request-level shape ошибки на default `System.Text.Json` binder exceptions, если этот слой уже находится внутри frozen `InputRequest` / `InputAction` / nested input DTO surface.
- Любая новая нетривиальная задача должна обновлять ExecPlan и соответствующие generated docs по факту проверок, а не по догадке.
- Если меняется tool contract или observability schema, синхронизируй [docs/generated/project-interfaces.md](docs/generated/project-interfaces.md) и [docs/architecture/observability.md](docs/architecture/observability.md) в том же цикле.
- Verification-first loop уже нормализован: `scripts/bootstrap.ps1` -> `scripts/build.ps1` -> `scripts/test.ps1` -> `scripts/smoke.ps1` -> `scripts/refresh-generated-docs.ps1`; для полного локального контура используй `scripts/ci.ps1`.
- Не запускай параллельно в одном worktree команды, которые делят `bin/obj`, поднимают local runtime/fixture процессы или перезаписывают generated/docs artifacts: `dotnet build`, `dotnet test`, `scripts/smoke.ps1`, `scripts/refresh-generated-docs.ps1`, `scripts/ci.ps1`, `scripts/codex/verify.ps1`. Для этого репозитория verification loop должен идти строго последовательно; если нужен и smoke, и docs refresh, сначала дождись завершения smoke, затем запускай refresh/verify.
