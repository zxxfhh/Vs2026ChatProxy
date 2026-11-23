# Changelog

## 0.1.0

- 初始版本：状态栏监控、启动/停止代理（--headless）、一键配置 chatLanguageModels.json、模型列表树视图、配置文件编辑

## 0.2.0

- 新增自动更新：启动时检查 GitHub Releases，新版提示一键下载安装（`vschatproxy.autoUpdate` 可关闭）
- 新增命令「检查更新」

## 1.1.3

- 版本号与仓库 tag 对齐（修复 0.2.0 vs tag 1.1.2 导致的更新提示死循环）

## 1.1.4

- 净化名转小写（Ollama 命名惯例，修复 VS 选择器仍置灰）

## 1.1.5

- **插件内置代理程序**：不再要求配置 `vschatproxy.executablePath`，安装后即可从状态栏启动
- 新增命令「打开代理窗口」：打开代理自带的设置界面，添加/修改供应商、查看日志
- 默认与 VS 2026 插件共用同一份配置（`LocalAppData\VsCopilotProxy\proxy-config.json`），两边供应商保持一致；可用 `vschatproxy.configPath` 指定其他文件
- 后台代理随配置文件修改自动重启；VS Code 扩展宿主退出后后台代理自行退出，不留残余进程占端口
- 启动时自动对齐配置文件里的端口与 `baseUrl`
