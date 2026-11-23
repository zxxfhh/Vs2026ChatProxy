import * as vscode from 'vscode';
import * as cp from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as os from 'os';
import {
    DEFAULT_MAX_INPUT_TOKENS,
    DEFAULT_MAX_OUTPUT_TOKENS,
    ModelTokenDefaults,
    ProxyModel,
    RegistrationState,
    buildChatProviderEntry,
    buildVsCodeModels,
    describeRegistration,
    findChatProviderEntry,
    proxyModelsFromResponse,
    readChatModelsFile,
    sameIdSet,
} from './chatProvider';
import {
    DEFAULT_PORT,
    ProxyConfigSummary,
    lastLogLines,
    normalizeBaseUrl,
    portOf,
    readProxyConfig,
    resolveProxyConfigPath,
    resolveProxyExe,
} from './proxyInfo';

let statusBarItem: vscode.StatusBarItem;
let output: vscode.OutputChannel;
let pollTimer: ReturnType<typeof setInterval> | undefined;
/** 插件拉起的进程。headless = 后台代理（随插件停止）；gui = 代理窗口（留给用户自己关） */
let ownedChild: { proc: cp.ChildProcess; kind: 'headless' | 'gui' } | undefined;
let modelsProvider: ModelsTreeProvider | undefined;
let configWatcher: vscode.FileSystemWatcher | undefined;
let configWatcherTimer: ReturnType<typeof setTimeout> | undefined;
/** 正在拉起代理（防止连点导致并发启动多个进程） */
let starting = false;
let activeContext: vscode.ExtensionContext | undefined;
/** 上一次轮询时代理是否在运行（只在"变为运行"时做一次模型配置核对） */
let wasRunning = false;
let skipNextSetupOffer = false;
let lastSetupOffer: { message: string; at: number } = { message: '', at: 0 };

const HEALTH_POLL_MS = 10_000;
const FETCH_TIMEOUT_MS = 2_500;
const START_WAIT_ROUNDS = 30;
const START_WAIT_MS = 500;
const CONFIG_RESTART_DELAY_MS = 800;

/** 写入 chatLanguageModels.json 的供应商条目名 */
const CHAT_PROVIDER_NAME = 'VsChatProxy';

/** 自动更新：从本仓库 GitHub Releases 拉取最新 vsix */
const GITHUB_REPO = 'zxxfhh/Vs2026ChatProxy';

const MENU_START = '$(play) 启动代理';
const MENU_WINDOW = '$(window) 打开代理窗口（添加/修改供应商）';
const MENU_STOP = '$(debug-stop) 停止代理';
const MENU_CONFIGURE = '$(settings-gear) 把模型写入 VS Code 聊天模型';
const MENU_MODELS = '$(list-unordered) 查看模型列表';
const MENU_OPEN_CONFIG = '$(go-to-file) 打开配置文件';
const MENU_UPDATE = '$(cloud-download) 检查更新';

interface GithubRelease {
    tag_name?: string;
    assets?: { name: string; browser_download_url: string }[];
}

// ── 工具函数 ──────────────────────────────────────────────

function getConfig(): vscode.WorkspaceConfiguration {
    return vscode.workspace.getConfiguration('vschatproxy');
}

function getBaseUrl(): string {
    return normalizeBaseUrl(getConfig().get<string>('baseUrl') ?? '');
}

function log(message: string): void {
    output?.appendLine(`[${new Date().toLocaleTimeString()}] ${message}`);
}

