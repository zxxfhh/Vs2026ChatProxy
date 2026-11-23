# VsChatProxy — 自定义模型代理（VS Code 插件）

让 VS Code Chat / Copilot 通过 Custom Endpoint 使用自定义 AI 供应商（OpenAI 兼容 / Claude / Gemini 等）的 VS Code 插件。插件**内置代理程序**（[Vs2026ChatProxy](https://github.com/zxxfhh/Vs2026ChatProxy)），安装后不需要再单独下载 exe 或配置路径。

## 使用

1. 安装插件，点击状态栏的 `$(plug) 代理(停)`（或命令面板执行 `VsChatProxy: 打开代理窗口`）
2. 首次使用时插件会打开**代理窗口**：点「➕ 添加」填入供应商（类型 / Base URL / API Key / 模型），保存
3. 回到 VS Code，状态栏菜单选「启动代理（后台运行）」；也可直接在代理窗口里点「启动代理」
4. 执行 `VsChatProxy: 配置 VS Code 聊天模型`，在 Chat 的模型选择器里选 `VsChatProxy` 下的模型

如果已经装了 VS 2026 插件并配置过供应商，VS Code 插件默认**共用同一份配置**，第 2 步可以跳过。

## 功能

- **状态栏监控**：轮询 `/health` 实时显示代理运行状态，点击弹出命令菜单
- **启动/停止代理**：以 `--headless` 模式后台拉起内置的代理程序；配置文件改动后自动重启生效
- **代理窗口**：打开代理自带的设置界面（独立 exe 的窗口），添加/修改供应商、查看日志
- **一键配置聊天模型**：自动向 `chatLanguageModels.json` 写入 Custom Endpoint 条目（`chat-completions`），含自动备份
- **模型列表**：侧边栏树视图展示代理 `/v1/models`，显示上下文长度，点击复制完整模型 ID
- **打开配置文件**：编辑代理的 `proxy-config.json`
- **自动更新**：启动时检查 GitHub Releases，有新版时一键更新

## 命令

| 命令 | 说明 |
|---|---|
| `VsChatProxy: 启动代理（后台运行）` | 后台拉起内置代理程序 |
| `VsChatProxy: 停止代理` | 结束插件拉起的进程 |
| `VsChatProxy: 打开代理窗口（配置供应商）` | 代理自带的设置界面 |
| `VsChatProxy: 配置 VS Code 聊天模型` | 写入 chatLanguageModels.json |
| `VsChatProxy: 查看模型列表` | 聚焦模型树视图 |
| `VsChatProxy: 打开配置文件` | 编辑 proxy-config.json |
| `VsChatProxy: 检查更新` | 手动检查 GitHub Releases |

## 设置

| 设置 | 默认值 | 说明 |
|---|---|---|
| `vschatproxy.baseUrl` | `http://127.0.0.1:11434` | 代理地址（不含 `/v1`）；启动时自动与配置文件里的端口对齐 |
| `vschatproxy.configPath` | 空 | 配置文件路径；留空时 Windows 下与 VS 2026 插件共用 `LocalAppData\VsCopilotProxy\proxy-config.json` |
| `vschatproxy.executablePath` | 空 | 可选：改用自己编译或别处的 `Vs2026ChatProxy.exe`（留空用内置的） |
| `vschatproxy.autoStart` | `false` | 启动 VS Code 时自动后台拉起代理（配置里已有供应商时） |
| `vschatproxy.autoUpdate` | `true` | 启动时检查 GitHub 新版本 |

诊断信息（代理程序位置、配置路径、启停记录）在输出面板的 `VsChatProxy` 频道；代理自身的请求日志在配置文件同目录的 `proxy_*.log`。

## 一键安装 / 卸载

见 `plugins\vscode\` 目录（install.bat / uninstall.bat，自动安装本目录最新打包的 vsix）。

## 从源码打包

```bash
# 1) 先用 VS 的 MSBuild 生成整个解决方案（Release），得到独立 exe 的构建产物
#    MSBuild Vs2026ChatProxy.sln /p:Configuration=Release
# 2) 编译插件、把 exe 产物同步进 proxy/ 并打包
cd vscode-extension
npm install
npm run package
```

`npm run proxy:sync` 把 `Vs2026ChatProxy/bin/Release/net48` 下的 exe / dll 拷进 `proxy/`，随插件一起打包；`npm test` 运行路径解析等逻辑的单元测试。

## 安装渠道

| 渠道 | 方式 |
|---|---|
| VS Code Marketplace | 上架后：扩展面板搜索 `VsChatProxy`，或 `code --install-extension zxxfhh.vs2026-chat-proxy`（原生自动更新） |
| GitHub Release（免账号） | `plugins\vscode\install.bat`，或一行命令见主 README「网络一键安装」 |
| 离线 | 下载 Release 中的 `VsCodeCopilotProxy.vsix` → Extensions → Install from VSIX |
