// VS Code「自定义端点」条目生成的单元测试：npm run compile && npm test
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const cp = require('../out/chatProvider');

/** 临时目录，测试结束时自动删除 */
function tempDir(t) {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'vschatproxy-test-'));
    t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
    return dir;
}

const URL_ = 'http://127.0.0.1:11434/v1';
const DEFAULTS = { maxOutputTokens: 8192, maxInputTokens: 128000 };
const model = (id, context) => ({ id, ...(context ? { model_info: { 'general.context_length': context } } : {}) });

test('stripContextTag: 去掉 " [128K]" 这类上下文标签后缀', () => {
    assert.equal(cp.stripContextTag('coding:glm-5.3 [128K]'), 'coding:glm-5.3');
    assert.equal(cp.stripContextTag('coding:glm-5.3 [1M/2M]'), 'coding:glm-5.3');
    assert.equal(cp.stripContextTag('coding:glm-5.3'), 'coding:glm-5.3');
    assert.equal(cp.stripContextTag('coding:a[b]c'), 'coding:a[b]c');
});

test('buildVsCodeModels: 新模型带齐 VS Code 要求的字段，上下文长度换算成 maxInputTokens', () => {
    const out = cp.buildVsCodeModels([model('coding:glm-5.3 [128K]', 131072), model('mock:m1')], URL_, undefined, DEFAULTS);
    assert.deepEqual(out, [
        { id: 'coding:glm-5.3', name: 'coding:glm-5.3', url: URL_, toolCalling: true, vision: false, maxOutputTokens: 8192, maxInputTokens: 122880 },
        { id: 'mock:m1', name: 'mock:m1', url: URL_, toolCalling: true, vision: false, maxOutputTokens: 8192, maxInputTokens: 128000 },
    ]);
    // 上下文很小的时候也别把 maxInputTokens 算成 0/负数
    assert.equal(cp.buildVsCodeModels([model('tiny', 6000)], URL_, undefined, DEFAULTS)[0].maxInputTokens, 4096);
});

test('buildVsCodeModels: 已有的模型沿用用户改过的设置，只刷新 url；重复 id 去重', () => {
    const previous = [
        { id: 'mock:m1', name: '我的模型', url: 'http://127.0.0.1:1/v1', toolCalling: false, vision: true, maxOutputTokens: 4096, maxInputTokens: 60000, thinking: true },
    ];
    const out = cp.buildVsCodeModels([model('mock:m1', 200000), model('mock:m1'), { id: 5 }, {}], 'http://127.0.0.1:2/v1', previous, DEFAULTS);
    assert.equal(out.length, 1);
    assert.deepEqual(out[0], {
        id: 'mock:m1', name: '我的模型', url: 'http://127.0.0.1:2/v1', toolCalling: false, vision: true,
        maxOutputTokens: 4096, maxInputTokens: 60000, thinking: true,
    });
});

test('buildChatProviderEntry: 不带顶层 url（带了 VS Code 会忽略 models），其余字段保留', () => {
    const legacy = { name: 'VsChatProxy', vendor: 'customendpoint', apiType: 'chat-completions', url: URL_, apiKey: '${input:chat.lm.secret.abcd}' };
    const entry = cp.buildChatProviderEntry('VsChatProxy', URL_, [model('mock:m1')], legacy, DEFAULTS);
    assert.equal('url' in entry, false);
    assert.equal(entry.name, 'VsChatProxy');
    assert.equal(entry.vendor, 'customendpoint');
    assert.equal(entry.apiType, 'chat-completions');
    assert.equal(entry.apiKey, '${input:chat.lm.secret.abcd}');
    assert.equal(entry.models.length, 1);
    assert.equal(entry.models[0].url, URL_);

    const fresh = cp.buildChatProviderEntry('VsChatProxy', URL_, [model('mock:m1')], undefined, DEFAULTS);
    assert.deepEqual(Object.keys(fresh).sort(), ['apiType', 'models', 'name', 'vendor']);
});

test('describeRegistration: 未配置 / 旧格式 / 地址不一致 / 已注册', () => {
    const other = { name: 'DeepSeek', vendor: 'deepseek', settings: {} };
    assert.deepEqual(cp.describeRegistration([other], 'VsChatProxy', URL_), { kind: 'missing' });
    assert.deepEqual(cp.describeRegistration([other, { name: 'VsChatProxy', vendor: 'customendpoint', apiType: 'chat-completions', url: URL_ }], 'VsChatProxy', URL_),
        { kind: 'legacy', index: 1 });
    const entry = cp.buildChatProviderEntry('VsChatProxy', 'http://127.0.0.1:9/v1', [model('mock:m1')], undefined, DEFAULTS);
    assert.deepEqual(cp.describeRegistration([entry], 'VsChatProxy', URL_), { kind: 'otherUrl', index: 0 });
    const ok = cp.buildChatProviderEntry('VsChatProxy', URL_, [model('mock:m1'), model('mock:m2')], undefined, DEFAULTS);
    assert.deepEqual(cp.describeRegistration([other, ok], 'VsChatProxy', URL_), { kind: 'registered', index: 1, modelIds: ['mock:m1', 'mock:m2'] });
    // 不是数组、条目残缺都不抛错
    assert.deepEqual(cp.describeRegistration('garbage', 'VsChatProxy', URL_), { kind: 'missing' });
    assert.deepEqual(cp.describeRegistration([{ name: 'VsChatProxy', vendor: 'customendpoint', models: 'x' }], 'VsChatProxy', URL_), { kind: 'otherUrl', index: 0 });
});

test('proxyModelsFromResponse / readChatModelsFile / sameIdSet', (t) => {
    assert.deepEqual(cp.proxyModelsFromResponse({ object: 'list', data: [{ id: 'a:b', model_info: { 'general.context_length': 1000 } }, 'junk', { id: 'c' }] }),
        [{ id: 'a:b', model_info: { 'general.context_length': 1000 } }, { id: 'c', model_info: undefined }]);
    assert.deepEqual(cp.proxyModelsFromResponse({}), []);
    assert.deepEqual(cp.proxyModelsFromResponse(undefined), []);

    const dir = tempDir(t);
    const file = path.join(dir, 'chatLanguageModels.json');
    assert.deepEqual(cp.readChatModelsFile(file), []);
    fs.writeFileSync(file, '[{"name":"A","vendor":"a"}]');
    assert.deepEqual(cp.readChatModelsFile(file), [{ name: 'A', vendor: 'a' }]);
    fs.writeFileSync(file, '{"not":"an array"}');
    assert.deepEqual(cp.readChatModelsFile(file), []);
    fs.writeFileSync(file, '{ broken');
    assert.throws(() => cp.readChatModelsFile(file));

    assert.equal(cp.sameIdSet(['a', 'b'], ['b', 'a', 'a']), true);
    assert.equal(cp.sameIdSet(['a'], ['a', 'b']), false);
});
