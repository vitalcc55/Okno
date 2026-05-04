# ExecPlan: Okno public surface pack

Status: `completed`  
Date: `2026-05-04`

## 1. Goal

Пересобрать публичную GitHub surface репозитория так, чтобы:

- `Okno` читался как главный бренд;
- `Computer Use for Windows` читался как текущая shipped capability;
- `MCP over STDIO` был явной частью product story;
- root public surface стал international-first через English canonical README
  и полные parity-версии на русском и упрощённом китайском;
- supporting public docs и metadata перестали ломать первое впечатление.

## 2. Scope

В этот workstream вошли:

- новый canonical English `README.md`;
- полные `README.ru.md` и `README.zh-CN.md`;
- English rewrite для `plugins/computer-use-win/README.md`;
- English rewrite для `docs/product/index.md`;
- metadata/trust pack через `docs/public/github-repository-metadata.md` и
  `SECURITY.md`;
- alignment для plugin display metadata;
- changelog и task-state sync.

## 3. Key decisions

- `Okno` остаётся brand-first public entry point.
- `Computer Use for Windows` подаётся как первая публичная capability, а не как
  новый основной бренд репозитория.
- Текущий product-ready transport формулируется как local `MCP over STDIO`.
- README не превращается в конкурентную таблицу или sales page ради sales page:
  differentiation строится через architecture/behavior claims, а не через
  broad market naming.
- Homepage не заполняется фиктивной ссылкой до появления настоящего внешнего
  landing page.

## 4. Non-goals

- не делать installer/productization wave;
- не переводить всю архитектурную документацию;
- не менять public tool contract;
- не добавлять новые runtime features;
- не обещать one-click consumer distribution раньше времени.

## 5. Outcome

После этого пакета:

- GitHub front door продаёт `Okno` как Windows-native MCP runtime;
- public story стала английской по умолчанию и international-friendly;
- русская и китайская README-версии стали полными и самодостаточными;
- supporting files больше не сбрасывают читателя из English front door в
  русскоязычную maintainer prose;
- у репозитория появился source of truth для About / Topics / social preview;
- public trust surface усилена через `SECURITY.md`.
