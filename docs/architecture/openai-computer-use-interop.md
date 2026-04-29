# OpenAI Computer Use Interop

Статус: historical/superseded.

Этот документ оставлен как запись прежнего архитектурного решения: не тащить
OpenAI-specific DTO и adapter glue в core runtime. После появления
`computer-use-win` как основного Codex-facing plugin surface отдельный
OpenAI-native adapter больше не является активным направлением разработки.

Текущий практический путь описан в:

- `docs/architecture/computer-use-win-surface.md`
- `docs/architecture/computer-use-win-next-actions.md`
- `docs/exec-plans/completed/completed-2026-04-28-computer-use-win-next-actions.md`

## Зачем нужен этот документ

Этот документ фиксирует, как `Okno` должен соотноситься с `shell`, `skills`, `MCP` и `computer use` в экосистеме OpenAI, и как future compatibility нужно встраивать без поломки текущего Windows-native продукта.

Он намеренно не менял ближайший delivery order и не объявлял built-in
`computer use` текущим runtime requirement. Это решение закрыто: основной
продуктовый путь теперь идёт через `computer-use-win`, без отдельного
OpenAI-native adapter.

## Ключевая позиция

`Okno` не конкурирует с OpenAI tool layers и не должен пытаться заменить их все одним runtime.
Для текущего продукта это означает: развиваем Windows-native
`computer-use-win` plugin, а не отдельный OpenAI adapter.

Правильное разбиение слоёв:

- `shell` — terminal/code execution;
- `skills` — routing/procedure layer;
- `MCP` — transport/integration boundary;
- `computer use` — внешний action protocol / compatibility target;
- `Okno` — Windows-native observe/verify/guardrails runtime.

## Что это значит для текущего продукта

- Для локального `Codex app/CLI/IDE` основной путь теперь идёт как `shell + computer-use-win plugin + skills`, где `Okno` остаётся внутренним engine/runtime.
- Built-in `computer use` не нужен для того, чтобы текущий `Okno` уже был полезным.
- Current public-facing local integration entry point должен быть `computer-use-win`, а не low-level engine plugin narrative.
- Official OpenAI docs и их sample repos дополнительно подтверждают этот выбор:
  mature structured harness/MCP integration не нужно перестраивать вокруг
  built-in visual loop, если проект уже имеет свой product-ready local path.
- `computer use` guide отдельно нормализует screenshot-first цикл: первый turn
  часто начинается со screenshot, а после каждого action batch harness должен
  вернуть updated screenshot как first-class image input. Для `Okno` это
  усиливает `get_app_state` / `windows.capture` как отдельные observe steps и
  не поддерживает деградацию screenshot до одного только `artifactPath`.
- `images-vision` отдельно подтверждает, что spatially sensitive
  computer-use screenshots должны сохранять original/full-fidelity detail либо
  явный coordinate remap после downscale, а `codex/mcp` и MCP tool guidance
  скорее добавляют client-side narrowing/ops правила, чем меняют runtime
  архитектуру.
- Если когда-нибудь появится новый внешний OpenAI adapter requirement, он
  должен быть отдельным новым ExecPlan. Текущий roadmap не считает его
  обязательным слоем.

## Architectural boundary

### Что остаётся в core runtime

- `windows.list_windows`
- `windows.attach_window`
- `windows.capture`
- `windows.uia_snapshot`
- `windows.wait`
- `okno.health`
- `windows.launch_process`
- `windows.open_target`
- `windows.input` click-first public boundary

Current `windows.input` state for this compatibility layer:

- Package D observability is already landed: factual input runtime results produce `input.runtime.completed` and `artifacts/diagnostics/<run_id>/input/input-*.json`.
- Package E smoke/fresh-host acceptance закрыт фактическим proof for the click-first subset: canonical smoke performs a real helper textbox click through `windows.input`, verifies focus through `windows.wait`, and repeats publication/binding checks in a fresh staged MCP host.

### Что не должно входить в core runtime

- Responses API-specific payloads
- `computer_call` / `computer_call_output` transport glue
- OpenAI session orchestration
- adapter-specific coordinate remapping state
- compatibility shims, которые не нужны собственному MCP contract Okno

## Input compatibility target

`windows.input` уже shipped в click-first public boundary и дальше должен расширяться так, чтобы vocabulary оставалось совместимо с типовым GUI action loop:

- `move`
- `click`
- `double_click`
- `drag`
- `scroll`
- `type`
- `keypress`

При этом остаются Okno-native расширения:

- `click(button=right)` уже shipped как quiet path для правой кнопки, а не отдельный public action literal
- `hotkey`
- `paste`

И остаются отдельными tools:

- `windows.capture`
- `windows.wait`
- `windows.launch_process`
- `windows.open_target`

## Adapter model

Правильный interop path:

```text
computer_call.actions[]
    -> adapter layer
    -> Okno tools / runtime services
    -> fresh capture / state
    -> computer_call_output
```

Adapter должен:

- маппить внешний action vocabulary на `Okno`;
- хранить coordinate-space / capture remap metadata;
- не менять core semantics shipped tools;
- оставаться опциональным слоем, который можно развивать отдельно от текущего runtime.

## Activation criteria

Эту compatibility track имеет смысл активировать только когда:

1. `windows.input` shipped хотя бы в `click`-first объёме;
2. action schema `windows.input` зафиксирована;
3. Package D observability для `windows.input` landed и расследуется через `input.runtime.completed` / `input/input-*.json`;
4. Package E smoke/fresh-host acceptance для `windows.input` закрыт фактическим proof;
5. сохранён split `windows.launch_process` / `windows.open_target`;
6. есть отдельный exec-plan для adapter layer;
7. future interop не ломает local-first MCP/plugin path.

## Что не делать раньше времени

- не перестраивать roadmap вокруг built-in `computer use`;
- не смешивать OpenAI API contracts с `WinBridge.Runtime.Contracts`;
- не встраивать adapter code в `WinBridge.Runtime` или `WinBridge.Server`;
- не объявлять adapter compatibility shipped без отдельного adapter-layer exec-plan, даже при уже proof-backed click-first `windows.input`.

## Official references

- [Computer use](https://developers.openai.com/api/docs/guides/tools-computer-use)
- [Images and vision](https://developers.openai.com/api/docs/guides/images-vision)
- [Skills](https://developers.openai.com/api/docs/guides/tools-skills)
- [MCP and Connectors](https://developers.openai.com/api/docs/guides/tools-connectors-mcp)
- [Tools overview](https://developers.openai.com/learn/tools)
- [Guide to Using the Responses API's MCP Tool](https://developers.openai.com/cookbook/examples/mcp/mcp_tool_guide)
- [Docs MCP](https://developers.openai.com/learn/docs-mcp)
- [Codex MCP](https://developers.openai.com/codex/mcp)
- [Shell + Skills + Compaction](https://developers.openai.com/blog/skills-shell-tips)
- [Codex app on Windows](https://developers.openai.com/codex/app/windows)
- [openai-cua-sample-app](https://github.com/openai/openai-cua-sample-app)
- [openai-testing-agent-demo](https://github.com/openai/openai-testing-agent-demo)
