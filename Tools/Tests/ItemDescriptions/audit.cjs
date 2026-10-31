// 核对正式物品描述与本地化表；--sync 仅对齐描述，不修改物品身份与玩法字段。
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const assets = path.join(root, 'Assets');
const itemRoot = path.join(assets, 'StreamingAssets/GameConfig/Items');
const tableRoot = path.join(assets, 'Localization/StringTables');
const localePath = path.join(assets, 'Localization/RuntimeLocalizationOverrides.additional.json');
const sync = process.argv.includes('--sync');

const manifest = JSON.parse(fs.readFileSync(path.join(itemRoot, 'item-manifest.json'), 'utf8'));
const descriptions = new Map();
for (const entry of manifest.packages.filter(item => item.enabled !== false)) {
    const data = JSON.parse(fs.readFileSync(path.join(itemRoot, entry.path), 'utf8'));
    for (const item of data.items || []) {
        if (item.abstract || typeof item.description !== 'string' || !item.description.trim())
            continue;
        if (descriptions.has(item.id))
            throw new Error('重复物品描述 ID: ' + item.id);
        descriptions.set(item.id, item.description);
    }
}

const shared = fs.readFileSync(path.join(tableRoot, 'FlatWorld Shared Data.asset'), 'utf8');
const keys = new Map(
    [...shared.matchAll(/^  - m_Id: (\d+)\r?\n    m_Key: ([^\r\n]+)/gm)]
        .map(match => [match[2], match[1]])
);
const english = JSON.parse(fs.readFileSync(localePath, 'utf8')).itemDescriptions || {};
const warnings = [];
const missingSharedKeys = [];
const encodeYaml = text => JSON.stringify(text).replace(/[\u0080-\uffff]/g,
    character => '\\u' + character.charCodeAt(0).toString(16).toUpperCase().padStart(4, '0'));

function decodeYaml(raw) {
    const value = raw.trim();
    if (!value.startsWith('"'))
        return value;
    // Unity YAML 会在较长字符串中插入续行，比较前先还原续行。
    const joined = value.replace(/\\\r?\n\s*/g, '').replace(/\r?\n\s*/g, ' ');
    try {
        return JSON.parse(joined);
    } catch {
        return null;
    }
}

function validateTable(file, desiredByKey) {
    const filename = path.join(tableRoot, file);
    const source = fs.readFileSync(filename, 'utf8');
    const seen = new Set();
    let changed = 0;
    const result = source.replace(
        /^(  - m_Id: (\d+)\r?\n    m_Localized: )([\s\S]*?)(?=\r?\n    m_Metadata:)/gm,
        (all, prefix, id, raw) => {
            if (!desiredByKey.has(id))
                return all;
            seen.add(id);
            const expected = desiredByKey.get(id);
            if (decodeYaml(raw) === expected)
                return all;
            changed++;
            return prefix + encodeYaml(expected);
        }
    );
    for (const id of desiredByKey.keys())
        if (!seen.has(id))
            warnings.push(file + ' 缺少描述表项 ' + id);
    if (sync && changed)
        fs.writeFileSync(filename, result, 'utf8');
    else if (!sync && changed)
        warnings.push(file + ' 有 ' + changed + ' 条过时描述');
    return changed;
}

const chineseById = new Map();
const englishById = new Map();
for (const [id, description] of descriptions) {
    const tableId = keys.get('item.' + id + '.description');
    if (!tableId) {
        missingSharedKeys.push('item.' + id + '.description');
        continue;
    }
    chineseById.set(tableId, description);
    if (Object.prototype.hasOwnProperty.call(english, id))
        englishById.set(tableId, english[id]);
}

const zhChanged = validateTable('FlatWorld_zh-CN.asset', chineseById);
const enChanged = validateTable('FlatWorld_en.asset', englishById);
console.log(`物品描述 ${descriptions.size} 项，中文表需对齐 ${zhChanged} 项，英文已提供翻译需对齐 ${enChanged} 项，未注册共享键 ${missingSharedKeys.length} 项。`);
if (missingSharedKeys.length)
    console.warn('缺少共享键（须在编辑器同步物品名称和描述后补齐）：' + missingSharedKeys.join(', '));
if (warnings.length) {
    for (const warning of warnings)
        console.error(warning);
    process.exitCode = 1;
}
