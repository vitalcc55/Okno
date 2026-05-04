# Okno

[English](README.md) | [Русский](README.ru.md) | [**简体中文**](README.zh-CN.md)

> 面向 AI 代理的 Windows 原生 MCP 运行时
>
> **Computer Use for Windows** 是 Okno 当前首个公开能力，用于通过
> `MCP` / `STDIO` 自动化 Windows 桌面应用。

| 平台 | 传输方式 | 当前能力 | 运行时 | 核心执行模型 |
| --- | --- | --- | --- | --- |
| Windows 11 | 基于 `STDIO` 的本地 MCP | `Computer Use for Windows` | C# / .NET 8 | UIA 语义路径 + 基于 screenshot 的验证 |

## 为什么是 Okno

Okno 把真实的 Windows 桌面应用变成 AI 代理可操作的界面层。它面向
这样的场景：shell 命令、仅限浏览器的自动化或应用 API 已经不够用了。

当前公开能力 **Computer Use for Windows** 允许代理查找窗口、获取可执行
动作的状态、对目标 UI 执行动作，并在动作后验证结果，而不是把一次点击
或一次按键直接当成成功。

它不是一个通用的 Windows 脚本工具箱，也不是伪装成 desktop automation
的浏览器项目。它是一个面向代理 GUI 工作的 Windows 原生运行时，当前
正式支持的集成路径建立在 `MCP over STDIO` 之上。

## Computer Use for Windows 今天已经能做什么

当前公开可用的工具层可以：

- 通过 `list_apps` 发现正在运行的桌面应用和窗口；
- 通过 `get_app_state` 获取带 screenshot 的状态；
- 返回 accessibility tree、几何信息、`captureReference` 和短时有效的
  `stateToken`；
- 执行 `click`、`press_key`、`set_value`、`type_text`、`scroll`、
  `perform_secondary_action` 和 `drag`；
- 当动作证据不足时返回 `verify_needed`，而不是伪造 semantic success；
- 在支持的动作上通过 `observeAfter=true` 返回 `successorState`；
- 同时覆盖 strong-UIA 应用和更复杂的 Qt / Electron / custom GUI，
  并在需要时使用受约束的 physical fallback path。

## 它是如何工作的

Okno 作为 MCP 运行时在 Windows 本地运行。当前正式支持的 transport 是
**本地 `STDIO`**，而最顺滑的集成路径是这个仓库附带的 Codex plugin。

`Computer Use for Windows` 是当前公开的能力层。底层仍然使用
Okno 运行时和内部 `WinBridge` 组件，但公开给用户的是一组安静、清晰的
操作界面层，而不是把底层 `windows.*` 工具直接当成主要产品叙事。

正常工作循环如下：

```text
list_apps -> get_app_state -> action -> verify
```

也就是：

1. 找到目标窗口；
2. 获取新的截图和 accessibility state；
3. 选择当前证据最强的动作路径；
4. 通过 `observeAfter=true` 或新的 `get_app_state` 验证结果。

## Okno 的区别在哪里

Okno 的设计围绕四条产品规则展开。

| 原则 | 实际含义 |
| --- | --- |
| 强语义路径 | 当目标证据足够强时，优先使用 UIA-backed 动作。 |
| screenshot 参与证明 | 截图不是装饰，而是 observation 和 verification 的一部分。 |
| 有边界的 physical fallback | 当 UIA 不够强时，运行时仍然可以通过受保护的 physical path 执行动作。 |
| 结果必须诚实 | 对低置信动作返回 `verify_needed`，而不是乐观地声称成功。 |

因此，Okno 比只会发送坐标的工具更适合真实 Windows GUI automation，也比
假设语义自动化处处可靠的工具更适合 weak-UIA 或 custom GUI 目标。

## 它最适合哪些场景

如果你需要下面这些能力，Okno 很合适：

- 面向 AI 代理的本地 Windows 桌面自动化；
- Windows 原生 MCP 运行时，而不是仅限浏览器的工具；
- 为 Codex 准备的、可控的桌面操作界面层；
- 面向不稳定或弱语义 UI 的 verify-first 执行模型。

如果你的主要需求是以下方向，Okno 不是首选：

- 浏览器 DOM-first 自动化；
- 无需本地准备的一键式 consumer distribution；
- 完整的 enterprise RPA orchestration 或 low-code workflow tooling。

## 快速开始

当前最短、最受支持的路径是：**Windows 上的 Codex** 加上本仓库提供的
本地 plugin。

### 前置条件

- Windows 11
- Windows 上的 Codex
- PowerShell
- 如果安装后的 plugin 副本在首次启动时需要解析其 pinned runtime
  release，则需要网络访问

### 1. 克隆仓库

```powershell
git clone https://github.com/vitalcc55/Okno.git
cd Okno
```

### 2. 从仓库 marketplace 条目安装本地 plugin

相关入口：

- [.agents/plugins/marketplace.json](.agents/plugins/marketplace.json)
- [plugins/computer-use-win](plugins/computer-use-win)
- [plugins/computer-use-win/.codex-plugin/plugin.json](plugins/computer-use-win/.codex-plugin/plugin.json)
- [plugins/computer-use-win/.mcp.json](plugins/computer-use-win/.mcp.json)

