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
