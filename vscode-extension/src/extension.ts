import * as vscode from 'vscode';
import * as cp from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as os from 'os';

let statusBarItem: vscode.StatusBarItem;
let pollTimer: ReturnType<typeof setInterval> | undefined;
let ownedChild: cp.ChildProcess | undefined;
let modelsProvider: ModelsTreeProvider | undefined;

const HEALTH_POLL_MS = 10_000;
const FETCH_TIMEOUT_MS = 2_500;

/** 自动更新：从本仓库 GitHub Releases 拉取最新 vsix */
const GITHUB_REPO = 'zxxfhh/Vs2026ChatProxy';

interface GithubRelease {
    tag_name?: string;
    assets?: { name: string; browser_download_url: string }[];
}

// ── 工具函数 ──────────────────────────────────────────────

function getConfig(): vscode.WorkspaceConfiguration {
    return vscode.workspace.getConfiguration('vschatproxy');
}

/** 规范化 baseUrl：去掉末尾斜杠与 /v1 后缀 */
function getBaseUrl(): string {
    const raw = (getConfig().get<string>('baseUrl') ?? 'http://127.0.0.1:11434').trim();
    let url = raw.replace(/\/+$/, '');
    if (url.endsWith('/v1')) {
        url = url.slice(0, -3).replace(/\/+$/, '');
    }
    return url || 'http://127.0.0.1:11434';
}

async function fetchJson<T>(url: string, timeoutMs = FETCH_TIMEOUT_MS): Promise<T> {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    try {
        const res = await fetch(url, { signal: controller.signal });
        if (!res.ok) {
            throw new Error(`HTTP ${res.status}`);
        }
        return (await res.json()) as T;
    } finally {
        clearTimeout(timer);
    }
}

interface HealthInfo {
    status: string;
    providers: number;
}

async function checkHealth(): Promise<HealthInfo | undefined> {
    try {
        const info = await fetchJson<HealthInfo>(`${getBaseUrl()}/health`);
        return info.status === 'ok' ? info : undefined;
    } catch {
        return undefined;
    }
}

/** chatLanguageModels.json 所在目录（VS Code 用户设置目录） */
function chatModelsFilePath(): string {
    if (process.platform === 'win32') {
        return path.join(process.env.APPDATA ?? path.join(os.homedir(), 'AppData', 'Roaming'),
            'Code', 'User', 'chatLanguageModels.json');
    }
    if (process.platform === 'darwin') {
        return path.join(os.homedir(), 'Library', 'Application Support', 'Code', 'User', 'chatLanguageModels.json');
    }
    return path.join(os.homedir(), '.config', 'Code', 'User', 'chatLanguageModels.json');
}

/** 插件专用代理配置文件路径（globalStorage/proxy-config.json） */
async function proxyConfigPath(context: vscode.ExtensionContext): Promise<string> {
    const dir = context.globalStorageUri.fsPath;
    await fs.promises.mkdir(dir, { recursive: true });
    return path.join(dir, 'proxy-config.json');
}

/** 首次运行时写入默认配置模板（含一个示例供应商，需自行改 Key） */
async function ensureProxyConfig(configPath: string, port: number): Promise<void> {
    if (fs.existsSync(configPath)) {
        return;
    }
    const template = {
        port,
        host: '127.0.0.1',
        compactThresholdPercent: 85,
        defaultModel: '',
        providers: [
            {
                type: 'openai',
                name: 'my-deepseek',
                baseUrl: 'https://api.deepseek.com',
                apiKey: 'sk-YOUR-API-KEY',
                models: [{ id: 'deepseek-chat', contextTag: '[128K]', appendContextTag: true, maxContextTokens: 131072 }],
            },
        ],
    };
    await fs.promises.writeFile(configPath, JSON.stringify(template, null, 2), 'utf-8');
}

// ── 命令实现 ──────────────────────────────────────────────

