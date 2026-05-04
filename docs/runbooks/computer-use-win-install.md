# Computer Use for Windows Install Paths

This runbook documents the three supported installation stories for the current
`computer-use-win` surface.

## 1. Codex plugin install

This is the primary OpenAI/Codex-native path.

Requirements:

- Windows 11
- Codex on Windows
- network access for first-run runtime resolution if the plugin copy does not
  already contain a validated runtime bundle

Steps:

1. Clone the repository.
2. Install the local plugin from [plugins/computer-use-win](../../plugins/computer-use-win).
3. Restart Codex or open a new thread.
4. Start with `list_apps`.

Behavior:

- if the install copy already contains a valid `runtime/win-x64` bundle, the
  launcher uses it directly;
- if the runtime bundle is missing or invalid, the launcher resolves the pinned
  runtime release described by `runtime-release.json`;
- the launcher validates SHA256 and
  `okno-runtime-bundle-manifest.json` before starting `Okno.Server.exe`.

## 2. Generic MCP STDIO install

This is the primary path for non-Codex MCP clients.

Requirements:

- Windows 11
- the `win-x64` runtime release zip

Steps:

1. Download the `okno-computer-use-win-runtime-<version>-win-x64.zip` asset
   from GitHub Releases.
2. Extract it to a stable local directory.
3. Configure your MCP client to launch:

```json
{
  "mcpServers": {
    "computer-use-win": {
      "command": "C:\\path\\to\\Okno.Server.exe",
      "args": ["--tool-surface-profile", "computer-use-win"]
    }
  }
}
```

Notes:

- the first release/install wave is `win-x64` only;
- the runtime asset is self-contained and does not require a machine-wide .NET
  installation;
- clients must treat this as a local `STDIO` MCP server, not as a remote HTTP
  endpoint.

## 3. Developer from source

This path remains available for maintainers and local runtime work.

Use it when:

- you are changing the runtime locally;
- you need a fresh plugin-local bundle without waiting for a GitHub release;
- you are testing runtime publication or install-surface invariants.

Command:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/codex/publish-computer-use-win-plugin.ps1
```

This materializes the plugin-local runtime bundle in
`plugins/computer-use-win/runtime/win-x64/`.
