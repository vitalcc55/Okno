# CHANGELOG

Политика: фиксировать только инженерно значимые изменения, влияющие на operating model, control plane, архитектуру, проверки или контракт инструментов.

## 2026-03-12 16:16

- Инициализирован harness bootstrap: созданы `docs/`, ExecPlan, верхнеуровневый `AGENTS.md`, solution/project skeleton и baseline-конфиг для `net8.0-windows`.
- Добавлен рабочий `STDIO` MCP skeleton runtime на `C# / .NET 8`, включая `windows.list_windows`, `windows.attach_window`, `windows.focus_window` и честный deferred tool contract.
- Нормализованы `scripts/bootstrap.ps1`, `scripts/build.ps1`, `scripts/test.ps1`, `scripts/smoke.ps1`, `scripts/ci.ps1`, `scripts/investigate.ps1` и `scripts/codex/*`.
- Проверки подтверждены командами `dotnet build`, `dotnet test`, `powershell -File scripts/smoke.ps1` и `powershell -File scripts/ci.ps1`.
- Продуктовое имя выровнено на `Okno`, product docs перенесены в `docs/product/`, добавлены `README.md` и `docs/architecture/engineering-principles.md`.
