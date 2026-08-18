const vscode = require('vscode');

const VIEW_TYPE = 'unityPrefabBrowser.prefabEditor';
const EXCLUDE_GLOB = '**/{Library,Temp,Logs,Obj,obj,Build,build,node_modules}/**';

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
        webviewPanel.webview.options = { enableScripts: true };
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
                    await postDocument(webviewPanel, document.data);
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
        void postDocument(webviewPanel, document.data);
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
            await postDocument(editor.panel, data);
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
                type: section.type
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
        const components = gameObject.componentIds
            .map(componentId => sectionsById.get(componentId))
            .filter(Boolean)
            .map(section => getComponentLabel(section));

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
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}';">
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

        .layout { display: grid; grid-template-columns: minmax(320px, 1fr) minmax(250px, 32%); min-height: calc(100vh - 80px); }
        .tree-panel { min-width: 0; padding: 10px 8px 24px 8px; overflow: auto; }
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
            .details-panel { border-top: 1px solid var(--border); border-left: 0; }
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

module.exports = { activate, deactivate, parsePrefabYaml };
