const vscode = require('vscode');
const path = require('path');

const VIEW_TYPE = 'unityPrefabBrowser.prefabEditor';
const EXCLUDE_GLOB = '**/{Library,Temp,Logs,Obj,obj,Build,build,node_modules}/**';
let assetGuidIndexPromise;

/**
 * VS Code 扩展入口：把 .prefab 注册为只读自定义编辑器。
 * 数据直接来自 Unity 已保存的 YAML，不需要启动额外的 Unity 网络服务。
 * @param {vscode.ExtensionContext} context
 */
function activate(context) {
    const provider = new PrefabEditorProvider();

    context.subscriptions.push(
        vscode.window.registerCustomEditorProvider(VIEW_TYPE, provider, {
            webviewOptions: { retainContextWhenHidden: true },
            // 允许本视图与其他 Prefab Custom Editor 同时打开同一个文件。
            supportsMultipleEditorsPerDocument: true
        })
    );

    const watcher = vscode.workspace.createFileSystemWatcher('**/*.prefab');
    context.subscriptions.push(
        watcher,
        watcher.onDidChange(uri => provider.refresh(uri)),
        watcher.onDidDelete(uri => provider.refresh(uri)),
        vscode.commands.registerCommand('unityPrefabBrowser.openAsHierarchy', uri => openAsHierarchy(uri)),
        vscode.commands.registerCommand('unityPrefabBrowser.openAsText', uri => openAsText(uri))
    );
}

/** @param {vscode.Uri | undefined} uri */
async function openAsHierarchy(uri) {
    if (!(uri instanceof vscode.Uri)) {
        return;
    }

    await vscode.commands.executeCommand('vscode.openWith', uri, VIEW_TYPE);
}

/** @param {vscode.Uri | undefined} uri */
async function openAsText(uri) {
    if (!(uri instanceof vscode.Uri)) {
        return;
    }

    // Custom Editor 的默认优先级仍然保留，这个命令只给需要查看原始 YAML 的情况使用。
    await vscode.commands.executeCommand('vscode.openWith', uri, 'default');
}

class PrefabDocument {
    /** @param {vscode.Uri} uri @param {object} data */
    constructor(uri, data) {
        this.uri = uri;
        this.data = data;
    }

    dispose() {
        // 只读文档没有需要释放的外部资源。
    }
}

class PrefabEditorProvider {
    constructor() {
        /** @type {Map<string, Set<{ document: PrefabDocument, panel: vscode.WebviewPanel }>>} */
        this.editors = new Map();
    }

    /** @param {vscode.Uri} uri */
    async openCustomDocument(uri) {
        return new PrefabDocument(uri, await readPrefabData(uri));
    }

    /** @param {PrefabDocument} document @param {vscode.WebviewPanel} webviewPanel */
    resolveCustomEditor(document, webviewPanel) {
        webviewPanel.webview.options = {
            enableScripts: true,
            localResourceRoots: getWebviewResourceRoots(document.uri)
        };
        webviewPanel.webview.html = getWebviewHtml(webviewPanel.webview);

        const key = document.uri.toString();
        const editor = { document, panel: webviewPanel };
        let editorsForDocument = this.editors.get(key);
        if (!editorsForDocument) {
            editorsForDocument = new Set();
            this.editors.set(key, editorsForDocument);
        }
        editorsForDocument.add(editor);

        const messageSubscription = webviewPanel.webview.onDidReceiveMessage(async message => {
            switch (message?.type) {
                case 'ready':
                    await this.sendDocument(document, webviewPanel);
                    break;
                case 'copyPath':
                    await vscode.env.clipboard.writeText(document.uri.fsPath);
                    break;
                case 'revealInExplorer':
                    await vscode.commands.executeCommand('revealInExplorer', document.uri);
                    break;
                case 'refresh':
                    await this.refresh(document.uri);
                    break;
                default:
                    break;
            }
        });

        webviewPanel.onDidDispose(() => {
            messageSubscription.dispose();
            const currentEditors = this.editors.get(key);
            currentEditors?.delete(editor);
            if (currentEditors?.size === 0) {
                this.editors.delete(key);
            }
        });

        // 某些 VS Code 版本在页面 ready 消息前不会立即显示首帧，保留一次兜底发送。
        void this.sendDocument(document, webviewPanel);
    }

    /** @param {PrefabDocument} document @param {vscode.WebviewPanel} panel */
    async sendDocument(document, panel) {
        const data = await attachPreviewAssets(document.data, panel.webview);
        await postDocument(panel, data);
    }

    /** @param {vscode.Uri} uri */
    async refresh(uri) {
        const editorsForDocument = this.editors.get(uri.toString());
        if (!editorsForDocument?.size) {
            return;
        }

        const data = await readPrefabData(uri);
        await Promise.all([...editorsForDocument].map(async editor => {
            editor.document.data = data;
            await this.sendDocument(editor.document, editor.panel);
        }));
    }
}

/** @param {vscode.WebviewPanel} panel @param {object} data */
async function postDocument(panel, data) {
    try {
        await panel.webview.postMessage({ type: 'documentData', data });
    } catch {
        // 面板正在关闭时，发送消息失败是正常情况。
    }
}

