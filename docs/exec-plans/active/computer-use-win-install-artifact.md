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
2. `plugins/computer-use-win/run-computer-use-win-mcp.ps1` стартует только `runtime/win-x64/Okno.Server.exe --tool-surface-profile computer-use-win`.
3. Repo-root resolver, hint script и hint narrative удаляются из public plugin path.
4. Integration tests доказывают launcher из temp plugin copy вне repo tree без env/hint dependency.
5. Docs и generated commands переводятся на publish/install flow вместо hint/install flow.
6. Follow-up hardening на этой же ветке оставляет public product surface fail-closed: explicit profile валидируется через shared source of truth, app/process identity канонизируется для policy/playbook/appId, `get_app_state` не commit-ит session и не выдаёт `stateToken` при broken observation, а low-confidence coordinate click требует explicit confirm.
7. `stateToken` и downstream action revalidation используют один observation envelope: click revalidation не ослабляет исходный `maxNodes` budget, а malformed request shapes fail-close-ятся как `invalid_request` до observation/runtime stages.
8. `get_app_state` оформлен как `prepare observation -> materialize response -> commit shared state`: expected advisory-unavailable path для instruction loading не валит successful capture/UIA result, unexpected provider/runtime bug materialize-ится как truthful `observation_failed`, а hidden token inserts до полного success boundary не допускаются.

## Acceptance criteria

- `computer-use-win` launcher не зависит от repo root, env hint и `.tmp` staging runtime.
- `plugins/computer-use-win/runtime/win-x64/Okno.Server.exe` materialize-ится через publish script и используется как единственный runtime source для plugin.
- Temp plugin copy вне repo tree публикует только `list_apps`, `get_app_state`, `click`.
- Unknown `--tool-surface-profile` не widen-ит surface, а fail-close-ится на старте.
- Explicit blank `--tool-surface-profile` тоже считается invalid input и не эквивалентен отсутствующему selector.
- `get_app_state` публикует action-ready state только после успешного capture + UIA proof и не мутирует attached session на failed observation path.
- request schema для shipped public tools enforcing-ится на boundary: nested extra fields и invalid `maxNodes` не уходят в поздний runtime failure.
- Docs больше не рекламируют repo-root hint как install path для `computer-use-win`.
- Milestone считается полностью закрытым только после fresh-thread black-box proof на cache-installed copy.
