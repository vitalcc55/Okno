# Engineering Principles

## Архитектурная позиция

Okno строится как Windows-native MCP runtime с явными границами между:

- host boundary;
- runtime composition root;
- tool contract source of truth;
- runtime/domain services;
- Windows integration;
- diagnostics and evidence.

Полный tactical DDD в проекте пока не вводится. Текущий подход ближе к boundary-first architecture: доменные и infrastructural ответственности уже разделяются, но без навязывания избыточного словаря aggregates/entities/value objects там, где это ещё не даёт практической выгоды.

## DDD / TDD статус

### DDD

- Не заявляется как full DDD project.
- Используется DDD-like дисциплина границ: явные контракты, narrow services, отделение host от runtime, избегание хаотичной смешанной логики.

### TDD

- TDD не является догмой.
- Базовый инженерный режим — verification-first.
- Для каждой нетривиальной правки обязательны релевантные checks: build, tests, smoke, evidence.
- Для сложной логики и bugfixes тесты можно и нужно писать заранее, но требование проекта не “test-first любой ценой”, а “no unverified diff”.

## Строгость

Строгость в проекте сейчас задаётся механически:

- `Nullable=enable`
- `TreatWarningsAsErrors=true`
- `EnableNETAnalyzers=true`
- `AnalysisLevel=latest-recommended`
- `xUnit` unit/integration tests
- smoke harness поверх реального `STDIO` MCP runtime

Это означает, что предупреждения analyzers считаются частью инженерного контракта, а не необязательным шумом.

## Стиль кода и именования

### Общий стиль

- boring, explicit, predictable code;
- минимум магии и скрытых side effects;
- service/class names должны отражать ответственность;
- transport/host код не должен знать Windows детали глубже сервисных интерфейсов;
- runtime logic не должна полагаться на `stdout`, потому что он принадлежит MCP transport.
- tool contract не должен дублироваться вручную между C#, PowerShell и docs.

### Именование

- public types и services — существительные или устойчивые role-based имена (`RuntimeInfo`, `AuditLog`, `Win32WindowManager`);
- методы — глагольные и action-oriented (`ListWindows`, `AttachWindow`, `TryFocus`);
- тесты — описывают поведение, а не внутреннюю реализацию;
- product-facing имя во внешней документации — `Okno`;
- internal codename в namespace/project layout пока — `WinBridge`.
- source of truth для MCP tool names — `ToolNames`, а не строки, размазанные по коду и scripts.

## Transport policy

- Единственный current product-ready transport — `STDIO` local process.
- HTTP/URL server сейчас не входит в delivery baseline.
- Любая работа по HTTP должна идти только после того, как `STDIO` контур остаётся стабильным и проверяемым.

## Что считается хорошим изменением

- изменяет минимально достаточную поверхность;
- добавляет или сохраняет механическую проверяемость;
- обновляет docs, если меняет contract или workflow;
- оставляет доказуемый след в scripts, tests или artifacts.