async function startProxy(context: vscode.ExtensionContext): Promise<void> {
    if (await checkHealth()) {
        vscode.window.showInformationMessage(`代理已在运行: ${getBaseUrl()}`);
        return;
    }

    const exePath = (getConfig().get<string>('executablePath') ?? '').trim();
    if (!exePath || !fs.existsSync(exePath)) {
        const choice = await vscode.window.showErrorMessage(
            '未配置 Vs2026ChatProxy.exe 路径（或文件不存在），无法启动代理',
            '打开设置',
        );
        if (choice === '打开设置') {
            await vscode.commands.executeCommand('workbench.action.openSettings', 'vschatproxy.executablePath');
        }
        return;
    }

    // 从 baseUrl 提取端口，写入默认配置模板
    let port = 11434;
    try {
        port = new URL(getBaseUrl()).port ? Number(new URL(getBaseUrl()).port) : 11434;
    } catch { /* 用默认值 */ }
    const configPath = await proxyConfigPath(context);
    await ensureProxyConfig(configPath, port);

    vscode.window.withProgress(
        { location: vscode.ProgressLocation.Notification, title: 'VsChatProxy: 正在启动代理…' },
        async (progress) => {
            progress.report({ message: `拉起 ${path.basename(exePath)} --headless` });
            ownedChild = cp.spawn(exePath, ['--headless', '--config', configPath], {
                windowsHide: true,
            });
            ownedChild.on('exit', () => {
                ownedChild = undefined;
                updateStatusBar(false);
            });
            // 出错输出写日志文件（headless 模式自身写 proxy_*.log），这里静默即可
            ownedChild.stderr?.on('data', () => { /* 进程级错误通过 exit 事件体现 */ });

            progress.report({ message: '等待 /health 就绪' });
            for (let i = 0; i < 30; i++) {
                await new Promise((r) => setTimeout(r, 500));
                if (await checkHealth()) {
                    vscode.window.showInformationMessage(`代理已启动: ${getBaseUrl()}`);
                    updateStatusBar(true);
                    modelsProvider?.refresh();
                    return;
                }
                if (ownedChild === undefined) {
                    break; // 进程已退出
                }
            }
            vscode.window.showErrorMessage(
                `代理启动失败（进程可能立即退出，请检查配置文件: ${configPath}）`,
            );
        },
    );
}

function stopProxy(): void {
    if (!ownedChild) {
        vscode.window.showWarningMessage('代理不是由本插件启动的（或已在运行外部实例），请自行停止');
        return;
    }
    ownedChild.kill();
    ownedChild = undefined;
    updateStatusBar(false);
    vscode.window.showInformationMessage('已停止代理进程');
}

async function configureChatModels(): Promise<void> {
    const filePath = chatModelsFilePath();
    const url = `${getBaseUrl()}/v1`;
    const entry = {
        name: 'VsChatProxy',
        vendor: 'customendpoint',
        apiType: 'chat-completions',
        url,
    };

    try {
        let list: unknown[] = [];
        if (fs.existsSync(filePath)) {
            const raw = await fs.promises.readFile(filePath, 'utf-8');
            // 备份原文件（含时间戳，避免覆盖早前备份）
            const backup = `${filePath}.${new Date().toISOString().replace(/[:.]/g, '-')}.bak`;
            await fs.promises.writeFile(backup, raw, 'utf-8');
            const parsed = JSON.parse(raw);
            if (Array.isArray(parsed)) {
                list = parsed;
            }
        } else {
            await fs.promises.mkdir(path.dirname(filePath), { recursive: true });
        }

        // 替换同名条目，否则追加
        const idx = list.findIndex(
            (e) => typeof e === 'object' && e !== null && (e as { name?: string }).name === 'VsChatProxy',
        );
        if (idx >= 0) {
            list[idx] = entry;
        } else {
            list.push(entry);
        }

        await fs.promises.writeFile(filePath, JSON.stringify(list, null, 2), 'utf-8');
        const choice = await vscode.window.showInformationMessage(
            `已写入 Custom Endpoint 条目（${url}）。如 Chat 模型列表未刷新，请重新加载窗口`,
            '重新加载窗口',
            '打开文件',
        );
        if (choice === '重新加载窗口') {
            await vscode.commands.executeCommand('workbench.action.reloadWindow');
        } else if (choice === '打开文件') {
            await vscode.window.showTextDocument(vscode.Uri.file(filePath));
        }
    } catch (err) {
        vscode.window.showErrorMessage(`写入 chatLanguageModels.json 失败: ${err instanceof Error ? err.message : String(err)}`);
    }
}

async function openConfig(context: vscode.ExtensionContext): Promise<void> {
    // 优先打开插件 globalStorage 下的配置；若代理在别处运行，也可手动浏览
    const configPath = await proxyConfigPath(context);
    const target = fs.existsSync(configPath)
        ? vscode.Uri.file(configPath)
        : await vscode.window.showOpenDialog({
            title: '选择 proxy-config.json',
            filters: { 'JSON 配置': ['json'] },
            canSelectMany: false,
        }).then((uris) => uris?.[0]);
    if (target) {
        await vscode.window.showTextDocument(target);
    }
}

// ── 状态栏 & 轮询 ────────────────────────────────────────

function updateStatusBar(running: boolean): void {
    if (running) {
        statusBarItem.text = `$(plug) 代理`;
        statusBarItem.tooltip = `VsChatProxy 运行中: ${getBaseUrl()}（点击查看命令）`;
        statusBarItem.backgroundColor = undefined;
        statusBarItem.color = undefined;
    } else {
        statusBarItem.text = `$(plug) 代理(停)`;
        statusBarItem.tooltip = `VsChatProxy 未运行（点击查看命令）`;
        statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.warningBackground');
    }
    statusBarItem.show();
    modelsProvider?.refresh();
}

