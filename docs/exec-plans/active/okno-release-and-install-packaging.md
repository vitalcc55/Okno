# ExecPlan: Okno release and install packaging

Status: `active`  
Date: `2026-05-04`

## 1. Goal

Определить и затем реализовать такую release/install model для `Okno`, которая:

- убирает текущую repo-first friction;
- даёт **нативный Codex/OpenAI path** для `computer-use-win` plugin;
- даёт **generic MCP STDIO path** для любых других LLM clients;
- не ломает local-first Windows runtime model;
- готовит почву для коммерческой и enterprise-friendly дистрибуции.

## 2. Current repo state

Сейчас фактическая install story для shipped public surface такая:

- основной public path — local Codex plugin `computer-use-win`;
- runtime публикуется repo-local скриптом
  `scripts/codex/publish-computer-use-win-plugin.ps1`;
- self-contained bundle materialize-ится в
  `plugins/computer-use-win/runtime/win-x64/`;
- install story зависит от локального checkout репозитория и plugin cache copy;
- generic “любой MCP client -> один command -> поехали” story пока нет;
- GitHub releases у `Okno` сейчас отсутствуют.

Это уже доказывает product surface, но плохо работает как лёгкая user-facing
дистрибуция.

## 3. Official constraints and signals

### 3.1 Codex / OpenAI-native local path

Official Codex plugin docs говорят:

- plugin стоит собирать, когда нужен reusable package, bundled MCP config,
  skills и stable shareable workflow;
- plugin structure строится вокруг `.codex-plugin/plugin.json`, optional
  `.mcp.json`, `skills/`, `assets/` и marketplace metadata;
- local plugins ставятся через marketplace и затем materialize-ятся в Codex
  plugin cache, а не исполняются прямо из marketplace root;
- Codex умеет конфигурировать и `STDIO`, и Streamable HTTP MCP servers, но для
  local child-process story first-class surface — именно `STDIO`.

Следствие:

- для OpenAI/Codex **нативная** форма дистрибуции для `Okno` — это plugin,
  а не “прочитай README и руками заполни config.toml”.

### 3.2 OpenAI API MCP path

Official OpenAI API docs для `mcp` tool сейчас описывают:

- connectors;
- **remote MCP servers** через `server_url`;
- approval policy на стороне developer integration.

Они не описывают “local stdio server launched by the Responses API process” как
основной native path.

Следствие:

- для OpenAI API proper будущий native path — это remote/service-hosted MCP,
  а не текущий local plugin.
- это другой продуктовый слой, не тот же install surface, что нужен сейчас для
  local Windows automation.

### 3.3 Generic MCP protocol path

MCP spec фиксирует:

- стандартных transport сейчас два: `stdio` и Streamable HTTP;
- clients SHOULD support `stdio` whenever possible;
- при `stdio` client запускает server как subprocess и общается через
  `stdin/stdout`;
- server не должен писать ничего лишнего в `stdout`.

Следствие:

- самый переносимый cross-client local path для `Okno` — это **one-command
  STDIO server**;
- remote HTTP story полезна позже, но не заменяет local-first path.

## 4. Competitor packaging patterns

### 4.1 `CursorTouch/Windows-MCP`

Current public install model:

- PyPI package;
- `uvx windows-mcp` как default local path;
- `stdio` по умолчанию;
- optional `sse` / `streamable-http` flags для network mode;
- регулярные GitHub releases с wheel / source tarball assets.

Сильная сторона:

- one-command install/use story для broad MCP ecosystem.

Слабая сторона для нашего кейса:

- Python/uv-first ergonomics не является естественным fit для C# /
  self-contained Windows-native runtime.

### 4.2 `sbroenne/mcp-windows`

Current public install model:

- VS Code extension;
- shared plugin bundle install (`copilot plugin install ...`);
- plugin on first use downloads the current standalone release into
  `plugin/bin/`;
- standalone downloadable release zip for direct MCP config;
- separate Windows binaries in GitHub Releases.

Сильная сторона:

- один и тот же runtime artifact обслуживает и plugin path, и generic MCP path;
- repo checkout не обязателен для normal users.

Это сейчас самый близкий packaging pattern к тому, что нужно `Okno`.

### 4.3 `FlaUI-MCP`