/** @param {vscode.Uri} uri */
async function readPrefabData(uri) {
    try {
        const bytes = await vscode.workspace.fs.readFile(uri);
        const yaml = Buffer.from(bytes).toString('utf8');
        return parsePrefabYaml(yaml, uri);
    } catch (error) {
        return {
            name: uri.path.split('/').pop() || 'Prefab',
            assetPath: getAssetPath(uri),
            roots: [],
            stats: { objects: 0, components: 0, nestedPrefabs: 0 },
            error: error instanceof Error ? error.message : String(error)
        };
    }
}

/** @param {vscode.Uri} documentUri */
function getWebviewResourceRoots(documentUri) {
    const workspaceFolder = vscode.workspace.getWorkspaceFolder(documentUri);
    return workspaceFolder ? [workspaceFolder.uri] : [];
}

/** @param {object} data @param {vscode.Webview} webview */
async function attachPreviewAssets(data, webview) {
    if (data.error || !data.roots?.length || !hasPreviewAssetReference(data.roots)) {
        return data;
    }

    const assetGuidIndex = await getAssetGuidIndex();
    const preparedData = JSON.parse(JSON.stringify(data));
    walkNodes(preparedData.roots, node => {
        for (const visual of node.visuals || []) {
            const assetPath = assetGuidIndex.get(visual.assetGuid);
            if (!assetPath) {
                continue;
            }

            visual.imageUri = webview.asWebviewUri(vscode.Uri.file(assetPath)).toString();
            visual.assetPath = getAssetPath(vscode.Uri.file(assetPath));
        }
    });
    return preparedData;
}

/** @param {Array<object>} roots */
function hasPreviewAssetReference(roots) {
    let found = false;
    walkNodes(roots, node => {
        if ((node.visuals || []).some(visual => visual.assetGuid)) {
            found = true;
        }
    });
    return found;
}

/** @returns {Promise<Map<string, string>>} */
async function getAssetGuidIndex() {
    if (!assetGuidIndexPromise) {
        assetGuidIndexPromise = buildAssetGuidIndex().catch(() => new Map());
    }
    return assetGuidIndexPromise;
}

/** @returns {Promise<Map<string, string>>} */
async function buildAssetGuidIndex() {
    const index = new Map();
    const metaUris = await vscode.workspace.findFiles(
        '**/*.{png,jpg,jpeg,gif,webp,svg}.meta',
        EXCLUDE_GLOB,
        20000
    );

    for (let offset = 0; offset < metaUris.length; offset += 64) {
        const batch = metaUris.slice(offset, offset + 64);
        await Promise.all(batch.map(async metaUri => {
            try {
                const metaText = Buffer.from(await vscode.workspace.fs.readFile(metaUri)).toString('utf8');
                const guid = metaText.match(/^\s*guid:\s*([0-9a-fA-F]{32})\s*$/m)?.[1];
                if (guid) {
                    index.set(guid, metaUri.fsPath.slice(0, -'.meta'.length));
                }
            } catch {
                // 单个资源的 meta 读取失败不应影响其他贴图预览。
            }
        }));
    }
    return index;
}

/**
 * 解析 Unity 文本序列化 Prefab 中的 GameObject、Transform 和组件引用。
 * 这里不把 YAML 当作通用 YAML 解析，因为 Unity 的 fileID 需要保持字符串，避免超出 JS 安全整数范围。
 * @param {string} yaml
 * @param {vscode.Uri} uri
 */