async function pollHealth(): Promise<void> {
    const info = await checkHealth();
    updateStatusBar(info !== undefined);
}

// ── 模型树视图 ───────────────────────────────────────────

interface ModelEntry {
    id: string;
    owned_by?: string;
    model_info?: Record<string, unknown>;
}

class ModelsTreeProvider implements vscode.TreeDataProvider<ModelTreeItem> {
    private _onDidChange = new vscode.EventEmitter<void>();
    readonly onDidChangeTreeData = this._onDidChange.event;
    private models: ModelEntry[] = [];

    refresh(): void {
        this._onDidChange.fire();
    }

    getTreeItem(element: ModelTreeItem): vscode.TreeItem {
        return element;
    }

    async getChildren(): Promise<ModelTreeItem[]> {
        try {
            const res = await fetchJson<{ data: ModelEntry[] }>(`${getBaseUrl()}/v1/models`);
            this.models = res.data ?? [];
        } catch {
            this.models = [];
        }
        if (this.models.length === 0) {
            return [new ModelTreeItem('代理未运行或无模型', undefined, false)];
        }
        return this.models.map((m) => {
            const ctx = (m.model_info?.['general.context_length'] as number | undefined) ?? undefined;
            const label = ctx ? `${m.id}  (${fmtContext(ctx)})` : m.id;
            return new ModelTreeItem(label, m.id, true);
        });
    }
}

class ModelTreeItem extends vscode.TreeItem {
    constructor(label: string, public readonly modelId: string | undefined, copyable: boolean) {
        super(label, vscode.TreeItemCollapsibleState.None);
        if (copyable && modelId) {
            this.tooltip = '点击复制完整模型 ID';
            this.command = {
                command: 'vschatproxy.copyModelId',
                title: '复制模型 ID',
                arguments: [modelId],
            };
        }
        this.iconPath = copyable ? new vscode.ThemeIcon('symbol-field') : new vscode.ThemeIcon('circle-slash');
    }
}

function fmtContext(n: number): string {
    return n >= 1_000_000 ? `${(n / 1_000_000).toFixed(n % 1_000_000 === 0 ? 0 : 1)}M`
        : n >= 1000 ? `${Math.round(n / 1000)}K`
        : String(n);
}

// ── 入口 ─────────────────────────────────────────────────

// ── 自动更新（GitHub Releases）──────────────────────────

/** 语义化比较：a < b 返回 true */
function versionLt(a: string, b: string): boolean {
    const pa = a.replace(/^v/, '').split('.').map((n) => parseInt(n, 10) || 0);
    const pb = b.replace(/^v/, '').split('.').map((n) => parseInt(n, 10) || 0);
    for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
        const d = (pa[i] ?? 0) - (pb[i] ?? 0);
        if (d !== 0) {
            return d < 0;
        }
    }
    return false;
}

/**
 * 检查 GitHub 最新 Release；发现新版本时提示更新。
 * 手动模式（notifyUser=true）下即使无更新也给出反馈。
 */
async function checkForUpdate(context: vscode.ExtensionContext, notifyUser = false): Promise<void> {
    const autoUpdate = getConfig().get<boolean>('autoUpdate') ?? true;
    if (!autoUpdate && !notifyUser) {
        return;
    }
    let latest = '';
    let asset: { name: string; browser_download_url: string } | undefined;
    try {
        const rel = await fetchJson<GithubRelease>(`https://api.github.com/repos/${GITHUB_REPO}/releases/latest`, 5_000);
        latest = (rel.tag_name ?? '').replace(/^v/, '');
        asset = rel.assets?.find((a) => a.name === 'VsCodeCopilotProxy.vsix');
    } catch {
        if (notifyUser) {
            void vscode.window.showWarningMessage('VsChatProxy: 检查更新失败（网络不可用或 GitHub 限流）');
        }
        return;
    }
    const current = String(context.extension.packageJSON?.version ?? '0.0.0');
    if (!latest || !asset || !versionLt(current, latest)) {
        if (notifyUser) {
            void vscode.window.showInformationMessage(`VsChatProxy 已是最新版本 ${current}`);
        }
        return;
    }

    const choice = await vscode.window.showInformationMessage(
        `VsChatProxy 有新版本 ${latest}（当前 ${current}）`,
        '立即更新',
        '查看发布说明',
    );
    if (choice === '查看发布说明') {
        void vscode.env.openExternal(vscode.Uri.parse(`https://github.com/${GITHUB_REPO}/releases/latest`));
        return;
    }
    if (choice !== '立即更新') {
        return;
    }

    try {
        // 下载 vsix 到 globalStorage 并调用内置命令安装
        const dir = context.globalStorageUri.fsPath;
        await fs.promises.mkdir(dir, { recursive: true });
        const target = path.join(dir, asset.name);
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), 30_000);
        try {
            const res = await fetch(asset.browser_download_url, { signal: controller.signal });
            if (!res.ok) {
                throw new Error(`HTTP ${res.status}`);
            }
            await fs.promises.writeFile(target, Buffer.from(await res.arrayBuffer()));
        } finally {
            clearTimeout(timer);
        }
        await vscode.commands.executeCommand('workbench.extensions.installExtension', vscode.Uri.file(target));
        const reload = await vscode.window.showInformationMessage(
            `VsChatProxy v${latest} 已下载并安装，重载窗口后生效`,
            '重新加载窗口',
        );
        if (reload === '重新加载窗口') {
            await vscode.commands.executeCommand('workbench.action.reloadWindow');
        }
    } catch (err) {
        void vscode.window.showErrorMessage(
            `更新失败: ${err instanceof Error ? err.message : String(err)}（可从 Release 页手动下载）`,
        );
    }
}