Current public install model:

- release zip download;
- direct MCP config pointing to local executable;
- optional `dotnet run` source path for developers.

Сильная сторона:

- максимально простая mental model для generic MCP clients.

Слабая сторона:

- нет отдельного polished plugin/install layer для Codex/OpenAI ecosystem.

### 4.4 `Windows-Use`

Current public install model:

- Python package / SDK (`pip install windows-use`, `uv add windows-use`);
- product is an agent library more than an MCP server package.

Вывод:

- это полезный reference для developer adoption, но не прямой packaging template
  для `Okno`, потому что он живёт на другом product layer.

## 5. Viable options for Okno

## Option A — keep repo-first plugin publication

### Shape

- оставляем текущий repo checkout + `publish-computer-use-win-plugin.ps1`;
- install path продолжает зависеть от локального source repo;
- generic MCP story не появляется.

### Pros

- минимальный engineering effort;
- сохраняет текущую proof model почти без изменений.

### Cons

- слабая user-facing install story;
- плохо подходит для коммерческой упаковки;
- не даёт clean generic MCP STDIO entry point;
- плохо масштабируется на non-maintainer аудиторию.

### Verdict

Полезно только как dev path. Не годится как основная release story.

## Option B — standalone binary release first

### Shape

- выпускать self-contained `win-x64` release assets;
- пользователь generic MCP client скачивает zip и указывает executable в MCP
  config;
- Codex plugin может оставаться отдельным repo-first layer ещё какое-то время.

### Pros

- лучший short-term path для generic MCP ecosystem;
- естественный fit для .NET / Windows-native runtime;
- убирает обязательную зависимость на source checkout для non-Codex users.

### Cons

- OpenAI/Codex plugin story остаётся отдельной и менее polished;
- дублируются две install narratives.

### Verdict

Хороший промежуточный шаг, но не лучший end-state.

## Option C — thin Codex plugin + downloadable release artifact

### Shape

- `computer-use-win` остаётся first-class Codex plugin;
- plugin больше не тащит runtime из source repo;
- plugin на install / first run / explicit update materialize-ит versioned
  runtime from GitHub Releases into install/cache-owned location;
- тот же release asset используется generic MCP clients напрямую.

### Pros

- единый runtime artifact для двух миров:
  - OpenAI/Codex plugin
  - generic MCP STDIO clients
- repo checkout перестаёт быть обязательным для product use;
- clean brand/product separation:
  - plugin = Codex-native packaging
  - release asset = runtime distribution
- лучший fit под коммерческую упаковку и future signing.

### Cons

- нужно проектировать download/update/rollback/integrity policy;
- нужен version contract между plugin manifest и runtime release;
- нужен хороший offline/air-gapped fallback story.

### Verdict

Это лучший target architecture для `Okno`.

## Option D — fat plugin with embedded runtime

### Shape

- plugin bundle всегда содержит весь self-contained runtime;
- plugin itself is the install artifact.

### Pros

- простой offline story;
- меньше runtime bootstrap logic.

### Cons

- тяжёлые plugin artifacts;
- duplication между plugin package и standalone release;
- хуже separation of concerns;
- хуже для multi-client distribution.

### Verdict

Можно использовать как временный шаг, но это хуже thin-plugin + shared release.

## Option E — MSI / winget / enterprise installer

### Shape

- поверх standalone runtime появляется installer-grade packaging;
- возможны:
  - MSI
  - winget manifest
  - private enterprise distribution

### Pros

- лучшая end-user and enterprise install ergonomics;
- удобнее для managed Windows fleets.

### Cons

- не решает сам по себе Codex plugin story;
- требует сначала стабильного versioned runtime artifact.

### Verdict

Это **Stage 2/3**, а не первый шаг.

## 6. Best fit for Okno

Под специфику `Okno` лучше всего подходит следующая целевая схема:

1. **Versioned standalone runtime release**
2. **Thin Codex plugin that resolves/releases the runtime**
3. **Generic MCP STDIO config examples for other clients**
4. **Later installer layer (MSI / winget / managed enterprise path)**

Почему именно так:

- продукт Windows-only и runtime-heavy;
- основной execution model local-first, not SaaS-first;
- physical input, screenshot capture и UIA лучше ложатся на local child-process
  runtime, чем на преждевременный remote service;
