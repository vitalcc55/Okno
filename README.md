# Okno

[**English**](README.md) | [Русский](README.ru.md) | [简体中文](README.zh-CN.md)

> Windows-native MCP runtime for AI agents
>
> **Computer Use for Windows** is Okno's first public capability for desktop
> automation over `MCP` / `STDIO`.

| Platform | Transport | Capability | Runtime | Core execution model |
| --- | --- | --- | --- | --- |
| Windows 11 | Local MCP over `STDIO` | `Computer Use for Windows` | C# / .NET 8 | UIA semantics + screenshot-backed verification |

## Why Okno

Okno turns real Windows desktop apps into an operator surface for AI agents.
It is built for cases where shell commands, browser-only automation, or app
APIs are not enough.

The current public capability, **Computer Use for Windows**, lets an agent
inspect windows, read action-ready state, act on the target UI, and verify the
result instead of assuming that a dispatched click or keystroke means success.

This repository is not a generic Windows scripting toolkit and not a browser
automation project in disguise. It is a Windows-native runtime for agentic GUI
work, with a product-ready local transport based on `MCP over STDIO`.

## What Computer Use for Windows Can Do Today

Today the shipped public surface can:

- discover running desktop apps and windows through `list_apps`;
- capture screenshot-backed state through `get_app_state`;
- return an accessibility tree, geometry, `captureReference`, and a short-lived
  `stateToken`;
- perform `click`, `press_key`, `set_value`, `type_text`, `scroll`,
  `perform_secondary_action`, and `drag`;
- return `verify_needed` instead of pretending that a low-confidence action is
  semantically complete;
- return `successorState` on supported actions through `observeAfter=true`;
- work with both strong-UIA applications and weaker Qt / Electron / custom GUI
  targets through bounded physical fallback paths.

## How It Works

Okno runs locally on Windows as an MCP runtime. The current product-ready
transport is **local `STDIO`**, and the current polished integration path is
the Codex plugin shipped in this repository.

`Computer Use for Windows` is the current public capability layer. Under the
hood, it uses the Okno runtime and internal `WinBridge` engine components, but
the public UX stays focused on a quiet operator surface instead of exposing
raw low-level `windows.*` tools as the main product story.

The normal loop is:

```text
list_apps -> get_app_state -> action -> verify
```

In practice, that means:

1. find the target window;
2. get fresh state with screenshot and accessibility data;
3. choose the strongest available action path;
4. verify the result through `observeAfter=true` or a follow-up
   `get_app_state`.

## Why It Is Different

Okno is designed around four product rules.

| Principle | What it means in practice |
| --- | --- |
| Strong semantic path | Prefer UIA-backed actions where the runtime can prove the target well. |
| Screenshot-backed state | Screenshots are part of observation and verification, not a decorative extra. |
| Bounded physical fallback | When UIA is weak, the runtime can still act through guarded physical input paths. |
| Honest outcomes | Low-confidence actions return `verify_needed` instead of optimistic fake success. |

This makes Okno a better fit for real Windows GUI work than tools that only
dispatch coordinates, and a better fit for poor-UIA targets than tools that
assume semantic automation is reliable everywhere.

## Where It Fits Best

Okno is a strong fit if you need:

- local Windows desktop automation for AI agents;
- a Windows-native MCP runtime instead of a browser-only tool;
- a Codex-friendly operator surface for real desktop apps;
- a verification-oriented execution model for unstable or weak-semantic UI.

Okno is not the primary tool to reach for if you need:

- browser-first DOM automation;
- one-click consumer distribution with no local setup;
- full enterprise RPA orchestration and low-code workflow tooling.

## Quick Start

The shortest supported path today is **Codex on Windows** with the local
plugin shipped from this repository.

### Prerequisites

- Windows 11
- Codex on Windows
- PowerShell
- network access if the plugin install copy needs to resolve its pinned runtime
  release on first run

### 1. Clone the repository

```powershell
git clone https://github.com/vitalcc55/Okno.git
cd Okno
```

### 2. Install the local plugin from the repository marketplace entry

Repository entry points:

- [.agents/plugins/marketplace.json](.agents/plugins/marketplace.json)
- [plugins/computer-use-win](plugins/computer-use-win)
- [plugins/computer-use-win/.codex-plugin/plugin.json](plugins/computer-use-win/.codex-plugin/plugin.json)
- [plugins/computer-use-win/.mcp.json](plugins/computer-use-win/.mcp.json)

