# Vs2026ChatProxy

让 Visual Studio 2026 / VS Code 的 GitHub Copilot Chat 使用自定义 AI 供应商（OpenAI 兼容 / Claude / Gemini / Ollama 等）的本地代理。

提供三种形态：

| 形态 | 说明 |
|---|---|
| **独立 exe**（`Vs2026ChatProxy`） | WinForms 窗口，配置供应商、启停代理、查看日志；支持 `--headless` 后台模式 |
| **VSIX 插件**（`Vs2026ChatProxy.VSIX`） | 集成到 VS 2026 的 Tool Window（View → Other Windows → VsCopilotProxy） |
| **VS Code 插件**（`vscode-extension/`） | 状态栏监控 + 后台拉起代理 + 一键配置 VS Code Chat 模型 |

**插件安装包与一键安装/卸载脚本在 `plugins/` 目录**：

```
plugins/
├── vs2026/   install.bat / uninstall.bat / Vs2026CopilotProxy.vsix   ← Visual Studio 2026 插件
└── vscode/   install.bat / uninstall.bat / VsCodeCopilotProxy.vsix   ← VS Code 插件
```

安装来源优先级：本地构建输出 → GitHub 最新 Release → 目录自带副本（可整个目录拷走离线分发）。

## 网络一键安装（仓库需 Public）

固定文件名解锁了 GitHub 稳定下载地址 `releases/latest/download/<文件名>`，无需 API、无需版本号：

**VS Code**（PowerShell 执行，一行安装最新版）：

```powershell
powershell -c "iwr https://github.com/zxxfhh/Vs2026ChatProxy/releases/latest/download/VsCodeCopilotProxy.vsix -OutFile $env:TEMP\p.vsix; code --install-extension $env:TEMP\p.vsix --force"
```

**Visual Studio**（PowerShell 下载 + 安装器）：

```powershell
powershell -c "iwr https://github.com/zxxfhh/Vs2026ChatProxy/releases/latest/download/Vs2026CopilotProxy.vsix -OutFile $env:TEMP\p.vsix; & \"${env:ProgramFiles}\Microsoft Visual Studio\18\Community\Common7\IDE\VSIXInstaller.exe\" $env:TEMP\p.vsix"
```

（路径按实际 VS 安装位置调整；也可以直接双击 `plugins/*/install.bat`，会自动走上述下载）

## 自动发布

推送 `v*` 标签触发 GitHub Actions 自动构建并发布 Release（`.github/workflows/release.yml`）：

```bash
git push origin main
git tag v1.1.2 && git push origin v1.1.2   # 示例
```

VS Code 插件（0.2.0+）启动时会自动检查 GitHub 新版本并提示一键更新。

参考项目：[CopilotProxy](../CopilotProxy)（端点行为与协议兼容性以其为准）。

---

## 快速开始

### 1. 编译

需要 VS 2026（含 .NET desktop workload）。VSIX 是经典工程，用 VS 的 MSBuild 编译整个解决方案：

```
MSBuild Vs2026ChatProxy.sln /p:Configuration=Release
```

或直接在 VS 中打开 `Vs2026ChatProxy.sln` 生成。

### 2. 配置供应商

启动独立 exe（或 VSIX 工具窗口），「➕ 添加」填入供应商：

- **类型**：`openai`（OpenAI 兼容：DeepSeek / Moonshot / Qwen / GLM / SiliconFlow / Groq / Ollama 均选此项）、`anthropic`、`gemini`
- **Base URL**：如 `https://api.deepseek.com`
- **API Key**：供应商密钥
- **模型**：至少一个；建议选上下文标签（如 `[128K]`），代理会在 `/v1/models`、`/api/show` 中向 IDE 广播 `context_length`（VS Code 依赖此字段）

配置文件为 JSON（`proxy-config.json`，VSIX 位于 `%LocalAppData%\VsCopilotProxy\`，VS Code 插件位于其 globalStorage）。旧版的 `maxInputTokens` 字段会自动迁移。

### 3. 启动代理

点击「启动代理」。默认监听 `http://127.0.0.1:11434`（可在界面修改端口）。

---

## 接入 Visual Studio 2026（18.10+ 新版）

18.10 起 BYOK 替换了旧的 Ask/Agent 体验，**入口在 Chat 窗口内**（不再是 Tools → Options）：

1. 打开 Chat 窗口 → 顶部**模型选择器**
2. 点击 **Add a model / Manage Models**
3. 二选一：
   - **Ollama 自定义端点**：服务地址 `http://127.0.0.1:11434`（不带 `/v1`），模型列表自动出现
   - **OpenAI 自定义端点**（Chat Completions）：基地址 `http://127.0.0.1:11434/v1`
4. 从列表选择完整模型 ID（格式 `供应商名:模型ID`，如 `my-anthropic:claude-sonnet-4-20250514`）
5. 进入 Agent (Preview) 使用

