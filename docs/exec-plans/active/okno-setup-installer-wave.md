# ExecPlan: Okno setup installer wave

Status: `active`  
Date: `2026-05-04`

## 1. Goal

Подготовить и затем реализовать первую **user-friendly installer wave** для
`Okno`, в которой продукт доставляется через **один Windows installer**
`Okno Setup.exe` с двумя режимами:

1. `Install for Codex (Recommended)`
2. `Install runtime only (Advanced)`

Цель этой волны:

- убрать repo checkout из основной пользовательской истории;
- сохранить `computer-use-win` как нативный Codex plugin surface;
- использовать **один shared runtime** для Codex и plain MCP;
- не дублировать install logic между PowerShell bootstrap, GUI installer и
  runtime/plugin layer;
- подготовить основу для последующих `signing`, `winget`, `MSI` и
  enterprise distribution, но не тащить их в первую волну без необходимости.

## 2. Baseline and dependency on the current packaging wave

Этот план не начинает install story с нуля. Он опирается на уже подготовленную
базу:

- versioned runtime release asset уже существует;
- generic MCP `STDIO` path уже можно использовать без repo checkout;
- plugin умеет release-backed runtime resolution;
- integrity contract для runtime release уже определён и проверяется.

Предусловие для этой волны:

- текущая release/install packaging wave остаётся source of truth для runtime
  artifact contract;
- installer wave не пересматривает уже принятый shared runtime release model,
  а строит поверх него более удобную delivery shell.

Текущий прогресс по состоянию на `2026-05-06`:

- thin plugin bundle release path уже реализован;
- shared runtime foundation bundle уже реализован:
  - canonical per-user runtime store;
  - managed CLI `runtime install/status/verify/repair`;
  - launcher preference для shared installed runtime;
- installer orchestration core уже реализован:
  - `install/update/repair/uninstall/status` для `codex` и `runtime-only`;
  - personal marketplace mutation;
  - install receipts и runtime retention policy;
- PowerShell/bootstrap shell уже реализован:
  - packaged setup CLI payload;
  - thin bootstrap shell without repo checkout;
- WinUI 3 shell уже реализован как unpackaged unsigned RC app:
  - two install modes;
  - progress/result screen;
  - runtime-only snippet copy action;
- следующая крупная недостающая часть — финальная docs sync, full verify и
  clean merge-ready closure.

Связанный baseline-документ:

- [okno-release-and-install-packaging.md](okno-release-and-install-packaging.md)

## 3. Target product shape

### 3.1 What the user should see

Пользователь должен видеть **один продукт** `Okno`, а не набор внутренних
репозиторных сценариев.

Целевой UX:

1. Пользователь скачивает `Okno Setup.exe`.
2. Запускает installer.
3. Выбирает один из двух режимов:
   - `Install for Codex (Recommended)`
   - `Install runtime only (Advanced)`
4. Installer выполняет установку и в конце показывает только те действия,
   которые реально ещё нужны пользователю:
   - для `Codex`: обычно только `Restart Codex`;
   - для `runtime only`: путь к `Okno.Server.exe` и готовый MCP snippet.

### 3.2 Semantic meaning of the two modes

#### Mode A — Install for Codex

Этот режим **всегда** включает:

- установку shared `Okno` runtime;
- установку `computer-use-win` plugin bundle;
- обновление personal marketplace для Codex;
- проверку, что после restart Codex увидит plugin и сможет поднять runtime.

То есть `Codex` mode уже включает полный локальный MCP runtime и не требует
отдельного режима `Both`.

#### Mode B — Install runtime only

Этот режим включает:

- установку shared `Okno` runtime;
- запись install receipt/state;
- вывод ready-to-use MCP `command + args` snippet;
- optional snippets для нескольких явно поддерживаемых MCP clients позже.

Он **не** устанавливает Codex plugin и не меняет Codex marketplace.

### 3.3 What is intentionally out of scope for this wave

В эту волну **не** входят как обязательные deliverables:

- `winget`;
- `MSI`;
- public Apps review / ChatGPT Apps directory submission;
- remote MCP / hosted bridge;
- отдельный режим `Both`;
- разные копии runtime для Codex и для plain MCP;
- repo-first main story для обычного пользователя.

`winget` и `MSI` — это следующая distribution wave после того, как unified
installer станет стабильным и доказанным.

## 4. Architectural decisions

