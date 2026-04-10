# Stack Research

## 1. .NET / C# baseline

### Что уже используется

- SDK-style `csproj`
- `global.json` для pin SDK
- `Directory.Build.props` для строгого build baseline
- Central Package Management
- `xUnit`

### Что рекомендуют official sources

- Закреплять SDK через [`global.json`](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json), если важно воспроизводимое поведение локально и в CI.
- Включать строгий build loop через [`Nullable`, analyzers и warnings-as-errors](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/errors-warnings) и опираться на [source code analysis в .NET](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview).
- Для долгоживущих типов и DTO выражать контракты через систему типов C#, nullability и immutable models ([C# type system fundamentals](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/types/)).
- Централизовать версии пакетов через [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management).

### Где проект соответствует

- Строгий baseline включён в `Directory.Build.props`.
- Версии пакетов централизованы.
- Build/test loop идёт через канонический `dotnet` CLI.

### Где есть drift / tech debt

- Пока только два проектных слоя (`Runtime` и `Server`), а не более детальная domain/application/infrastructure split.
- Нет architectural tests на зависимости между сборками.

## 2. MCP / tool runtime

### Что уже используется

- `ModelContextProtocol 1.1.0`
- `STDIO` transport
- tool registration через `McpServerToolType` + `McpServerTool`

### Official guidance

- Официальный C# SDK рекомендует `AddMcpServer().WithStdioServerTransport()` и tool registration через SDK builder ([Build servers with the C# SDK](https://modelcontextprotocol.github.io/csharp-sdk/quickstart/quickstart.html?tabs=server)).
- Спецификация MCP фиксирует для `STDIO`, что `stdout` зарезервирован под protocol messages, а diagnostics допустимы через `stderr` ([STDIO transport spec](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#stdio)).
- Lifecycle `initialize -> initialized -> requests` должен соблюдаться явно ([Lifecycle spec](https://modelcontextprotocol.io/specification/2025-06-18/basic/lifecycle)).

### Где проект соответствует

- Runtime общается через `STDIO`.
- `stdout` не используется для обычного логирования.
- Smoke harness проходит через реальный lifecycle и `tools/list`.

### Где есть drift / tech debt

- Удобные C# APIs SDK остаются чувствительными к версии пакета и требуют внимательного watch на `MCPEXP001`.

## 3. Windows automation stack

### Что уже используется

- Top-level window enumeration/focus через Win32 P/Invoke.
- Public `windows.uia_snapshot` через managed UIA (`System.Windows.Automation`) с isolated worker boundary, JSON artifacts и MCP-facing contract.

### Official guidance

- Для semantic automation базовым официальным направлением остаётся [UI Automation overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-overview).
- Для capture roadmap-spec опирается на Windows Graphics Capture; официальный entry point описан в [Screen capture guidance for Windows](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture).

### Где проект соответствует

- Capture и UIA в текущем репозитории уже реализованы и подтверждаются build/test/smoke.
- Deferred state сохраняется только у следующих slices: input, clipboard и semantic actions.

## 4. Observability

### Что уже используется

- File-based structured audit
- Human-readable summary
- `ActivitySource` correlation ids

### Official guidance

- Microsoft рекомендует строить observability вокруг standard .NET diagnostics primitives и OTel-compatible workflows ([Observability with OpenTelemetry in .NET](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel)).

### Почему выбран текущий путь

- Для локального `STDIO` MCP server внешние exporters пока не нужны.
- File artifacts удобнее для agent-led расследований и не конфликтуют со спецификой transport.

## 5. Testing

### Что уже используется

- `dotnet test`
- unit tests
- integration test over raw stdio JSON-RPC
- smoke script

### Official guidance

- Microsoft рассматривает `build -> test -> additional validation` как естественный baseline для .NET repos ([Testing in .NET](https://learn.microsoft.com/en-us/dotnet/core/testing/)).
- `dotnet format` — стандартный SDK-level entry point для formatting checks, если проект решит добавить их позже ([dotnet format](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format)).

## 6. OpenAI / harness engineering guidance

### Использованные официальные материалы

- [Harness Engineering](https://openai.com/index/harness-engineering/)
- [Unrolling the Codex Agent Loop](https://openai.com/index/unrolling-the-codex-agent-loop/)
- [How OpenAI uses Codex](https://openai.com/index/how-openai-uses-codex/)
- [Computer use](https://developers.openai.com/api/docs/guides/tools-computer-use)
- [Skills](https://developers.openai.com/api/docs/guides/tools-skills)
- [MCP and Connectors](https://developers.openai.com/api/docs/guides/tools-connectors-mcp)
- [Shell + Skills + Compaction](https://developers.openai.com/blog/skills-shell-tips)
- [Codex app on Windows](https://developers.openai.com/codex/app/windows)

### Что реально применено в этом репозитории

- Repo-local source of truth вынесен в `AGENTS.md`, `docs/`, scripts и skill.
- Verification-first loop материализован как команды, а не как устная договорённость.
- Evidence pack хранится в артефактах запуска, а не только в чате.
- `shell`, `skills`, `MCP` и `computer use` трактуются как соседние слои, а не как взаимозаменяемые части одного продукта.
- Текущий локальный integration path для Codex остаётся `shell + Okno(MCP/plugin) + skills`; built-in `computer use` не считается немедленной зависимостью для текущего `Okno`.
- Future OpenAI interop фиксируется как compatibility track для `windows.input` и отдельного adapter-слоя, а не как причина смешивать OpenAI-specific contracts с `WinBridge.Runtime`.
