# Okno: Computer Use for Windows

Computer Use for Windows is the first public capability shipped from the Okno
runtime. It gives Codex a Windows desktop automation surface through a local
MCP server over `STDIO`.

The plugin keeps the public UX focused on a quiet operator layer while the
lower-level Okno / `WinBridge` engine remains the internal execution substrate.

## What the plugin ships today

The plugin publishes:

- the installable plugin `computer-use-win`;
- a plugin-local MCP server launched through `run-computer-use-win-mcp.ps1`;
- the current public operator surface:
  - `list_apps`
  - `get_app_state`
  - `click`
  - `press_key`
  - `set_value`
  - `type_text`
  - `scroll`
  - `perform_secondary_action`
  - `drag`
- the bundled skill `computer-use-win`;
- the repository marketplace entry in `.agents/plugins/marketplace.json`.

## Runtime model

- the plugin starts through `powershell -NoProfile -NonInteractive`;
- the launcher starts only the plugin-local runtime bundle
  `runtime/win-x64/Okno.Server.exe`;
- the public profile is selected explicitly through
  `--tool-surface-profile computer-use-win`;
- the product-ready transport is local `MCP over STDIO`;
- low-level `windows.*` tools remain an internal substrate, not the main public
  story of this plugin.

If an operator needs an even narrower client-side surface, it is better to use
Codex MCP configuration (`enabled_tools` / `disabled_tools`) than to multiply
plugin profiles or fork the runtime narrative.

## Publish the runtime bundle

Before plugin install or reinstall, and after runtime/server layout changes,
publish the plugin-local bundle:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/codex/publish-computer-use-win-plugin.ps1
```

After that, refresh the installed plugin copy and restart Codex. The installed
plugin should run only from its own cache-installed copy and should not depend
on repository-root hints or mutable local artifact directories.

## State-first usage model

The bundled skill should be read as an onboarding guide for new agents.

The normal discipline is:

- start each GUI turn with `get_app_state`;
- treat `stateToken` as a short-lived proof artifact, not as durable session
  state;
- prefer semantic targets over coordinate clicks;
- use coordinate input only with explicit confirmation when semantic proof is
  weak;
- verify after actions through `observeAfter=true` or a follow-up
  `get_app_state`;
- keep blocked targets outside the normal automation path.

## Current capability notes

The plugin already supports strong semantic paths and bounded poor-UIA fallback
paths.

Notable current behavior:

- `type_text` supports ordinary editable targets, focused fallback with
  `allowFocusedFallback=true`, and coordinate-confirmed typing from the latest
  `capture_pixels` state;
- `click`, `press_key`, `type_text`, `scroll`, and `drag` support
  `observeAfter=true` and can return `successorState`;
- repeated unchanged `list_apps` snapshots reuse the same runtime-owned
  `windowId`;
- the next public hardening focus is physical execution policy, not a wider
  tool count.
