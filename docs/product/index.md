# Okno Product Docs

## Статус

Да, теперь продуктовую разработку можно продолжать без трёх старых markdown-файлов в корне. Их содержимое перенесено в `docs/product/`, а ссылки в repo-memory перепривязаны сюда.

## Source of truth

- [okno-vision.md](okno-vision.md)
- [okno-spec.md](okno-spec.md)
- [okno-roadmap.md](okno-roadmap.md)
- [../generated/computer-use-win-interfaces.md](../generated/computer-use-win-interfaces.md)

## Как использовать

- `okno-vision.md` — north star и архитектурный вектор продукта.
- `okno-spec.md` — текущий продуктовый контракт.
- `okno-roadmap.md` — порядок реализации и delivery milestones.

## Transport policy

- Product-ready target сейчас только `STDIO` local process.
- HTTP/URL transport не входит в текущий delivery scope.
- После готового и стабильного `STDIO` можно будет проектировать и добавлять HTTP-режим как отдельный этап.

## OpenAI interop note

- `shell`, `skills`, `MCP` и `computer use` рассматриваются как соседние слои, а не как одна и та же feature под разными именами.
- Для текущего продукта публичным Codex path становится `computer-use-win`, а `Okno` остаётся внутренним Windows-native runtime/engine.
- Текущий локальный integration path для Codex идёт через plugin/MCP surface `computer-use-win` поверх этого репозитория; built-in OpenAI `computer use` остаётся внешней compatibility track и не меняет ближайший roadmap продукта.
- Source of truth по этой теме лежит в [../architecture/openai-computer-use-interop.md](../architecture/openai-computer-use-interop.md) и дополняется roadmap в [okno-roadmap.md](okno-roadmap.md).

## Примечание о codename

Внутренние проекты, namespaces и часть путей пока сохраняют имя `WinBridge`. Это сознательно оставлено как внутренний codename и не считается product-facing source of truth.