### 4.1 One shared runtime, not two install families

Целевой инвариант:

- у продукта есть **один canonical installed runtime** на пользователя/машину;
- этот runtime живёт в product-owned per-user path `%LocalAppData%\Okno\computer-use-win`,
  а не внутри `.codex`;
- Codex plugin использует именно этот runtime;
- plain MCP clients используют тот же runtime;
- installer не плодит отдельные runtime copy под каждый install mode.

Следствие:

- `Codex` mode не должен materialize-ить второй hidden runtime внутри plugin
  install surface, если shared installed runtime уже есть и валиден.

### 4.2 Plugin remains a packaging layer, not the runtime itself

`computer-use-win` остаётся:

- Codex-facing packaging surface;
- manifest + skills + metadata layer;
- thin launcher/config layer.

`Okno.Server.exe` остаётся:

- реальным local `STDIO` MCP runtime;
- shared execution binary для обоих install modes.

### 4.3 Installer shells must not own business logic

PowerShell bootstrap installer и будущий `Okno Setup.exe` не должны
дублировать install logic.

Правильная модель:

- общий installer core определяет contracts, state transitions, integrity checks
  и file mutations;
- PowerShell bootstrap — thin delivery shell;
- GUI `Okno Setup.exe` — вторая thin shell поверх того же core.

Это обязательное решение для минимизации техдолга.

### 4.4 Codex marketplace integration must move to a user-owned stable path

Installer wave должна уйти от repo marketplace как основной user story.

Целевая модель:

- installer кладёт plugin bundle в stable user-owned location;
- installer обновляет personal marketplace;
- Codex после restart materialize-ит plugin в cache уже из этой stable source.

Repo marketplace остаётся только maintainer/dev path.

## 5. Where DDD is justified

DDD здесь уместен **только** в install/distribution domain, где он реально
закрывает границы ответственности.

Нужно явно выделить следующие bounded contexts:

1. `Runtime Distribution`
   - runtime release descriptor;
   - installed runtime layout;
   - integrity proof;
   - active version / installed versions.

2. `Plugin Distribution`
   - plugin bundle release descriptor;
   - plugin source layout for personal marketplace;
   - plugin version compatibility against runtime version.

3. `Installer Orchestration`
   - install mode;
   - install plan;
   - install receipt;
   - update / repair / uninstall transitions.

4. `Codex Integration`
   - personal marketplace mutation;
   - plugin source placement;
   - restart-required signaling;
   - cache-materialization proof.

DDD **не** нужно формально тащить в:

- GitHub Actions YAML;
- changelog/docs wording;
- signing scripts;
- GUI styling/layout;
- release notes.

## 6. Where TDD is justified

TDD нужен там, где поведение deterministic, contract-heavy и prone to
regression.

### TDD is required

1. Plugin bundle release descriptor parsing and validation.
2. Shared runtime discovery and reuse rules.
3. Install mode semantics:
   - `Codex` installs runtime + plugin;
   - `runtime only` installs runtime only.
4. Marketplace mutation logic:
   - create;
   - update existing entry;
   - idempotent reinstall.
5. Install receipt / repair / uninstall transitions.
6. Failure paths:
   - checksum mismatch;
   - missing asset;
   - stale descriptor;
   - partial install recovery.

### TDD is optional / low-value

1. GitHub release workflow YAML.
2. Basic PowerShell wrapper argument plumbing.
3. GUI shell layout itself.
4. Code-signing step wiring.

Там достаточно static validation, smoke и end-to-end proof.

## 7. Sequential implementation plan

Ниже — линейная последовательность, которую можно брать в реализацию без
перепрыгивания между этапами.

### Step 1 — Freeze the installer-wave scope and kill the wrong product shapes

Purpose:

- зафиксировать конечную install model до кода.

Preconditions:

- runtime release contract уже существует;
- release-backed plugin resolution уже работает.

Dependencies:

- baseline packaging wave completed enough for reuse.

Constraints:

- не вводить режим `Both`;
- не вводить вторую runtime family;
- не тянуть `MSI/winget` в first installer wave;
- не сохранять repo-first install как основной пользовательский path.

Actions:

- зафиксировать в docs и design notes, что installer mode только два:
  `Codex` и `runtime only`;
- зафиксировать, что `Codex` mode всегда включает runtime;
- зафиксировать out-of-scope list.

Expected result:

- дальнейшая реализация не расползается в три install mode, fat plugin или
  duplicated runtime model.