- C# / self-contained executable лучше соответствует этой природе, чем
  искусственный Python package-first layer;
- для OpenAI/Codex plugin остаётся нативным surface;
- для любых других MCP clients остаётся универсальный `command + args` story;
- один release artifact обслуживает обе install narratives.

## 7. Recommended staged rollout

## Stage 1 — introduce runtime releases

Deliver:

- GitHub Releases for `Okno`;
- self-contained `win-x64` asset;
- checksum / digest publication;
- versioned release notes;
- generic MCP config examples for:
  - Claude Desktop / Claude Code
  - Cursor
  - VS Code / Copilot where relevant
  - raw MCP config consumers

Keep:

- current repo-first plugin path as a dev/install fallback.

## Stage 2 — switch Codex plugin to release-backed runtime

Deliver:

- plugin install/runtime resolution from versioned release artifact;
- pinned compatibility between plugin version and runtime version;
- integrity checks;
- rollback / stale detection;
- explicit offline override for enterprise/local mirrors.

Stop doing:

- source-checkout-first install as the main product story.

## Stage 3 — improve end-user packaging

Deliver one or more:

- zip + launcher docs polish;
- MSI installer;
- winget package;
- update channel strategy;
- signed binaries.

## Stage 4 — optional remote/managed story

Only later, if product direction requires it:

- Streamable HTTP or managed remote wrapper;
- hosted or customer-hosted remote bridge;
- enterprise governance around remote execution.

This should stay separate from the first packaging wave.

## 8. OpenAI-native vs generic MCP-native summary

### OpenAI / Codex native

Best organization:

- keep `computer-use-win` as the product-facing plugin;
- package install surface through `.codex-plugin/plugin.json`, `.mcp.json`,
  skills, marketplace metadata;
- make the plugin consume a versioned runtime release instead of a source repo.

### OpenAI API native

Best organization:

- if needed later, build a separate remote MCP deployment story;
- do **not** confuse this with the local Windows plugin install surface.

### Generic LLM provider / MCP STDIO native

Best organization:

- ship one stable executable or launcher command;
- document `command`, `args`, optional env vars, and cwd;
- avoid repo checkout as the primary install requirement.

## 9. Decision summary

Recommended target:

> `Okno` should converge on a **dual-surface distribution model**:
> a **Codex-native thin plugin** for OpenAI users and a **shared standalone
> self-contained MCP STDIO runtime** for all other clients.

Recommended next implementation milestone:

> introduce versioned standalone runtime releases first, then rewire the
> `computer-use-win` plugin to consume those releases.

## 10. Stop conditions for the next implementation slice

The next implementation slice should stop when all of the following are true:

- `Okno` publishes a versioned runtime artifact that does not require a source
  checkout;
- a generic MCP client can launch the runtime by `command` only;
- Codex plugin install no longer depends on repo-local runtime publication as
  the main story;
- docs clearly separate:
  - Codex plugin install
  - generic MCP STDIO install
  - developer-from-source workflow

## 11. Additional best-practice decisions

### 11.1 Release asset shape

For Stage 1, `Okno` should ship a **zipped runtime directory bundle**, not a
single-file executable.

Reasoning:

- current runtime already has a manifest-backed multi-file integrity model;
- the UIA worker is a sidecar executable and already participates in the
  current self-contained runtime bundle proof;
- the current launcher, publish script, and install-surface tests are all
  directory-bundle oriented;
- Microsoft docs confirm that self-contained deployment is the standard path for
  machine-independent runtime distribution, while single-file deployment trades
  easier copying for larger size and slower startup due to extraction.

So the correct first release asset is:

- self-contained `win-x64` directory bundle
- zipped for distribution
- still manifest-backed after extraction

### 11.2 Versioning rule

Do **not** make the plugin resolve “latest” at runtime.

Use a pinned compatibility contract:

- plugin version `X.Y.Z`
- runtime release tag `vX.Y.Z`
- explicit release manifest in the plugin install copy

Reasoning:

- `latest` is useful for docs and manual downloads, not for deterministic plugin
  execution;
- the plugin must remain reproducible and reviewable;
- compatibility between tool schema, runtime behavior, and plugin skills should
  be explicit.

