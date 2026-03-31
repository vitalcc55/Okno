# Okno Product Docs

## Статус

Да, теперь продуктовую разработку можно продолжать без трёх старых markdown-файлов в корне. Их содержимое перенесено в `docs/product/`, а ссылки в repo-memory перепривязаны сюда.

## Source of truth

- [okno-vision.md](okno-vision.md)
- [okno-spec.md](okno-spec.md)
- [okno-roadmap.md](okno-roadmap.md)

## Как использовать

- `okno-vision.md` — north star и архитектурный вектор продукта.
- `okno-spec.md` — продуктовый контракт V1.
- `okno-roadmap.md` — порядок реализации и delivery milestones.

## Transport policy

- Product-ready target сейчас только `STDIO` local process.
- HTTP/URL transport не входит в текущий delivery scope.
- После готового и стабильного `STDIO` можно будет проектировать и добавлять HTTP-режим как отдельный этап.

## OpenAI interop note

- `shell`, `skills`, `MCP` и `computer use` рассматриваются как соседние слои, а не как одна и та же feature под разными именами.
- Для текущего продукта `Okno` остаётся Windows-native runtime и MCP surface, а built-in `computer use` рассматривается как будущая compatibility track.
- Текущий локальный integration path для Codex остаётся plugin/MCP поверх этого репозитория; adapter к OpenAI `computer use` должен быть отдельным слоем и не должен менять ближайший V1 roadmap.
- Source of truth по этой теме лежит в [../architecture/openai-computer-use-interop.md](../architecture/openai-computer-use-interop.md) и дополняется roadmap в [okno-roadmap.md](okno-roadmap.md).

## Примечание о codename

Внутренние проекты, namespaces и часть путей пока сохраняют имя `WinBridge`. Это сознательно оставлено как внутренний codename и не считается product-facing source of truth.
