# Capability Design Policy

## Зачем нужен этот документ

Этот документ фиксирует универсальную policy для проектирования и hardening новых capability slices в `Okno`.

Он появился после первого полного цикла `windows.list_windows -> windows.attach_window -> windows.capture`, где стало видно, что отдельные локальные фиксы не заменяют явную модель:

- identity;
- lifecycle;
- fallback policy;
- false-success policy;
- contract honesty;
- evidence contract.

Документ нужен, чтобы следующие feature не проходили тот же путь через цепочку частичных исправлений.

## Где применять

Эту policy нужно применять ко всем следующим capability slices и их follow-up hardening:

- `windows.focus_window` / будущий `activate_window`;
- `windows.clipboard_get` / `windows.clipboard_set`;
- `paste` как отдельное действие или как часть `input`;
- `click`, `double_click`, `right_click`, `move`, `scroll`, `keypress`, `hotkey`;
- `windows.wait` и частные режимы вроде `wait_for_window_active`, `wait_for_visual_change`, `wait_for_focus`, `wait_for_timeout`;
- `windows.uia_snapshot`;
- `windows.uia_action.*`;
- любые следующие tools, которые читают или изменяют live state внешней Windows session.

## Базовая позиция

### 1. Сначала инварианты, потом код

Перед реализацией capability должен быть описан минимальный design contract:

- что считается success;
- что считается explicit error;
- где допустим fallback;
- где fallback запрещён;
- что хуже: false success или extra failure;
- какие поля задают identity;
- какие поля являются mutable display metadata;
- какие side effects реально есть у tool;
- какие artifacts и evidence должен оставлять runtime.

Если это нельзя сформулировать кратко и однозначно, реализация ещё не готова.

### 2. False success хуже, чем честный error

Для `observe`, `act` и `verify` tools по умолчанию действует правило:

- лучше вернуть понятный failure;
- чем молча выполнить действие или observation не над тем target.

Если tool делает fallback, этот fallback не должен менять смысл операции на другой без явно задокументированной политики.

### 3. Identity и display metadata нельзя смешивать

Для live external entities нужно заранее разделять:

- **identity signals**: признаки, по которым runtime подтверждает, что это тот же самый объект;
- **display metadata**: поля, которые полезны человеку и агенту, но могут легитимно меняться без смены сущности.

Для окон `Title` по умолчанию относится ко второй группе, а не к первой, если не доказано обратное.

### 4. Source of truth должен быть единым

Если capability меняет контракт, нужно синхронно обновлять:

- runtime code;
- tool contract source of truth;
- smoke expectations;
- generated docs;
- architecture docs;
- changelog;
- tests.

Нельзя оставлять “правду” размазанной между несколькими частично обновлёнными слоями.

### 5. Sensitive summaries never beat audit safety

Если capability принимает текст, clipboard payload, launch args/env, file/URL targets или любой другой потенциально чувствительный payload, audit/evidence path обязан быть redaction-first:

- readable reason можно сохранять только в safe виде;
- raw request/exception payload нельзя писать “временно для диагностики”;
- если redaction не может честно и стабильно выпустить safe summary, правильный fallback — suppression marker, а не raw value.

## Обязательная design-card перед реализацией

Для каждой новой capability агент должен зафиксировать хотя бы в рабочем виде следующие поля.

### A. Intent

- какую задачу пользователя закрывает capability;
- какой минимальный outcome считается полезным.

### B. Boundary

- это `observe`, `act`, `wait`, `contract`, `admin` или смешанный tool;
- что является host/transport responsibility;
- что является runtime/service responsibility;
- какие OS APIs являются primary path.

### C. Target model

- explicit target arguments;
- implicit target через session/attach;
- behavior при отсутствии target;
- behavior при stale target;
- behavior при ambiguous target.

### D. Identity model

- primary identity signals;
- secondary compatibility signals;
- mutable metadata;
- случаи, где identity доказать нельзя.

### E. Fallback policy

- когда fallback разрешён;
- к чему именно fallback-имся;
- когда fallback запрещён;
- сохраняется ли semantic meaning операции после fallback.

### F. Effect model

- читает ли tool только state;
- меняет ли он OS state;
- меняет ли session state;
- пишет ли artifacts на диск;
- создаёт ли observable side effects даже при “read-like” операции.

### G. Evidence

- что должно остаться как доказуемый след;
- что проверяет smoke;
- что проверяют unit/integration tests;
- где лежит человекочитаемый investigation path.
- какие request/result/runtime summaries допустимо писать в audit как есть, а какие обязаны проходить redaction-first path.

## Универсальная матрица сценариев

Ниже минимальный набор сценариев, который нужно прогонять до финализации capability.

