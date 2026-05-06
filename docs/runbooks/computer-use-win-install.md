# Computer Use for Windows Install Paths

This runbook documents the supported installation stories for the current
`computer-use-win` surface.

## 1. Installer-first Codex install

This is the recommended OpenAI/Codex-native path on the installer-wave branch.

Requirements:

- Windows 11
- Codex on Windows
- either the GUI installer archive `okno-setup-unsigned-<version>-win-x64.zip`
  or the bootstrap pair `install-computer-use-win.ps1` +
  `okno-setup-cli-payload-<version>-win-x64.zip`
- network access if the install needs to resolve runtime or plugin assets

Steps:

1. Extract `okno-setup-unsigned-<version>-win-x64.zip` and run `Okno Setup.exe`,
   or run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File install-computer-use-win.ps1 -Mode codex -PayloadArchivePath .\okno-setup-cli-payload-<version>-win-x64.zip
   ```

2. Choose `Install for Codex (Recommended)`.
3. Restart Codex or open a new thread.
4. Start with `list_apps`.

Behavior:

- the installer lays out the shared runtime under `<codex-home>/okno/computer-use-win`;
- the installer lays out the thin plugin under `<codex-home>/plugins/computer-use-win`;
- the installer creates or updates `%USERPROFILE%\.agents\plugins\marketplace.json`;
- the launcher first prefers the shared installed runtime;
- plugin-local runtime remains only a developer fallback;
- if the shared runtime is missing or invalid, the launcher resolves the pinned
  runtime release described by `runtime-release.json` and rehydrates the shared
  runtime store before starting `Okno.Server.exe`.

## 2. Installer-first runtime-only install

This is the advanced local MCP path when you want the shared runtime without
installing the Codex plugin.

Requirements:

- Windows 11
- either the GUI installer archive `okno-setup-unsigned-<version>-win-x64.zip`
  or the bootstrap pair `install-computer-use-win.ps1` +
  `okno-setup-cli-payload-<version>-win-x64.zip`

Steps:

1. Run `Okno Setup.exe` and choose `Install runtime only (Advanced)`, or run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File install-computer-use-win.ps1 -Mode runtime-only -PayloadArchivePath .\okno-setup-cli-payload-<version>-win-x64.zip
   ```

2. Copy the emitted MCP snippet into your client configuration.

Behavior:

- this mode installs the same shared runtime store used by Codex mode;
- it does not touch the personal Codex marketplace;
- it returns a ready-to-paste local `STDIO` snippet.

## 3. Generic MCP STDIO runtime zip

This remains the manual non-Codex path when you want only the standalone
runtime release zip without the installer shells.

Requirements:

- Windows 11
- the `win-x64` runtime release zip

Steps:

1. Download the `okno-computer-use-win-runtime-<version>-win-x64.zip` asset.
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

## 4. Developer from source

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