DDD/TDD:

- DDD: нет, это design freeze;
- TDD: нет.

### Step 2 — Define the canonical installed runtime layout

Purpose:

- создать installer-owned runtime model, на которую потом смогут опираться и
  plain MCP users, и Codex plugin cache copies.

Preconditions:

- Step 1 frozen.

Dependencies:

- existing runtime release asset contract.

Constraints:

- canonical runtime path должен быть stable and user-owned;
- layout должен быть version-aware;
- installer не должен зависеть от plugin cache location как source of truth.

Actions:

- определить canonical runtime root;
- определить installed-version directory layout;
- определить active/current pointer model;
- определить runtime install receipt format;
- определить garbage-collection policy для old versions.

Expected result:

- появляется одна installer-owned runtime store model, пригодная для update,
  repair, uninstall и plugin reuse.

DDD/TDD:

- DDD: да, это core domain boundary;
- TDD: да, для path planning, active-version selection and install receipt
  semantics.

### Step 3 — Define the plugin bundle release contract

Purpose:

- перестать считать plugin только repo-local folder и сделать его releaseable
  install artifact.

Preconditions:

- Step 2 runtime layout frozen.

Dependencies:

- shared runtime contract from existing packaging wave.

Constraints:

- plugin bundle не должен включать вторую копию runtime;
- plugin bundle release и runtime release должны быть version-compatible by
  contract, not by guesswork;
- plugin asset naming and descriptor must be decision-complete.

Actions:

- определить plugin bundle zip format;
- определить plugin bundle descriptor schema;
- определить compatibility fields plugin <-> runtime;
- определить checksum/integrity publication rules.

Expected result:

- появляется releaseable Codex plugin artifact, который можно ставить без repo
  checkout.

DDD/TDD:

- DDD: да, `Plugin Distribution` bounded context;
- TDD: да, для descriptor parsing, compatibility validation and bundle layout.

### Step 4 — Implement plugin bundle packaging and release publication

Purpose:

- materialize-ить plugin release artifact и сделать его воспроизводимым.

Preconditions:

- Step 3 descriptors and naming frozen.

Dependencies:

- existing runtime packaging scripts;
- plugin bundle contract from Step 3.

Constraints:

- не дублировать release logic между runtime and plugin packaging;
- release workflow должен оставаться draft-first;
- integrity artifacts публикуются вместе с plugin asset.

Actions:

- написать packaging script для plugin bundle;
- добавить workflow publication path;
- подготовить asset publication sequence;
- синхронизировать proof docs и install docs.

Expected result:

- GitHub Releases содержат не только runtime zip, но и plugin bundle asset.

DDD/TDD:

- DDD: нет;
- TDD: да, для packaging layout / checksum / contract-level validation.

### Step 5 — Extend the plugin launcher to prefer the shared installed runtime

Purpose:

- перевести Codex plugin с install-owned runtime copy model на shared
  installer-owned runtime reuse model.

Preconditions:

- canonical runtime layout from Step 2;
- plugin release contract from Step 3.

Dependencies:

- existing launcher and release-backed runtime resolution.

Constraints:

- shared installed runtime must become primary preferred path;
- release-backed self-bootstrap remains fallback, not main install story;
- no silent drift between plugin version and runtime version.

Actions:

- добавить discovery of shared installed runtime;
- добавить compatibility check against plugin version/descriptor;
- сохранить fail-close integrity behavior;
- обновить fallback ordering.

Expected result:

- cache-installed Codex plugin сначала использует shared runtime from installer,
  а не живёт как отдельный mini-installer со своей скрытой жизнью.

DDD/TDD:

- DDD: да, на границе `Runtime Distribution` -> `Codex Integration`;
- TDD: обязательно, потому что здесь много failure/fallback paths.

### Step 6 — Build the installer core as a headless orchestration layer

Purpose:

- не размазывать install logic по PowerShell и GUI.

Preconditions:

- Steps 2-5 frozen enough to be encoded.

Dependencies:

- runtime layout;
- plugin bundle contract;
- launcher shared-runtime behavior.

Constraints:

- installer core должен быть shell-agnostic;
- install state transitions должны быть deterministic and idempotent;
- core не должен зависеть от GUI framework.

Actions:

- выделить installer core module/service;
- определить `InstallMode`, `InstallPlan`, `InstallReceipt`, `InstallResult`;
- определить update/repair/uninstall commands;
- определить diagnostics surface for shells.