### 11.3 GitHub release publication rule

Follow GitHub immutable-release best practice:

1. create the release as a draft;
2. attach all assets to the draft;
3. publish the release.

This matters because `Okno` is a runtime artifact, not just a source archive.

### 11.4 Provenance rule

For public releases, include:

- the release zip asset;
- a machine-readable checksum file;
- GitHub artifact attestation for the binary asset.

Attestation is not required to unblock Stage 1, but it is a high-value
near-default because GitHub supports binary provenance in Actions and public
repos can verify it with `gh`.

## 12. Source pack for this plan

Primary external sources behind this plan:

- OpenAI Codex plugin packaging:
  - `https://developers.openai.com/codex/plugins/build`
- OpenAI Codex MCP transport/config:
  - `https://developers.openai.com/codex/mcp`
- OpenAI API MCP tool:
  - `https://developers.openai.com/api/docs/guides/tools-connectors-mcp`
- MCP transports:
  - `https://modelcontextprotocol.io/specification/2025-03-26/basic/transports`
- GitHub immutable releases:
  - `https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases`
- GitHub linking to latest release assets:
  - `https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases`
- GitHub artifact attestations:
  - `https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations`
- .NET self-contained publishing:
  - `https://learn.microsoft.com/en-us/dotnet/core/deploying/`
- .NET single-file trade-offs:
  - `https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview`

## 13. File map for implementation

### Files that almost certainly need to be created

- `.github/workflows/release-computer-use-win.yml`
- `scripts/codex/package-computer-use-win-runtime-release.ps1`
- `plugins/computer-use-win/runtime-release.json`
- `docs/runbooks/computer-use-win-install.md`

### Files that likely need to be modified

- `plugins/computer-use-win/run-computer-use-win-mcp.ps1`
- `plugins/computer-use-win/.codex-plugin/plugin.json`
- `plugins/computer-use-win/README.md`
- `README.md`
- `README.ru.md`
- `README.zh-CN.md`
- `docs/generated/commands.md`
- `scripts/refresh-generated-docs.ps1`
- `scripts/codex/prove-computer-use-win-cache-install.ps1`
- `scripts/codex/publish-computer-use-win-plugin-core.ps1`
- `tests/WinBridge.Server.IntegrationTests/ComputerUseWinInstallSurfaceTests.cs`

### Files that may need to be created or modified depending on design pressure

- `scripts/codex/resolve-computer-use-win-runtime-release.ps1`
- `scripts/codex/test-package-computer-use-win-runtime-release.ps1`
- `tests/WinBridge.Server.IntegrationTests/ComputerUseWinReleasePackagingTests.cs`

## 14. Detailed implementation plan

### Stage 1: Versioned standalone runtime releases

#### Package A — define the release contract

Deliver:

- asset naming convention;
- version/tag convention;
- release bundle directory layout;
- checksum file format;
- release-manifest schema.

Required decisions:

- Asset name:
  `okno-computer-use-win-runtime-<version>-win-x64.zip`
- Checksum file:
  `okno-computer-use-win-runtime-<version>-SHA256SUMS.txt`
- Release bundle content:
  - `Okno.Server.exe`
  - runtime dependencies
  - UIA worker sidecar
  - `okno-runtime-bundle-manifest.json`
- The release asset remains **runtime-only**, not a full plugin bundle.

Stop condition:

- the contract is concrete enough that both the plugin resolver and a generic
  MCP client can target it without repo-specific assumptions.

#### Package B — extract release packaging from the current publish flow

Deliver:

- a repo-side script that packages a versioned runtime release from the current
  self-contained publish output;
- no silent dependence on plugin cache or marketplace state;
- reusable packaging path for both local dry-run and CI.

Preferred approach:

- keep `publish-computer-use-win-plugin-core.ps1` as the source of truth for
  building the runtime bundle;
- add a sibling packaging script that:
  - builds or reuses a validated runtime bundle;
  - stages a release directory outside `plugins/computer-use-win/runtime/`;
  - writes checksum output;
  - creates a zip artifact without mutating plugin install state.

Stop condition:

- maintainers can produce a release candidate zip locally without modifying the
  installed plugin cache story.

#### Package C — add GitHub Actions release workflow

Deliver:

