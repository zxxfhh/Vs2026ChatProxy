// 代理程序与配置文件的定位逻辑。不依赖 vscode 模块，便于用 Node 直接做单元测试。
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

export const DEFAULT_PORT = 11434;

/** 规范化 baseUrl：去掉末尾斜杠与 /v1 后缀 */
export function normalizeBaseUrl(raw: string): string {
    let url = raw.trim().replace(/\/+$/, '');
    if (url.endsWith('/v1')) {
        url = url.slice(0, -3).replace(/\/+$/, '');
    }
    return url || `http://127.0.0.1:${DEFAULT_PORT}`;
}

export function portOf(url: string): number | undefined {
    try {
        const u = new URL(url);
        return u.port ? Number(u.port) : u.protocol === 'https:' ? 443 : 80;
    } catch {
        return undefined;
    }
}

/**
 * 代理程序位置：设置里指定且存在的 exe 优先，其次插件内置的 proxy/Vs2026ChatProxy.exe。
 * 两者都拿不到时返回 problem（给用户看的中文说明）。
 */
export function resolveProxyExe(configured: string, extensionPath: string): { exe?: string; problem?: string } {
    const custom = configured.trim();
    if (custom) {
        return fs.existsSync(custom)
            ? { exe: custom }
            : { problem: `设置 vschatproxy.executablePath 指向的文件不存在：${custom}` };
    }
    const bundled = path.join(extensionPath, 'proxy', 'Vs2026ChatProxy.exe');
    return fs.existsSync(bundled)
        ? { exe: bundled }
        : { problem: '插件包里没有内置的代理程序（proxy/Vs2026ChatProxy.exe），请重新安装插件，或在设置里指定 vschatproxy.executablePath' };
}

/** 与 VS 2026 插件共用的配置文件（非 Windows 返回 undefined） */
export function sharedConfigPath(): string | undefined {
    if (process.platform !== 'win32') return undefined;
    const base = process.env.LOCALAPPDATA ?? path.join(os.homedir(), 'AppData', 'Local');
    return path.join(base, 'VsCopilotProxy', 'proxy-config.json');
}

/**
 * 配置文件位置：设置 configPath → 旧版插件自己的 globalStorage 配置（若存在，保持兼容）
 * → 与 VS 2026 插件共用的文件 → globalStorage。
 */
export function resolveProxyConfigPath(configured: string, storageDir: string): string {
    const custom = configured.trim();
    if (custom) {
        return path.resolve(custom.replace(/^~(?=$|[\\/])/, os.homedir()));
    }
    const own = path.join(storageDir, 'proxy-config.json');
    if (fs.existsSync(own)) return own;
    return sharedConfigPath() ?? own;
}

export interface ProxyConfigSummary {
    port?: number;
    host?: string;
    providerCount: number;
    /** 文件存在但解析失败时的原因 */
    error?: string;
}

/** 读取配置里的端口/供应商数量；文件不存在返回 undefined */
export function readProxyConfig(configPath: string): ProxyConfigSummary | undefined {
    if (!fs.existsSync(configPath)) return undefined;
    try {
        const raw = JSON.parse(fs.readFileSync(configPath, 'utf-8')) as { port?: unknown; host?: unknown; providers?: unknown };
        const providers = Array.isArray(raw.providers) ? raw.providers : [];
        return {
            port: typeof raw.port === 'number' ? raw.port : undefined,
            host: typeof raw.host === 'string' ? raw.host : undefined,
            providerCount: providers.length,
        };
    } catch (err) {
        return { providerCount: 0, error: err instanceof Error ? err.message : String(err) };
    }
}

/** 配置目录里最新一份 proxy_*.log 的末尾几行（代理启动失败时给出原因） */
export function lastLogLines(configPath: string, lines = 8): string | undefined {
    const dir = path.dirname(configPath);
    let newest: string | undefined;
    let newestTime = -1;
    try {
        for (const name of fs.readdirSync(dir)) {
            if (!/^proxy_.*\.log$/i.test(name)) continue;
            const full = path.join(dir, name);
            const time = fs.statSync(full).mtimeMs;
            if (time > newestTime) {
                newestTime = time;
                newest = full;
            }
        }
        if (!newest) return undefined;
        const all = fs.readFileSync(newest, 'utf-8').split(/\r?\n/).filter((l) => l.trim() !== '');
        return all.slice(-lines).join('\n');
    } catch {
        return undefined;
    }
}