Expected result:

- появляется единый headless installer engine, который можно вызывать и из
  PowerShell, и из GUI installer.

DDD/TDD:

- DDD: да, это центр `Installer Orchestration`;
- TDD: обязательно.

### Step 7 — Implement runtime-only install mode in the installer core

Purpose:

- закрыть first clean plain-MCP product path внутри нового installer model.

Preconditions:

- installer core exists.

Dependencies:

- Step 6 core;
- Step 2 runtime layout.

Constraints:

- runtime-only mode не должен писать в Codex marketplace;
- install должен быть idempotent;
- uninstall/repair hooks должны использовать тот же receipt model.

Actions:

- реализовать download/verify/extract/install runtime path;
- реализовать receipt write/update;
- реализовать output contract for MCP snippet generation.

Expected result:

- headless installer уже умеет полностью ставить `Okno` как plain MCP runtime.

DDD/TDD:

- DDD: нет отдельного нового bounded context;
- TDD: обязательно.

### Step 8 — Implement Codex install mode in the installer core

Purpose:

- сделать главный пользовательский mode поверх уже готового runtime-only
  foundation.

Preconditions:

- runtime-only mode green.

Dependencies:

- Step 7 complete;
- Step 3 plugin bundle asset;
- Step 5 launcher shared-runtime support.

Constraints:

- Codex mode должен использовать тот же shared runtime, а не отдельную копию;
- installer должен мутировать только user-owned plugin/marketplace surfaces;
- reinstall/update должен быть idempotent.

Actions:

- реализовать plugin bundle download/verify/install;
- реализовать personal marketplace create/update path;
- реализовать restart-required signaling;
- реализовать install proof for plugin source visibility.

Expected result:

- headless installer умеет полностью поставить `Okno` для Codex без repo
  checkout.

DDD/TDD:

- DDD: да, `Codex Integration` bounded context;
- TDD: обязательно.

### Step 9 — Add repair, update, and uninstall invariants before any GUI polish

Purpose:

- закрыть эксплуатационный минимум до красивой оболочки.

Preconditions:

- оба install mode green in core.

Dependencies:

- Steps 7 and 8.

Constraints:

- не откладывать uninstall/update на “потом”;
- не плодить separate scripts with custom logic per mode;
- repairs must preserve integrity-first behavior.

Actions:

- реализовать `repair`;
- реализовать `update`;
- реализовать `uninstall runtime-only`;
- реализовать `uninstall Codex mode`;
- определить policy для shared runtime removal when Codex/plugin still present.

Expected result:

- installer core становится пригодным для нормальной жизни после первой
  установки, а не только для happy-path install demo.

DDD/TDD:

- DDD: да, для install state transitions;
- TDD: обязательно.

### Step 10 — Ship the first PowerShell bootstrap installer as a thin shell

Purpose:

- дать быстрый user-facing path до появления GUI exe without duplicating logic.

Preconditions:

- installer core feature-complete enough for both modes.

Dependencies:

- Steps 6-9.

Constraints:

- PowerShell shell only orchestrates download/start/report;
- никакой business logic не утекает обратно в ad-hoc script branches;
- shell supports silent and interactive parameters only where core already does.

Actions:

- сделать bootstrap script that downloads/verifies installer package;
- вызвать installer core in selected mode;
- оформить user-facing messages and exit codes;
- добавить docs and support runbook.

Expected result:

- появляется первый практичный one-command installer path without repo checkout.

DDD/TDD:

- DDD: нет;
- TDD: только на thin parameter mapping if low-cost, otherwise smoke enough.

### Step 11 — Wrap the same core in `Okno Setup.exe`

Purpose:

- перейти от technical bootstrap UX к нормальному Windows installer UX.

Preconditions:

- PowerShell bootstrap proven;
- installer core stable.

Dependencies:

- Steps 6-10.

Constraints:

- GUI shell не переписывает install logic;
- GUI only selects mode, shows progress, renders results and errors;
- if signing material is unavailable, stage may stop at unsigned RC but must not
  be declared final.

Actions:

- выбрать Windows-native installer shell;
- подключить installer core;
- реализовать screens:
  - mode selection;
  - install destination summary where relevant;
  - progress;
  - completion + next action;
- подготовить signed build path.

Expected result:

- появляется `Okno Setup.exe` как единый product-facing installer.

