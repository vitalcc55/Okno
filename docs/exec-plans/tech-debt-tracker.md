# Tech Debt / Risk Tracker

| ID | Область | Риск / долг | Влияние | Статус |
| --- | --- | --- | --- | --- |
| `WB-001` | MCP SDK | Часть удобных C# APIs помечена как experimental (`MCPEXP001`) | Нужен осознанный suppression и watch на breaking changes | open |
| `WB-002` | Windows APIs | Clipboard и broad input actions пока только в roadmap, не в shipped slice | Контракт V1 неполный для clipboard/keyboard/scroll/drag workflows | open |
| `WB-003` | Observability | Пока без внешнего trace exporter; опора на локальные file artifacts | Достаточно для bootstrap, но не для распределённой диагностики | open |
| `WB-004` | Tooling freshness | Template test stack не пересматривался отдельно от bootstrap | Возможна миграция на более свежие test packages позже | open |
