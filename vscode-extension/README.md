# VsChatProxy — 自定义模型代理（VS Code 插件）

管理 [Vs2026ChatProxy](https://github.com/zxxfhh/Vs2026ChatProxy) 本地代理的 VS Code 插件：让 VS Code Chat / Copilot 通过 Custom Endpoint 使用自定义 AI 供应商（OpenAI 兼容 / Claude / Gemini 等）。

## 功能

- **状态栏监控**：轮询 `/health` 实时显示代理运行状态，点击弹出命令菜单
- **启动/停止代理**：以 `--headless` 模式后台拉起独立 exe（需设置 `vschatproxy.executablePath`）
- **一键配置聊天模型**：自动向 `chatLanguageModels.json` 写入 Custom Endpoint 条目（`chat-completions`），含自动备份
- **模型列表**：侧边栏树视图展示代理 `/v1/models`，显示上下文长度，点击复制完整模型 ID
- **打开配置文件**：编辑代理的 `proxy-config.json`

## 使用

1. 先用独立 exe（或 VS 2026 VSIX）配置好供应商，或让插件生成模板后编辑
2. 设置 `vschatproxy.baseUrl`（默认 `http://127.0.0.1:11434`）
3. 执行命令 `VsChatProxy: 配置 VS Code 聊天模型`
4. 在 Chat 的模型选择器中选择 `VsChatProxy` 供应商下的模型

## 命令

| 命令 | 说明 |
|---|---|
| `VsChatProxy: 启动代理` | 后台拉起独立 exe |
| `VsChatProxy: 停止代理` | 结束插件拉起的进程 |
| `VsChatProxy: 配置 VS Code 聊天模型` | 写入 chatLanguageModels.json |
| `VsChatProxy: 查看模型列表` | 聚焦模型树视图 |
| `VsChatProxy: 打开配置文件` | 编辑 proxy-config.json |

## 设置

| 设置 | 默认值 | 说明 |
|---|---|---|
| `vschatproxy.baseUrl` | `http://127.0.0.1:11434` | 代理地址（不含 `/v1`） |
| `vschatproxy.executablePath` | 空 | `Vs2026ChatProxy.exe` 路径 |
| `vschatproxy.autoStart` | `false` | 启动 VS Code 时自动拉起代理 |
| `vschatproxy.autoUpdate` | `true` | 启动时检查 GitHub 新版本 |

## 一键安装 / 卸载

见 `plugins\vscode\` 目录（install.bat / uninstall.bat，自动安装本目录最新打包的 vsix）。

## 从源码打包

```bash
npm install
npm run compile
npx vsce package
# 然后运行 plugins\vscode\install.bat，或在 VS Code 中: Extensions → ... → Install from VSIX
```

## 安装渠道

| 渠道 | 方式 |
|---|---|
| VS Code Marketplace | 上架后：扩展面板搜索 `VsChatProxy`，或 `code --install-extension zxxfhh.vs2026-chat-proxy`（原生自动更新） |
| GitHub Release（免账号） | `plugins\vscode\install.bat`，或一行命令见主 README「网络一键安装」 |
| 离线 | 下载 Release 中的 `VsCodeCopilotProxy.vsix` → Extensions → Install from VSIX |
