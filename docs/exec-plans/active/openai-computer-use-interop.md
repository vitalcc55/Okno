# ExecPlan: OpenAI computer use interop

Статус: planned
Создан: 2026-03-31

## Goal

Зафиксировать future compatibility track для OpenAI `computer use` без изменения ближайшего delivery order `windows.launch_process -> windows.open_target -> windows.input`.

## Non-goals

- не реализовывать adapter в рамках текущего workstream;
- не менять core runtime transport с `STDIO MCP`;
- не вводить OpenAI-specific DTO в `WinBridge.Runtime.Contracts`;
- не объявлять built-in `computer use` replacement для repo-local plugin/MCP path.

## Current repo fit

- `Okno` уже закрывает observe/verify/guardrails слой.
- `windows.input` уже имеет implemented public click-first MCP boundary для `move`, `click`, `double_click` и `click(button=right)`.
- Package D/E для `windows.input` ещё остаются открыты: runtime artifacts/events/materializer, smoke proof и fresh-host acceptance не считаются закрытыми этим interop plan.
- repo-local plugin `okno` уже даёт текущий локальный integration path для Codex.
- safety baseline уже даёт execution-policy foundation для будущих action tools.

## Decisions fixed up front

- `computer use` трактуется как protocol/adapter target, а не как core runtime requirement.
- `windows.input` проектируется с vocabulary discipline под типовой GUI action loop.
- `windows.capture` и `windows.wait` остаются отдельными tools и не поглощаются в action tool.
- `windows.launch_process` и `windows.open_target` остаются разделёнными.

## Activation criteria

- shipped `windows.input` в `click`-first объёме;
- action schema freeze для `windows.input`;
- Package D/E proof для input observability и smoke/fresh-host acceptance;
- docs/spec/roadmap синхронизированы с этой boundary;
- отдельный adapter package можно строить без изменения core runtime contracts.

## Integration points

- `docs/product/okno-vision.md`
- `docs/product/okno-spec.md`
- `docs/product/okno-roadmap.md`
- `docs/architecture/openai-computer-use-interop.md`
- будущий adapter package вне `WinBridge.Runtime` / `WinBridge.Server`

## References

- [Computer use](https://developers.openai.com/api/docs/guides/tools-computer-use)
- [Skills](https://developers.openai.com/api/docs/guides/tools-skills)
- [MCP and Connectors](https://developers.openai.com/api/docs/guides/tools-connectors-mcp)
- [Shell + Skills + Compaction](https://developers.openai.com/blog/skills-shell-tips)
- [Codex app on Windows](https://developers.openai.com/codex/app/windows)
