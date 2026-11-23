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

/**
 * 配置文件位置：设置 configPath，否则用插件自己的配置（globalStorage/proxy-config.json）。
 * VS 2026 插件用的是另一份（LocalAppData\VsCopilotProxy\proxy-config.json）；想两边共用就把
 * configPath 指过去。代理窗口里添加的供应商保存在这里。
 */
export function resolveProxyConfigPath(configured: string, storageDir: string): string {
    const custom = configured.trim();
    if (custom) {
        return path.resolve(custom.replace(/^~(?=$|[\\/])/, os.homedir()));
    }
    return path.join(storageDir, 'proxy-config.json');
}

export interface ProxyConfigSummary {
    port?: number;
    host?: string;
    providerCount: number;
    /** 文件存在但解析失败时的原因 */
    error?: string;
}

/**
 * 配置文件的键名大小写不敏感：代理窗口保存出来的是 PascalCase（Port / Providers / Models），
 * 手写文件和插件生成的模板是 camelCase（port / providers）。
 */
function pick(raw: Record<string, unknown>, camelKey: string): unknown {
    return raw[camelKey] ?? raw[camelKey[0].toUpperCase() + camelKey.slice(1)];
}

/** 读取配置里的端口/供应商数量；文件不存在返回 undefined */
export function readProxyConfig(configPath: string): ProxyConfigSummary | undefined {
    if (!fs.existsSync(configPath)) return undefined;
    try {
        const raw = JSON.parse(fs.readFileSync(configPath, 'utf-8')) as Record<string, unknown>;
        const providers = pick(raw, 'providers');
        const port = pick(raw, 'port');
        const host = pick(raw, 'host');
        return {
            port: typeof port === 'number' ? port : undefined,
            host: typeof host === 'string' ? host : undefined,
            providerCount: Array.isArray(providers) ? providers.length : 0,
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
