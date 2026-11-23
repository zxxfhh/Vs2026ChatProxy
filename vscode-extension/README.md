# VsChatProxy — 自定义模型代理（VS Code 插件）

让 VS Code Chat / Copilot 使用自定义 AI 供应商（OpenAI 兼容 / Claude / Gemini 等）的 VS Code 插件。插件**内置代理程序**（[Vs2026ChatProxy](https://github.com/zxxfhh/Vs2026ChatProxy)），安装后不需要再单独下载 exe 或配置路径。

## 使用

1. 安装插件，点状态栏的 `代理(停)` → **打开代理窗口**，点「➕ 添加」填入供应商（类型 / Base URL / API Key / 模型），保存后关掉窗口
2. 再点状态栏 → **启动代理**（代理在运行时这一项显示为「停止代理」，是同一个按钮）
3. 代理一运行，右下角提示「把它们写入 VS Code 的聊天模型…」→ 点 **立即写入**
4. 回到 Chat 的模型选择器，在 `VsChatProxy` 下选模型；没出现就执行「重新加载窗口」

第 3 步只需做一次。之后代理的模型列表有变化（增减供应商或模型）时，插件会在代理运行后提示更新；也可以随时执行 `VsChatProxy: 把模型写入 VS Code 聊天模型`。

## 两份文件

| 文件 | 作用 | 位置 |
|---|---|---|
| `proxy-config.json` | 代理的配置：供应商、Key、端口（代理窗口里添加的供应商保存在这里） | 插件自己的目录，路径见状态栏悬停提示或输出面板 `VsChatProxy` |
| `chatLanguageModels.json` | VS Code 自己的「有哪些聊天模型可选」清单 | `%APPDATA%\Code\User\`，由插件「把模型写入 VS Code 聊天模型」维护 |

VS Code 新版（实测 1.146）不再按地址自动发现「自定义端点」的模型，必须在 `chatLanguageModels.json` 里逐条列出——这就是第 3 步要做的事，格式示例见主 README「接入 VS Code」。

## 命令

| 命令 | 说明 |
|---|---|
| `VsChatProxy: 启动/停止代理` | 没在运行就后台启动内置代理；在运行就停止 |
| `VsChatProxy: 打开代理窗口（配置供应商）` | 代理自带的设置界面 |
| `VsChatProxy: 把模型写入 VS Code 聊天模型` | 读代理的模型列表，备份后写入 chatLanguageModels.json |
| `VsChatProxy: 查看模型列表` | 侧边栏树视图展示 `/v1/models`，点击复制完整 ID |
| `VsChatProxy: 打开配置文件` | 编辑 proxy-config.json |
| `VsChatProxy: 检查更新` | 手动检查 GitHub Releases |

## 设置

| 设置 | 默认值 | 说明 |
|---|---|---|
| `vschatproxy.baseUrl` | `http://127.0.0.1:11434` | 代理地址（不含 `/v1`）；启动时自动与配置文件里的端口对齐 |
| `vschatproxy.configPath` | 空 | 代理配置文件路径；留空用插件自己的那份（VS 2026 插件用的是另一份，想共用就指过去） |
| `vschatproxy.executablePath` | 空 | 可选：改用自己编译或别处的 `Vs2026ChatProxy.exe`（留空用内置的） |
| `vschatproxy.maxOutputTokens` | `8192` | 写入 VS Code 模型配置的最大输出 token（VS Code 作为 `max_tokens` 发给代理） |
| `vschatproxy.maxInputTokens` | `128000` | 代理没有广播模型上下文长度时用的最大输入 token |
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

`npm test` 运行单元测试（路径解析、配置读取、VS Code 模型条目生成）。