function sleep(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
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

function chatProviderUrl(): string {
    return `${getBaseUrl()}/v1`;
}

/** 空白配置模板（含一个示例供应商，需自行改 Key） */
function templateConfig(port: number): object {
    return {
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
}

// ── 进程管理 ──────────────────────────────────────────────

function trackChild(child: cp.ChildProcess, kind: 'headless' | 'gui'): void {
    ownedChild = { proc: child, kind };
    child.once('exit', () => {
        log(`代理进程已退出（${kind}）`);
        if (ownedChild?.proc === child) {
            ownedChild = undefined;
            if (kind === 'headless') {
                stopWatchingConfig();
            }
        }
    });
    child.once('error', (err) => {
        log(`代理进程错误: ${err.message}`);
        void vscode.window.showErrorMessage(`代理程序无法启动: ${err.message}`);
    });
}

/** 结束插件自己拉起的进程；返回是否确实结束了 */
function killOwnedChild(): boolean {
    const owned = ownedChild;
    if (!owned) return false;
    ownedChild = undefined; // 先摘掉，exit 回调里不再改状态
    owned.proc.kill();
    if (owned.kind === 'headless') {
        stopWatchingConfig();
    }
    log(`已结束代理进程（${owned.kind}）`);
    return true;
}

function stopWatchingConfig(): void {
    if (configWatcherTimer) {
        clearTimeout(configWatcherTimer);
        configWatcherTimer = undefined;
    }
    configWatcher?.dispose();
    configWatcher = undefined;
}

/** 结束进程后等 /health 真正下线（进程被结束后端口释放有几十毫秒延迟） */
async function waitUntilStopped(): Promise<void> {
    for (let i = 0; i < 10 && (await checkHealth()); i++) {
        await sleep(200);
    }
}

/** 防抖后按最新配置重启后台代理 */
function scheduleConfigRestart(context: vscode.ExtensionContext): void {
    if (configWatcherTimer) clearTimeout(configWatcherTimer);
    configWatcherTimer = setTimeout(() => {
        configWatcherTimer = undefined;
        if (ownedChild?.kind !== 'headless') return;
        log('配置文件已变更，重启后台代理');
        void (async () => {
            killOwnedChild();
            await waitUntilStopped();
            await startProxy(context, true);
            // 轮询没看到"停→跑"的过程，所以这里主动核对一次模型配置
            if (await checkHealth()) void offerChatModelSetup(context);
        })();
    }, CONFIG_RESTART_DELAY_MS);
}

function configMtime(configPath: string): number | undefined {
    try {
        return fs.statSync(configPath).mtimeMs;
    } catch {
        return undefined;
    }
}

/** 后台代理运行期间盯住配置文件：改了就重启代理让新配置生效 */
function watchConfig(context: vscode.ExtensionContext, configPath: string): void {
    // 只换监视器，不取消已排队的重启：启动过程中改的配置也要生效
    configWatcher?.dispose();
    configWatcher = undefined;
    try {
        configWatcher = vscode.workspace.createFileSystemWatcher(
            new vscode.RelativePattern(vscode.Uri.file(path.dirname(configPath)), path.basename(configPath)));
    } catch (err) {
        log(`无法监视配置文件: ${err instanceof Error ? err.message : String(err)}`);
        return;
    }
    configWatcher.onDidChange(() => scheduleConfigRestart(context));
    configWatcher.onDidCreate(() => scheduleConfigRestart(context));
    configWatcher.onDidDelete(() => {
        if (ownedChild?.kind === 'headless') {
            log('配置文件已删除，停止后台代理');
            killOwnedChild();
            void waitUntilStopped().then(() => pollHealth());
        }
    });
}

// ── 命令实现 ──────────────────────────────────────────────

/**
 * 配置里的端口才是代理真正的监听端口。与 baseUrl 不一致时把 baseUrl 改成一致的；
 * 用户显式设置过 baseUrl 则先询问。返回是否可以继续启动。
 */
async function alignPortWithConfig(summary: ProxyConfigSummary, silent: boolean): Promise<boolean> {
    const port = summary.port ?? DEFAULT_PORT;
    const urlPort = portOf(getBaseUrl());
    if (port === urlPort) return true;

    const target = `http://127.0.0.1:${port}`;
    const info = getConfig().inspect<string>('baseUrl');
    const scope = info?.workspaceValue !== undefined
        ? vscode.ConfigurationTarget.Workspace
        : info?.workspaceFolderValue !== undefined
            ? vscode.ConfigurationTarget.WorkspaceFolder
            : info?.globalValue !== undefined ? vscode.ConfigurationTarget.Global : undefined;

    if (scope === undefined) {
        // 只是默认值对不上，直接采用配置文件的端口
        await getConfig().update('baseUrl', target, vscode.ConfigurationTarget.Global);
        return true;
    }
    if (silent) {
        // 自动启动不弹询问：保持用户的设置，等手动启动时再问
        log(`配置文件端口 ${port} 与 baseUrl 端口 ${urlPort} 不一致，已跳过自动启动`);
        return false;
    }
    const pick = await vscode.window.showWarningMessage(
        `配置文件的端口是 ${port}，与插件设置 baseUrl 的端口 ${urlPort} 不一致，启动后插件会连不上代理。`,
        '改用配置文件的端口',
        '取消',
    );
    if (pick !== '改用配置文件的端口') return false;
    await getConfig().update('baseUrl', target, scope);
    return true;
}

/**
 * 后台启动代理（--headless）。silent=true 用于 VS Code 启动时的自动拉起与配置变更后的重启：
 * 不弹"未配置 / 端口不一致"之类的询问。
 */
async function startProxy(context: vscode.ExtensionContext, silent = false): Promise<void> {
    if (starting) return;
    starting = true; // 整个启动过程（含等待 /health）只允许一个在进行
    try {
        await startProxyInner(context, silent);
    } finally {
        starting = false;
    }
}

async function startProxyInner(context: vscode.ExtensionContext, silent: boolean): Promise<void> {
    if (await checkHealth()) {
        if (!silent) void vscode.window.showInformationMessage(`代理已在运行: ${getBaseUrl()}`);
        return;
    }
    // 代理窗口开着但代理没在跑：关掉窗口，统一改成后台运行（窗口只用来配置）
    if (ownedChild?.kind === 'gui') killOwnedChild();

    const exe = resolveProxyExe(getConfig().get<string>('executablePath') ?? '', context.extensionPath);
    if (!exe.exe) {
        log(`无法启动代理：${exe.problem}`);
        if (!silent) {
            const pick = await vscode.window.showErrorMessage(`无法启动代理：${exe.problem}`, '打开设置');
            if (pick === '打开设置') {
                void vscode.commands.executeCommand('workbench.action.openSettings', 'vschatproxy.executablePath');
            }
        }
        return;
    }

    const configPath = resolveProxyConfigPath(getConfig().get<string>('configPath') ?? '', context.globalStorageUri.fsPath);
    const summary = readProxyConfig(configPath);
    if (!summary || summary.providerCount === 0) {
        // 没有任何供应商：后台起来也没有模型可用，直接引导去代理窗口配置
        log(summary?.error ? `配置文件无法解析: ${summary.error}` : `配置里还没有供应商（${configPath}）`);
        if (silent) return;
        const pick = await vscode.window.showInformationMessage(
            summary?.error
                ? `代理配置文件无法解析：${summary.error}`
                : '还没有配置任何供应商（模型 API）。在代理窗口里添加后即可使用。',
            ...(summary?.error ? ['打开配置文件'] : ['打开代理窗口']),
        );
        if (pick === '打开代理窗口') {
            void openProxyWindow(context);
        } else if (pick === '打开配置文件') {
            void openConfig(context);
        }
        return;
    }
    if (!(await alignPortWithConfig(summary, silent))) return;

    let exited = false;
    await fs.promises.mkdir(path.dirname(configPath), { recursive: true });
    log(`启动后台代理: ${exe.exe} --config ${configPath}`);
    const mtimeBeforeStart = configMtime(configPath);
    const child = cp.spawn(exe.exe, ['--headless', '--config', configPath, '--parent-pid', String(process.pid)], {
        windowsHide: true,
    });
    let stderr = '';
    child.stderr?.on('data', (chunk) => { stderr += String(chunk); });
    trackChild(child, 'headless');
    child.once('exit', () => { exited = true; });

    const ok = await vscode.window.withProgress(
        { location: vscode.ProgressLocation.Notification, title: 'VsChatProxy: 正在启动代理…' },
        async () => {
            for (let i = 0; i < START_WAIT_ROUNDS; i++) {
                await sleep(START_WAIT_MS);
                if (exited) return false;
                if (await checkHealth()) return true;
            }
            return false;
        });

    if (ok) {
        log(`代理已启动: ${getBaseUrl()}`);
        // pollHealth 发现"变为运行"后，会核对 VS Code 聊天模型配置并按需提示
        void pollHealth();
        watchConfig(context, configPath);
        // 启动那段时间（监视器还没建好）配置又被改过：补一次重启，别让那次修改丢掉
        if (configMtime(configPath) !== mtimeBeforeStart) {
            log('启动期间配置文件有变动，再重启一次');
            scheduleConfigRestart(context);
        }
        return;
    }

    // 没就绪：结束进程，把日志尾部亮出来
    killOwnedChild();
    await waitUntilStopped();
    const hint = lastLogLines(configPath) ?? stderr.trim().slice(-400);
    log(`代理启动失败\n${hint ?? '(无日志输出)'}`);
    if (!silent) {
        const pick = await vscode.window.showErrorMessage(
            `代理启动失败${exited ? '（进程已退出）' : '（/health 一直未就绪）'}。\n${hint ?? ''}`,
            '打开配置文件',
        );
        if (pick === '打开配置文件') void openConfig(context);
    }
}

/** 一个按钮：代理在运行就停止，否则后台启动。插件打开的代理窗口也一并关掉 */
async function toggleProxy(context: vscode.ExtensionContext): Promise<void> {
    if (ownedChild?.kind === 'gui' && ownedChild.proc.exitCode === null) {
        killOwnedChild();
        await waitUntilStopped();
        await pollHealth();
        return;
    }
    if (await checkHealth()) {
        if (!killOwnedChild()) {
            void vscode.window.showWarningMessage(
                '代理在别处运行（例如 VS 2026 的工具窗口里，或是手动打开的代理窗口），请在那里停止。');
            return;
        }
        await waitUntilStopped();
        await pollHealth();
        return;
    }
    await startProxy(context);
}

/**
 * 打开代理自带的设置窗口（独立 exe 的界面），在里面添加/修改供应商、启停代理。
 * 后台代理在运行时先停掉，把端口让给窗口。
 */
async function openProxyWindow(context: vscode.ExtensionContext): Promise<void> {
    const exe = resolveProxyExe(getConfig().get<string>('executablePath') ?? '', context.extensionPath);
    if (!exe.exe) {
        void vscode.window.showErrorMessage(`无法打开代理窗口：${exe.problem}`);
        return;
    }
    const configPath = resolveProxyConfigPath(getConfig().get<string>('configPath') ?? '', context.globalStorageUri.fsPath);
    if (ownedChild?.kind === 'gui' && ownedChild.proc.exitCode === null) {
        void vscode.window.showInformationMessage('代理窗口已经打开（可能被其他窗口挡住了）');
        return;
    }
    await fs.promises.mkdir(path.dirname(configPath), { recursive: true });

    let handedOver = false;
    if (await checkHealth()) {
        if (ownedChild?.kind === 'headless') {
            killOwnedChild();
            await waitUntilStopped();
            handedOver = true;
        } else {
            void vscode.window.showInformationMessage(
                '代理正在别处运行（例如 VS 2026 的工具窗口）。代理窗口里只能修改配置，里面的「启动代理」会因端口被占用而失败。');
        }
    }

    log(`打开代理窗口: ${exe.exe} --config ${configPath}`);
    const child = cp.spawn(exe.exe, ['--config', configPath], { windowsHide: false });
    trackChild(child, 'gui');
    if (handedOver) {
        void vscode.window.showInformationMessage('后台代理已停止，请在代理窗口里点「启动代理」（或关闭窗口后再从状态栏后台启动）');
    }
}

function tokenDefaults(): ModelTokenDefaults {
    const output = getConfig().get<number>('maxOutputTokens') || DEFAULT_MAX_OUTPUT_TOKENS;
    const input = getConfig().get<number>('maxInputTokens') || DEFAULT_MAX_INPUT_TOKENS;
    return {
        maxOutputTokens: Math.min(Math.max(Math.floor(output), 256), 262144),
        maxInputTokens: Math.min(Math.max(Math.floor(input), 4096), 4194304),
    };
}

async function fetchProxyModels(): Promise<ProxyModel[]> {
    return proxyModelsFromResponse(await fetchJson<unknown>(`${getBaseUrl()}/v1/models`));
}

/** chatLanguageModels.json 里本代理条目的现状；文件 JSON 非法时抛错 */
function readRegistration(): { filePath: string; list: unknown[]; state: RegistrationState } {
    const filePath = chatModelsFilePath();
    const list = readChatModelsFile(filePath);
    return { filePath, list, state: describeRegistration(list, CHAT_PROVIDER_NAME, chatProviderUrl()) };
}

/** 还没把模型写进 VS Code（没条目、旧格式、或指向别的地址） */
/** 「把代理的模型写入 VS Code」自己拉起代理时，代理变为运行后不再弹一次"要不要写入" */
function suppressNextSetupOffer(): void {
    skipNextSetupOffer = true;
}

/**
 * 把代理的模型列表写进 VS Code 的聊天模型配置——chatLanguageModels.json 里本代理的「自定义端点」条目。
 * 模型列表取自代理的 /v1/models，所以需要代理在运行。
 */
async function configureChatModels(context: vscode.ExtensionContext): Promise<void> {
    const url = chatProviderUrl();
    if (!(await checkHealth())) {
        const pick = await vscode.window.showInformationMessage('代理还没运行，读不到模型列表。现在后台启动吗？', '启动代理');
        if (pick !== '启动代理') return;
        suppressNextSetupOffer();
        await startProxy(context);
        if (!(await checkHealth())) {
            skipNextSetupOffer = false;
            return;
        }
    }

    let models: ProxyModel[];
    try {
        models = await fetchProxyModels();
    } catch (err) {
        void vscode.window.showErrorMessage(`读取代理模型列表失败（${url}/models）：${err instanceof Error ? err.message : String(err)}`);
        return;
    }
    if (models.length === 0) {
        void vscode.window.showWarningMessage('代理里还没有任何模型。先在代理窗口里添加供应商，再执行「配置 VS Code 聊天模型」。', '打开代理窗口')
            .then((pick) => { if (pick === '打开代理窗口') void openProxyWindow(context); });
        return;
    }

    try {
        const { filePath, list } = readRegistration();
        if (fs.existsSync(filePath)) {
            const raw = await fs.promises.readFile(filePath, 'utf-8');
            // 备份原文件（含时间戳，避免覆盖早前备份）
            await fs.promises.writeFile(`${filePath}.${new Date().toISOString().replace(/[:.]/g, '-')}.bak`, raw, 'utf-8');
        } else {
            await fs.promises.mkdir(path.dirname(filePath), { recursive: true });
        }

        // 替换本代理的条目（沿用用户在旧条目里改过的模型设置），没有则追加
        const previous = findChatProviderEntry(list, CHAT_PROVIDER_NAME);
        const entry = buildChatProviderEntry(CHAT_PROVIDER_NAME, url, models, previous, tokenDefaults());
        const idx = list.indexOf(previous ?? null);
        if (idx >= 0) {
            list[idx] = entry;
        } else {
            list.push(entry);
        }

        await fs.promises.writeFile(filePath, JSON.stringify(list, null, 2), 'utf-8');
        const count = (entry.models as unknown[]).length;
        log(`已把 ${count} 个模型写入 ${filePath}`);
        const choice = await vscode.window.showInformationMessage(
            `已把 ${count} 个模型写入 VS Code 聊天模型配置（供应商「${CHAT_PROVIDER_NAME}」，${url}）。模型列表未刷新时请重新加载窗口`,
            '重新加载窗口',
            '打开文件',
        );
        if (choice === '重新加载窗口') {
            await vscode.commands.executeCommand('workbench.action.reloadWindow');
        } else if (choice === '打开文件') {
            await vscode.window.showTextDocument(vscode.Uri.file(filePath));
        }
    } catch (err) {
        void vscode.window.showErrorMessage(
            `写入 VS Code 聊天模型配置失败：${err instanceof Error ? err.message : String(err)}`, '打开文件');
    }
}

/**
 * 代理变为运行时核对一次 VS Code 聊天模型配置（chatLanguageModels.json 里本代理的条目）：
 * 还没写入、是旧格式、地址变了、或模型列表跟代理对不上时，提示一键写入/更新。
 */
async function offerChatModelSetup(context: vscode.ExtensionContext): Promise<void> {
    if (skipNextSetupOffer) {
        skipNextSetupOffer = false;
        return;
    }
    let state: RegistrationState;
    try {
        state = readRegistration().state;
    } catch {
        return;
    }
    const url = chatProviderUrl();
    let models: ProxyModel[];
    try {
        models = await fetchProxyModels();
    } catch {
        return;
    }
    if (models.length === 0) return;
    const current = buildVsCodeModels(models, url, undefined, tokenDefaults()).map((m) => m.id as string);

    const ask = async (message: string): Promise<void> => {
        // 轮询与显式核对可能前后脚各触发一次，同一条提示 5 秒内只弹一回
        if (message === lastSetupOffer.message && Date.now() - lastSetupOffer.at < 5000) return;
        lastSetupOffer = { message, at: Date.now() };
        log(`VS Code 聊天模型配置待更新：${message}`);
        const pick = await vscode.window.showInformationMessage(message, '立即写入');
        if (pick === '立即写入') void configureChatModels(context);
    };
    if (state.kind === 'missing') {
        await ask(`代理里有 ${current.length} 个模型。写入 VS Code 的聊天模型后，才能在 Chat 的模型选择器里选到。现在写入吗？`);
        return;
    }
    if (state.kind === 'legacy') {
        await ask('VS Code 聊天模型配置里本代理的条目是旧格式：当前版本的 VS Code 需要逐条列出模型，所以选不到模型。现在按代理的模型列表重写吗？');
        return;
    }
    if (state.kind === 'otherUrl') {
        await ask(`VS Code 聊天模型配置里的代理地址与当前设置不一致（当前 ${url}）。现在更新吗？`);
        return;
    }
    if (sameIdSet(current, state.modelIds)) return;
    const added = current.filter((id) => !state.modelIds.includes(id)).length;
    const removed = state.modelIds.filter((id) => !current.includes(id)).length;
    await ask(`代理的模型列表变了（新增 ${added} 个、移除 ${removed} 个）。更新 VS Code 聊天模型配置？`);
}

async function openConfig(context: vscode.ExtensionContext): Promise<void> {
    const configPath = resolveProxyConfigPath(getConfig().get<string>('configPath') ?? '', context.globalStorageUri.fsPath);
    if (!fs.existsSync(configPath)) {
        const pick = await vscode.window.showInformationMessage(
            `还没有代理配置文件：\n${configPath}\n在代理窗口里添加供应商会自动创建。`,
            '打开代理窗口',
            '新建空白配置',
        );
        if (pick === '打开代理窗口') {
            void openProxyWindow(context);
            return;
        }
        if (pick !== '新建空白配置') return;
        await fs.promises.mkdir(path.dirname(configPath), { recursive: true });
        await fs.promises.writeFile(configPath, JSON.stringify(templateConfig(portOf(getBaseUrl()) ?? DEFAULT_PORT), null, 2), 'utf-8');
    }
    await vscode.window.showTextDocument(vscode.Uri.file(configPath));
}

// ── 状态栏 & 轮询 ────────────────────────────────────────

function updateStatusBar(running: boolean): void {
    const configPath = activeContext
        ? resolveProxyConfigPath(getConfig().get<string>('configPath') ?? '', activeContext.globalStorageUri.fsPath)
        : '';
    if (running) {
        statusBarItem.text = '$(plug) 代理';
        statusBarItem.tooltip = new vscode.MarkdownString(
            `VsChatProxy 运行中：${getBaseUrl()}\n\n点击：停止代理 / 打开代理窗口 / 更新 VS Code 聊天模型`
            + (configPath ? `\n\n配置文件：${configPath}` : ''));
        statusBarItem.backgroundColor = undefined;
        statusBarItem.color = undefined;
    } else {
        statusBarItem.text = '$(plug) 代理(停)';
        statusBarItem.tooltip = new vscode.MarkdownString(
            'VsChatProxy 未运行\n\n点击：启动代理 / 打开代理窗口配置供应商'
            + (configPath ? `\n\n配置文件：${configPath}` : ''));
        statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.warningBackground');
    }
    statusBarItem.show();
    modelsProvider?.refresh();
}

async function pollHealth(): Promise<void> {
    const info = await checkHealth();
    const running = info !== undefined;
    updateStatusBar(running);
    // 只在"变为运行"的那一刻核对一次，轮询不会反复弹提示
    if (running && !wasRunning && activeContext) {
        void offerChatModelSetup(activeContext);
    }
    wasRunning = running;
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
        if ((await checkHealth()) === undefined) {
            return [ModelTreeItem.action('代理未运行 — 点击启动', 'vschatproxy.toggle')];
        }
        try {
            const res = await fetchJson<{ data: ModelEntry[] }>(`${getBaseUrl()}/v1/models`);
            this.models = res.data ?? [];
        } catch {
            this.models = [];
        }
        if (this.models.length === 0) {
            return [ModelTreeItem.action('代理里还没有模型 — 点击打开代理窗口配置供应商', 'vschatproxy.openWindow')];
        }
        return this.models.map((m) => {
            const ctx = (m.model_info?.['general.context_length'] as number | undefined) ?? undefined;
            const label = ctx ? `${m.id}  (${fmtContext(ctx)})` : m.id;
            return new ModelTreeItem(label, m.id, true);
        });
    }
}

class ModelTreeItem extends vscode.TreeItem {
    /** 显示一行提示，点击时执行命令 */
    static action(label: string, command: string): ModelTreeItem {
        const item = new ModelTreeItem(label, undefined, false);
        item.iconPath = new vscode.ThemeIcon('info');
        item.command = { command, title: label };
        return item;
    }

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

// ── 入口 ─────────────────────────────────────────────────

export function activate(context: vscode.ExtensionContext): void {
    activeContext = context;
    output = vscode.window.createOutputChannel('VsChatProxy');
    context.subscriptions.push(output);
    log(`插件 ${String(context.extension.packageJSON?.version ?? '?')} 激活；代理程序: ${resolveProxyExe('', context.extensionPath).exe ?? '(未内置)'}`);
    log(`配置文件: ${resolveProxyConfigPath(getConfig().get<string>('configPath') ?? '', context.globalStorageUri.fsPath)}；代理地址: ${getBaseUrl()}`);

    statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    statusBarItem.command = 'vschatproxy.statusMenu';

    modelsProvider = new ModelsTreeProvider();
    const treeView = vscode.window.createTreeView('vschatproxy.models', {
        treeDataProvider: modelsProvider,
    });

    const register = (id: string, cb: (...args: any[]) => any) =>
        context.subscriptions.push(vscode.commands.registerCommand(id, cb));

    register('vschatproxy.toggle', () => toggleProxy(context));
    register('vschatproxy.openWindow', () => openProxyWindow(context));
    register('vschatproxy.configureChatModels', () => configureChatModels(context));
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
        const toggle = running ? MENU_STOP : MENU_START;
        const pick = await vscode.window.showQuickPick(
            [
                { label: running ? '$(check) 代理运行中' : '$(circle-slash) 代理未运行', description: getBaseUrl(), kind: vscode.QuickPickItemKind.Separator },
                { label: toggle },
                { label: MENU_WINDOW, description: '代理自带的设置界面：添加供应商、看日志' },
                { label: MENU_CONFIGURE, description: '写入 VS Code 的 chatLanguageModels.json，模型才会出现在 Chat 的模型选择器里' },
                { label: MENU_MODELS },
                { label: MENU_OPEN_CONFIG, description: '代理自己的 proxy-config.json' },
                { label: MENU_UPDATE },
            ],
            { placeHolder: 'VsChatProxy' },
        );
        switch (pick?.label) {
            case MENU_START:
            case MENU_STOP: void vscode.commands.executeCommand('vschatproxy.toggle'); break;
            case MENU_WINDOW: void vscode.commands.executeCommand('vschatproxy.openWindow'); break;
            case MENU_CONFIGURE: void vscode.commands.executeCommand('vschatproxy.configureChatModels'); break;
            case MENU_MODELS: void vscode.commands.executeCommand('vschatproxy.showModels'); break;
            case MENU_OPEN_CONFIG: void vscode.commands.executeCommand('vschatproxy.openConfig'); break;
            case MENU_UPDATE: void vscode.commands.executeCommand('vschatproxy.checkUpdate'); break;
        }
    });

    context.subscriptions.push(statusBarItem, treeView);

    // 初始状态 + 轮询
    void pollHealth();
    pollTimer = setInterval(() => void pollHealth(), HEALTH_POLL_MS);
    context.subscriptions.push({ dispose: () => { if (pollTimer) clearInterval(pollTimer); } });

    // 自动启动：代理没在运行且配置里已有供应商时后台拉起
    const autoStart = getConfig().get<boolean>('autoStart') ?? false;
    if (autoStart) {
        void checkHealth().then((h) => {
            if (!h) {
                void startProxy(context, true);
            }
        });
    }

    // 启动时静默检查更新（vschatproxy.autoUpdate 控制，也可手动触发命令）
    void checkForUpdate(context);
}

export function deactivate(): void {
    // 后台代理随插件停止；代理窗口是用户看得见的独立窗口，留着
    if (ownedChild?.kind === 'headless') {
        ownedChild.proc.kill();
        ownedChild = undefined;
    }
    stopWatchingConfig();
    if (pollTimer) {
        clearInterval(pollTimer);
    }
    activeContext = undefined;
}
