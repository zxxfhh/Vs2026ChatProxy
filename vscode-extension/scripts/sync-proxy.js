// 把独立 exe 的构建产物拷进插件的 proxy/ 目录，随插件一起打包（插件内置代理，装完即用）。
// 用法：node scripts/sync-proxy.js [源目录] [目标目录]
//   源目录默认 ../Vs2026ChatProxy/bin/Release/net48（需先用 VS 的 MSBuild 生成 Vs2026ChatProxy.sln）
const fs = require('fs');
const path = require('path');

const source = path.resolve(process.argv[2] || path.join(__dirname, '..', '..', 'Vs2026ChatProxy', 'bin', 'Release', 'net48'));
const target = path.resolve(process.argv[3] || path.join(__dirname, '..', 'proxy'));

if (!fs.existsSync(path.join(source, 'Vs2026ChatProxy.exe'))) {
    console.error(`未找到 Vs2026ChatProxy.exe：${source}`);
    console.error('请先以 Release 配置生成 Vs2026ChatProxy.sln，或把源目录作为第一个参数传入。');
    process.exit(1);
}

fs.rmSync(target, { recursive: true, force: true });
fs.mkdirSync(target, { recursive: true });

let total = 0;
const copied = [];
for (const name of fs.readdirSync(source)) {
    // 只要可执行文件、程序集和 exe.config；pdb、运行日志等不带
    if (!/\.(exe|dll|config)$/i.test(name)) continue;
    fs.copyFileSync(path.join(source, name), path.join(target, name));
    total += fs.statSync(path.join(source, name)).size;
    copied.push(name);
}

console.log(`已同步代理程序到 ${target}（${copied.length} 个文件，${(total / 1024).toFixed(0)} KB）`);