**重要：自定义模型只在 Agent (Preview) 模式可用**。在普通"询问 Copilot"聊天线程的模型选择器里，自定义模型显示为灰色（悬浮提示"你自己的模型仅在 Agent (Preview) 中工作"）——这是 VS 的产品规则，不是代理故障。点击输入框旁的 `Agent ▾` 切换模式或新建 Agent 线程即可。

提示：字段名称以当前 VS 界面为准；不要把 VS Code 的 JSON 配置粘贴进 Visual Studio。
旧版 VS（17.x / 18.10 之前）仍走 Tools → Options → GitHub → Copilot Chat → Add Custom Model → Ollama。

## 接入 VS Code

VS Code 自己不知道代理里有哪些模型，需要两份东西：**代理在运行**，以及 VS Code 的聊天模型配置（`%APPDATA%\Code\User\chatLanguageModels.json`）里**逐条列出**这些模型。新版 VS Code（实测 1.146）不再按地址自动发现「自定义端点」的模型。

方式一（推荐）：安装本项目 VS Code 插件（内置代理程序，见下节）。

1. 点状态栏的 `代理(停)` → **启动代理**（还没添加供应商就先点「打开代理窗口」）
2. 代理一运行，插件会提示「把它们写入 VS Code 的聊天模型…」→ 点 **立即写入**（也可以随时执行 `VsChatProxy: 把模型写入 VS Code 聊天模型`）
3. 回到 Chat 的模型选择器，在 `VsChatProxy` 下选模型；没出现就重新加载窗口

方式二（手动）：编辑 `chatLanguageModels.json`，在数组里加一个「自定义端点」供应商，每个模型一条：

```json
{
  "name": "VsChatProxy",
  "vendor": "customendpoint",
  "apiType": "chat-completions",
  "models": [
    {
      "id": "my-deepseek:deepseek-chat",
      "name": "my-deepseek:deepseek-chat",
      "url": "http://127.0.0.1:11434/v1",
      "toolCalling": true,
      "vision": false,
      "maxInputTokens": 122880,
      "maxOutputTokens": 8192
    }
  ]
}
```

- `id`：`/v1/models` 返回的模型 ID（去掉末尾的上下文标签，如 ` [128K]`）；VS Code 发请求时原样作为 `model` 传给代理
- `maxOutputTokens`：VS Code 会把它作为 `max_tokens` 发给代理，`8192` 是各家兼容接口都接受的上限
- **不要写顶层 `url`**（`{ "name", "vendor", "apiType", "url" }` 这种旧写法）：VS Code 看到它会改走「自动发现」，而那条路径对自定义端点不产出任何模型，表现就是供应商在、却选不到模型
- API Key 不用填：代理不校验鉴权；该字段在 VS Code 里是机密字段，直接写明文也不生效
- 保存后 VS Code 会自动刷新；没出现就重新加载窗口

## 接入其他 Ollama 兼容客户端（Navicat 等）

任何支持自定义 Ollama 端点的软件都能用本代理。以 **Navicat**（AI 助手）为例（已实测）：

1. Navicat → AI 助手设置 → 模型供应商选 **Ollama / 自定义**
2. 服务地址填 `http://127.0.0.1:11434`
3. 模型从列表选择（`供应商名:模型ID` 格式），点测试连接

说明：这类客户端通常走 `GET /api/tags`（模型列表）+ `POST /api/chat`（非流式对话），代理均已兼容；对话响应速度取决于所选模型（思考型模型会先消耗推理 token）。

### 常见问题：模型在 VS 选择器里是灰色

VS 的 Ollama 客户端会校验模型标签格式（仅允许 `[a-zA-Z0-9._:-]`）。Provider 名称含中文（如 `智谱Coding`）时，管理页能显示但选择器置灰不可选。代理已内置净化（自动把 `智谱Coding:glm-5.3` 广播为 `Coding:glm-5.3`，请求两种形式都能识别）；也可以直接把 Provider 名改成纯英文/数字（推荐，ID 更直观）。改完后在 VS「管理模型」里移除再重新添加 Ollama 端点即可刷新。

---

## VS Code 插件（vscode-extension/）

插件**内置代理程序**（`Vs2026ChatProxy.exe` 及其依赖随插件打包），安装后不需要再单独下载 exe 或配置路径。

点状态栏的 `代理(停)` 弹出菜单（命令相同）：

- `VsChatProxy: 启动/停止代理` —— 一个按钮：没在运行就后台启动，在运行就停止（插件打开的代理窗口也一并关闭）；配置文件改动后自动重启生效
- `VsChatProxy: 打开代理窗口（配置供应商）` —— 代理自带的设置界面：添加/修改供应商、查看日志
- `VsChatProxy: 把模型写入 VS Code 聊天模型` —— 读代理的 `/v1/models`，备份后逐条写入 `chatLanguageModels.json`（见上一节）。代理一运行，插件会自动核对并在需要时提示写入
- `VsChatProxy: 查看模型列表` —— 侧边栏树视图展示 `/v1/models`，点击复制完整 ID
- `VsChatProxy: 打开配置文件`、`VsChatProxy: 检查更新`

