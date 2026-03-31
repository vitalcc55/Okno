# OpenAI Computer Use Interop

## Зачем нужен этот документ

Этот документ фиксирует, как `Okno` должен соотноситься с `shell`, `skills`, `MCP` и `computer use` в экосистеме OpenAI, и как future compatibility нужно встраивать без поломки текущего Windows-native продукта.

Он намеренно не меняет ближайший V1 delivery order и не объявляет built-in `computer use` текущим runtime requirement.

## Ключевая позиция

`Okno` не конкурирует с OpenAI tool layers и не должен пытаться заменить их все одним runtime.

Правильное разбиение слоёв:

- `shell` — terminal/code execution;
- `skills` — routing/procedure layer;
- `MCP` — transport/integration boundary;
- `computer use` — внешний action protocol / compatibility target;
- `Okno` — Windows-native observe/verify/guardrails runtime.

## Что это значит для текущего продукта

- Для локального `Codex app/CLI/IDE` основной путь остаётся `shell + Okno(MCP/plugin) + skills`.
- Built-in `computer use` не нужен для того, чтобы `Okno V1` уже был полезным.
- Current repo-local plugin `okno` остаётся каноническим local integration entry point.
- Любой будущий bridge к OpenAI `computer use` должен быть adapter-слоем поверх `Okno`, а не частью core runtime.

## Architectural boundary

### Что остаётся в core runtime

- `windows.list_windows`
- `windows.attach_window`
- `windows.capture`
- `windows.uia_snapshot`
- `windows.wait`
- `okno.health`
- будущий `windows.launch_process`
- будущий `windows.open_target`
- будущий `windows.input`

### Что не должно входить в core runtime

- Responses API-specific payloads
- `computer_call` / `computer_call_output` transport glue
- OpenAI session orchestration
- adapter-specific coordinate remapping state
- compatibility shims, которые не нужны собственному MCP contract Okno

## Input compatibility target

Future `windows.input` нужно проектировать так, чтобы vocabulary было совместимо с типовым GUI action loop:

- `move`
- `click`
- `double_click`
- `drag`
- `scroll`
- `type`
- `keypress`

При этом остаются Okno-native расширения:

- `right_click`
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
- оставаться опциональным слоем, который можно развивать отдельно от V1 runtime.

## Activation criteria

Эту compatibility track имеет смысл активировать только когда:

1. `windows.input` shipped хотя бы в `click`-first объёме;
2. action schema `windows.input` зафиксирована;
3. сохранён split `windows.launch_process` / `windows.open_target`;
4. есть отдельный exec-plan для adapter layer;
5. future interop не ломает local-first MCP/plugin path.

## Что не делать раньше времени

- не перестраивать roadmap вокруг built-in `computer use`;
- не смешивать OpenAI API contracts с `WinBridge.Runtime.Contracts`;
- не встраивать adapter code в `WinBridge.Runtime` или `WinBridge.Server`;
- не объявлять compatibility shipped до production-ready `windows.input`.

## Official references

- [Computer use](https://developers.openai.com/api/docs/guides/tools-computer-use)
- [Skills](https://developers.openai.com/api/docs/guides/tools-skills)
- [MCP and Connectors](https://developers.openai.com/api/docs/guides/tools-connectors-mcp)
- [Shell + Skills + Compaction](https://developers.openai.com/blog/skills-shell-tips)
- [Codex app on Windows](https://developers.openai.com/codex/app/windows)