DDD/TDD:

- DDD: нет;
- TDD: только for shared presenter/controller logic where deterministic;
- GUI layout itself verifies through smoke/UX review.

### Step 12 — Sign the installer and binaries

Purpose:

- довести installer wave до доверяемой delivery surface.

Preconditions:

- installer executable exists and is stable.

Dependencies:

- Step 11;
- access to code-signing certificate/process.

Constraints:

- signing не должен менять runtime/plugin contracts;
- unsigned build не называется final product-ready path.

Actions:

- подписать installer;
- подписать relevant binaries if policy requires;
- зафиксировать verification and trust checks.

Expected result:

- installer wave готова для нормального пользовательского распространения.

DDD/TDD:

- DDD: нет;
- TDD: нет, достаточно reproducible signing verification.

### Step 13 — Sync docs, proof surfaces, and acceptance

Purpose:

- закрепить installer wave как реальный shipped path, а не устную модель.

Preconditions:

- previous steps complete.

Dependencies:

- all implementation steps.

Constraints:

- docs должны описывать installer-first story, а не держать repo-first как main
  path;
- dev/source path остаётся, но уходит в maintainer section;
- acceptance должен проверять обе install modes separately.

Actions:

- обновить root README;
- обновить plugin README;
- обновить install runbook;
- обновить generated command/docs surfaces where affected;
- обновить proof scripts;
- обновить changelog and completion record.

Expected result:

- репозиторий, release assets и installer UX говорят об одном и том же.

DDD/TDD:

- DDD: нет;
- TDD: нет, но acceptance proof обязателен.

## 8. Verification strategy

### Required automated checks

1. Plugin bundle packaging tests.
2. Shared runtime layout and active-version selection tests.
3. Marketplace mutation tests.
4. Runtime-only install/update/repair/uninstall tests.
5. Codex install/update/repair/uninstall tests.
6. Launcher tests for shared-runtime preference and fallback behavior.

### Required proof scenarios

1. Fresh `runtime only` install without repo checkout.
2. Fresh `Codex` install without repo checkout.
3. Codex restart and first tool call from cache-installed plugin.
4. Reinstall over existing runtime.
5. Plugin update with compatible runtime update.
6. Uninstall plugin while runtime remains valid for plain MCP.
7. Uninstall runtime-only install.
8. Offline / checksum mismatch fail-close behavior.

### Required final acceptance statement

Installer wave считается завершённой только если одновременно доказано:

- `Codex` mode installs runtime + plugin from installer artifacts only;
- `runtime only` mode installs reusable plain MCP runtime only;
- оба mode не требуют repo checkout;
- shared runtime is single source of truth;
- uninstall/update/repair are real, not postponed;
- docs no longer describe repo-first install as the main user story.

## 9. Explicit non-goals for this wave

Следующие вещи не должны задерживать первую installer wave:

- `winget`;
- `MSI`;
- enterprise fleet distribution policy;
- Apps review / public directory submission;
- remote MCP hosting;
- multi-client auto-config zoo for every MCP client on day one.

Их можно делать только после stable unified installer.

## 10. Documentation sync invariant

Installer wave не считается завершённой, если install story обновлена только в
одной языковой версии front page.

Обязательный инвариант:

- root [README.md](README.md);
- root [README.ru.md](README.ru.md);
- root [README.zh-CN.md](README.zh-CN.md);

должны обновляться **вместе**, если меняется:

- install story;
- user-facing mode names;
- Codex vs MCP positioning;
- prerequisites;
- post-install next steps;
- honest status wording around product readiness.

Локализованные версии не должны оставаться на старом install flow, если
английский front door уже описывает новую модель.

## 11. Why this plan minimizes technical debt

Этот план специально избегает четырёх плохих путей:

1. `PowerShell now, rewrite everything later in GUI`
   - запрещено: business logic сразу уходит в shared installer core.

2. `Codex installer` и `plain MCP installer` как две разные code paths
   - запрещено: оба mode строятся поверх shared runtime model и одного core.

3. `Fat plugin with embedded runtime forever`
   - запрещено: plugin остаётся thin packaging layer.

4. `Repo-first install remains main story while installer exists`
   - запрещено: repo path остаётся только dev/maintainer fallback.

Именно поэтому порядок шагов такой: сначала contracts and shared state model,
потом core, потом shells, потом signing, а не наоборот.