两份配置不要混淆：

| 文件 | 谁在用 | 位置 |
|---|---|---|
| `proxy-config.json` | 代理：供应商、Key、端口 | 插件自己的目录（代理窗口里添加的供应商保存在这里；路径见状态栏悬停提示或输出面板 `VsChatProxy`）。VS 2026 插件用的是另一份 `%LocalAppData%\VsCopilotProxy\proxy-config.json`，想两边共用就把 `vschatproxy.configPath` 指过去 |
| `chatLanguageModels.json` | VS Code：有哪些聊天模型可选 | `%APPDATA%\Code\User\`，由插件「把模型写入 VS Code 聊天模型」维护 |

设置项：

| 设置 | 默认值 | 说明 |
|---|---|---|
| `vschatproxy.baseUrl` | `http://127.0.0.1:11434` | 代理地址（启动时自动与配置文件里的端口对齐） |
| `vschatproxy.configPath` | （空） | 代理配置文件路径；留空用插件自己的那份 |
| `vschatproxy.executablePath` | （空） | 可选：改用自己编译或别处的 exe（留空用内置的） |
| `vschatproxy.maxOutputTokens` | `8192` | 写入 VS Code 模型配置的最大输出 token（VS Code 作为 `max_tokens` 发给代理） |
| `vschatproxy.maxInputTokens` | `128000` | 代理没有广播模型上下文长度时用的最大输入 token |
| `vschatproxy.autoStart` | `false` | VS Code 启动时自动后台拉起代理（配置里已有供应商时） |
| `vschatproxy.autoUpdate` | `true` | 启动时检查 GitHub 新版本并提示更新 |

打包（先用 VS 的 MSBuild 以 Release 生成整个解决方案，得到独立 exe 的产物）：

```
cd vscode-extension
npm install
npm run package     # 编译 + 把 exe 产物同步进 proxy/ + vsce package
```

---

## 独立 exe 命令行

```
Vs2026ChatProxy.exe [--config <路径>] [--headless]
```

- `--config`：指定配置文件路径（或环境变量 `UCP_PROXY_CONFIG`）
- `--headless`：无窗口后台运行，日志写配置文件同目录 `proxy_*.log`，结束进程即停止

## HTTP 端点

| 端点 | 说明 |
|---|---|
| `POST /v1/chat/completions` | OpenAI 格式（SSE 流式），VS "OpenAI 自定义端点"走此通道 |
| `POST /api/chat` | Ollama NDJSON 格式，VS "Ollama 自定义端点"走此通道 |
| `GET /v1/models` | OpenAI 模型列表（含 capabilities + model_info 元数据） |
| `GET /api/tags` / `/api/list` / `/api/models` | Ollama 模型列表 |
| `POST /api/show` | Ollama 模型详情（model_info 含 context_length，VS Code 读取） |
| `GET /api/ps` / `/api/version` / `/api/embeddings` 等 | Ollama 兼容探测端点 |
| `GET /health` | 健康检查 |
| `POST /v1/compact` | 手动上下文压缩 |

## 工具调用规范化

两条通道（OpenAI / Ollama）都会对模型输出的 tool_calls 做规范化修复：

- 无效 JSON 转义序列 / 畸形 JSON 修复 / 字段抢救
- 字符串值中的多重反斜杠路径清理
- 工具参数别名映射（`filePath`→`filename` 等，覆盖 VS 2026 / VS Code Copilot 的 30+ 工具）
- 相对路径 → 绝对路径（从请求文本提取 workspaceRoot）
- 从正文提取被淹没的工具调用（``` ```tool ``` 块 / MiMo·DeepSeek XML 格式）
- 已知工具缺失参数填充默认值

## 配置参考（proxy-config.json）

```json
{
  "port": 11434,
  "host": "127.0.0.1",
  "compactThresholdPercent": 85,
  "defaultModel": "",
  "providers": [
    {
      "type": "openai",
      "name": "my-deepseek",
      "baseUrl": "https://api.deepseek.com",
      "apiKey": "sk-xxx",
      "models": [
        { "id": "deepseek-chat", "contextTag": "[128K]", "appendContextTag": true, "maxContextTokens": 131072 }
      ]
    }
  ]
}
```

- `defaultModel`：非空时覆盖客户端请求的模型（格式 `供应商名:模型ID`）
- `compactThresholdPercent`：上下文用量超过模型上限的百分比时自动压缩
- `maxContextTokens`：0 表示不限（不压缩、不广播 context_length）
