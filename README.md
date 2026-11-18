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

提示：字段名称以当前 VS 界面为准；不要把 VS Code 的 JSON 配置粘贴进 Visual Studio。
旧版 VS（17.x / 18.10 之前）仍走 Tools → Options → GitHub → Copilot Chat → Add Custom Model → Ollama。

## 接入 VS Code

方式一（推荐）：安装本项目 VS Code 插件（见下节），执行命令 `VsChatProxy: 配置 VS Code 聊天模型` 一键写入。

方式二（手动）：

1. 命令面板 → `Chat: Manage Language Models` → **Add Models → Custom Endpoint → Chat Completions**
2. 基地址填 `http://127.0.0.1:11434/v1`

或直接编辑 `chatLanguageModels.json`（Windows: `%APPDATA%\Code\User\`）：

```json
[
  {
    "name": "VsChatProxy",
    "vendor": "customendpoint",
    "apiType": "chat-completions",
    "url": "http://127.0.0.1:11434/v1"
  }
]
```

说明：

- 供应商 Key 只配置在代理端；VS Code 若强制要求 Key，可填任意占位文本
- 手动配置时模型 ID 从 `/v1/models` 复制（`供应商名:模型ID` 格式）
- Agent Host 的 BYOK 可能需要实验设置 `chat.agentHost.byokModels.enabled`

---

## VS Code 插件（vscode-extension/）

```
cd vscode-extension
npm install
npm run compile
npx vsce package    # 产出 .vsix，VS Code 中 "Install from VSIX" 安装
```

功能：

- **状态栏**：轮询 `/health` 显示代理运行状态
- `VsChatProxy: 启动代理` —— 后台拉起独立 exe（`--headless` 模式，需要 `vschatproxy.executablePath` 设置）
- `VsChatProxy: 停止代理` —— 结束插件自己拉起的进程
- `VsChatProxy: 配置 VS Code 聊天模型` —— 备份后自动写入 `chatLanguageModels.json`
- `VsChatProxy: 查看模型列表` —— 树视图展示 `/v1/models`，点击复制完整 ID
- `VsChatProxy: 打开配置文件`

设置项：

| 设置 | 默认值 | 说明 |
|---|---|---|
| `vschatproxy.baseUrl` | `http://127.0.0.1:11434` | 代理地址 |
| `vschatproxy.executablePath` | （空） | 独立 exe 路径（启动代理用） |
| `vschatproxy.autoStart` | `false` | VS Code 启动时自动拉起代理 |

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
