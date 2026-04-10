# Reference Research Policy

## Зачем нужен этот документ

`Okno` уже дорос до состояния, где инженерные решения удобно и полезно сверять не только с official docs, но и с живыми reference repos. При этом project contract нельзя строить как смесь:

- случайно подсмотренных implementation details из чужого репо;
- устных договорённостей из чатов;
- непроверенных предположений про Win32/.NET/MCP/OpenAI semantics.

Этот документ фиксирует, как использовать:

- локальный cache reference repos в `references/`;
- official documentation;
- внутренние docs и exec-plans самого `Okno`;

так, чтобы результатом были более сильные design decisions, а не drift от продукта.

## Главный принцип

Reference repos нужны как **источник инженерных идей и сравнительных решений**, но не как source of truth для поведения `Okno`.

Source of truth для `Okno` всегда строится в таком порядке:

1. текущий repo state `Okno`;
2. official platform documentation;
3. reference repos;
4. локальные эксперименты/тесты для спорных мест.

Если reference repo противоречит official docs или текущему продуктному контракту `Okno`, побеждает не reference repo.

## Иерархия источников

### 1. Внутренние документы `Okno`

Сначала ищи ответ внутри проекта:

- `docs/product/okno-spec.md`
- `docs/product/okno-roadmap.md`
- `docs/product/okno-vision.md`
- `docs/architecture/capability-design-policy.md`
- `docs/architecture/observability.md`
- completed/active exec-plans
- `src/WinBridge.Runtime.Tooling/ToolNames.cs`
- `src/WinBridge.Runtime.Tooling/ToolContractManifest.cs`

Это отвечает на вопрос:

- что продукт уже обещает;
- какие invariants уже приняты;
- где текущий repo уже выбрал одну из нескольких возможных стратегий.

### 2. Official documentation

Для platform semantics authoritative являются только official docs:

- Microsoft Learn / Win32 / .NET API docs для Windows и `System.Diagnostics.Process`, UIA, shell, desktop/session/integrity;
- MCP specification для tool/result/`structuredContent`/`isError`;
- OpenAI official docs для `computer use`, tools, MCP/connectors, skills, Codex/Windows interop.

Это отвечает на вопрос:

- что платформа действительно гарантирует;
- где behaviour стабильный, а где best-effort;
- что можно честно обещать в public contract `Okno`.

### 3. Reference repos

Reference repos помогают ответить на другие вопросы:

- как другие проекты режут feature boundary;
- какие DTO/result modes они вводят;
- как они решают smoke/evidence/cleanup;
- где они уходят в breadth-first, а где в safer narrow slice;
- какие проблемы у них уже surfaced в code/tests/README.

Но они не отвечают за нашу contract honesty.

### 4. Локальная верификация

Если после internal docs + official docs + references остаётся ambiguity:

- проводи узкий локальный эксперимент;
- закрепляй результат тестом/exec-plan note;
- не заполняй пробел догадкой.

## Локальный cache reference repos

Для проекта заведён локальный cache в:

- `references/repos/`
- локальный индекс: `references/INDEX.md`

Важно:

- `references/` целиком игнорируется git;
- это рабочий cache для инженера, а не часть shipped surface;
- его можно обновлять локально без влияния на repo contract.

Примеры use cases:

- grep по решениям `shell-open`, `wait`, `window management`, `close`, `dialog`;
- сравнение input vocabulary;
- поиск patterns для smoke helper / process handling / app/window teardown;
- проверка, есть ли у конкурентов явная ownership model или только action model.

## Как работать с reference repos правильно

### Шаг 1. Сформулировать вопрос

Нельзя идти в references с вопросом “посмотрю, что там есть”.

Формулируй вопрос узко:

- как проекты разделяют `launch_process` и `open_target`;
- есть ли у них ownership model для reused shell surfaces;
- как они materialize-ят runtime evidence;
- как у них устроен app/window close path;
- есть ли у них best-effort process id или strict factual result.

### Шаг 2. Сначала сверить `Okno`

Перед references выпиши:

- current repo state;
- уже shipped sibling slice;
- что уже закреплено в spec/roadmap/exec-plan;
- что нельзя ломать.

Если этого не сделать, reference repo начнёт диктовать форму задачи вместо того, чтобы помогать.

### Шаг 3. Потом official docs

Для каждой фичи сначала собери official constraints.

Примеры:

- Win32 / .NET process semantics;
- UIA guarantees и ограничения;
- shell-open / `ShellExecuteExW`;
- foreground/focus limitations;
- MCP tool result contract;
- OpenAI `computer use` / MCP integration semantics.

Только после этого reference repos становятся действительно полезными: уже видно, где они опираются на платформу, а где делают свой pragmatic compromise.

### Шаг 4. Смотреть references по срезам, а не целиком

Не нужно “изучать весь repo”.

