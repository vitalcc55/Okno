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

## Примечание о codename

Внутренние проекты, namespaces и часть путей пока сохраняют имя `WinBridge`. Это сознательно оставлено как внутренний codename и не считается product-facing source of truth.
