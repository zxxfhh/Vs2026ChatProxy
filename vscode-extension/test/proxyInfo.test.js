// 代理程序/配置定位逻辑与打包脚本的单元测试：npm run compile && npm test
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync } = require('node:child_process');
const info = require('../out/proxyInfo');

function tempDir() {
    return fs.mkdtempSync(path.join(os.tmpdir(), 'vschatproxy-test-'));
}

function write(file, content) {
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, content);
    return file;
}

test('normalizeBaseUrl: 去掉 /v1 与末尾斜杠，空值回退默认地址', () => {
    assert.equal(info.normalizeBaseUrl('http://127.0.0.1:11434/v1'), 'http://127.0.0.1:11434');
    assert.equal(info.normalizeBaseUrl('http://127.0.0.1:11434/v1/'), 'http://127.0.0.1:11434');
    assert.equal(info.normalizeBaseUrl(' http://localhost:8080/// '), 'http://localhost:8080');
    assert.equal(info.normalizeBaseUrl(''), 'http://127.0.0.1:11434');
});

test('portOf: 显式端口 / 协议默认端口 / 非法地址', () => {
    assert.equal(info.portOf('http://127.0.0.1:11434'), 11434);
    assert.equal(info.portOf('http://127.0.0.1'), 80);
    assert.equal(info.portOf('https://example.com'), 443);
    assert.equal(info.portOf('not a url'), undefined);
});

test('resolveProxyExe: 设置优先，其次内置，都没有时给出说明', () => {
    const dir = tempDir();
    const custom = write(path.join(dir, 'elsewhere', 'Vs2026ChatProxy.exe'), 'x');
    const bundled = write(path.join(dir, 'ext', 'proxy', 'Vs2026ChatProxy.exe'), 'x');

    assert.deepEqual(info.resolveProxyExe('  ', path.join(dir, 'ext')), { exe: bundled });
    assert.deepEqual(info.resolveProxyExe(custom, path.join(dir, 'ext')), { exe: custom });
    // 既没设置、插件里也没有：给出说明
    assert.equal(info.resolveProxyExe('', path.join(dir, 'no-such-ext')).exe, undefined);
    assert.match(info.resolveProxyExe('', path.join(dir, 'no-such-ext')).problem, /内置/);

    // 设置了但文件不存在：如实报告，而不是悄悄换成内置的
    const broken = info.resolveProxyExe(path.join(dir, 'missing.exe'), path.join(dir, 'ext'));
    assert.equal(broken.exe, undefined);
    assert.match(broken.problem, /不存在/);
});

test('resolveProxyConfigPath: 设置了就用设置的，否则是插件自己的 globalStorage 配置', () => {
    const dir = tempDir();
    const storage = path.join(dir, 'storage');
    const own = path.join(storage, 'proxy-config.json');
    assert.equal(info.resolveProxyConfigPath('', storage), own);
    assert.equal(info.resolveProxyConfigPath('  ', storage), own);
    assert.equal(info.resolveProxyConfigPath(path.join(dir, 'custom.json'), storage), path.join(dir, 'custom.json'));
    assert.equal(info.resolveProxyConfigPath(`~${path.sep}cfg${path.sep}p.json`, storage),
        path.join(os.homedir(), 'cfg', 'p.json'));
});

test('readProxyConfig: 文件不存在 / 供应商数量 / 非法 JSON', () => {
    const dir = tempDir();
    assert.equal(info.readProxyConfig(path.join(dir, 'none.json')), undefined);

    const ok = write(path.join(dir, 'ok.json'), JSON.stringify({ port: 12345, host: '127.0.0.1', providers: [{}, {}] }));
    assert.deepEqual(info.readProxyConfig(ok), { port: 12345, host: '127.0.0.1', providerCount: 2 });

    // 代理窗口保存出来的文件是 PascalCase 键名
    const pascal = write(path.join(dir, 'pascal.json'),
        JSON.stringify({ Port: 11434, Host: '127.0.0.1', Providers: [{ Name: 'p', Models: [{ Id: 'm' }] }], CompactThresholdPercent: 85 }));
    assert.deepEqual(info.readProxyConfig(pascal), { port: 11434, host: '127.0.0.1', providerCount: 1 });

    const empty = write(path.join(dir, 'empty.json'), JSON.stringify({ providers: [] }));
    assert.equal(info.readProxyConfig(empty).providerCount, 0);

    const bad = write(path.join(dir, 'bad.json'), '{ not json');
    const parsedBad = info.readProxyConfig(bad);
    assert.equal(parsedBad.providerCount, 0);
    assert.ok(parsedBad.error);
});

test('lastLogLines: 取最新日志的末尾几行，没有日志返回 undefined', () => {
    const dir = tempDir();
    assert.equal(info.lastLogLines(path.join(dir, 'proxy-config.json')), undefined);

    const log = path.join(dir, 'proxy_1.log');
    write(log, 'a\nb\nc\n\nd\n');
    const past = new Date(Date.now() - 60_000);
    fs.utimesSync(log, past, past);
    write(path.join(dir, 'proxy_2.log'), 'x\ny\n');
    assert.equal(info.lastLogLines(path.join(dir, 'proxy-config.json')), 'x\ny');

    // 系统时钟有粒度，直接拨到"将来"，确保比 proxy_2.log 新
    const later = new Date(Date.now() + 5000);
    fs.utimesSync(log, later, later);
    assert.equal(info.lastLogLines(path.join(dir, 'proxy-config.json'), 2), 'c\nd');
});

test('sync-proxy.js: 只拷 exe / dll / config，pdb 与日志不带；缺 exe 时报错退出', () => {
    const dir = tempDir();
    const src = path.join(dir, 'src');
    const dst = path.join(dir, 'dst');
    write(path.join(src, 'Vs2026ChatProxy.exe'), 'exe');
    write(path.join(src, 'Vs2026ChatProxy.exe.config'), 'config');
    write(path.join(src, 'Newtonsoft.Json.dll'), 'dll');
    write(path.join(src, 'Vs2026ChatProxy.pdb'), 'pdb');
    write(path.join(src, 'proxy_20260101_000000.log'), 'log');
    write(path.join(dst, 'stale.txt'), 'should be removed');

    const script = path.join(__dirname, '..', 'scripts', 'sync-proxy.js');
    const out = execFileSync(process.execPath, [script, src, dst], { encoding: 'utf-8' });
    assert.match(out, /3 个文件/);
    assert.deepEqual(fs.readdirSync(dst).sort(),
        ['Newtonsoft.Json.dll', 'Vs2026ChatProxy.exe', 'Vs2026ChatProxy.exe.config']);

    const emptySrc = path.join(dir, 'empty');
    fs.mkdirSync(emptySrc);
    assert.throws(() => execFileSync(process.execPath, [script, emptySrc, path.join(dir, 'dst2')]), /未找到/);
});
