// VS Code 聊天模型配置（chatLanguageModels.json）里"自定义端点"（vendor = customendpoint）条目的生成。
// 格式依据 VS Code 内置 Copilot 的 languageModelChatProviders 声明。不依赖 vscode 模块，便于单元测试。
//
// 要点：VS Code 新版不再按 url 自动发现自定义端点的模型——条目里写了顶层 url 时它走"自动发现"分支，
// 但对自定义端点那条分支不产出任何模型；必须在这里逐条写明 models（id/name/url/toolCalling/vision/
// maxOutputTokens 必填）。所以条目里不能出现顶层 url。apiKey 属于机密字段（值须是机密存储的引用），
// 代理不校验鉴权，直接不写。
import * as fs from 'fs';

export const CHAT_PROVIDER_VENDOR = 'customendpoint';

/** VS Code 把 maxOutputTokens 原样作为 max_tokens 发给代理，8192 是各家兼容接口的稳妥上限 */
export const DEFAULT_MAX_OUTPUT_TOKENS = 8192;
/** 代理没有广播上下文长度时按 128K 记 */
export const DEFAULT_MAX_INPUT_TOKENS = 128000;

export interface ProxyModel {
    id: string;
    model_info?: Record<string, unknown>;
}

export interface ModelTokenDefaults {
    maxOutputTokens: number;
    maxInputTokens: number;
}

type Obj = Record<string, unknown>;

function isObj(v: unknown): v is Obj {
    return typeof v === 'object' && v !== null && !Array.isArray(v);
}

/** 去掉代理广播的上下文标签后缀（"coding:glm-5.3 [128K]" → "coding:glm-5.3"）；代理两种写法都认 */
export function stripContextTag(id: string): string {
    return id.replace(/\s+\[[^\]]*\]\s*$/, '');
}

function positiveInt(v: unknown): number | undefined {
    return typeof v === 'number' && Number.isFinite(v) && v > 0 ? Math.floor(v) : undefined;
}

/**
 * 把代理 /v1/models 的结果转成 VS Code 的模型条目。
 * 条目里 id/url 由插件维护；previous 是上次写入的条目，同一 id 已有配置时其余字段
 * （名称、上下文长度、thinking…）视为用户自己的设置原样保留。
 */
export function buildVsCodeModels(
    models: ProxyModel[], endpointUrl: string, previous: unknown[] | undefined, defaults: ModelTokenDefaults): Obj[] {
    const priorById = new Map<string, Obj>();
    for (const p of Array.isArray(previous) ? previous : []) {
        if (isObj(p) && typeof p.id === 'string' && !priorById.has(p.id)) priorById.set(p.id, p);
    }

    const seen = new Set<string>();
    const result: Obj[] = [];
    for (const m of Array.isArray(models) ? models : []) {
        if (!isObj(m) || typeof m.id !== 'string') continue;
        const id = stripContextTag(m.id);
        if (!id || seen.has(id)) continue;
        seen.add(id);

        const prior = priorById.get(id);
        const entry: Obj = { ...prior };
        entry.id = id;
        entry.url = endpointUrl;
        if (!prior) {
            entry.name = id;
            entry.toolCalling = true;
            entry.vision = false;
            entry.maxOutputTokens = defaults.maxOutputTokens;
            const context = positiveInt(isObj(m.model_info) ? m.model_info['general.context_length'] : undefined);
            entry.maxInputTokens = context ? Math.max(context - defaults.maxOutputTokens, 4096) : defaults.maxInputTokens;
        }
        result.push(entry);
    }
    return result;
}

/** 组装（或更新）chatLanguageModels.json 里本代理的条目；previousEntry 是文件里现有的同名条目 */
export function buildChatProviderEntry(
    groupName: string, endpointUrl: string, models: ProxyModel[], previousEntry: unknown, defaults: ModelTokenDefaults): Obj {
    const prior = isObj(previousEntry) ? { ...previousEntry } : {};
    const entry: Obj = { ...prior };
    // 旧格式（顶层 url）会让 VS Code 忽略 models，必须去掉
    delete entry.url;
    entry.name = groupName;
    entry.vendor = CHAT_PROVIDER_VENDOR;
    entry.apiType = 'chat-completions';
    entry.models = buildVsCodeModels(models, endpointUrl, prior.models as unknown[] | undefined, defaults);
    return entry;
}

/** 从 chatLanguageModels.json 解析出的数组里找出本代理的条目 */
export function findChatProviderEntry(list: unknown, groupName: string): Obj | undefined {
    for (const e of Array.isArray(list) ? list : []) {
        if (isObj(e) && e.name === groupName && e.vendor === CHAT_PROVIDER_VENDOR) return e;
    }
    return undefined;
}

export type RegistrationState =
    /** 文件里没有本代理的条目 */
    | { kind: 'missing' }
    /** 条目是旧格式（顶层 url、没有 models），这版 VS Code 认不出 */
    | { kind: 'legacy'; index: number }
    /** 条目指向别的地址（端口变了） */
    | { kind: 'otherUrl'; index: number }
    /** 条目有效，models 为已注册的模型 id */
    | { kind: 'registered'; index: number; modelIds: string[] };

export function describeRegistration(list: unknown, groupName: string, endpointUrl: string): RegistrationState {
    const entries = Array.isArray(list) ? list : [];
    const index = entries.findIndex((e) => isObj(e) && e.name === groupName && e.vendor === CHAT_PROVIDER_VENDOR);
    if (index < 0) return { kind: 'missing' };
    const entry = entries[index] as Obj;
    const models = Array.isArray(entry.models) ? entry.models.filter(isObj) : [];
    if (entry.url !== undefined && models.length === 0) return { kind: 'legacy', index };
    const urls = models.map((m) => m.url).filter((u): u is string => typeof u === 'string');
    if (urls.length === 0 || urls.some((u) => u !== endpointUrl)) return { kind: 'otherUrl', index };
    return {
        kind: 'registered',
        index,
        modelIds: models.map((m) => m.id).filter((id): id is string => typeof id === 'string'),
    };
}

/** 读 chatLanguageModels.json；文件不存在返回空列表，JSON 非法时抛错 */
export function readChatModelsFile(filePath: string): unknown[] {
    if (!fs.existsSync(filePath)) return [];
    const parsed = JSON.parse(fs.readFileSync(filePath, 'utf-8')) as unknown;
    return Array.isArray(parsed) ? parsed : [];
}

export function sameIdSet(a: readonly string[], b: readonly string[]): boolean {
    const sa = new Set(a), sb = new Set(b);
    return sa.size === sb.size && [...sa].every((x) => sb.has(x));
}

/** 从 /v1/models 的应答里取出模型列表（容错） */
export function proxyModelsFromResponse(body: unknown): ProxyModel[] {
    const data = isObj(body) ? body.data : undefined;
    return Array.isArray(data) ? data.filter(isObj).map((m) => ({ id: String(m.id), model_info: isObj(m.model_info) ? m.model_info : undefined })) : [];
}
