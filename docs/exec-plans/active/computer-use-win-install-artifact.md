# ExecPlan: Computer Use for Windows install artifact

## Контекст

Public plugin `plugins/computer-use-win/` уже публикует правильный product surface, но install artifact исторически запускал runtime через checkout репозитория. Это делало plugin корректным как local/dev published path, но не как self-contained installed artifact.

## Цель

Сделать `computer-use-win` self-contained install artifact:

1. installed plugin copy запускает только plugin-local runtime bundle;
2. launcher не ищет repo root;
3. launcher не использует `.tmp/.codex/artifacts/local` и не materialize-ит bundle на старте;
4. product install path для `computer-use-win` больше не требует repo-root hint.

## Границы

- Входит: plugin-local runtime publish pipeline, launcher rewrite, legacy hint-path removal, docs/generated sync, install-surface tests.
- Не входит: расширение public tool surface beyond `list_apps`, `get_app_state`, `click`.
- Не входит: автоматическое изменение `~/.codex/config.toml` или plugin cache из repo scripts.

## Repo changes

1. `scripts/codex/publish-computer-use-win-plugin.ps1` materialize-ит self-contained `win-x64` runtime bundle в `plugins/computer-use-win/runtime/win-x64/`.
2. `plugins/computer-use-win/run-computer-use-win-mcp.ps1` стартует только `runtime/win-x64/Okno.Server.exe --tool-surface-profile computer-use-win` и fail-fast-ится, если canonical runtime bundle не совпадает с shipped runtime manifest, а не только если отсутствует apphost или несколько sentinel files.
3. Publish promote path сохраняет last-known-good runtime до тех пор, пока swap новой директории не завершён успешно; failure не должен оставлять plugin без runnable runtime, rollback обязан пытаться восстановить canonical `runtime/win-x64`, а canonical path не должен использоваться как repair scratch space: restore-repair materialize-ится в side directory и handoff-ится только после completion proof по полному published runtime manifest. Rollback source не должен потребляться destructive move-ом до validated terminal state; pre-manifest installed runtime допускается только через migration-aware restore candidate, который получает generated manifest до canonical handoff. Cleanup старого backup/staging/swap/repair после terminal success считается best-effort и не должен превращать уже успешный promote в ложный install failure. Если final repair handoff падает, recovery path должен пытаться вернуть runnable bundle именно на canonical path, а не оставлять его только во временных side directories.
4. Repo-root resolver, hint script и hint narrative удаляются из public plugin path.
5. Integration tests доказывают launcher из temp plugin copy вне repo tree без env/hint dependency.
6. Docs и generated commands переводятся на publish/install flow вместо hint/install flow.
7. Follow-up hardening на этой же ветке оставляет public product surface fail-closed: explicit profile валидируется через shared source of truth, app/process identity канонизируется для policy/playbook/appId и остаётся обязательной для approval path, `get_app_state` не commit-ит session и не выдаёт `stateToken` при broken observation, а low-confidence coordinate click требует explicit confirm.
8. `stateToken` и downstream action revalidation используют один observation envelope: click revalidation не ослабляет исходный `maxNodes` budget, malformed request shapes fail-close-ятся как `invalid_request` до observation/runtime stages, а published click schema требует `stateToken` и ровно один selector mode.
9. `get_app_state` оформлен как `prepare observation -> materialize response -> commit shared state`: expected advisory-unavailable path для instruction loading не валит successful capture/UIA result, unexpected provider/runtime bug materialize-ится как truthful `observation_failed`, а hidden token inserts до полного success boundary не допускаются.
10. Public tool metadata для `computer-use-win` должна быть honest about side effects: `get_app_state` не может публиковаться как `read_only`, потому что approved/confirmed path меняет approval/focus/session state; registration, manifest и generated docs должны выводиться из одной safety model.

## Acceptance criteria

- `computer-use-win` launcher не зависит от repo root, env hint и `.tmp` staging runtime.
- `plugins/computer-use-win/runtime/win-x64/Okno.Server.exe` materialize-ится через publish script и используется как единственный runtime source для plugin.
- `plugins/computer-use-win/runtime/win-x64` считается валидным только если совпадает с полным published runtime manifest; missing dependency DLL или size drift fail-close-ятся до launcher startup и до canonical handoff после recovery.
- Rollback сохраняет usable source до validated canonical restore: corrupt manifest backup fail-close-ится без consuming `$backupRoot`, а legacy pre-manifest backup мигрируется через side candidate с generated manifest.
- Temp plugin copy вне repo tree публикует только `list_apps`, `get_app_state`, `click`.
- Unknown `--tool-surface-profile` не widen-ит surface, а fail-close-ится на старте.
- Explicit blank `--tool-surface-profile` тоже считается invalid input и не эквивалентен отсутствующему selector.
- `get_app_state` публикует action-ready state только после успешного capture + UIA proof и не мутирует attached session на failed observation path.
- request schema для shipped public tools enforcing-ится на boundary: nested extra fields и invalid `maxNodes` не уходят в поздний runtime failure.
- `get_app_state` не рекламируется как `read_only` в MCP annotations, `ToolContractManifest` и generated contract docs.
- Docs больше не рекламируют repo-root hint как install path для `computer-use-win`.
- Milestone считается полностью закрытым только после fresh-thread black-box proof на cache-installed copy.