function parsePrefabYaml(yaml, uri) {
    if (!/^\s*%YAML\s+/m.test(yaml) && !/^---\s+!u!/m.test(yaml)) {
        throw new Error('该 Prefab 不是可解析的文本序列化格式，请在 Unity 中启用 Force Text。');
    }

    const sections = parseYamlSections(yaml);
    const sectionsById = new Map(sections.map(section => [section.id, section]));
    const gameObjects = new Map();
    const transformsByGameObject = new Map();
    const gameObjectByTransform = new Map();
    const prefabInstances = new Map();

    for (const section of sections) {
        if (section.type === 'GameObject') {
            gameObjects.set(section.id, {
                id: section.id,
                name: readScalar(section.body, 'm_Name') || 'GameObject',
                active: readScalar(section.body, 'm_IsActive') !== '0',
                componentIds: readComponentIds(section.body),
                prefabInstanceId: readFileId(section.body, 'm_PrefabInstance'),
                sourceIndex: section.index
            });
            continue;
        }

        if (section.type === 'Transform' || section.type === 'RectTransform') {
            const gameObjectId = readFileId(section.body, 'm_GameObject');
            const transform = {
                id: section.id,
                gameObjectId,
                parentTransformId: readFileId(section.body, 'm_Father'),
                siblingOrder: readInteger(section.body, 'm_RootOrder', section.index),
                type: section.type,
                x: readVector2(section.body, 'm_AnchoredPosition')?.x ?? readVector3(section.body, 'm_LocalPosition').x,
                y: readVector2(section.body, 'm_AnchoredPosition')?.y ?? readVector3(section.body, 'm_LocalPosition').y,
                scaleX: readVector3(section.body, 'm_LocalScale').x,
                scaleY: readVector3(section.body, 'm_LocalScale').y,
                rotation: readRotationZ(section.body),
                width: section.type === 'RectTransform'
                    ? readVector2(section.body, 'm_SizeDelta')?.x || 0
                    : readVector2(section.body, 'm_Size')?.x || 0,
                height: section.type === 'RectTransform'
                    ? readVector2(section.body, 'm_SizeDelta')?.y || 0
                    : readVector2(section.body, 'm_Size')?.y || 0,
                isRectTransform: section.type === 'RectTransform'
            };
            transformsByGameObject.set(gameObjectId, transform);
            gameObjectByTransform.set(section.id, gameObjectId);
            continue;
        }

        if (section.type === 'PrefabInstance') {
            prefabInstances.set(section.id, {
                guid: readGuid(section.body, 'm_SourcePrefab'),
                name: readPrefabInstanceName(section.body),
                parentTransformId: readFileId(section.body, 'm_TransformParent'),
                sourceIndex: section.index
            });
        }
    }

    const nodes = new Map();
    let componentCount = 0;
    let nestedPrefabCount = 0;

    for (const gameObject of gameObjects.values()) {
        const transform = transformsByGameObject.get(gameObject.id);
        const parentGameObjectId = transform
            ? gameObjectByTransform.get(transform.parentTransformId) || null
            : null;
        const nestedPrefab = prefabInstances.get(gameObject.prefabInstanceId);
        const componentSections = gameObject.componentIds
            .map(componentId => sectionsById.get(componentId))
            .filter(Boolean);
        const components = componentSections.map(section => getComponentLabel(section));
        const visuals = componentSections
            .map((section, index) => {
                const visual = getVisualComponent(section);
                return visual ? { ...visual, id: `${gameObject.id}:visual:${index}` } : null;
            })
            .filter(Boolean);

        componentCount += components.length;
        if (nestedPrefab) {
            nestedPrefabCount += 1;
        }

        nodes.set(gameObject.id, {
            id: gameObject.id,
            name: gameObject.name,
            active: gameObject.active,
            parentId: parentGameObjectId,
            siblingOrder: transform?.siblingOrder ?? gameObject.sourceIndex,
            components,
            visuals,
            transform: transform || createDefaultTransform(),
            nestedPrefab: nestedPrefab || null,
            children: []
        });
    }

    // Unity 对嵌套 Prefab 通常只序列化 stripped Transform，不会重复写入源 Prefab 的 GameObject。
    // 因此这里补一个可展开的占位根节点，至少保证资源关系和挂载位置在层级中可见。
    const representedPrefabInstances = new Set(
        [...gameObjects.values()]
            .map(gameObject => gameObject.prefabInstanceId)
            .filter(instanceId => instanceId !== '0')
    );
    for (const [instanceId, prefabInstance] of prefabInstances) {
        if (representedPrefabInstances.has(instanceId)) {
            continue;
        }

        const parentId = gameObjectByTransform.get(prefabInstance.parentTransformId) || null;
        nodes.set(`nested:${instanceId}`, {
            id: `nested:${instanceId}`,
            name: prefabInstance.name || 'Nested Prefab',
            active: true,
            parentId,
            siblingOrder: prefabInstance.sourceIndex,
            components: ['Nested Prefab'],
            visuals: [],
            transform: createDefaultTransform(),
            nestedPrefab: prefabInstance,
            children: []
        });
        componentCount += 1;
        nestedPrefabCount += 1;
    }

    const roots = [];
    for (const node of nodes.values()) {
        const parent = node.parentId ? nodes.get(node.parentId) : null;
        if (parent && parent !== node) {
            parent.children.push(node);
        } else {
            roots.push(node);
        }
    }

    const sortNodes = (left, right) => {
        if (left.siblingOrder !== right.siblingOrder) {
            return left.siblingOrder - right.siblingOrder;
        }
        return left.name.localeCompare(right.name);
    };

    roots.sort(sortNodes);
    walkNodes(roots, node => node.children.sort(sortNodes));

    return {
        name: uri.path.split('/').pop() || 'Prefab',
        assetPath: getAssetPath(uri),
        roots,
        stats: {
            objects: nodes.size,
            components: componentCount,
            nestedPrefabs: nestedPrefabCount
        }
    };
}

/** @param {string} yaml */
function parseYamlSections(yaml) {
    // 嵌套 Prefab 生成的对象区块会带有可选的 "stripped" 标记。
    const headers = [...yaml.matchAll(/^---\s+!u!\d+\s+&(-?\d+)(?:\s+stripped)?\r?\n/gm)];
    return headers.map((header, index) => {
        const bodyStart = header.index + header[0].length;
        const bodyEnd = index + 1 < headers.length ? headers[index + 1].index : yaml.length;
        const rawBody = yaml.slice(bodyStart, bodyEnd);
        const typeMatch = rawBody.match(/^([A-Za-z0-9_]+):\r?\n/);
        return {
            id: header[1],
            type: typeMatch ? typeMatch[1] : 'Unknown',
            body: typeMatch ? rawBody.slice(typeMatch[0].length) : rawBody,
            index
        };
    });
}