### 3. 重启 Codex 或打开新的 thread

安装后的 plugin 运行于 Codex plugin cache，而不是仓库根目录。如果安装
副本里已经存在经过校验的 runtime bundle，launcher 会直接使用它。如果
runtime bundle 缺失或无效，launcher 会按
[plugins/computer-use-win/runtime-release.json](plugins/computer-use-win/runtime-release.json)
中描述的 pinned runtime release 进行解析，校验 SHA256 和
`okno-runtime-bundle-manifest.json`，然后才启动 MCP host。

### 4. 跑通第一次操作循环

1. 调用 `list_apps`；
2. 选择一个 `windowId`；
3. 调用 `get_app_state(windowId=...)`；
4. 执行动作；
5. 通过 `observeAfter=true` 或新的 `get_app_state` 验证结果。

如果你要给通用 MCP `STDIO` 客户端使用，或者要走维护者的源码工作流，请
参见
[docs/runbooks/computer-use-win-install.md](docs/runbooks/computer-use-win-install.md)。
维护者仍可通过 `scripts/codex/publish-computer-use-win-plugin.ps1` 显式生成
plugin-local bundle。

## 公开工具 surface

| 工具 | 用途 |
| --- | --- |
| `list_apps` | 发现正在运行的桌面应用和窗口。 |
| `get_app_state` | 返回带 screenshot、边界、token 和 accessibility data 的状态。 |
| `click` | 激活语义目标或已确认的点位目标。 |
| `press_key` | 发送明确的键盘输入。 |
| `set_value` | 在支持时走语义化的 value-setting path。 |
| `type_text` | 通过语义路径或受保护的 fallback typing path 输入文本。 |
| `scroll` | 执行滚动，并在可能时验证滚动结果。 |
| `perform_secondary_action` | 执行 toggle、expand-collapse 一类 secondary semantic action。 |
| `drag` | 以显式 source / destination proof 执行 bounded drag。 |

几个重要字段：

- `windowId` 是当前 discovery state 的公共选择器，不是永久窗口身份。
- `stateToken` 是最近一次 observation state 的短期 proof artifact。
- `verify_needed` 表示动作已经 dispatch，但语义结果仍需通过观察确认。
- `successorState` 是 `observeAfter=true` 成功后返回的 post-action state。

## 信任、安全与边界

- 运行时工作在**真实**的 Windows desktop session 中。
- physical mouse 和 keyboard input 是系统共享资源。
- 本项目并不声称提供“第二个独立系统光标”。
- weak-semantic 或 poor-UIA 目标可能需要有边界的 physical fallback。
- blocked 或 sensitive targets 仍然需要明确的策略约束。
- 对低置信动作，正确理解应该是 `dispatch + verify`，而不是盲目当成成功。

## 文档地图

如果你需要的不只是 front page：

- product docs: [docs/product/index.md](docs/product/index.md)
- product spec: [docs/product/okno-spec.md](docs/product/okno-spec.md)
- roadmap: [docs/product/okno-roadmap.md](docs/product/okno-roadmap.md)
- product vision: [docs/product/okno-vision.md](docs/product/okno-vision.md)
- architecture docs: [docs/architecture/index.md](docs/architecture/index.md)
- public capability docs:
  [plugins/computer-use-win/README.md](plugins/computer-use-win/README.md)
- 安装路径说明：
  [docs/runbooks/computer-use-win-install.md](docs/runbooks/computer-use-win-install.md)
- generated interfaces:
  [docs/generated/computer-use-win-interfaces.md](docs/generated/computer-use-win-interfaces.md)
- commands inventory: [docs/generated/commands.md](docs/generated/commands.md)

## 当前状态

今天的 Okno 已经可以作为 Codex 的本地 Windows plugin/runtime 使用，也可
以作为一个基于 `STDIO` 的本地 MCP surface 使用。

已经比较成熟的部分：

- 公开 capability 已经 shipped，并且可以从 source repo 安装；
- 面向通用 MCP 客户端的 release-backed runtime contract 已经定义完成；
- runtime bundle 和 plugin install surface 已经存在；
- public contract、smoke path 和 verification loop 都是真实可运行的；
- 项目早已不是 research prototype。

仍需诚实说明的部分：

- 安装体验依然偏向开发者；
- Codex plugin 的安装路径目前仍依赖本地仓库 checkout；
- 在“无需本地预先构建 runtime 的 plugin 安装路径”成为主要公开路径
  之前，GitHub runtime releases 仍必须先存在；
- 一键式 consumer distribution 不是当前产品形态。

## 许可证

本仓库使用 **GNU Affero General Public License v3.0 or later**
(`AGPL-3.0-or-later`)。

Copyright © 2025–2026 Vlasov Vitaly

- [LICENSE](LICENSE)
- [LICENSES/AGPL-3.0-or-later.txt](LICENSES/AGPL-3.0-or-later.txt)
- [REUSE.toml](REUSE.toml)