| Сценарий | Что проверить |
| --- | --- |
| Happy path | Базовый успешный вызов даёт ожидаемый semantic result. |
| Explicit target | Явно переданный target имеет приоритет и не размывается implicit state. |
| Implicit target | Session/attached path работает предсказуемо и документированно. |
| Missing target | Tool даёт честный error, если target обязателен. |
| Stale target | Tool не использует уничтоженный/устаревший target как valid success path. |
| Mutated metadata | Нормальное изменение display metadata не ломает identity. |
| Reused identity primitive | Handle/id/process-local token reuse не превращается в ложный success. |
| Missing identity signals | Политика для `null`/unavailable identity полей описана явно. |
| Timeout | Timeout path даёт ожидаемый result или fallback, а не зависание. |
| Unsupported environment | Tool возвращает честный unsupported/error path без transport breakage. |
| Repeated call | Поведение повторного вызова согласовано с annotations и side effects. |
| Artifact/evidence | Артефакты создаются, уникальны и соответствуют result metadata. |

## Verification ladder для capability slices

### L1. Unit / narrow contract tests

Проверяются:

- selectors;
- match/fallback policy;
- naming/evidence builders;
- serializer/result shape;
- contract/exporter behavior.

### L2. Server-side integration tests

Проверяются:

- MCP-facing behavior handler;
- attach/session interaction;
- explicit vs implicit target resolution;
- error/result shape;
- regression scenarios на stale/reuse/mutation.

### L3. Real smoke

Проверяются:

- живой `STDIO` runtime;
- tools/list annotations;
- end-to-end сценарий capability в реальной session;
- artifacts/evidence;
- отсутствие drift между runtime и generated/docs слоем.

Для capability, которая зависит от live OS state, отсутствие `L2 + L3` считается риском по умолчанию.

## Policy для identity-sensitive capabilities

Если feature работает с живыми окнами, controls, clipboard state, visual snapshots или focus state, нужно соблюдать следующие правила.

### 1. Не использовать один слабый signal как identity

Один `HWND`, один `Title`, один `ProcessName`, один `AutomationId` или один `Bounds` не считаются достаточной identity-моделью, если платформа не гарантирует их устойчивость.

### 2. OS-backed identity сильнее display metadata

Если доступны platform-backed signals, они должны иметь приоритет над display metadata.

### 3. Ambiguous identity должна иметь явную политику

Если runtime не может отличить “то же окно” от “другое окно того же процесса”, это не должно решаться молчаливой эвристикой без documented policy.

Допустимые варианты:

- explicit failure;
- conservative fallback;
- статус `ambiguous`, если это совместимо с tool contract.

### 4. Session snapshot не равен live identity

Session хранит рабочий контекст, но не заменяет revalidation against live OS state.

## Policy для fallback

Fallback допустим только если одновременно выполняются все условия:

- он явно задокументирован;
- semantic meaning операции сохраняется;
- клиент не вводится в заблуждение ложным success;
- metadata/result отражают фактический target/result после fallback.

Если fallback меняет смысл операции, он должен быть запрещён.

## Policy для MCP annotations

Перед финализацией capability агент обязан отдельно проверить:

- `ReadOnly`;
- `Destructive`;
- `Idempotent`;
- `OpenWorld`;
- `UseStructuredContent`.

Аннотации не должны описывать “желательное восприятие” tool. Они должны описывать реальное поведение.

## Минимальный review-checklist для нового capability

Перед завершением работы по новой feature агент должен вручную ответить хотя бы на эти вопросы:

1. Где у capability граница между identity и display metadata?
2. Что произойдёт при stale target?
3. Что произойдёт при mutated metadata?
4. Что произойдёт при reused handle/id/token?
5. Где tool может вернуть ложный success?
6. Где tool должен падать явно, а не fallback-иться?
7. Соответствуют ли MCP annotations реальным side effects?
8. Есть ли отдельный evidence path для расследования?
9. Обновлены ли source-of-truth docs и generated docs?
10. Есть ли тесты не только на найденный баг, но и на соседние сценарии того же класса?

## Как применять это на следующих slices

### Focus / Activate

Нужно заранее определить:

- что считается “окно действительно стало active/foreground”;
- когда `SetForegroundWindow` недостаточен;
- какой verify path подтверждает реальный success;
- где допустим retry/fallback, а где нужен честный failure.

### Clipboard / Paste

Нужно заранее определить:

- кто владеет semantic contract: clipboard tool или input tool;
- где read-like operation на самом деле имеет side effects;
- как проверяется, что вставка действительно произошла туда, куда ожидалось.

### Input actions

Нужно заранее определить:

- target resolution;
- coordinate semantics;
- focus preconditions;
- false-success policy для click/move/hotkey;
- verify hooks после действия.

### Wait

Нужно заранее определить:

- что именно считается condition source of truth;
- когда timeout — это expected result, а когда failure;
- как wait взаимодействует с stale session и visual drift.

### UIA snapshot / action

Нужно заранее определить:

- identity model для element/window;
- mutable metadata vs stable identifiers;
- что делать при partially stale tree;
- как не смешать semantic success и transport success.

## Итоговая норма

Новая capability считается инженерно готовой только если:

- её identity model сформулирована;
- fallback policy сформулирована;
- false-success policy сформулирована;
- tests покрывают не только happy-path, но и соседние failure classes;
- docs и source of truth синхронизированы;
- `verify` оставляет доказуемый след.