Смотри только relevant slices:

- README / docs, если нужно понять public promise;
- файлы регистрации tools;
- DTO/result types;
- runtime service под конкретную feature;
- tests/smoke;
- issues/README warnings, если вопрос про ограничения.

### Шаг 5. Выписывать не код, а решения

Для каждого reference repo фиксируй не “что там написано”, а:

- какая problem boundary выбрана;
- какая success model;
- какие failure modes;
- какой observability/evidence path;
- где они делают risky simplification;
- что у них сильнее нашего;
- что у них слабее нашего.

## Что именно искать в reference repos

Хороший reference scan должен вытаскивать такие вещи:

### 1. Boundary decisions

- один tool или split;
- semantic-first или raw-action-first;
- process/window/dialog/task split;
- hidden follow-up effects или explicit next step.

### 2. Contract shape

- request DTO;
- result modes;
- failure codes;
- preview/dry-run path;
- what is promised vs best-effort enrichment.

### 3. Verification model

- есть ли deterministic smoke;
- как они выбирают helper target;
- как решают cleanup;
- как выражают ambiguous/unowned state.

### 4. Safety model

- есть ли confirmation;
- есть ли allow/block/degraded;
- есть ли redaction;
- что считается unacceptable false success.

### 5. Product tradeoffs

- breadth-first vs narrow verify-first;
- convenience vs honesty;
- shell shortcuts vs deterministic runtime semantics;
- platform-specific hacks vs maintainable contract.

## Как забирать инженерные идеи

Брать из reference repos нужно не “готовый кусок продукта”, а:

- decomposition;
- invariants;
- patterns;
- anti-patterns;
- gaps, которые стоит закрыть у нас лучше.

Полезная формула:

1. Что они решают хорошо?
2. Почему это у них работает?
3. Какая у этого цена?
4. Нужно ли это именно `Okno`?
5. Как это сделать уже в стиле `Okno`, а не копией?

## Как перекладывать идеи на `Okno`

При переносе идеи в `Okno` обязательно:

- выровнять naming под наш repo;
- подчинить решение нашему `ToolContractManifest`;
- встроить его в существующий `observe -> act -> verify` loop;
- пропустить через `capability-design-policy`;
- встроить в existing gate/redaction/evidence model;
- закрепить в tests, smoke и docs.

Если идея не ложится в наш contract/evidence model без специальных исключений, её не надо тянуть как есть.

## Что нельзя копировать напрямую

- public naming и surface area “как у них” без проверки product fit;
- shell/platform hacks, если они не подтверждены official docs;
- broad tool zoo вместо узкого shipped slice;
- ad hoc cleanup/close semantics без ownership proof;
- code snippets с неочевидной лицензией/контекстом;
- assumptions, которые завязаны на другой OS, другой runtime или другой transport.

## Как выявлять сильные стороны references

Используй матрицу сравнения:

| Вопрос | `Okno` сейчас | Official constraints | Reference repo | Что взять | Что улучшить у нас |
| --- | --- | --- | --- | --- | --- |

Сильная reference-идея обычно выглядит так:

- она уменьшает ambiguity;
- делает smoke/user story детерминированнее;
- даёт более честный contract;
- улучшает evidence/safety;
- не ломает уже выбранную архитектуру `Okno`.

Если идея только “добавляет удобство”, но размывает ownership, success criteria или audit path, это слабый reference signal.

## Роль OpenAI docs и `computer use`

Для `Okno` это отдельный обязательный слой исследований.

OpenAI docs нужны не вместо reference repos, а параллельно с ними:

- official OpenAI docs объясняют, как выглядят `computer use`, `skills`, `MCP/connectors`, `shell`;
- reference repos показывают, как похожие runtime layers practically живут в коде;
- `Okno` должен брать из OpenAI docs compatibility target, а из reference repos — инженерные приёмы.

При конфликте между:

- official OpenAI docs,
- MCP spec,
- Windows platform docs,
- reference repo implementation,

побеждают official docs и platform constraints.

## Практический workflow для любой новой фичи

1. Зафиксировать feature question.
2. Прочитать relevant docs/spec/roadmap/exec-plans в `Okno`.
3. Собрать official docs по платформе и протоколу.
4. Посмотреть 2-4 reference repos ровно по нужному срезу.
5. Составить comparison matrix.
6. Выписать design decisions для `Okno`.
7. Закрепить их в exec-plan.
8. Реализовывать только после этого.

## Минимальный expected output исследования

Каждое нетривиальное исследование по references должно заканчиваться не “вроде у них похоже”, а такими артефактами:

- список просмотренных official sources;
- список просмотренных reference repos / файлов;
- comparison notes;
- список design decisions;
- список consciously rejected ideas;
- какие tests/smoke/docs это потом повлияет.

Только после этого reference research считается полезным, а не просто exploratory browsing.