/** @param {string} body */
function readComponentIds(body) {
    const ids = [];
    const expression = /^\s*-\s+component:\s*\{fileID:\s*(-?\d+)/gm;
    for (const match of body.matchAll(expression)) {
        ids.push(match[1]);
    }
    return ids;
}

/** @param {string} body @param {string} key */
function readFileId(body, key) {
    const expression = new RegExp(`^\\s*${escapeRegExp(key)}:\\s*\\{fileID:\\s*(-?\\d+)`, 'm');
    return body.match(expression)?.[1] || '0';
}

/** @param {string} body @param {string} key @param {number} fallback */
function readInteger(body, key, fallback) {
    const value = Number.parseInt(readScalar(body, key), 10);
    return Number.isFinite(value) ? value : fallback;
}

/** @param {string} body @param {string} key */
function readVector2(body, key) {
    const value = readInlineObject(body, key);
    if (!value) {
        return null;
    }
    return {
        x: Number.isFinite(value.x) ? value.x : 0,
        y: Number.isFinite(value.y) ? value.y : 0
    };
}

/** @param {string} body @param {string} key */
function readVector3(body, key) {
    const value = readInlineObject(body, key);
    return {
        x: Number.isFinite(value?.x) ? value.x : 0,
        y: Number.isFinite(value?.y) ? value.y : 0,
        z: Number.isFinite(value?.z) ? value.z : 0
    };
}

/** @param {string} body */
function readRotationZ(body) {
    const rotation = readInlineObject(body, 'm_LocalRotation');
    if (!rotation) {
        return 0;
    }

    const numerator = 2 * ((rotation.w ?? 1) * (rotation.z ?? 0) + (rotation.x ?? 0) * (rotation.y ?? 0));
    const denominator = 1 - 2 * ((rotation.y ?? 0) ** 2 + (rotation.z ?? 0) ** 2);
    return Math.atan2(numerator, denominator) * 180 / Math.PI;
}

/** @param {string} body @param {string} key */
function readColor(body, key) {
    const value = readInlineObject(body, key);
    return {
        r: clamp(value?.r ?? 1),
        g: clamp(value?.g ?? 1),
        b: clamp(value?.b ?? 1),
        a: clamp(value?.a ?? 1)
    };
}

/** @param {string} body @param {string} key */
function readReference(body, key) {
    const expression = new RegExp(`^\\s*${escapeRegExp(key)}:\\s*\\{([^}]*)\\}`, 'm');
    const content = body.match(expression)?.[1];
    if (!content) {
        return { fileID: '0', guid: '' };
    }
    return {
        fileID: content.match(/fileID:\s*(-?\d+)/)?.[1] || '0',
        guid: content.match(/guid:\s*([0-9a-fA-F]{32})/)?.[1] || ''
    };
}

/** @param {string} body @param {string} key */
function readInlineObject(body, key) {
    const expression = new RegExp(`^\\s*${escapeRegExp(key)}:\\s*\\{([^}]*)\\}`, 'm');
    const content = body.match(expression)?.[1];
    if (!content) {
        return null;
    }

    const value = {};
    for (const match of content.matchAll(/([A-Za-z]+):\s*(-?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)/g)) {
        value[match[1]] = Number.parseFloat(match[2]);
    }
    return value;
}

/** @param {number} value */
function clamp(value) {
    return Math.max(0, Math.min(1, value));
}

/** @returns {object} */
function createDefaultTransform() {
    return {
        x: 0,
        y: 0,
        scaleX: 1,
        scaleY: 1,
        rotation: 0,
        width: 0,
        height: 0,
        isRectTransform: false
    };
}

/** @param {{ type: string, body: string }} section */
function getVisualComponent(section) {
    if (section.type === 'SpriteRenderer') {
        return {
            label: 'SpriteRenderer',
            kind: 'sprite',
            assetGuid: readReference(section.body, 'm_Sprite').guid,
            color: readColor(section.body, 'm_Color'),
            enabled: readScalar(section.body, 'm_Enabled') !== '0'
        };
    }

    if (section.type !== 'MonoBehaviour') {
        return null;
    }

    const sprite = readReference(section.body, 'm_Sprite');
    const texture = readReference(section.body, 'm_Texture');
    if (!hasKey(section.body, 'm_Sprite') && !hasKey(section.body, 'm_Texture')) {
        return null;
    }

    return {
        label: texture.guid && !sprite.guid ? 'RawImage' : 'Image',
        kind: texture.guid && !sprite.guid ? 'rawImage' : 'image',
        assetGuid: sprite.guid || texture.guid,
        color: readColor(section.body, 'm_Color'),
        enabled: readScalar(section.body, 'm_Enabled') !== '0'
    };
}

/** @param {string} body @param {string} key */
function hasKey(body, key) {
    return new RegExp(`^\\s*${escapeRegExp(key)}:`, 'm').test(body);
}

/** @param {string} body @param {string} key */
function readScalar(body, key) {
    const expression = new RegExp(`^\\s*${escapeRegExp(key)}:\\s*(.*?)\\s*$`, 'm');
    return cleanYamlScalar(body.match(expression)?.[1] || '');
}

/** @param {string} body */
function readPrefabInstanceName(body) {
    const expression = /^\s*propertyPath:\s*m_Name\s*\r?\n\s*value:\s*(.*?)\s*$/m;
    return cleanYamlScalar(body.match(expression)?.[1] || '');
}

/** @param {string} value */
function cleanYamlScalar(value) {
    const trimmed = value.trim();
    if (trimmed.length >= 2 && trimmed.startsWith('"') && trimmed.endsWith('"')) {
        return trimmed.slice(1, -1).replaceAll('\\"', '"');
    }
    return trimmed;
}

/** @param {string} body @param {string} key */
function readGuid(body, key) {
    const expression = new RegExp(
        `^\\s*${escapeRegExp(key)}:\\s*\\{[^\\n]*?guid:\\s*([0-9a-fA-F]{32})`,
        'm'
    );
    return body.match(expression)?.[1] || '';
}

/** @param {{ type: string, body: string }} section */
function getComponentLabel(section) {
    if (section.type !== 'MonoBehaviour') {
        return section.type;
    }

    const visual = getVisualComponent(section);
    if (visual) {
        return visual.label;
    }

    const classIdentifier = readScalar(section.body, 'm_EditorClassIdentifier');
    if (!classIdentifier) {
        return 'MonoBehaviour';
    }
    const separatorIndex = classIdentifier.lastIndexOf('::');
    return separatorIndex >= 0 ? classIdentifier.slice(separatorIndex + 2) : classIdentifier;
}

/** @param {string} value */
function escapeRegExp(value) {
    return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** @param {Array<object>} roots @param {(node: object) => void} action */
function walkNodes(roots, action) {
    for (const node of roots) {
        action(node);
        walkNodes(node.children, action);
    }
}

/** @param {vscode.Uri} uri */
function getAssetPath(uri) {
    const workspaceFolder = vscode.workspace.getWorkspaceFolder(uri);
    if (!workspaceFolder) {
        return uri.fsPath;
    }
    return vscode.workspace.asRelativePath(uri, false).replaceAll('\\', '/');
}

/** @param {vscode.Webview} webview */
function getWebviewHtml(webview) {
    const nonce = createNonce();
    return `<!DOCTYPE html>
<html lang="zh-CN">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource} data:; style-src ${webview.cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}';">
    <title>Unity Prefab Hierarchy</title>
    <style>
        :root {
            color-scheme: light dark;
            --border: var(--vscode-panel-border, #3b3b3b);
            --muted: var(--vscode-descriptionForeground, #999);
            --selected: var(--vscode-list-activeSelectionBackground, #094771);
            --hover: var(--vscode-list-hoverBackground, #2a2d2e);
            --panel: var(--vscode-sideBar-background, var(--vscode-editor-background));
        }

        * { box-sizing: border-box; }
        body {
            margin: 0;
            padding: 0;
            color: var(--vscode-foreground);
            background: var(--vscode-editor-background);
            font-family: var(--vscode-font-family);
            font-size: var(--vscode-font-size);
        }

        button, input { font: inherit; }
        button { color: inherit; }

        .toolbar {
            display: flex;
            align-items: center;
            gap: 8px;
            min-height: 46px;
            padding: 8px 12px;
            border-bottom: 1px solid var(--border);
            background: var(--panel);
        }

        .title-block { min-width: 180px; flex: 1; overflow: hidden; }
        .title { font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .path { margin-top: 3px; color: var(--muted); font-size: 0.9em; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .search {
            width: min(280px, 30vw);
            padding: 5px 8px;
            color: var(--vscode-input-foreground);
            background: var(--vscode-input-background);
            border: 1px solid var(--vscode-input-border, var(--border));
            outline: none;
        }
        .search:focus { border-color: var(--vscode-focusBorder); }
        .toolbar-button {
            border: 1px solid var(--border);
            padding: 4px 8px;
            background: transparent;
            cursor: pointer;
        }
        .toolbar-button:hover { background: var(--hover); }

        .stats {
            display: flex;
            gap: 12px;
            padding: 7px 12px;
            color: var(--muted);
            border-bottom: 1px solid var(--border);
            font-size: 0.9em;
        }

        .layout { display: grid; grid-template-columns: minmax(260px, 28%) minmax(360px, 1fr) minmax(250px, 25%); min-height: calc(100vh - 80px); }
        .tree-panel { min-width: 0; padding: 10px 8px 24px 8px; overflow: auto; }
        .preview-panel { display: flex; min-width: 0; min-height: 360px; flex-direction: column; border-left: 1px solid var(--border); }
        .preview-toolbar { display: flex; align-items: center; gap: 8px; min-height: 36px; padding: 6px 10px; border-bottom: 1px solid var(--border); background: var(--panel); }
        .preview-title { font-weight: 600; }
        .preview-status { flex: 1; color: var(--muted); font-size: 0.9em; }
        .preview-stage { position: relative; flex: 1; min-height: 320px; overflow: hidden; background-color: var(--vscode-editor-background); background-image: linear-gradient(45deg, rgba(127,127,127,.08) 25%, transparent 25%), linear-gradient(-45deg, rgba(127,127,127,.08) 25%, transparent 25%), linear-gradient(45deg, transparent 75%, rgba(127,127,127,.08) 75%), linear-gradient(-45deg, transparent 75%, rgba(127,127,127,.08) 75%); background-size: 24px 24px; background-position: 0 0, 0 12px, 12px -12px, -12px 0; }
        .preview-item { position: absolute; display: flex; align-items: center; justify-content: center; border: 1px solid transparent; cursor: pointer; transform-origin: center; }
        .preview-item:hover, .preview-item.selected { border-color: var(--vscode-focusBorder); z-index: 9999; }
        .preview-item.selected { box-shadow: 0 0 0 1px var(--vscode-focusBorder); }
        .preview-image { display: block; width: 100%; height: 100%; object-fit: fill; pointer-events: none; }
        .preview-placeholder { display: flex; width: 100%; height: 100%; align-items: center; justify-content: center; padding: 4px; color: var(--muted); background: rgba(127,127,127,.16); font-size: 0.8em; text-align: center; overflow-wrap: anywhere; pointer-events: none; }
        .preview-empty { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; padding: 24px; color: var(--muted); text-align: center; }
        .details-panel { min-width: 0; padding: 14px; border-left: 1px solid var(--border); background: var(--panel); overflow: auto; }

        .tree-node { min-width: max-content; }
        .tree-row {
            display: flex;
            align-items: center;
            gap: 5px;
            min-height: 28px;
            width: 100%;
            padding: 3px 8px 3px 0;
            border: 0;
            text-align: left;
            background: transparent;
            cursor: pointer;
        }
        .tree-row:hover { background: var(--hover); }
        .tree-row.selected { background: var(--selected); }
        .tree-row.inactive .node-name { color: var(--muted); text-decoration: line-through; }
        .twisty { width: 18px; padding: 0; border: 0; background: transparent; cursor: pointer; color: var(--muted); }
        .twisty.empty { cursor: default; }
        .node-icon { color: var(--vscode-symbolIcon-classForeground, #ee9d28); width: 16px; text-align: center; }
        .node-name { max-width: 420px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .badge {
            padding: 1px 5px;
            border: 1px solid var(--border);
            border-radius: 3px;
            color: var(--muted);
            font-size: 0.82em;
            white-space: nowrap;
        }
        .badge.nested { color: var(--vscode-charts-yellow, #cca700); }
        .children { margin-left: 18px; }
        .empty-state, .error-state { padding: 32px 20px; color: var(--muted); text-align: center; }
        .error-state { color: var(--vscode-errorForeground); }
        .detail-title { margin: 0 0 14px; font-size: 1.2em; overflow-wrap: anywhere; }
        .detail-section { margin-top: 18px; }
        .detail-label { margin-bottom: 6px; color: var(--muted); font-size: 0.9em; }
        .component-list { display: flex; flex-wrap: wrap; gap: 5px; }
        .component { padding: 4px 7px; border: 1px solid var(--border); border-radius: 3px; overflow-wrap: anywhere; }
        .detail-value { overflow-wrap: anywhere; }
        .detail-button { margin-top: 14px; padding: 5px 8px; border: 1px solid var(--border); background: transparent; cursor: pointer; }
        .detail-button:hover { background: var(--hover); }
        .hint { margin-top: 16px; color: var(--muted); font-size: 0.9em; line-height: 1.5; }

        @media (max-width: 720px) {
            .layout { grid-template-columns: 1fr; }
            .preview-panel, .details-panel { border-top: 1px solid var(--border); border-left: 0; }
            .search { width: 150px; }
        }
    </style>
</head>
<body>
    <header class="toolbar">
        <div class="title-block">
            <div id="title" class="title">Unity Prefab</div>
            <div id="path" class="path"></div>
        </div>
        <input id="search" class="search" type="search" placeholder="搜索节点或组件" aria-label="搜索节点或组件">
        <button id="expand" class="toolbar-button" type="button">展开全部</button>
        <button id="collapse" class="toolbar-button" type="button">折叠全部</button>
        <button id="refresh" class="toolbar-button" type="button" title="重新读取 Prefab">↻</button>
    </header>
    <div id="stats" class="stats"></div>
    <main class="layout">
        <section id="tree" class="tree-panel" role="tree"></section>
        <section class="preview-panel" aria-label="2D预览">
            <div class="preview-toolbar">
                <span class="preview-title">2D 预览</span>
                <span id="previewStatus" class="preview-status">等待读取图像</span>
            </div>
            <div id="preview" class="preview-stage"></div>
        </section>
        <aside id="details" class="details-panel"></aside>
    </main>

    <script nonce="${nonce}">
        const api = acquireVsCodeApi();
        let documentData = null;
        let selectedId = null;
        let filterText = '';
        const expandedIds = new Set();
        const nodesById = new Map();

        api.postMessage({ type: 'ready' });

        window.addEventListener('message', event => {
            if (event.data?.type !== 'documentData') return;
            documentData = event.data.data;
            indexNodes();
            if (!selectedId || !nodesById.has(selectedId)) {
                selectedId = documentData.roots?.[0]?.id || null;
            }
            render();
        });

        document.getElementById('search').addEventListener('input', event => {
            filterText = event.target.value.trim().toLocaleLowerCase();
            renderTree();
        });
        document.getElementById('expand').addEventListener('click', () => {
            nodesById.forEach(node => { if (node.children.length > 0) expandedIds.add(node.id); });
            renderTree();
        });
        document.getElementById('collapse').addEventListener('click', () => {
            expandedIds.clear();
            renderTree();
        });
        document.getElementById('refresh').addEventListener('click', () => api.postMessage({ type: 'refresh' }));
        window.addEventListener('resize', () => renderPreview());

        document.getElementById('tree').addEventListener('click', event => {
            const target = event.target.closest('[data-action]');
            if (!target) return;
            const id = target.dataset.id;
            if (target.dataset.action === 'toggle') {
                if (expandedIds.has(id)) expandedIds.delete(id); else expandedIds.add(id);
                renderTree();
            } else if (target.dataset.action === 'select') {
                selectedId = id;
                renderTree();
                renderDetails();
            }
        });

        document.getElementById('details').addEventListener('click', event => {
            const target = event.target.closest('[data-action]');
            if (!target) return;
            if (target.dataset.action === 'copyPath') api.postMessage({ type: 'copyPath' });
            if (target.dataset.action === 'revealInExplorer') api.postMessage({ type: 'revealInExplorer' });
        });

        document.getElementById('preview').addEventListener('click', event => {
            const item = event.target.closest('[data-preview-id]');
            if (!item) return;
            selectedId = item.dataset.previewId;
            renderTree();
            renderDetails();
            renderPreview();
        });

        function indexNodes() {
            nodesById.clear();
            for (const root of documentData?.roots || []) walk(root, node => nodesById.set(node.id, node));
        }

        function walk(node, action) {
            action(node);
            for (const child of node.children || []) walk(child, action);
        }

        function render() {
            document.getElementById('title').textContent = documentData?.name || 'Unity Prefab';
            document.getElementById('path').textContent = documentData?.assetPath || '';
            renderStats();
            renderTree();
            renderDetails();
            renderPreview();
        }

        function renderStats() {
            const stats = documentData?.stats || { objects: 0, components: 0, nestedPrefabs: 0 };
            document.getElementById('stats').innerHTML = documentData?.error
                ? '<span>读取失败</span>'
                : '<span>对象 ' + stats.objects + '</span><span>组件 ' + stats.components + '</span><span>嵌套 Prefab ' + stats.nestedPrefabs + '</span><span>只读预览</span>';
        }

        function renderTree() {
            const tree = document.getElementById('tree');
            if (documentData?.error) {
                tree.innerHTML = '<div class="error-state">' + escapeHtml(documentData.error) + '</div>';
                return;
            }
            if (!documentData?.roots?.length) {
                tree.innerHTML = '<div class="empty-state">没有找到 GameObject 层级。</div>';
                return;
            }
            const html = documentData.roots.map(node => renderNode(node, 0)).join('');
            tree.innerHTML = html || '<div class="empty-state">没有匹配的节点。</div>';
        }

        function renderNode(node, depth) {
            if (!matchesFilter(node)) return '';
            const hasChildren = node.children.length > 0;
            const isExpanded = filterText.length > 0 || expandedIds.has(node.id);
            const selected = node.id === selectedId ? ' selected' : '';
            const inactive = node.active ? '' : ' inactive';
            const nested = node.nestedPrefab ? '<span class="badge nested">嵌套 Prefab</span>' : '';
            const components = node.components.length > 0
                ? '<span class="badge">' + escapeHtml(node.components[0]) + (node.components.length > 1 ? ' +' + (node.components.length - 1) : '') + '</span>'
                : '';
            const twisty = hasChildren
                ? '<button class="twisty" type="button" data-action="toggle" data-id="' + escapeAttribute(node.id) + '" aria-label="切换展开">' + (isExpanded ? '▾' : '▸') + '</button>'
                : '<span class="twisty empty"> </span>';
            const children = hasChildren && isExpanded
                ? '<div class="children">' + node.children.map(child => renderNode(child, depth + 1)).join('') + '</div>'
                : '';
            return '<div class="tree-node" role="treeitem" aria-expanded="' + (hasChildren ? isExpanded : 'false') + '">' +
                '<button class="tree-row' + selected + inactive + '" type="button" data-action="select" data-id="' + escapeAttribute(node.id) + '" style="padding-left:' + (depth * 4) + 'px">' +
                twisty + '<span class="node-icon">' + (hasChildren ? '◇' : '·') + '</span><span class="node-name" title="' + escapeAttribute(node.name) + '">' + escapeHtml(node.name) + '</span>' + nested + components +
                '</button>' + children + '</div>';
        }

        function matchesFilter(node) {
            if (!filterText) return true;
            const values = [node.name, ...(node.components || [])].join(' ').toLocaleLowerCase();
            return values.includes(filterText) || node.children.some(matchesFilter);
        }

        function renderDetails() {
            const details = document.getElementById('details');
            const node = selectedId ? nodesById.get(selectedId) : null;
            if (!node) {
                details.innerHTML = '<div class="empty-state">选择一个节点查看组件。</div>';
                return;
            }
            const nested = node.nestedPrefab?.guid
                ? '<div class="detail-value">GUID: ' + escapeHtml(node.nestedPrefab.guid) + '</div>'
                : node.nestedPrefab ? '<div class="detail-value">这是一个嵌套 Prefab 实例。</div>' : '';
            const components = node.components.length
                ? '<div class="component-list">' + node.components.map(item => '<span class="component">' + escapeHtml(item) + '</span>').join('') + '</div>'
                : '<div class="detail-value">无组件信息</div>';
            details.innerHTML = '<h2 class="detail-title">' + escapeHtml(node.name) + '</h2>' +
                '<div class="detail-section"><div class="detail-label">状态</div><div class="detail-value">' + (node.active ? '启用' : '禁用') + '</div></div>' +
                (nested ? '<div class="detail-section"><div class="detail-label">Prefab 关系</div>' + nested + '</div>' : '') +
                '<div class="detail-section"><div class="detail-label">组件（' + node.components.length + '）</div>' + components + '</div>' +
                '<div class="detail-section"><div class="detail-label">资源文件</div><div class="detail-value">' + escapeHtml(documentData.assetPath) + '</div></div>' +
                '<button class="detail-button" type="button" data-action="copyPath">复制文件路径</button> ' +
                '<button class="detail-button" type="button" data-action="revealInExplorer">在资源管理器中定位</button>' +
                '<div class="hint">这是保存到磁盘的 Prefab 预览。Unity 中尚未保存的改动不会出现在这里。</div>';
        }

        function renderPreview() {
            const stage = document.getElementById('preview');
            const status = document.getElementById('previewStatus');
            if (documentData?.error) {
                stage.innerHTML = '<div class="preview-empty">无法读取 Prefab。</div>';
                status.textContent = '读取失败';
                return;
            }

            const items = [];
            collectPreviewItems(documentData?.roots || [], 0, 0, 1, 1, true, items);
            if (!items.length) {
                stage.innerHTML = '<div class="preview-empty">没有可绘制的 2D 图像。<br>需要 SpriteRenderer、Image 或 RawImage，并且引用可读取的图片资源。</div>';
                status.textContent = '没有图像';
                return;
            }

            const width = Math.max(stage.clientWidth, 640);
            const height = Math.max(stage.clientHeight, 360);
            const bounds = getPreviewBounds(items);
            const contentWidth = Math.max(bounds.maxX - bounds.minX, 1);
            const contentHeight = Math.max(bounds.maxY - bounds.minY, 1);
            const drawScale = Math.max(0.08, Math.min(1, (width - 40) / contentWidth, (height - 40) / contentHeight));
            const centerX = (bounds.minX + bounds.maxX) / 2;
            const centerY = (bounds.minY + bounds.maxY) / 2;

            stage.innerHTML = items.map(item => {
                const itemWidth = Math.max(item.width * drawScale, 2);
                const itemHeight = Math.max(item.height * drawScale, 2);
                const left = width / 2 + (item.x - centerX) * drawScale;
                const top = height / 2 - (item.y - centerY) * drawScale;
                const selected = item.nodeId === selectedId ? ' selected' : '';
                const color = item.color || { r: 1, g: 1, b: 1, a: 1 };
                const image = item.imageUri
                    ? '<img class="preview-image" src="' + escapeAttribute(item.imageUri) + '" alt="' + escapeAttribute(item.label) + '">'
                    : '<span class="preview-placeholder">' + escapeHtml(item.label) + '</span>';
                return '<div class="preview-item' + selected + '" data-preview-id="' + escapeAttribute(item.nodeId) + '" title="' + escapeAttribute(item.label) + '" style="left:' + left + 'px;top:' + top + 'px;width:' + itemWidth + 'px;height:' + itemHeight + 'px;transform:translate(-50%,-50%) rotate(' + (-item.rotation) + 'deg) scale(' + item.scaleX + ',' + item.scaleY + ');opacity:' + color.a + ';background:rgba(' + Math.round(color.r * 255) + ',' + Math.round(color.g * 255) + ',' + Math.round(color.b * 255) + ',' + color.a + ')">' + image + '</div>';
            }).join('');
            status.textContent = items.length + ' 个图像 · 预览比例 ' + Math.round(drawScale * 100) + '%';
        }

        function collectPreviewItems(nodes, parentX, parentY, parentScaleX, parentScaleY, parentActive, result) {
            for (const node of nodes) {
                const transform = node.transform || { x: 0, y: 0, scaleX: 1, scaleY: 1, rotation: 0, width: 0, height: 0, isRectTransform: false };
                const coordinateScale = transform.isRectTransform ? 1 : 64;
                const x = parentX + transform.x * coordinateScale * parentScaleX;
                const y = parentY + transform.y * coordinateScale * parentScaleY;
                const scaleX = parentScaleX * (transform.scaleX || 1);
                const scaleY = parentScaleY * (transform.scaleY || 1);
                const active = parentActive && node.active;
                if (active) {
                    for (const visual of node.visuals || []) {
                        if (!visual.enabled) continue;
                        const defaultWidth = visual.kind === 'sprite' ? 1 : 120;
                        const defaultHeight = visual.kind === 'sprite' ? 1 : 80;
                        const baseWidth = transform.width || defaultWidth;
                        const baseHeight = transform.height || defaultHeight;
                        result.push({
                            nodeId: node.id,
                            label: node.name + ' · ' + visual.label,
                            imageUri: visual.imageUri,
                            x,
                            y,
                            width: Math.abs(baseWidth * coordinateScale),
                            height: Math.abs(baseHeight * coordinateScale),
                            scaleX,
                            scaleY,
                            rotation: transform.rotation || 0,
                            color: visual.color
                        });
                    }
                }
                collectPreviewItems(node.children || [], x, y, scaleX, scaleY, active, result);
            }
        }

        function getPreviewBounds(items) {
            const bounds = { minX: 0, maxX: 0, minY: 0, maxY: 0 };
            for (const item of items) {
                const halfWidth = item.width * Math.abs(item.scaleX) / 2;
                const halfHeight = item.height * Math.abs(item.scaleY) / 2;
                bounds.minX = Math.min(bounds.minX, item.x - halfWidth);
                bounds.maxX = Math.max(bounds.maxX, item.x + halfWidth);
                bounds.minY = Math.min(bounds.minY, item.y - halfHeight);
                bounds.maxY = Math.max(bounds.maxY, item.y + halfHeight);
            }
            return bounds;
        }

        function escapeHtml(value) {
            return String(value ?? '').replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character]));
        }

        function escapeAttribute(value) { return escapeHtml(value); }
    </script>
</body>
</html>`;
}

/** @returns {string} */
function createNonce() {
    const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    let value = '';
    for (let index = 0; index < 32; index += 1) {
        value += alphabet.charAt(Math.floor(Math.random() * alphabet.length));
    }
    return value;
}

function deactivate() {}

module.exports = { activate, deactivate, parsePrefabYaml, getWebviewHtml };
