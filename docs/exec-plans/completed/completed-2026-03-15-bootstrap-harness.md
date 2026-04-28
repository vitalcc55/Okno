# ExecPlan: Okno Harness Bootstrap

Статус: completed. Архивируется в `docs/exec-plans/completed/`, потому что
bootstrap scaffold, runtime skeleton, control-plane scripts, observe/capture
slice, display/activation hardening and generated docs уже закрыты и больше не
являются active workstream.

## Контекст

Репозиторий пока содержит только product-spec и roadmap. Цель bootstrap — превратить его в legible, controllable, testable Windows-native `.NET` MCP runtime с reproducible scripts, docs и observability baseline.

## Границы

- Входит: repo-memory, solution scaffold, minimal MCP runtime, safe window/session vertical slice, first observe/capture slice, observability artifacts, scripts, verification.
- Не входит: полноценные UIA/input/clipboard реализации, OCR, HTTP transport, browser-specific layer beyond docs/planning.

## Milestones

1. Завести durable memory и централизованный build baseline.
2. Поднять `net8.0-windows` solution с runtime/server/tests.
3. Реализовать MCP skeleton и безопасный `observe -> attach` loop.
4. Добавить audit/summary artifacts и investigation workflow.
5. Нормализовать scripts и local CI equivalent.
6. Обновить generated docs по фактическим результатам команд.

## Acceptance criteria

- `dotnet restore`, `build`, `test` и smoke выполняются через documented entry points.
- MCP server стартует по `STDIO`, объявляет tool contract и проходит smoke-call минимум для `okno.health` и `windows.list_windows`.
- Есть структурированный audit artifact со стабильной schema version.
- Есть human-readable investigation path без чтения сырых stdout/stderr дампов.

## Validation commands

- `powershell -File scripts/bootstrap.ps1`
- `powershell -File scripts/build.ps1`
- `powershell -File scripts/test.ps1`
- `powershell -File scripts/smoke.ps1`
- `powershell -File scripts/ci.ps1`

## Recovery / rollback

- Если MCP SDK окажется несовместимым с текущим skeleton, fallback — вручную зарегистрированный tool handler без смены solution/layout.
- Если конкретный Windows API требует additional SDK/workload, фиксируем это в tracker и оставляем contract stub.

## Decisions log

- 2026-03-12: выбран `net8.0-windows10.0.19041.0` и `global.json` на SDK `8.0.401`.
- 2026-03-12: control plane строится через PowerShell wrappers, без `make/just`, так как проект Windows-only.
- 2026-03-12: observability baseline — file-based structured audit + summary, без обязательного внешнего telemetry backend.
- 2026-03-12: product-ready transport scope ограничен `STDIO` local process; HTTP transport отложен до post-STDIO stage.
- 2026-03-13: для новых capability slices вводится отдельная design/verification policy по identity, fallback, false-success и scenario matrix; source of truth — `docs/architecture/capability-design-policy.md`.
- 2026-03-15: display/activation slice реализуется через отдельный `Windows.Display` seam, а `windows.capture(window)` не получает hidden restore semantics; restore/focus проверяется отдельным `windows.activate_window`.

## Progress

- `done`: инвентаризация репозитория и stack decision.
- `done`: bootstrap scaffold, runtime skeleton, observability vertical slice.
- `done`: control plane scripts и local CI equivalent.
- `done`: `windows.capture` с window/desktop monitor observe loop, PNG artifact и MCP image result.
- `done`: display/activation hardening: `windows.list_monitors`, `windows.activate_window`, monitor-aware `windows.list_windows` и explicit desktop capture по `monitorId`.
- `done`: generated docs по фактическим проверкам.
- `done`: universal capability design policy для следующих feature slices.