- workflow that builds the runtime bundle on tag or manual dispatch;
- draft release publication;
- asset upload;
- checksum publication;
- artifact attestation generation.

Required workflow properties:

- use a Windows runner;
- publish self-contained `win-x64`;
- create release as draft first;
- attach all assets before final publish;
- add `id-token: write`, `contents: read`, `attestations: write` where the
  attestation step runs.

Stop condition:

- a tagged release can produce a downloadable runtime artifact without manual
  repo-local repackaging.

#### Package D — add generic MCP install docs

Deliver:

- a short official install doc for non-Codex clients;
- config examples that point directly to the runtime executable;
- clear split between:
  - Codex plugin install
  - generic MCP STDIO install
  - developer-from-source workflow

Important:

- docs for generic clients may use GitHub “latest release” links;
- the plugin resolver must **not** depend on latest links.

Stop condition:

- an external MCP client can follow docs and launch the runtime without
  checking out the repository.

### Stage 2: Release-backed runtime resolution for the Codex plugin

#### Package E — introduce plugin-side runtime release manifest

Deliver:

- `plugins/computer-use-win/runtime-release.json`

Suggested schema:

- `version`
- `tag`
- `assetName`
- `sha256`
- `relativeRuntimeRoot`
- optional `offlineOverrideEnvVar`

Reasoning:

- keep release compatibility outside `.codex-plugin/plugin.json` unless Codex
  explicitly needs that metadata;
- plugin metadata should describe the user-facing plugin, not become an
  internal release-resolution database.

Stop condition:

- the installed plugin can know exactly which runtime artifact it expects.

#### Package F — rework the launcher into a resolver-backed launcher

Deliver:

- launcher first checks for a validated local runtime bundle;
- if missing or stale, it resolves the pinned runtime release;
- after download/extract, it verifies:
  - zip integrity / checksum
  - extracted runtime bundle manifest
  - expected executable path
- then it starts `Okno.Server.exe --tool-surface-profile computer-use-win`.

Required safety properties:

- no “download latest” logic;
- no silent fallback to repo root;
- clear stderr diagnostics on:
  - offline failure
  - stale asset
  - hash mismatch
  - extraction failure
  - unsupported version mapping
- lock or single-writer guard for concurrent first-run resolution.

Stop condition:

- a cache-installed plugin can bootstrap its runtime from the pinned release
  artifact without a source checkout.

#### Package G — preserve and downgrade the maintainer path

Deliver:

- current repo-local publish flow remains available for maintainers and tests;
- docs move it under “developer workflow”, not “primary install”.

Stop condition:

- no regression for maintainers, but public docs stop advertising repo-local
  publish as the main user install path.

#### Package H — strengthen install-surface tests and proof

Deliver:

- integration tests for:
  - runtime release manifest presence
  - pinned version mapping
  - offline / missing-release failure text
  - hash mismatch fail-close
  - successful resolution from a prepared local release fixture
- proof script updated to validate release-backed install behavior.

Strong preference:

- tests should not depend on live network;
- use prepared local fixtures or test hooks for release resolution.

Stop condition:

- release-backed plugin install is proven hermetically in CI.

## 15. Verification matrix

### For Stage 1

- local packaging dry-run:
  - package script produces zip + checksum
- release workflow dry-run:
  - draft release + asset upload path
- artifact verification:
  - checksum matches
  - attestation exists or is intentionally deferred with explicit note

### For Stage 2

- plugin launcher from temp install copy
- release resolution from pinned asset
- stale/missing/hash-mismatch fail-close behavior
- cache-installed proof script green
- full docs/generated sync

## 16. Recommended implementation order

1. Package A — release contract
2. Package B — packaging script
3. Package C — GitHub Actions workflow
4. Package D — generic MCP install docs
5. Package E — plugin runtime release manifest
6. Package F — resolver-backed launcher
7. Package G — developer-path downgrade
8. Package H — install-surface tests and proof refresh

## 17. Immediate next step

The next active implementation session should start with **Package A + Package B**
only:

- freeze the release asset contract;
- extract the packaging script;
- prove a local release zip can be produced reproducibly.

Do **not** start with the resolver-backed launcher first.

That would lock Stage 2 onto an unstable asset contract and make the install
path harder to verify.