export function activate(context: vscode.ExtensionContext): void {
    statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    statusBarItem.command = 'vschatproxy.statusMenu';

    modelsProvider = new ModelsTreeProvider();
    const treeView = vscode.window.createTreeView('vschatproxy.models', {
        treeDataProvider: modelsProvider,
    });

    const register = (id: string, cb: (...args: any[]) => any) =>
        context.subscriptions.push(vscode.commands.registerCommand(id, cb));

    register('vschatproxy.start', () => startProxy(context));
    register('vschatproxy.stop', () => stopProxy());
    register('vschatproxy.configureChatModels', () => configureChatModels());
    // 树视图自带 `${viewId}.focus` 命令，直接转发
    register('vschatproxy.showModels', () => vscode.commands.executeCommand('vschatproxy.models.focus'));
    register('vschatproxy.refreshModels', () => modelsProvider?.refresh());
    register('vschatproxy.openConfig', () => openConfig(context));
    register('vschatproxy.checkUpdate', () => checkForUpdate(context, true));
    register('vschatproxy.copyModelId', async (id: string) => {
        await vscode.env.clipboard.writeText(id);
        void vscode.window.setStatusBarMessage(`已复制: ${id}`, 3000);
    });
    register('vschatproxy.statusMenu', async () => {
        const running = (await checkHealth()) !== undefined;
        const pick = await vscode.window.showQuickPick(
            [
                { label: running ? '$(check) 代理运行中' : '$(circle-slash) 代理未运行', description: getBaseUrl(), kind: vscode.QuickPickItemKind.Separator },
                { label: '$(play) 启动代理', description: '后台拉起独立 exe（--headless）' },
                { label: '$(debug-stop) 停止代理', description: '结束本插件拉起的进程' },
                { label: '$(settings-gear) 配置 VS Code 聊天模型', description: '写入 chatLanguageModels.json' },
                { label: '$(list-unordered) 查看模型列表' },
                { label: '$(go-to-file) 打开配置文件' },
            ],
            { placeHolder: 'VsChatProxy' },
        );
        switch (pick?.label) {
            case '$(play) 启动代理': void vscode.commands.executeCommand('vschatproxy.start'); break;
            case '$(debug-stop) 停止代理': void vscode.commands.executeCommand('vschatproxy.stop'); break;
            case '$(settings-gear) 配置 VS Code 聊天模型': void vscode.commands.executeCommand('vschatproxy.configureChatModels'); break;
            case '$(list-unordered) 查看模型列表': void vscode.commands.executeCommand('vschatproxy.showModels'); break;
            case '$(go-to-file) 打开配置文件': void vscode.commands.executeCommand('vschatproxy.openConfig'); break;
        }
    });

    context.subscriptions.push(statusBarItem, treeView);

    // 初始状态 + 轮询
    void pollHealth();
    pollTimer = setInterval(() => void pollHealth(), HEALTH_POLL_MS);
    context.subscriptions.push({ dispose: () => pollTimer && clearInterval(pollTimer) });

    // 自动启动
    const autoStart = getConfig().get<boolean>('autoStart') ?? false;
    if (autoStart) {
        void checkHealth().then((h) => {
            if (!h) {
                void startProxy(context);
            }
        });
    }

    // 启动时静默检查更新（vschatproxy.autoUpdate 控制，也可手动触发命令）
    void checkForUpdate(context);
}

export function deactivate(): void {
    ownedChild?.kill();
    if (pollTimer) {
        clearInterval(pollTimer);
    }
}