### 3. Restart Codex or open a new thread

The installed plugin runs from the Codex plugin cache, not from the repository
root. If the install copy already has a validated runtime bundle, the launcher
uses it directly. If the runtime bundle is missing or invalid, the launcher
resolves the pinned runtime release described by
[plugins/computer-use-win/runtime-release.json](plugins/computer-use-win/runtime-release.json),
verifies SHA256 plus `okno-runtime-bundle-manifest.json`, and only then starts
the MCP host.

### 4. Run the first loop

1. call `list_apps`;
2. choose a `windowId`;
3. call `get_app_state(windowId=...)`;
4. act;
5. verify with `observeAfter=true` or a new `get_app_state`.

For generic MCP `STDIO` clients and the maintainer source workflow, see
[docs/runbooks/computer-use-win-install.md](docs/runbooks/computer-use-win-install.md).
Maintainers can still materialize a plugin-local bundle explicitly with
`scripts/codex/publish-computer-use-win-plugin.ps1`.

## Public Tool Surface

| Tool | Purpose |
| --- | --- |
| `list_apps` | Discover running desktop apps and windows. |
| `get_app_state` | Return screenshot-backed state, bounds, tokens, and accessibility data. |
| `click` | Activate a semantic target or confirmed point target. |
| `press_key` | Send explicit keyboard input. |
| `set_value` | Use semantic value-setting paths where supported. |
| `type_text` | Enter text through semantic or guarded fallback typing paths. |
| `scroll` | Perform scroll actions and verify the movement path when possible. |
| `perform_secondary_action` | Run toggle / expand-collapse style secondary semantic actions. |
| `drag` | Perform bounded drag operations with explicit source and destination proof. |

Important runtime-facing fields:

- `windowId` is a public selector for the current discovery state, not an
  eternal window identity.
- `stateToken` is a short-lived proof artifact for the last observation state.
- `verify_needed` means the dispatch happened but the semantic outcome still
  needs observation.
- `successorState` is the fresh post-action state when `observeAfter=true`
  succeeded.

## Trust, Safety, and Boundaries

- The runtime works in a **real** Windows desktop session.
- Physical mouse and keyboard input are shared system resources.
- The project does not pretend to provide a second independent system cursor.
- Weak-semantic or poor-UIA targets may require bounded physical fallback.
- Blocked or sensitive targets still need explicit policy discipline.
- Low-confidence actions should be treated as `dispatch + verify`, not as blind
  success.

## Documentation Map

If you want more than the front page:

- product docs: [docs/product/index.md](docs/product/index.md)
- product spec: [docs/product/okno-spec.md](docs/product/okno-spec.md)
- roadmap: [docs/product/okno-roadmap.md](docs/product/okno-roadmap.md)
- product vision: [docs/product/okno-vision.md](docs/product/okno-vision.md)
- architecture docs: [docs/architecture/index.md](docs/architecture/index.md)
- public capability docs:
  [plugins/computer-use-win/README.md](plugins/computer-use-win/README.md)
- install paths:
  [docs/runbooks/computer-use-win-install.md](docs/runbooks/computer-use-win-install.md)
- generated interfaces:
  [docs/generated/computer-use-win-interfaces.md](docs/generated/computer-use-win-interfaces.md)
- commands inventory: [docs/generated/commands.md](docs/generated/commands.md)

## Status

Okno is already usable today as a local Windows plugin/runtime for Codex and
as a local MCP surface over `STDIO`.

What is already strong:

- the public capability is shipped and installable from source;
- the release-backed runtime contract for generic MCP clients is now defined;
- the runtime bundle and plugin install surface already exist;
- the public contract, smoke path, and verification loop are real;
- the runtime is past the research-prototype stage.

What is still intentionally honest:

- installation is still developer-oriented;
- the Codex plugin install path is still repo-backed today;
- GitHub runtime releases must exist before the runtime-less plugin path becomes
  the main public story;
- one-click consumer distribution is not the current shape of the product.

## License

This repository is licensed under **GNU Affero General Public License v3.0 or
later** (`AGPL-3.0-or-later`).

Copyright © 2025–2026 Vlasov Vitaly

- [LICENSE](LICENSE)
- [LICENSES/AGPL-3.0-or-later.txt](LICENSES/AGPL-3.0-or-later.txt)
- [REUSE.toml](REUSE.toml)
