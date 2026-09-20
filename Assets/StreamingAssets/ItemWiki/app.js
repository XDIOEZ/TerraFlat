(() => {
    "use strict";

    // Wiki 与 GameConfig 同处 StreamingAssets，物品配置始终通过相对目录读取。
    const ITEM_CONFIG_ROOT = "../GameConfig/Items/";
    const ITEM_MANIFEST_PATH = `${ITEM_CONFIG_ROOT}item-manifest.json`;
    const LOOT_TABLE_PATH = "../GameConfig/LootTables/loot-tables.json";
    const MODULE_GLOSSARY_PATH = "module-glossary.json";
    const ITEM_METADATA_PATH = "item-metadata.json";
    const DESIGN_DOC_INDEX_PATH = "docs/README.md";
    const SYSTEM_MAP_DOC_PATH = "docs/system-script-map.md";
    const SYSTEM_MAP_NODE_WIDTH = 300;
    const SYSTEM_MAP_NODE_HEIGHT = 132;
    const SYSTEM_MAP_MIN_ZOOM = 0.16;
    const SYSTEM_MAP_MAX_ZOOM = 1.8;
    const WIKI_STATUS_API = "/api/wiki/status";
    const ITEM_SAVE_API = "/api/items/save";
    const BUFF_CONFIG_ROOT = "../GameConfig/Buffs/";
    const BUFF_SAVE_API = "/api/buffs/save";

    const CATEGORY_ORDER = [
        "全部", "材料", "食物", "武器", "工具", "装备", "种子", "作物",
        "资源节点", "建筑", "维度入口", "生物", "其他", "BUFF"
    ];

    const CATEGORY_HINTS = {
        weapons: "武器",
        tools: "工具",
        hoes: "工具",
        equipment: "装备",
        seeds: "种子",
        crops: "作物",
        chili: "作物",
        resource_nodes: "资源节点",
        building_summoners: "建筑",
        building_bodies: "建筑",
        dimension_portals: "维度入口"
    };

    const FIELD_LABELS = {
        id: "稳定 ID",
        gameName: "名称",
        description: "说明",
        shellPrefab: "运行时外壳",
        shellAddress: "外壳地址",
        sourcePrefab: "源 Prefab",
        durability: "当前耐久",
        maxDurability: "最大耐久",
        amount: "初始数量",
        volume: "单个体积",
        canBePickedUp: "允许拾取",
        abstract: "抽象模板",
        lootTableId: "战利品表",
        hp: "生命值",
        maxHp: "最大生命值",
        cutting: "切割防御",
        piercing: "穿刺防御",
        chopping: "劈砍防御",
        blunt: "钝击防御",
        CuttingDamage: "切割伤害",
        PiercingDamage: "穿刺伤害",
        ChoppingDamage: "劈砍伤害",
        BluntDamage: "钝击伤害",
        AttackRange: "攻击距离",
        Cooldown: "冷却",
        Damage: "伤害",
        Range: "范围",
        Speed: "速度",
        Duration: "持续时间",
        Interval: "间隔",
        Probability: "概率",
        Chance: "概率",
        Temperature: "温度",
        MaxTemperature: "最大温度",
        Carbohydrates: "碳水",
        Fat: "脂肪",
        Protein: "蛋白质",
        Water: "水分",
        Vitamins: "维生素"
    };

    const state = {
        manifest: null,
        packages: [],
        sourceById: new Map(),
        packageById: new Map(),
        packageHashes: new Map(),
        buffSources: new Map(),
        buffPackages: new Map(),
        buffHashes: new Map(),
        buffWritable: false,
        resolvedById: new Map(),
        lootTables: new Map(),
        moduleGlossary: { modules: {}, fields: {}, fieldHelp: {} },
        itemMetadata: { schemaVersion: 1, items: {} },
        entries: [],
        actors: null,
        recipes: [],
        mechanicsContext: null,
        selectedId: null,
        spriteCache: new Map(),
        designDocs: [],
        designLoaded: false,
        selectedDesignPath: DESIGN_DOC_INDEX_PATH,
        designDocCache: new Map(),
        systemMapLoaded: false,
        projectRoot: "",
        publicReadOnly: false,
        systemMap: {
            width: 5400,
            height: 3100,
            nodes: [],
            nodeById: new Map(),
            regions: [],
            panX: 0,
            panY: 0,
            zoom: 1,
            dragging: false,
            dragPointerId: null,
            dragStartX: 0,
            dragStartY: 0,
            dragOriginX: 0,
            dragOriginY: 0,
            frameRequest: 0
        },
        writable: false,
        activeView: "catalog",
        indexSide: "left"
    };

    const els = {
        statusBar: document.getElementById("statusBar"),
        statusText: document.getElementById("statusText"),
        globalSearch: document.getElementById("globalSearch"),
        reloadButton: document.getElementById("reloadButton"),
        catalogViewButton: document.getElementById("catalogViewButton"),
        buffViewButton: document.getElementById("buffViewButton"),
        overviewViewButton: document.getElementById("overviewViewButton"),
        designViewButton: document.getElementById("designViewButton"),
        systemMapViewButton: document.getElementById("systemMapViewButton"),
        settingsButton: document.getElementById("settingsButton"),
        catalogBook: document.getElementById("catalogBook"),
        overviewBook: document.getElementById("overviewBook"),
        designBook: document.getElementById("designBook"),
        systemMapBook: document.getElementById("systemMapBook"),
        settingsBook: document.getElementById("settingsBook"),
        designSearch: document.getElementById("designSearch"),
        designDocList: document.getElementById("designDocList"),
        designDocMeta: document.getElementById("designDocMeta"),
        designDocContent: document.getElementById("designDocContent"),
        systemMapSearch: document.getElementById("systemMapSearch"),
        systemMapNodeCount: document.getElementById("systemMapNodeCount"),
        systemMapZoomOut: document.getElementById("systemMapZoomOut"),
        systemMapZoomValue: document.getElementById("systemMapZoomValue"),
        systemMapZoomIn: document.getElementById("systemMapZoomIn"),
        systemMapFit: document.getElementById("systemMapFit"),
        systemMapHome: document.getElementById("systemMapHome"),
        systemMapViewport: document.getElementById("systemMapViewport"),
        systemMapSurface: document.getElementById("systemMapSurface"),
        systemMapRegions: document.getElementById("systemMapRegions"),
        systemMapEdges: document.getElementById("systemMapEdges"),
        systemMapNodes: document.getElementById("systemMapNodes"),
        systemMapHint: document.getElementById("systemMapHint"),
        systemMapVisibleCount: document.getElementById("systemMapVisibleCount"),
        categorySelect: document.getElementById("categorySelect"),
        packageSelect: document.getElementById("packageSelect"),
        sortSelect: document.getElementById("sortSelect"),
        showAbstract: document.getElementById("showAbstract"),
        itemList: document.getElementById("itemList"),
        resultCount: document.getElementById("resultCount"),
        categoryTabs: document.getElementById("categoryTabs"),
        indexCollapseButton: document.getElementById("indexCollapseButton"),
        indexToolsPanel: document.getElementById("indexToolsPanel"),
        indexToolsCollapseButton: document.getElementById("indexToolsCollapseButton"),
        countConcrete: document.getElementById("countConcrete"),
        countAbstract: document.getElementById("countAbstract"),
        countPackages: document.getElementById("countPackages"),
        previousItemButton: document.getElementById("previousItemButton"),
        nextItemButton: document.getElementById("nextItemButton"),
        locateItemButton: document.getElementById("locateItemButton"),
        detailPosition: document.getElementById("detailPosition"),
        detailContent: document.getElementById("detailContent"),
        moduleTemplate: document.getElementById("moduleTemplate"),
        overviewCount: document.getElementById("overviewCount"),
        overviewCategorySelect: document.getElementById("overviewCategorySelect"),
        overviewPackageSelect: document.getElementById("overviewPackageSelect"),
        overviewSortSelect: document.getElementById("overviewSortSelect"),
        overviewShowAbstract: document.getElementById("overviewShowAbstract"),
        overviewGrid: document.getElementById("overviewGrid"),
        indexSideToggle: document.getElementById("indexSideToggle"),
        indexSideValue: document.getElementById("indexSideValue"),
        lootModal: document.getElementById("lootModal"),
        lootModalTitle: document.getElementById("lootModalTitle"),
        lootModalBody: document.getElementById("lootModalBody"),
        lootModalClose: document.getElementById("lootModalClose"),
        imageZoomModal: document.getElementById("imageZoomModal"),
        imageZoomTitle: document.getElementById("imageZoomTitle"),
        imageZoomMeta: document.getElementById("imageZoomMeta"),
        imageZoomBody: document.getElementById("imageZoomBody"),
        imageZoomClose: document.getElementById("imageZoomClose")
    };

    // 页面启动时绑定交互并加载当前 JSON。
    function init() {
        bindEvents();
        restoreIndexSideState();
        restoreIndexCollapseState();
        restoreIndexToolsCollapseState();
        if (location.protocol === "file:") {
            setStatus("error", "浏览器禁止 file:// 页面直接读取 JSON。请双击同目录的“打开物品Wiki.cmd”。");
            return;
        }
        loadCatalog();
    }

    // 绑定检索、筛选和快捷键。
    function bindEvents() {
        els.globalSearch.addEventListener("input", () => {
            renderList();
            renderOverview();
        });
        els.categorySelect.addEventListener("change", () => {
            syncCategoryTabs();
            renderList();
        });
        els.packageSelect.addEventListener("change", renderList);
        els.sortSelect.addEventListener("change", renderList);
        els.showAbstract.addEventListener("change", renderList);
        els.previousItemButton.addEventListener("click", () => navigateRelativeItem(-1));
        els.nextItemButton.addEventListener("click", () => navigateRelativeItem(1));
        els.locateItemButton.addEventListener("click", locateSelectedItemInIndex);
        els.reloadButton.addEventListener("click", () => {
            loadCatalog();
            if (state.designLoaded) {
                state.designLoaded = false;
                state.designDocCache.clear();
                if (state.activeView === "design") loadDesignDocs();
            }
            if (state.systemMapLoaded) {
                state.systemMapLoaded = false;
                if (state.activeView === "system-map") loadSystemMap();
            }
        });
        els.indexCollapseButton.addEventListener("click", toggleIndexPanel);
        els.indexToolsCollapseButton.addEventListener("click", toggleIndexToolsPanel);
        els.catalogViewButton.addEventListener("click", () => setView("catalog"));
        els.buffViewButton.addEventListener("click", () => setView("buffs"));
        els.overviewViewButton.addEventListener("click", () => setView("overview"));
        els.designViewButton.addEventListener("click", () => setView("design"));
        els.systemMapViewButton.addEventListener("click", () => setView("system-map"));
        els.settingsButton.addEventListener("click", () => setView("settings"));
        els.designSearch.addEventListener("input", renderDesignDocList);
        bindSystemMapEvents();
        els.indexSideToggle.addEventListener("change", () => {
            setIndexSide(els.indexSideToggle.checked ? "right" : "left", true);
        });
        els.overviewCategorySelect.addEventListener("change", renderOverview);
        els.overviewPackageSelect.addEventListener("change", renderOverview);
        els.overviewSortSelect.addEventListener("change", renderOverview);
        els.overviewShowAbstract.addEventListener("change", renderOverview);
        els.lootModalClose.addEventListener("click", closeLootModal);
        els.lootModal.addEventListener("click", event => {
            if (event.target === els.lootModal) closeLootModal();
        });
        els.imageZoomClose.addEventListener("click", closeImageZoom);
        els.imageZoomModal.addEventListener("click", event => {
            if (event.target === els.imageZoomModal) closeImageZoom();
        });
        document.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                if (!els.imageZoomModal.hidden) closeImageZoom();
                else if (!els.lootModal.hidden) closeLootModal();
            }
            if (event.key === "/" && document.activeElement !== els.globalSearch) {
                if (!els.imageZoomModal.hidden || !els.lootModal.hidden) return;
                event.preventDefault();
                els.globalSearch.focus();
                els.globalSearch.select();
            }
            if ((event.key === "ArrowLeft" || event.key === "ArrowRight") && canUseCatalogPageKeys(event)) {
                event.preventDefault();
                navigateRelativeItem(event.key === "ArrowLeft" ? -1 : 1);
            }
        });
        window.addEventListener("resize", () => hideModuleFieldTooltip());
        window.addEventListener("scroll", () => hideModuleFieldTooltip(), true);
    }

    // 仅在档案浏览且未编辑文本/打开弹窗时接管左右方向键，避免干扰输入框与原生控件。
    function canUseCatalogPageKeys(event) {
        if (state.activeView !== "catalog" && state.activeView !== "buffs") return false;
        if (!els.imageZoomModal.hidden || !els.lootModal.hidden) return false;
        if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return false;

        const target = event.target instanceof Element ? event.target : document.activeElement;
        if (!target) return true;
        return !target.closest("input, textarea, select, [contenteditable='true'], .inline-editing");
    }

    // 恢复索引页左右位置；这是浏览器偏好，不写入项目配置。
    function restoreIndexSideState() {
        const saved = localStorage.getItem("flatworld.itemWiki.indexSide");
        setIndexSide(saved === "right" ? "right" : "left", false);
    }

    // 切换 INDEX 位于书页左侧或右侧，并同步折叠按钮方向。
    function setIndexSide(side, persist) {
        const normalized = side === "right" ? "right" : "left";
        state.indexSide = normalized;
        const right = normalized === "right";
        els.catalogBook.classList.toggle("index-right", right);
        els.indexSideToggle.checked = right;
        els.indexSideValue.textContent = right ? "右侧" : "左侧";
        updateIndexCollapseControl();
        if (persist) localStorage.setItem("flatworld.itemWiki.indexSide", normalized);
    }

    // 恢复开发者上次使用的索引页展开状态。
    function restoreIndexCollapseState() {
        const collapsed = localStorage.getItem("flatworld.itemWiki.indexCollapsed") === "1";
        setIndexCollapsed(collapsed, false);
    }

    // 切换左侧 Index 页折叠状态。
    function toggleIndexPanel() {
        const collapsed = !els.catalogBook.classList.contains("index-collapsed");
        setIndexCollapsed(collapsed, true);
    }

    // 应用索引页折叠状态，并同步按钮文案和可访问性信息。
    function setIndexCollapsed(collapsed, persist) {
        els.catalogBook.classList.toggle("index-collapsed", collapsed);
        els.indexCollapseButton.setAttribute("aria-expanded", String(!collapsed));
        updateIndexCollapseControl();
        if (persist) {
            localStorage.setItem("flatworld.itemWiki.indexCollapsed", collapsed ? "1" : "0");
        }
    }

    // 根据索引位置和展开状态设置正确的箭头方向与提示。
    function updateIndexCollapseControl() {
        const collapsed = els.catalogBook.classList.contains("index-collapsed");
        const right = state.indexSide === "right";
        els.indexCollapseButton.title = collapsed ? "展开索引页" : "收起索引页";
        els.indexCollapseButton.querySelector(".index-collapse-icon").textContent = collapsed
            ? (right ? "◀" : "▶")
            : (right ? "▶" : "◀");
        els.indexCollapseButton.querySelector(".index-collapse-text").textContent = collapsed ? "展开索引" : "收起索引";
    }

    // 恢复索引工具面板上次使用的折叠状态。
    function restoreIndexToolsCollapseState() {
        const collapsed = localStorage.getItem("flatworld.itemWiki.indexToolsCollapsed") === "1";
        setIndexToolsCollapsed(collapsed, false);
    }

    // 切换统计与筛选工具面板的折叠状态。
    function toggleIndexToolsPanel() {
        const collapsed = !els.indexToolsPanel.classList.contains("is-collapsed");
        setIndexToolsCollapsed(collapsed, true);
    }

    // 应用索引工具面板折叠状态，并同步按钮与无障碍信息。
    function setIndexToolsCollapsed(collapsed, persist) {
        els.indexToolsPanel.classList.toggle("is-collapsed", collapsed);
        els.indexToolsCollapseButton.setAttribute("aria-expanded", String(!collapsed));
        els.indexToolsCollapseButton.title = collapsed ? "展开索引工具" : "收起索引工具";
        els.indexToolsCollapseButton.querySelector(".index-tools-collapse-icon").textContent = collapsed ? "▼" : "▲";
        els.indexToolsCollapseButton.querySelector(".index-tools-collapse-text").textContent = collapsed ? "展开" : "收起";
        if (persist) {
            localStorage.setItem("flatworld.itemWiki.indexToolsCollapsed", collapsed ? "1" : "0");
        }
    }

    // 从 Manifest 读取全部启用分包，并按项目运行时规则解析继承。
    async function loadCatalog(preferredId = state.selectedId) {
        setStatus("loading", "正在读取 item-manifest.json 与全部启用分包…");
        els.reloadButton.disabled = true;
        try {
            await checkWriteCapability();
            state.spriteCache.clear();
            const [manifest, lootRoot, moduleGlossary, itemMetadata, actors, recipes, buffs] = await Promise.all([
                fetchJson(ITEM_MANIFEST_PATH),
                fetchJson(LOOT_TABLE_PATH),
                fetchJson(MODULE_GLOSSARY_PATH),
                fetchJson(ITEM_METADATA_PATH),
                ItemMechanics.loadManifest(fetchJson, "../GameConfig/Actors/", "actor-manifest.json", "actors"),
                ItemMechanics.loadManifest(fetchJson, "../GameConfig/Recipes/", "recipe-manifest.json", "recipes"),
                loadBuffCatalog()
            ]);
            const packages = (manifest.packages || []).filter(pkg => pkg && pkg.enabled !== false);
            const packagePayloads = await Promise.all(packages.map(async pkg => ({
                package: pkg,
                ...(await fetchJsonWithHash(`${ITEM_CONFIG_ROOT}${pkg.path}`))
            })));

            const sourceById = new Map();
            const packageById = new Map();
            const packageHashes = new Map();
            for (const payload of packagePayloads) {
                const items = payload.data.items;
                if (!Array.isArray(items)) {
                    throw new Error(`分包 ${payload.package.id} 缺少 items 数组`);
                }
                packageHashes.set(payload.package.id, payload.hash);
                for (const source of items) {
                    const id = String(source?.id || "").trim();
                    if (!id) throw new Error(`分包 ${payload.package.id} 存在空 Item ID`);
                    const key = id.toLowerCase();
                    if (sourceById.has(key)) throw new Error(`跨分包存在重复 Item ID：${id}`);
                    sourceById.set(key, deepClone(source));
                    packageById.set(key, payload.package);
                }
            }

            state.manifest = manifest;
            state.packages = packages;
            state.sourceById = sourceById;
            state.packageById = packageById;
            state.packageHashes = packageHashes;
            state.buffSources = buffs.sources;
            state.buffPackages = buffs.packages;
            state.buffHashes = buffs.hashes;
            state.actors = actors;
            state.recipes = Array.from(recipes.resolved.values());
            state.itemMetadata = itemMetadata && itemMetadata.schemaVersion === 1 && isPlainObject(itemMetadata.items)
                ? itemMetadata
                : { schemaVersion: 1, items: {} };
            state.resolvedById = resolveDefinitions(sourceById);
            state.lootTables = new Map((lootRoot.lootTables || [])
                .filter(table => table && table.id)
                .map(table => [String(table.id).toLowerCase(), table]));
            state.moduleGlossary = moduleGlossary && moduleGlossary.schemaVersion === 1
                ? moduleGlossary
                : { modules: {}, fields: {}, fieldHelp: {} };

            rebuildEntries();

            renderFilters();
            updateSummary();
            renderList();
            renderOverview();
            restoreOrSelectFirst(preferredId);
            setStatus(
                "ready",
                `已载入 ${state.entries.filter(entry => !entry.final.abstract && entry.kind === "item").length} 个物品、${state.entries.filter(entry => !entry.final.abstract && entry.kind === "actor").length} 个生物、${state.recipes.length} 个配方、${state.buffSources.size} 个 BUFF；双击数值编辑，生物只读。`
            );
        } catch (error) {
            console.error(error);
            setStatus("error", `读取失败：${error.message}`);
            els.itemList.innerHTML = `<div class="no-data">${escapeHtml(error.message)}</div>`;
            showEmpty();
        } finally {
            els.reloadButton.disabled = false;
        }
    }

    // 获取 JSON，并把 HTTP 错误转换成可读异常。
    async function fetchJson(path) {
        const response = await fetch(path, { cache: "no-store" });
        if (!response.ok) throw new Error(`${response.status} ${response.statusText} · ${path}`);
        return response.json();
    }

    // 独立命名空间避免 BUFF 与 Item 同名或分包同名时互相覆盖。
    async function loadBuffCatalog() {
        const manifest = await fetchJson(`${BUFF_CONFIG_ROOT}buff-manifest.json`);
        if (manifest.schemaVersion !== 1 || !Array.isArray(manifest.packages)) throw new Error("BUFF 清单格式无效");
        const sources = new Map(), packages = new Map(), hashes = new Map();
        for (const pkg of manifest.packages.filter(value => value && value.enabled !== false)) {
            const { data, hash } = await fetchJsonWithHash(`${BUFF_CONFIG_ROOT}${pkg.path}`);
            if (data.schemaVersion !== 1 || !Array.isArray(data.buffs)) throw new Error(`BUFF 分包 ${pkg.id} 格式无效`);
            hashes.set(pkg.id, hash);
            for (const source of data.buffs) {
                const key = String(source?.id || "").trim().toLowerCase();
                if (!key || sources.has(key)) throw new Error(`BUFF ID 为空或重复：${source?.id}`);
                sources.set(key, deepClone(source));
                packages.set(key, pkg);
            }
        }
        return { sources, packages, hashes };
    }

    // 读取分包原始字节并计算服务端同算法指纹，用于保存时检测外部修改。
    async function fetchJsonWithHash(path) {
        const response = await fetch(path, { cache: "no-store" });
        if (!response.ok) throw new Error(`${response.status} ${response.statusText} · ${path}`);
        const bytes = await response.arrayBuffer();
        const text = new TextDecoder("utf-8").decode(bytes).replace(/^\uFEFF/, "");
        const hash = globalThis.crypto?.subtle ? await sha256(bytes) : "";
        return { data: JSON.parse(text), hash };
    }

    // 使用浏览器 WebCrypto 计算 SHA-256，格式与本地写入服务一致。
    async function sha256(bytes) {
        const digest = await crypto.subtle.digest("SHA-256", bytes);
        return Array.from(new Uint8Array(digest), value => value.toString(16).padStart(2, "0")).join("");
    }

    // 探测当前 Wiki 服务能力；本机模式额外返回项目根目录，用于构造 VS Code 深链。
    async function checkWriteCapability() {
        try {
            const response = await fetch(WIKI_STATUS_API, { cache: "no-store" });
            const payload = response.ok ? await response.json() : null;
            state.writable = payload?.writable === true;
            state.buffWritable = payload?.buffWritable === true;
            state.publicReadOnly = payload?.publicReadOnly === true;
            state.projectRoot = typeof payload?.projectRoot === "string" ? payload.projectRoot : "";
        } catch {
            state.writable = false;
            state.buffWritable = false;
            state.publicReadOnly = false;
            state.projectRoot = "";
        }
        updateSystemMapEditorHint();
    }

    // 完整模拟 ItemDefinitionCatalogLoader 的 parent 合并策略。
    function resolveDefinitions(sourceById) {
        return ItemMechanics.resolveDefinitions(sourceById);
    }

    function buildEntry(final, kind = "item") {
        const key = String(final.id || "").toLowerCase();
        const source = (kind === "actor" ? state.actors.sources : state.sourceById).get(key) || {};
        const pkg = (kind === "actor" ? state.actors.packages : state.packageById).get(key) || { id: "unknown", path: "" };
        const category = kind === "actor" ? "生物" : inferCategory(pkg.id, final, source);
        const displayName = String(final.gameName || final.id || "<未命名>").trim();
        const searchBlob = [
            displayName,
            final.id,
            final.description,
            pkg.id,
            category,
            ...(final.tags || []),
            JSON.stringify(final)
        ].filter(Boolean).join(" ").toLowerCase();

        return {
            id: kind === "actor" ? `actor:${final.id}` : final.id,
            kind,
            displayName,
            category,
            package: pkg,
            final,
            source,
            searchBlob,
            modifiedAt: kind === "actor" ? null : state.itemMetadata?.items?.[final.id] || null
        };
    }

    function rebuildEntries() {
        state.entries = Array.from(state.resolvedById.values()).map(final => buildEntry(final));
        state.entries.push(...Array.from(state.actors.resolved.values()).map(final => buildEntry(final, "actor")));
        state.mechanicsContext = ItemMechanics.createContext(state.entries, state.recipes, state.lootTables);
        state.entries.push(...Array.from(state.buffSources, ([key, source]) => BuffWiki.makeEntry(source, state.buffPackages.get(key))));
    }

    // 优先按 Manifest 分包和模块语义生成开发者友好分类。
    function inferCategory(packageId, final, source) {
        if (CATEGORY_HINTS[packageId]) return CATEGORY_HINTS[packageId];

        const modules = Object.keys(final.modules || {}).join(" ").toLowerCase();
        const tags = (final.tags || []).join(" ").toLowerCase();
        const lineage = `${source.parent || ""} ${final.shellPrefab || ""}`.toLowerCase();
        if (modules.includes("food") || modules.includes("食物") || tags.includes("food")) return "食物";
        if (modules.includes("weapon") || modules.includes("damage") || lineage.includes("weapon")) return "武器";
        if (modules.includes("equipment") || lineage.includes("equipment")) return "装备";
        if (modules.includes("seed") || lineage.includes("seed")) return "种子";
        if (modules.includes("crop") || lineage.includes("crop")) return "作物";
        if (modules.includes("building") || lineage.includes("building")) return "建筑";
        if (modules.includes("gather") || modules.includes("resource") || final.health?.hasHp) return "资源节点";
        if (packageId === "basic_items" || lineage.includes("basicitem")) return "材料";
        return "其他";
    }

    // 刷新分类、分包选择器和右侧书签。
    function renderFilters() {
        const catalogEntries = state.entries.filter(entry => (entry.kind === "buff") === (state.activeView === "buffs"));
        const presentCategories = new Set(catalogEntries.map(entry => entry.category));
        const categories = CATEGORY_ORDER.filter(category => category === "全部" || presentCategories.has(category));
        const categoryOptions = categories.map(category =>
            `<option value="${escapeAttr(category)}">${escapeHtml(category)}</option>`
        ).join("");
        els.categorySelect.innerHTML = categoryOptions;
        els.overviewCategorySelect.innerHTML = CATEGORY_ORDER.filter(category => category !== "BUFF").map(category =>
            `<option value="${escapeAttr(category)}">${escapeHtml(category)}</option>`).join("");

        const packageOptions = [
            `<option value="全部">全部分包</option>`,
            ...Array.from(new Map(catalogEntries.map(entry => [entry.package.id, entry.package])).values()).map(pkg =>
                `<option value="${escapeAttr(pkg.id)}">${escapeHtml(pkg.id)}</option>`
            )
        ].join("");
        els.packageSelect.innerHTML = packageOptions;
        els.overviewPackageSelect.innerHTML = `<option value="全部">全部分包</option>` + Array.from(
            new Map(state.entries.filter(entry => entry.kind !== "buff").map(entry => [entry.package.id, entry.package])).values()
        ).map(pkg => `<option value="${escapeAttr(pkg.id)}">${escapeHtml(pkg.id)}</option>`).join("");

        els.categoryTabs.innerHTML = categories.map((category, index) =>
            `<button class="category-tab${category === "全部" ? " active" : ""}" type="button" ` +
            `data-category="${escapeAttr(category)}" title="${escapeAttr(category)}">` +
            `${String(index + 1).padStart(2, "0")} · ${escapeHtml(category)}</button>`
        ).join("");

        els.categoryTabs.querySelectorAll(".category-tab").forEach(button => {
            button.addEventListener("click", () => {
                els.categorySelect.value = button.dataset.category;
                syncCategoryTabs();
                renderList();
            });
        });
    }

    // 切换档案浏览、全物品总览、游戏设计、脚本世界与设置页；书本只作为视觉容器，不限制信息架构。
    function setView(view) {
        state.activeView = ["overview", "design", "system-map", "settings", "buffs"].includes(view) ? view : "catalog";
        const overview = state.activeView === "overview";
        const design = state.activeView === "design";
        const systemMap = state.activeView === "system-map";
        const settings = state.activeView === "settings";
        document.body.classList.toggle("overview-mode", overview);
        els.catalogBook.hidden = overview || design || systemMap || settings;
        els.overviewBook.hidden = !overview;
        els.designBook.hidden = !design;
        els.systemMapBook.hidden = !systemMap;
        els.settingsBook.hidden = !settings;
        els.catalogViewButton.classList.toggle("active", state.activeView === "catalog");
        els.buffViewButton.classList.toggle("active", state.activeView === "buffs");
        els.overviewViewButton.classList.toggle("active", overview);
        els.designViewButton.classList.toggle("active", design);
        els.systemMapViewButton.classList.toggle("active", systemMap);
        els.settingsButton.classList.toggle("active", settings);
        if (overview) renderOverview();
        if (design) loadDesignDocs();
        if (systemMap) loadSystemMap();
        if (state.activeView === "catalog" || state.activeView === "buffs") {
            renderFilters();
            updateSummary();
            renderList();
            const candidates = getCatalogEntries();
            const selected = candidates.find(entry => entry.id === state.selectedId) || candidates[0];
            if (selected) selectItem(selected.id);
            else showEmpty();
        }
    }

    // README 的“系统目录”表即 03 页导航真源，新增/排序文档时无需同步第二份网页配置。
    async function loadDesignDocs() {
        if (state.designLoaded) {
            renderDesignDocList();
            if (!state.designDocCache.has(state.selectedDesignPath)) {
                openDesignDoc(state.selectedDesignPath);
            }
            return;
        }

        els.designDocList.innerHTML = `<div class="no-data">正在读取文档目录…</div>`;
        els.designDocContent.innerHTML = `<div class="no-data">正在读取游戏设计 Markdown…</div>`;
        try {
            const markdown = await fetchText(DESIGN_DOC_INDEX_PATH);
            state.designDocCache.set(DESIGN_DOC_INDEX_PATH, markdown);
            state.designDocs = parseDesignDocCatalog(markdown);
            state.designLoaded = true;
            renderDesignDocList();
            await openDesignDoc(state.selectedDesignPath || DESIGN_DOC_INDEX_PATH);
        } catch (error) {
            els.designDocList.innerHTML = `<div class="no-data">文档目录读取失败。</div>`;
            els.designDocContent.innerHTML = `<div class="no-data">${escapeHtml(error.message)}</div>`;
        }
    }

    // 从 README 的系统表提取标题、相对路径与一句话说明，保持 Markdown 的层级排序为唯一权威。
    function parseDesignDocCatalog(markdown) {
        const docs = [{
            title: "总览与开发导航",
            path: DESIGN_DOC_INDEX_PATH,
            summary: "当前系统形势、开发导航与文档维护规则。"
        }];
        const lines = String(markdown || "").split(/\r?\n/);
        let inSystemTable = false;
        for (const line of lines) {
            if (/^##\s+系统目录\s*$/.test(line.trim())) {
                inSystemTable = true;
                continue;
            }
            if (inSystemTable && /^##\s+/.test(line.trim())) break;
            if (!inSystemTable || !line.trim().startsWith("|")) continue;
            const columns = line.split("|").slice(1, -1).map(value => value.trim());
            if (columns.length < 3 || columns[0] === "系统" || /^:?-{3,}/.test(columns[0])) continue;
            const link = columns[1].match(/^\[([^\]]+)\]\(([^)]+)\)$/);
            if (!link) continue;
            docs.push({
                title: columns[0],
                path: resolveDesignDocPath(DESIGN_DOC_INDEX_PATH, link[2]),
                summary: columns[2]
            });
        }
        return docs;
    }

    // 根据筛选条件绘制文档导航；README 条目始终排在第一项。
    function renderDesignDocList() {
        if (!els.designDocList) return;
        const query = String(els.designSearch?.value || "").trim().toLowerCase();
        const docs = state.designDocs.filter(doc =>
            !query || `${doc.title} ${doc.summary} ${doc.path}`.toLowerCase().includes(query));
        if (!docs.length) {
            els.designDocList.innerHTML = `<div class="no-data">没有符合筛选条件的系统文档。</div>`;
            return;
        }
        els.designDocList.innerHTML = "";
        docs.forEach((doc, index) => {
            const button = document.createElement("button");
            button.type = "button";
            button.className = `design-doc-button${doc.path === state.selectedDesignPath ? " active" : ""}`;
            button.dataset.docPath = doc.path;
            button.innerHTML = `<span>${String(index + 1).padStart(2, "0")}</span>` +
                `<strong>${escapeHtml(doc.title)}</strong>` +
                `<small>${escapeHtml(doc.summary)}</small>`;
            button.addEventListener("click", () => openDesignDoc(doc.path));
            els.designDocList.appendChild(button);
        });
    }

    // 读取并渲染单份 Markdown；所有文档链接继续在 03 页内部跳转。
    async function openDesignDoc(path) {
        const normalized = normalizeDesignDocPath(path);
        state.selectedDesignPath = normalized;
        renderDesignDocList();
        els.designDocMeta.textContent = normalized;
        els.designDocContent.innerHTML = `<div class="no-data">正在读取 ${escapeHtml(normalized)}…</div>`;
        try {
            let markdown = state.designDocCache.get(normalized);
            if (markdown === undefined) {
                markdown = await fetchText(normalized);
                state.designDocCache.set(normalized, markdown);
            }
            els.designDocContent.innerHTML = renderDesignMarkdown(markdown, normalized);
            els.designDocContent.querySelectorAll("[data-doc-path]").forEach(link => {
                link.addEventListener("click", () => openDesignDoc(link.dataset.docPath));
            });
            els.designDocContent.scrollTop = 0;
        } catch (error) {
            els.designDocContent.innerHTML = `<div class="no-data">${escapeHtml(error.message)}</div>`;
        }
    }

    // Wiki 只允许设计文档在 docs/ 内部导航，避免 Markdown 相对路径逃出公开白名单。
    function normalizeDesignDocPath(path) {
        const raw = String(path || DESIGN_DOC_INDEX_PATH).replace(/\\/g, "/");
        const stack = [];
        for (const part of raw.split("/")) {
            if (!part || part === ".") continue;
            if (part === "..") stack.pop();
            else stack.push(part);
        }
        const normalized = stack.join("/");
        return normalized.startsWith("docs/") && normalized.toLowerCase().endsWith(".md")
            ? normalized
            : DESIGN_DOC_INDEX_PATH;
    }

    // 以当前 Markdown 所在目录解析相对文档链接。
    function resolveDesignDocPath(currentPath, relativePath) {
        const href = String(relativePath || "").split("#")[0];
        if (!href) return normalizeDesignDocPath(currentPath);
        if (href.startsWith("docs/")) return normalizeDesignDocPath(href);
        const baseParts = String(currentPath || DESIGN_DOC_INDEX_PATH).split("/");
        baseParts.pop();
        return normalizeDesignDocPath(`${baseParts.join("/")}/${href}`);
    }

    // 轻量 Markdown 渲染器覆盖当前设计文档所使用的标题、列表、引用、表格、链接和代码。
    function renderDesignMarkdown(markdown, currentPath) {
        const lines = String(markdown || "").replace(/\r/g, "").split("\n");
        const output = [];
        let index = 0;
        while (index < lines.length) {
            const line = lines[index];
            if (!line.trim()) {
                index++;
                continue;
            }

            const heading = line.match(/^(#{1,4})\s+(.+)$/);
            if (heading) {
                const level = heading[1].length;
                output.push(`<h${level}>${renderDesignInline(heading[2], currentPath)}</h${level}>`);
                index++;
                continue;
            }

            if (/^```/.test(line.trim())) {
                const language = line.trim().slice(3).trim();
                const code = [];
                index++;
                while (index < lines.length && !/^```/.test(lines[index].trim())) code.push(lines[index++]);
                if (index < lines.length) index++;
                output.push(`<pre><code data-language="${escapeAttr(language)}">${escapeHtml(code.join("\n"))}</code></pre>`);
                continue;
            }

            if (line.trim().startsWith(">")) {
                const quote = [];
                while (index < lines.length && lines[index].trim().startsWith(">")) {
                    quote.push(lines[index].trim().replace(/^>\s?/, ""));
                    index++;
                }
                output.push(`<blockquote>${quote.map(value => renderDesignInline(value, currentPath)).join("<br>")}</blockquote>`);
                continue;
            }

            if (isDesignMarkdownTable(lines, index)) {
                const headers = splitDesignTableRow(lines[index]);
                index += 2;
                const rows = [];
                while (index < lines.length && lines[index].trim().startsWith("|")) {
                    rows.push(splitDesignTableRow(lines[index++]));
                }
                output.push(`<div class="markdown-table-wrap"><table><thead><tr>${headers.map(cell =>
                    `<th>${renderDesignInline(cell, currentPath)}</th>`).join("")}</tr></thead><tbody>${rows.map(row =>
                    `<tr>${row.map(cell => `<td>${renderDesignInline(cell, currentPath)}</td>`).join("")}</tr>`).join("")}</tbody></table></div>`);
                continue;
            }

            const unordered = line.match(/^\s*[-*]\s+(.+)$/);
            const ordered = line.match(/^\s*\d+[.)]\s+(.+)$/);
            if (unordered || ordered) {
                const orderedList = Boolean(ordered);
                const items = [];
                const pattern = orderedList ? /^\s*\d+[.)]\s+(.+)$/ : /^\s*[-*]\s+(.+)$/;
                while (index < lines.length) {
                    const match = lines[index].match(pattern);
                    if (!match) break;
                    items.push(match[1]);
                    index++;
                }
                const tag = orderedList ? "ol" : "ul";
                output.push(`<${tag}>${items.map(item => `<li>${renderDesignInline(item, currentPath)}</li>`).join("")}</${tag}>`);
                continue;
            }

            const paragraph = [line.trim()];
            index++;
            while (index < lines.length && lines[index].trim() &&
                   !/^(#{1,4})\s+/.test(lines[index]) &&
                   !/^```/.test(lines[index].trim()) &&
                   !/^>/.test(lines[index].trim()) &&
                   !/^\s*[-*]\s+/.test(lines[index]) &&
                   !/^\s*\d+[.)]\s+/.test(lines[index]) &&
                   !isDesignMarkdownTable(lines, index)) {
                paragraph.push(lines[index].trim());
                index++;
            }
            output.push(`<p>${renderDesignInline(paragraph.join(" "), currentPath)}</p>`);
        }
        return output.join("\n");
    }

    function isDesignMarkdownTable(lines, index) {
        if (index + 1 >= lines.length || !lines[index].trim().startsWith("|")) return false;
        const delimiter = lines[index + 1].trim();
        return delimiter.startsWith("|") && /^\|?\s*:?-{3,}/.test(delimiter);
    }

    function splitDesignTableRow(line) {
        return String(line).trim().replace(/^\|/, "").replace(/\|$/, "").split("|").map(value => value.trim());
    }

    function renderDesignInline(value, currentPath) {
        const tokens = [];
        let text = String(value || "");
        text = text.replace(/`([^`]+)`/g, (_, code) => {
            const token = `@@FWDOC${tokens.length}@@`;
            tokens.push(`<code>${escapeHtml(code)}</code>`);
            return token;
        });
        text = text.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_, label, href) => {
            const token = `@@FWDOC${tokens.length}@@`;
            if (/\.md(?:#.*)?$/i.test(href)) {
                const path = resolveDesignDocPath(currentPath, href);
                tokens.push(`<button type="button" class="markdown-doc-link" data-doc-path="${escapeAttr(path)}">${escapeHtml(label)}</button>`);
            } else {
                tokens.push(`<span class="markdown-link-label">${escapeHtml(label)}</span>`);
            }
            return token;
        });
        text = escapeHtml(text)
            .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
            .replace(/~~([^~]+)~~/g, "<del>$1</del>");
        tokens.forEach((html, tokenIndex) => {
            text = text.replace(`@@FWDOC${tokenIndex}@@`, html);
        });
        return text;
    }

    // 第四分页只负责解释 system-script-map.md；坐标、职责和主连接均由 Markdown 维护。
    async function loadSystemMap() {
        if (state.systemMapLoaded) {
            updateSystemMapEditorHint();
            requestSystemMapFrame();
            return;
        }

        els.systemMapNodes.innerHTML = `<div class="system-map-loading">正在读取系统脚本结构 Markdown…</div>`;
        try {
            const markdown = await fetchText(SYSTEM_MAP_DOC_PATH);
            const parsed = parseSystemMapMarkdown(markdown);
            state.systemMap.width = parsed.width;
            state.systemMap.height = parsed.height;
            state.systemMap.nodes = parsed.nodes;
            state.systemMap.nodeById = parsed.nodeById;
            state.systemMap.regions = parsed.regions;
            renderSystemMap();
            state.systemMapLoaded = true;
            updateSystemMapEditorHint();
            requestAnimationFrame(() => focusSystemMapHome(true));
        } catch (error) {
            state.systemMapLoaded = false;
            els.systemMapNodes.innerHTML = `<div class="system-map-loading error">${escapeHtml(error.message)}</div>`;
        }
    }

    // 解析指定二级标题后的 Markdown 表格，避免为结构图再引入第二份 JSON 配置。
    function parseSystemMapTable(markdown, heading) {
        const lines = String(markdown || "").replace(/\r/g, "").split("\n");
        const headingIndex = lines.findIndex(line => line.trim() === `## ${heading}`);
        if (headingIndex < 0) throw new Error(`系统结构图缺少“${heading}”表。`);

        let index = headingIndex + 1;
        while (index < lines.length && !lines[index].trim().startsWith("|")) index++;
        if (index + 1 >= lines.length || !isDesignMarkdownTable(lines, index)) {
            throw new Error(`系统结构图“${heading}”不是有效 Markdown 表格。`);
        }

        const headers = splitDesignTableRow(lines[index]);
        index += 2;
        const rows = [];
        while (index < lines.length) {
            // 结构图 Markdown 会用空行分隔不同系统分组；空行不应提前截断同一张表的数据。
            while (index < lines.length && !lines[index].trim()) index++;
            if (index >= lines.length || !lines[index].trim().startsWith("|")) break;

            const values = splitDesignTableRow(lines[index++]);
            const row = {};
            headers.forEach((header, columnIndex) => {
                row[header] = values[columnIndex] ?? "";
            });
            rows.push(row);
        }
        return rows;
    }

    function parseSystemMapMarkdown(markdown) {
        const sizeMatch = String(markdown || "").match(/<!--\s*map-size:\s*(\d+)x(\d+)\s*-->/i);
        const width = sizeMatch ? Number(sizeMatch[1]) : 5400;
        const height = sizeMatch ? Number(sizeMatch[2]) : 3100;
        if (!Number.isFinite(width) || !Number.isFinite(height) || width < 1000 || height < 700) {
            throw new Error("系统结构图地图尺寸无效。");
        }

        const numberOf = (value, label) => {
            const number = Number(value);
            if (!Number.isFinite(number)) throw new Error(`${label} 不是有效数字：${value}`);
            return number;
        };

        const regions = parseSystemMapTable(markdown, "区域").map(row => ({
            id: String(row.ID || "").trim(),
            name: String(row["名称"] || "").trim(),
            x: numberOf(row.X, "区域 X"),
            y: numberOf(row.Y, "区域 Y"),
            width: numberOf(row["宽"], "区域宽度"),
            height: numberOf(row["高"], "区域高度"),
            description: String(row["说明"] || "").trim()
        }));
        if (!regions.length || regions.some(region => !region.id || !region.name)) {
            throw new Error("系统结构图区域定义为空或缺少 ID/名称。");
        }

        const nodeById = new Map();
        const nodes = parseSystemMapTable(markdown, "脚本节点").map(row => {
            const node = {
                id: String(row.ID || "").trim(),
                system: String(row["系统"] || "").trim(),
                title: String(row["脚本"] || "").trim(),
                path: String(row["路径"] || "").trim().replace(/\\/g, "/"),
                x: numberOf(row.X, "节点 X"),
                y: numberOf(row.Y, "节点 Y"),
                description: String(row["说明"] || "").trim(),
                mainId: String(row["主连接"] || "").trim(),
                element: null,
                edgeElement: null
            };
            if (!node.id || !node.system || !node.title || !node.path || !node.description) {
                throw new Error(`脚本节点字段不完整：${node.id || node.title || "<未知>"}`);
            }
            if (!node.path.startsWith("Assets/") || !node.path.toLowerCase().endsWith(".cs")) {
                throw new Error(`脚本节点路径必须是 Assets 下的 .cs：${node.path}`);
            }
            if (nodeById.has(node.id)) throw new Error(`系统结构图包含重复节点 ID：${node.id}`);
            node.searchBlob = `${node.system} ${node.title} ${node.path} ${node.description}`.toLowerCase();
            nodeById.set(node.id, node);
            return node;
        });
        if (!nodes.length) throw new Error("系统结构图没有脚本节点。");
        nodes.forEach(node => {
            if (node.mainId && !nodeById.has(node.mainId)) {
                throw new Error(`节点 ${node.id} 的主连接不存在：${node.mainId}`);
            }
            if (node.mainId === node.id) throw new Error(`节点 ${node.id} 不能连接自己。`);
        });
        return { width, height, regions, nodes, nodeById };
    }

    function renderSystemMap() {
        const map = state.systemMap;
        els.systemMapSurface.style.width = `${map.width}px`;
        els.systemMapSurface.style.height = `${map.height}px`;
        els.systemMapRegions.innerHTML = "";
        els.systemMapNodes.innerHTML = "";
        els.systemMapEdges.innerHTML = "";
        els.systemMapEdges.setAttribute("viewBox", `0 0 ${map.width} ${map.height}`);
        els.systemMapEdges.setAttribute("width", String(map.width));
        els.systemMapEdges.setAttribute("height", String(map.height));

        map.regions.forEach(region => {
            const element = document.createElement("section");
            element.className = "system-map-region";
            element.style.left = `${region.x}px`;
            element.style.top = `${region.y}px`;
            element.style.width = `${region.width}px`;
            element.style.height = `${region.height}px`;
            element.innerHTML = `<div class="system-map-region-title"><strong>${escapeHtml(region.name)}</strong>` +
                `<span>${escapeHtml(region.description)}</span></div>`;
            region.element = element;
            els.systemMapRegions.appendChild(element);
        });

        map.nodes.forEach(node => {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "system-map-node";
            button.style.left = `${node.x}px`;
            button.style.top = `${node.y}px`;
            button.dataset.nodeId = node.id;
            button.title = `${node.title}\n${node.path}\n点击在 VS Code 中打开`;
            button.innerHTML = `<span class="system-map-node-system">${escapeHtml(node.system)}</span>` +
                `<strong>${escapeHtml(node.title)}</strong>` +
                `<small>${escapeHtml(node.description)}</small>` +
                `<code>${escapeHtml(node.path)}</code>` +
                `<span class="system-map-node-open">↗ VS Code</span>`;
            button.addEventListener("click", () => openSystemScript(node));
            node.element = button;
            els.systemMapNodes.appendChild(button);
        });

        const svgNamespace = "http://www.w3.org/2000/svg";
        map.nodes.forEach(node => {
            if (!node.mainId) return;
            const target = map.nodeById.get(node.mainId);
            const line = document.createElementNS(svgNamespace, "line");
            line.setAttribute("x1", String(node.x + SYSTEM_MAP_NODE_WIDTH / 2));
            line.setAttribute("y1", String(node.y + SYSTEM_MAP_NODE_HEIGHT / 2));
            line.setAttribute("x2", String(target.x + SYSTEM_MAP_NODE_WIDTH / 2));
            line.setAttribute("y2", String(target.y + SYSTEM_MAP_NODE_HEIGHT / 2));
            line.classList.add("system-map-edge");
            line.dataset.sourceId = node.id;
            line.dataset.targetId = target.id;
            node.edgeElement = line;
            els.systemMapEdges.appendChild(line);
        });

        els.systemMapNodeCount.textContent = String(map.nodes.length);
        requestSystemMapFrame();
    }

    function bindSystemMapEvents() {
        if (!els.systemMapViewport) return;
        els.systemMapSearch.addEventListener("input", () => requestSystemMapFrame());
        els.systemMapSearch.addEventListener("keydown", event => {
            if (event.key !== "Enter") return;
            const query = els.systemMapSearch.value.trim().toLowerCase();
            if (!query) return;
            const match = state.systemMap.nodes.find(node => node.searchBlob.includes(query));
            if (match) focusSystemMapNode(match);
        });
        els.systemMapZoomOut.addEventListener("click", () => zoomSystemMapAtCenter(0.82));
        els.systemMapZoomIn.addEventListener("click", () => zoomSystemMapAtCenter(1.22));
        els.systemMapZoomValue.addEventListener("click", () => setSystemMapZoomAtCenter(1));
        els.systemMapFit.addEventListener("click", fitSystemMap);
        els.systemMapHome.addEventListener("click", () => focusSystemMapHome(false));

        els.systemMapViewport.addEventListener("wheel", event => {
            if (!state.systemMapLoaded) return;
            event.preventDefault();
            const rect = els.systemMapViewport.getBoundingClientRect();
            const factor = Math.exp(-event.deltaY * 0.0013);
            zoomSystemMapAtPoint(factor, event.clientX - rect.left, event.clientY - rect.top);
        }, { passive: false });

        els.systemMapViewport.addEventListener("pointerdown", event => {
            if (event.button !== 0 || event.target.closest(".system-map-node")) return;
            const map = state.systemMap;
            map.dragging = true;
            map.dragPointerId = event.pointerId;
            map.dragStartX = event.clientX;
            map.dragStartY = event.clientY;
            map.dragOriginX = map.panX;
            map.dragOriginY = map.panY;
            els.systemMapViewport.classList.add("dragging");
            els.systemMapViewport.setPointerCapture(event.pointerId);
        });
        els.systemMapViewport.addEventListener("pointermove", event => {
            const map = state.systemMap;
            if (!map.dragging || map.dragPointerId !== event.pointerId) return;
            map.panX = map.dragOriginX + event.clientX - map.dragStartX;
            map.panY = map.dragOriginY + event.clientY - map.dragStartY;
            requestSystemMapFrame();
        });
        const stopDragging = event => {
            const map = state.systemMap;
            if (!map.dragging || (event.pointerId !== undefined && map.dragPointerId !== event.pointerId)) return;
            map.dragging = false;
            map.dragPointerId = null;
            els.systemMapViewport.classList.remove("dragging");
            if (event.pointerId !== undefined && els.systemMapViewport.hasPointerCapture(event.pointerId)) {
                els.systemMapViewport.releasePointerCapture(event.pointerId);
            }
        };
        els.systemMapViewport.addEventListener("pointerup", stopDragging);
        els.systemMapViewport.addEventListener("pointercancel", stopDragging);

        els.systemMapViewport.addEventListener("keydown", event => {
            if (!state.systemMapLoaded) return;
            const panStep = 80;
            if (event.key === "+" || event.key === "=") {
                event.preventDefault();
                zoomSystemMapAtCenter(1.2);
            } else if (event.key === "-" || event.key === "_") {
                event.preventDefault();
                zoomSystemMapAtCenter(0.84);
            } else if (event.key === "0") {
                event.preventDefault();
                fitSystemMap();
            } else if (["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) {
                event.preventDefault();
                state.systemMap.panX += event.key === "ArrowLeft" ? panStep : event.key === "ArrowRight" ? -panStep : 0;
                state.systemMap.panY += event.key === "ArrowUp" ? panStep : event.key === "ArrowDown" ? -panStep : 0;
                requestSystemMapFrame();
            }
        });
        window.addEventListener("resize", () => {
            if (state.systemMapLoaded && state.activeView === "system-map") requestSystemMapFrame();
        });
    }

    function clampSystemMapZoom(value) {
        return Math.min(SYSTEM_MAP_MAX_ZOOM, Math.max(SYSTEM_MAP_MIN_ZOOM, value));
    }

    function zoomSystemMapAtCenter(factor) {
        const rect = els.systemMapViewport.getBoundingClientRect();
        zoomSystemMapAtPoint(factor, rect.width / 2, rect.height / 2);
    }

    function setSystemMapZoomAtCenter(zoom) {
        const map = state.systemMap;
        const rect = els.systemMapViewport.getBoundingClientRect();
        const worldX = (rect.width / 2 - map.panX) / map.zoom;
        const worldY = (rect.height / 2 - map.panY) / map.zoom;
        map.zoom = clampSystemMapZoom(zoom);
        map.panX = rect.width / 2 - worldX * map.zoom;
        map.panY = rect.height / 2 - worldY * map.zoom;
        requestSystemMapFrame();
    }

    function zoomSystemMapAtPoint(factor, viewportX, viewportY) {
        const map = state.systemMap;
        const previousZoom = map.zoom;
        const nextZoom = clampSystemMapZoom(previousZoom * factor);
        if (Math.abs(previousZoom - nextZoom) < 0.0001) return;
        const worldX = (viewportX - map.panX) / previousZoom;
        const worldY = (viewportY - map.panY) / previousZoom;
        map.zoom = nextZoom;
        map.panX = viewportX - worldX * nextZoom;
        map.panY = viewportY - worldY * nextZoom;
        requestSystemMapFrame();
    }

    function fitSystemMap() {
        if (!state.systemMapLoaded) return;
        const map = state.systemMap;
        const rect = els.systemMapViewport.getBoundingClientRect();
        const padding = Math.min(55, Math.max(24, rect.width * 0.035));
        const availableWidth = Math.max(100, rect.width - padding * 2);
        const availableHeight = Math.max(100, rect.height - padding * 2);
        map.zoom = clampSystemMapZoom(Math.min(availableWidth / map.width, availableHeight / map.height));
        map.panX = (rect.width - map.width * map.zoom) / 2;
        map.panY = (rect.height - map.height * map.zoom) / 2;
        requestSystemMapFrame();
    }

    function focusSystemMapHome(initial) {
        if (!state.systemMapLoaded) return;
        const map = state.systemMap;
        const rect = els.systemMapViewport.getBoundingClientRect();
        const core = map.regions.find(region => region.id === "core") || map.regions[0];
        const desiredZoom = clampSystemMapZoom(Math.min(
            (rect.width * 0.9) / Math.max(1, core.width),
            (rect.height * 0.86) / Math.max(1, core.height),
            initial ? 0.72 : 0.9
        ));
        map.zoom = desiredZoom;
        map.panX = rect.width / 2 - (core.x + core.width / 2) * desiredZoom;
        map.panY = rect.height / 2 - (core.y + core.height / 2) * desiredZoom;
        requestSystemMapFrame();
    }

    function focusSystemMapNode(node) {
        if (!node) return;
        const map = state.systemMap;
        const rect = els.systemMapViewport.getBoundingClientRect();
        map.zoom = Math.max(map.zoom, 0.82);
        map.panX = rect.width / 2 - (node.x + SYSTEM_MAP_NODE_WIDTH / 2) * map.zoom;
        map.panY = rect.height / 2 - (node.y + SYSTEM_MAP_NODE_HEIGHT / 2) * map.zoom;
        requestSystemMapFrame();
        node.element?.focus({ preventScroll: true });
    }

    // 所有拖拽/缩放更新合并到一个 RAF，避免 pointermove/wheel 高频触发布局与样式刷新。
    function requestSystemMapFrame() {
        const map = state.systemMap;
        if (map.frameRequest) return;
        map.frameRequest = requestAnimationFrame(() => {
            map.frameRequest = 0;
            els.systemMapSurface.style.transform = `translate3d(${map.panX}px, ${map.panY}px, 0) scale(${map.zoom})`;
            els.systemMapZoomValue.textContent = `${Math.round(map.zoom * 100)}%`;
            updateSystemMapVisibility();
        });
    }

    // 可视区域裁剪只显示附近节点；脚本数继续增长时不会让整张大图持续参与绘制。
    function updateSystemMapVisibility() {
        if (!state.systemMapLoaded && !state.systemMap.nodes.length) return;
        const map = state.systemMap;
        const rect = els.systemMapViewport.getBoundingClientRect();
        const padding = 320;
        const worldLeft = -map.panX / map.zoom - padding;
        const worldTop = -map.panY / map.zoom - padding;
        const worldRight = worldLeft + rect.width / map.zoom + padding * 2;
        const worldBottom = worldTop + rect.height / map.zoom + padding * 2;
        const query = String(els.systemMapSearch.value || "").trim().toLowerCase();
        let visibleCount = 0;

        map.nodes.forEach(node => {
            const matches = !query || node.searchBlob.includes(query);
            const inViewport = node.x + SYSTEM_MAP_NODE_WIDTH >= worldLeft && node.x <= worldRight &&
                node.y + SYSTEM_MAP_NODE_HEIGHT >= worldTop && node.y <= worldBottom;
            const visible = matches && inViewport;
            node.element.hidden = !visible;
            if (visible) visibleCount++;
        });
        map.nodes.forEach(node => {
            if (!node.edgeElement) return;
            const target = map.nodeById.get(node.mainId);
            node.edgeElement.hidden = node.element.hidden || target?.element?.hidden !== false;
        });
        map.regions.forEach(region => {
            const inViewport = region.x + region.width >= worldLeft && region.x <= worldRight &&
                region.y + region.height >= worldTop && region.y <= worldBottom;
            region.element.hidden = !inViewport;
        });
        els.systemMapVisibleCount.textContent = `${visibleCount} / ${map.nodes.length}`;
    }

    async function openSystemScript(node) {
        if (!state.projectRoot) await checkWriteCapability();
        if (!state.projectRoot) {
            setStatus("warning", state.publicReadOnly
                ? "公开只读 Wiki 不暴露本机项目路径，脚本跳转仅在本机 Wiki 中启用。"
                : "当前 Wiki 服务没有提供本机项目路径，无法打开 VS Code。请使用本地启动脚本打开 Wiki。");
            return;
        }
        const relativePath = String(node?.path || "").replace(/\\/g, "/").replace(/^\/+/, "");
        if (!relativePath.startsWith("Assets/") || !relativePath.toLowerCase().endsWith(".cs")) {
            setStatus("error", `拒绝打开非法脚本路径：${relativePath || "<空>"}`);
            return;
        }
        const root = state.projectRoot.replace(/\\/g, "/").replace(/\/+$/, "");
        const fullPath = `${root}/${relativePath}`;
        const vscodeUri = `vscode://file/${encodeURI(fullPath).replace(/#/g, "%23").replace(/\?/g, "%3F")}`;
        window.location.href = vscodeUri;
    }

    function updateSystemMapEditorHint() {
        if (!els.systemMapHint) return;
        if (state.projectRoot) {
            els.systemMapHint.textContent = "拖拽移动 · 滚轮缩放 · 点击节点打开 VS Code";
        } else if (state.publicReadOnly) {
            els.systemMapHint.textContent = "拖拽移动 · 滚轮缩放 · 公开只读模式不启用本机源码跳转";
        } else {
            els.systemMapHint.textContent = "拖拽移动 · 滚轮缩放 · 本地服务就绪后可点击节点打开 VS Code";
        }
    }

    async function fetchText(path) {
        const response = await fetch(path, { cache: "no-store" });
        if (!response.ok) throw new Error(`读取失败：${path}（HTTP ${response.status}）`);
        return response.text();
    }

    // 获取总览页当前筛选后的条目，和左侧档案索引共享全局搜索条件。
    function getOverviewEntries() {
        const query = els.globalSearch.value.trim().toLowerCase();
        const category = els.overviewCategorySelect.value || "全部";
        const packageId = els.overviewPackageSelect.value || "全部";
        const includeAbstract = els.overviewShowAbstract.checked;
        let entries = state.entries.filter(entry => {
            if (entry.kind === "buff") return false;
            if (!includeAbstract && entry.final.abstract === true) return false;
            if (category !== "全部" && entry.category !== category) return false;
            if (packageId !== "全部" && entry.package.id !== packageId) return false;
            if (query && !entry.searchBlob.includes(query)) return false;
            return true;
        });
        entries = entries.slice().sort((a, b) => {
            switch (els.overviewSortSelect.value) {
                case "id": return a.id.localeCompare(b.id, "zh-CN", { sensitivity: "base" });
                case "package": {
                    const packageCompare = a.package.id.localeCompare(b.package.id, "zh-CN", { sensitivity: "base" });
                    return packageCompare || a.displayName.localeCompare(b.displayName, "zh-CN", { numeric: true });
                }
                default: return a.displayName.localeCompare(b.displayName, "zh-CN", { numeric: true });
            }
        });
        return entries;
    }

    // 以 Minecraft Wiki 风格的密集图鉴分组展示大量 Item，点击后进入完整档案。
    function renderOverview() {
        if (!els.overviewGrid) return;
        const entries = getOverviewEntries();
        els.overviewCount.textContent = String(entries.length);
        els.overviewGrid.innerHTML = "";
        if (!entries.length) {
            els.overviewGrid.innerHTML = `<div class="overview-empty">没有符合当前条件的物品。</div>`;
            return;
        }

        const categoryFilter = els.overviewCategorySelect.value || "全部";
        const groups = new Map();
        if (categoryFilter === "全部") {
            for (const category of CATEGORY_ORDER.filter(value => value !== "全部")) groups.set(category, []);
            for (const entry of entries) {
                if (!groups.has(entry.category)) groups.set(entry.category, []);
                groups.get(entry.category).push(entry);
            }
        } else {
            groups.set(categoryFilter, entries);
        }

        const fragment = document.createDocumentFragment();
        for (const [category, groupEntries] of groups) {
            if (!groupEntries.length) continue;
            const section = document.createElement("section");
            section.className = "overview-section";
            const heading = document.createElement("div");
            heading.className = "overview-section-heading";
            heading.innerHTML = `<h3>${escapeHtml(category)}</h3><span>${groupEntries.length} 项</span>`;
            const grid = document.createElement("div");
            grid.className = "overview-card-grid";
            for (const entry of groupEntries) grid.appendChild(createOverviewCard(entry));
            section.append(heading, grid);
            fragment.appendChild(section);
        }
        els.overviewGrid.appendChild(fragment);
    }

    // 创建总览页单个紧凑物品卡片。
    function createOverviewCard(entry) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "overview-item-card";
        button.title = `${entry.displayName}\n${entry.id}\n${entry.package.id}`;
        const icon = document.createElement("span");
        icon.className = "overview-item-icon";
        icon.textContent = fallbackGlyph(entry);
        fillSpriteIcon(icon, entry.final.visual?.spriteAddress, entry.displayName);
        attachItemIconZoom(icon, entry);
        const name = document.createElement("strong");
        name.textContent = entry.displayName;
        const id = document.createElement("small");
        id.textContent = entry.id;
        if (entry.final.abstract) button.classList.add("abstract-card");
        button.append(icon, name, id);
        button.addEventListener("click", event => {
            if (event.target.closest(".zoomable-item-icon")) return;
            setView("catalog");
            selectItem(entry.id);
        });
        return button;
    }

    // 同步右侧书签选中状态。
    function syncCategoryTabs() {
        const category = els.categorySelect.value;
        els.categoryTabs.querySelectorAll(".category-tab").forEach(button => {
            button.classList.toggle("active", button.dataset.category === category);
        });
    }

    // 更新物品总数摘要。
    function updateSummary() {
        const entries = state.entries.filter(entry => (entry.kind === "buff") === (state.activeView === "buffs"));
        const abstractCount = entries.filter(entry => entry.final.abstract === true).length;
        els.countConcrete.textContent = String(entries.length - abstractCount);
        els.countAbstract.textContent = String(abstractCount);
        els.countPackages.textContent = String(new Set(entries.map(entry => entry.package.id)).size);
    }

    // 获取左页索引当前筛选与排序后的条目，供列表和前后浏览共同使用。
    function getCatalogEntries() {
        const query = els.globalSearch.value.trim().toLowerCase();
        const category = els.categorySelect.value || "全部";
        const packageId = els.packageSelect.value || "全部";
        const includeAbstract = els.showAbstract.checked;

        let entries = state.entries.filter(entry => {
            if ((entry.kind === "buff") !== (state.activeView === "buffs")) return false;
            if (!includeAbstract && entry.final.abstract === true) return false;
            if (category !== "全部" && entry.category !== category) return false;
            if (packageId !== "全部" && entry.package.id !== packageId) return false;
            if (query && !entry.searchBlob.includes(query)) return false;
            return true;
        });

        entries = entries.slice().sort((a, b) => {
            switch (els.sortSelect.value) {
                case "id": return a.id.localeCompare(b.id, "zh-CN", { sensitivity: "base" });
                case "package": {
                    const packageCompare = a.package.id.localeCompare(b.package.id, "zh-CN", { sensitivity: "base" });
                    return packageCompare || a.displayName.localeCompare(b.displayName, "zh-CN");
                }
                default: return a.displayName.localeCompare(b.displayName, "zh-CN", { numeric: true });
            }
        });
        return entries;
    }

    // 根据当前筛选器绘制左页索引。
    function renderList() {
        const entries = getCatalogEntries();

        els.resultCount.textContent = `${entries.length} 条结果`;
        els.itemList.innerHTML = "";

        if (!entries.length) {
            els.itemList.innerHTML = `<div class="no-data">没有符合当前条件的条目。</div>`;
            updateDetailNavigation();
            return;
        }

        const fragment = document.createDocumentFragment();
        for (const entry of entries) fragment.appendChild(createItemRow(entry));
        els.itemList.appendChild(fragment);
        updateDetailNavigation();
    }

    // 创建单条物品索引，并异步填充 Sprite 图标。
    function createItemRow(entry) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = `item-row${state.selectedId?.toLowerCase() === entry.id.toLowerCase() ? " selected" : ""}`;
        button.dataset.itemId = entry.id;

        const icon = document.createElement("span");
        icon.className = "item-icon";
        icon.textContent = fallbackGlyph(entry);
        fillSpriteIcon(icon, entry.final.visual?.spriteAddress, entry.displayName);
        attachItemIconZoom(icon, entry);

        const main = document.createElement("span");
        main.className = "item-row-main";
        main.innerHTML = `<span class="item-row-name">${escapeHtml(entry.displayName)}</span>` +
            `<span class="item-row-id">${escapeHtml(entry.id)}</span>`;

        const category = document.createElement("span");
        category.className = "item-row-category";
        category.textContent = entry.final.abstract ? "抽象模板" : entry.category;

        button.append(icon, main, category);
        button.addEventListener("click", () => selectItem(entry.id));
        return button;
    }

    // 选中物品并刷新详情和列表选中态。
    function selectItem(id) {
        const entry = state.entries.find(candidate => candidate.id.toLowerCase() === String(id).toLowerCase());
        if (!entry) return;
        state.selectedId = entry.id;
        history.replaceState(null, "", `#${encodeURIComponent(entry.id)}`);
        els.itemList.querySelectorAll(".item-row").forEach(row => {
            row.classList.toggle("selected", row.dataset.itemId.toLowerCase() === entry.id.toLowerCase());
        });
        renderDetail(entry);
        updateDetailNavigation();
    }

    // 按当前索引筛选和排序顺序切换到相邻物品，并让索引行跟随滚动。
    function navigateRelativeItem(offset) {
        const entries = getCatalogEntries();
        const currentIndex = entries.findIndex(entry => entry.id.toLowerCase() === String(state.selectedId || "").toLowerCase());
        if (currentIndex < 0) return;
        const target = entries[currentIndex + offset];
        if (!target) return;
        selectItem(target.id);
        const selectedRow = Array.from(els.itemList.querySelectorAll(".item-row"))
            .find(row => row.dataset.itemId.toLowerCase() === target.id.toLowerCase());
        selectedRow?.scrollIntoView({ block: "nearest" });
    }

    // 展开 INDEX 并把当前详情对应的条目滚动到列表中央；必要时只解除会隐藏该条目的筛选条件。
    function locateSelectedItemInIndex() {
        const selectedId = String(state.selectedId || "");
        const entry = state.entries.find(candidate => candidate.id.toLowerCase() === selectedId.toLowerCase());
        if (!entry) return;

        setIndexCollapsed(false, true);

        let needsRender = false;
        if (els.categorySelect.value !== "全部" && els.categorySelect.value !== entry.category) {
            els.categorySelect.value = entry.category;
            needsRender = true;
        }
        if (els.packageSelect.value !== "全部" && els.packageSelect.value !== entry.package.id) {
            els.packageSelect.value = entry.package.id;
            needsRender = true;
        }
        if (entry.final.abstract === true && !els.showAbstract.checked) {
            els.showAbstract.checked = true;
            needsRender = true;
        }
        const query = els.globalSearch.value.trim().toLowerCase();
        if (query && !entry.searchBlob.includes(query)) {
            els.globalSearch.value = "";
            needsRender = true;
            renderOverview();
        }
        if (needsRender) {
            syncCategoryTabs();
            renderList();
        }

        const row = Array.from(els.itemList.querySelectorAll(".item-row"))
            .find(candidate => candidate.dataset.itemId.toLowerCase() === entry.id.toLowerCase());
        if (!row) return;

        requestAnimationFrame(() => {
            const listRect = els.itemList.getBoundingClientRect();
            const rowRect = row.getBoundingClientRect();
            const centeredTop = els.itemList.scrollTop + rowRect.top - listRect.top - (listRect.height - rowRect.height) / 2;
            const maxScrollTop = Math.max(0, els.itemList.scrollHeight - els.itemList.clientHeight);
            els.itemList.scrollTo({
                top: Math.max(0, Math.min(centeredTop, maxScrollTop)),
                behavior: "smooth"
            });

            row.classList.remove("index-located");
            void row.offsetWidth;
            row.classList.add("index-located");
            window.setTimeout(() => row.classList.remove("index-located"), 900);
        });
    }

    // 同步详情页前后箭头、当前位置和边界禁用状态。
    function updateDetailNavigation() {
        const entries = getCatalogEntries();
        const currentIndex = entries.findIndex(entry => entry.id.toLowerCase() === String(state.selectedId || "").toLowerCase());
        const previous = currentIndex > 0 ? entries[currentIndex - 1] : null;
        const next = currentIndex >= 0 && currentIndex < entries.length - 1 ? entries[currentIndex + 1] : null;

        els.previousItemButton.disabled = !previous;
        els.nextItemButton.disabled = !next;
        els.previousItemButton.title = previous ? `上一个：${previous.displayName}` : "已经是当前结果的第一个物品";
        els.nextItemButton.title = next ? `下一个：${next.displayName}` : "已经是当前结果的最后一个物品";
        els.detailPosition.textContent = currentIndex >= 0
            ? `${currentIndex + 1} / ${entries.length}`
            : (entries.length ? `— / ${entries.length}` : "0 / 0");
    }

    // URL Hash 优先恢复上次条目，否则选择当前第一个可用物品。
    function restoreOrSelectFirst(preferredId = null) {
        const hashId = decodeURIComponent(location.hash.replace(/^#/, ""));
        const requestedId = String(preferredId || hashId || "").toLowerCase();
        const preferred = state.entries.find(entry => entry.id.toLowerCase() === requestedId);
        if (preferred?.kind === "buff" && state.activeView === "catalog") setView("buffs");
        const firstConcrete = getCatalogEntries().find(entry => entry.final.abstract !== true);
        if (preferred || firstConcrete) selectItem((preferred || firstConcrete).id);
        else showEmpty();
    }

    // 绘制右页完整开发者详情。
    function renderDetail(entry) {
        els.detailContent.innerHTML = "";
        if (entry.kind === "buff") {
            renderBuffDetail(entry);
            return;
        }

        const hero = document.createElement("section");
        hero.className = "detail-hero";
        const icon = document.createElement("div");
        icon.className = "detail-icon";
        icon.textContent = fallbackGlyph(entry);
        fillSpriteIcon(icon, entry.final.visual?.spriteAddress, entry.displayName);
        attachItemIconZoom(icon, entry);

        const title = document.createElement("div");
        title.className = "detail-title";
        title.innerHTML = `<h2>${escapeHtml(entry.displayName)}</h2>` +
            `<div class="detail-id">${escapeHtml(entry.id)}</div>` +
            `<div class="detail-modified">最近修改：${escapeHtml(formatModifiedAt(entry.modifiedAt))}</div>` +
            `<div class="badge-row">` +
            `<span class="badge">${escapeHtml(entry.category)}</span>` +
            `<span class="badge blue">${escapeHtml(entry.package.id)}</span>` +
            (entry.final.abstract ? `<span class="badge red">ABSTRACT</span>` : `<span class="badge green">RUNTIME ITEM</span>`) +
            `</div>`;
        hero.append(icon, title);
        els.detailContent.appendChild(hero);

        const titleHeading = title.querySelector("h2");
        attachInlineEditor(titleHeading, entry, {
            sourceType: "root",
            path: "gameName",
            value: entry.final.gameName ?? entry.displayName,
            label: "名称"
        });

        const description = document.createElement("div");
        description.className = "description-card";
        description.textContent = entry.final.description || "当前定义没有填写说明。";
        attachInlineEditor(description, entry, {
            sourceType: "root",
            path: "description",
            value: entry.final.description ?? "",
            label: "说明",
            multiline: true
        });
        els.detailContent.appendChild(description);

        appendSectionTitle("玩法机制");
        const mechanicsCard = ItemMechanics.render(entry, state.mechanicsContext, document);
        const mechanicsText = ItemMechanics.describe(entry, state.mechanicsContext).join("\n");
        const mechanicsOverride = typeof entry.final.wiki?.mechanics === "string" && entry.final.wiki.mechanics.trim()
            ? entry.final.wiki.mechanics
            : mechanicsText;
        attachInlineEditor(mechanicsCard, entry, {
            sourceType: "root",
            path: "wiki.mechanics",
            value: mechanicsOverride,
            label: "玩法机制",
            multiline: true
        });
        els.detailContent.appendChild(mechanicsCard);

        appendSectionTitle("基础数值");
        els.detailContent.appendChild(renderStatGrid(buildBaseStats(entry.final), entry));

        if (entry.final.lootTableId) {
            appendSectionTitle("战利品预览");
            els.detailContent.appendChild(renderLootPreview(entry.final.lootTableId));
        }

        if (entry.final.health) {
            appendSectionTitle("生命与防御");
            els.detailContent.appendChild(renderStatGrid(buildHealthStats(entry.final.health), entry));
        }

        appendSectionTitle("继承关系");
        els.detailContent.appendChild(renderInheritance(entry));

        if (Array.isArray(entry.final.tags) && entry.final.tags.length) {
            appendSectionTitle("标签");
            const tags = document.createElement("div");
            tags.className = "tag-list";
            for (const tag of entry.final.tags) {
                const chip = document.createElement("span");
                chip.className = "tag-chip";
                chip.textContent = tag;
                tags.appendChild(chip);
            }
            els.detailContent.appendChild(tags);
        }

        appendSectionTitle(`模块 · ${Object.keys(entry.final.modules || {}).length}`);
        els.detailContent.appendChild(renderModules(entry));

        appendSectionTitle("开发者数据");
        els.detailContent.appendChild(renderDeveloperPanels(entry));

        els.detailContent.scrollTop = 0;
    }

    // 向详情页添加带横线的章节标题。
    function appendSectionTitle(text) {
        const heading = document.createElement("h3");
        heading.className = "section-title";
        heading.textContent = text;
        els.detailContent.appendChild(heading);
    }

    // 复用 Item 的数值卡片、内联编辑和保存反馈；说明始终根据当前 BUFF JSON 生成。
    function renderBuffDetail(entry) {
        const heading = document.createElement("h2");
        heading.textContent = entry.displayName;
        attachInlineEditor(heading, entry, { sourceType: "root", path: "displayName",
            value: entry.final.displayName || entry.final.id, label: "名称" });
        const meta = document.createElement("p");
        meta.className = "detail-id";
        meta.textContent = `${entry.final.id} · ${entry.package.path}`;
        els.detailContent.append(heading, meta);
        const description = document.createElement("div");
        description.className = "description-card";
        description.textContent = entry.final.description || "暂无说明，双击填写。";
        attachInlineEditor(description, entry, { sourceType: "root", path: "description",
            value: entry.final.description || "", label: "说明", multiline: true });
        els.detailContent.appendChild(description);
        appendSectionTitle("当前机制");
        const rules = document.createElement("div");
        rules.className = "description-card";
        for (const line of BuffWiki.describe(entry.final)) {
            const paragraph = document.createElement("p");
            paragraph.textContent = line;
            rules.appendChild(paragraph);
        }
        els.detailContent.appendChild(rules);
        appendSectionTitle("基础数值 · 双击编辑");
        els.detailContent.appendChild(renderStatGrid(BuffWiki.stats(entry.final), entry));
        (entry.final.effects || []).forEach((effect, index) => {
            appendSectionTitle(`效果 ${index + 1} · ${BuffWiki.effectNames[effect.typeId] || effect.typeId}`);
            els.detailContent.appendChild(renderStatGrid(BuffWiki.effectStats(effect, index), entry));
        });
        appendSectionTitle("效果集合 · 双击编辑 JSON");
        const effects = jsonPre(entry.final.effects || []);
        attachInlineEditor(effects, entry, { sourceType: "root", path: "effects", value: entry.final.effects || [], label: "效果集合" });
        els.detailContent.appendChild(effects);
        const note = document.createElement("p");
        note.textContent = "保存写回游戏实际 BUFF 分包，并保留备份。运行中的游戏需重新加载资源或重启后使用新数值；null 表示永久。公开分享模式只读。";
        els.detailContent.appendChild(note);
        els.detailContent.scrollTop = 0;
    }

    // 提取物品常用基础字段。
    function buildBaseStats(item) {
        return [
            ["运行时外壳", item.shellPrefab, { sourceType: "root", path: "shellPrefab" }],
            ["当前耐久", item.durability, { sourceType: "root", path: "durability" }],
            ["最大耐久", item.maxDurability, { sourceType: "root", path: "maxDurability" }],
            ["初始数量", item.amount, { sourceType: "root", path: "amount" }],
            ["单个体积", item.volume, { sourceType: "root", path: "volume" }],
            ["允许拾取", item.canBePickedUp, { sourceType: "root", path: "canBePickedUp" }],
            ["Sprite", item.visual?.spriteAddress, { sourceType: "root", path: "visual.spriteAddress" }]
        ].filter(([, value]) => value !== undefined && value !== null && value !== "");
    }

    // 在物品外页显示战利品表摘要；点击后打开完整表内容。
    function renderLootPreview(tableId) {
        const table = state.lootTables.get(String(tableId).toLowerCase());
        const card = document.createElement("button");
        card.type = "button";
        card.className = "loot-preview-card";
        if (!table) {
            card.classList.add("missing");
            card.innerHTML = `<div class="loot-preview-head"><strong>${escapeHtml(tableId)}</strong><span>找不到战利品表</span></div>`;
            return card;
        }

        const entries = Array.isArray(table.entries) ? table.entries : [];
        const header = document.createElement("div");
        header.className = "loot-preview-head";
        header.innerHTML = `<div><small>LOOT TABLE</small><strong>${escapeHtml(table.id)}</strong></div>` +
            `<span>${entries.length} 种掉落 · 查看详细 →</span>`;
        const items = document.createElement("div");
        items.className = "loot-preview-items";
        for (const loot of entries.slice(0, 6)) {
            const target = findItemEntry(loot.itemId);
            const chip = document.createElement("span");
            chip.className = "loot-preview-item";
            const icon = document.createElement("span");
            icon.className = "loot-mini-icon";
            if (target) {
                icon.textContent = fallbackGlyph(target);
                fillSpriteIcon(icon, target.final.visual?.spriteAddress, target.displayName);
                attachItemIconZoom(icon, target);
            } else {
                icon.textContent = "?";
            }
            const text = document.createElement("span");
            text.innerHTML = `<strong>${escapeHtml(target?.displayName || loot.itemId || "未知物品")}</strong>` +
                `<small>${escapeHtml(formatLootAmount(loot))} · ${escapeHtml(formatProbability(loot.probability))}</small>`;
            chip.append(icon, text);
            items.appendChild(chip);
        }
        if (entries.length > 6) {
            const more = document.createElement("span");
            more.className = "loot-preview-more";
            more.textContent = `+${entries.length - 6}`;
            items.appendChild(more);
        }
        card.append(header, items);
        card.addEventListener("click", event => {
            if (event.target.closest(".zoomable-item-icon")) return;
            openLootModal(table.id);
        });
        return card;
    }

    // 打开战利品表详细弹窗，每个掉落物都可跳转到对应 Item 档案。
    function openLootModal(tableId) {
        const table = state.lootTables.get(String(tableId).toLowerCase());
        els.lootModalTitle.textContent = table?.id || tableId;
        els.lootModalBody.innerHTML = "";
        if (!table) {
            els.lootModalBody.innerHTML = `<div class="no-data">找不到战利品表：${escapeHtml(tableId)}</div>`;
            els.lootModal.hidden = false;
            return;
        }

        const summary = document.createElement("div");
        summary.className = "loot-detail-summary";
        summary.innerHTML = `<strong>${table.entries?.length || 0}</strong><span>条掉落定义</span>` +
            `<small>数据源：GameConfig/LootTables/loot-tables.json</small>`;
        els.lootModalBody.appendChild(summary);

        const list = document.createElement("div");
        list.className = "loot-detail-list";
        for (const loot of table.entries || []) {
            const target = findItemEntry(loot.itemId);
            const row = document.createElement("button");
            row.type = "button";
            row.className = "loot-detail-row";
            if (!target) row.disabled = true;
            const icon = document.createElement("span");
            icon.className = "loot-detail-icon";
            if (target) {
                icon.textContent = fallbackGlyph(target);
                fillSpriteIcon(icon, target.final.visual?.spriteAddress, target.displayName);
                attachItemIconZoom(icon, target);
            } else {
                icon.textContent = "?";
            }
            const identity = document.createElement("span");
            identity.className = "loot-detail-identity";
            identity.innerHTML = `<strong>${escapeHtml(target?.displayName || loot.itemId || "未知物品")}</strong>` +
                `<small>${escapeHtml(loot.itemId || "—")}</small>`;
            const values = document.createElement("span");
            values.className = "loot-detail-values";
            values.innerHTML = `<span><small>数量</small><strong>${escapeHtml(formatLootAmount(loot))}</strong></span>` +
                `<span><small>概率</small><strong>${escapeHtml(formatProbability(loot.probability))}</strong></span>`;
            const jump = document.createElement("span");
            jump.className = "loot-jump";
            jump.textContent = target ? "打开物品 →" : "物品不存在";
            row.append(icon, identity, values, jump);
            if (target) {
                row.addEventListener("click", event => {
                    if (event.target.closest(".zoomable-item-icon")) return;
                    closeLootModal();
                    setView("catalog");
                    selectItem(target.id);
                });
            }
            list.appendChild(row);
        }
        els.lootModalBody.appendChild(list);

        const raw = document.createElement("details");
        raw.className = "loot-raw-panel";
        raw.innerHTML = `<summary>查看战利品表原始 JSON</summary>`;
        raw.appendChild(jsonPre(table));
        els.lootModalBody.appendChild(raw);
        els.lootModal.hidden = false;
    }

    // 关闭战利品详情弹窗。
    function closeLootModal() {
        els.lootModal.hidden = true;
    }

    // 格式化战利品最小/最大数量。
    function formatLootAmount(loot) {
        const min = Number(loot?.minAmount ?? 0);
        const max = Number(loot?.maxAmount ?? min);
        return min === max ? String(min) : `${min}–${max}`;
    }

    // 将 0..1 概率转换成开发者更容易扫读的百分比。
    function formatProbability(value) {
        const probability = Number(value);
        if (!Number.isFinite(probability)) return "—";
        const percent = probability * 100;
        return `${Number.isInteger(percent) ? percent : percent.toFixed(2).replace(/0+$/, "").replace(/\.$/, "")}%`;
    }

    // 将 Wiki 元数据中的 UTC 时间显示为开发者本机时间；未记录时明确提示。
    function formatModifiedAt(value) {
        if (!value) return "尚未由 Wiki 记录";
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return String(value);
        return date.toLocaleString("zh-CN", { hour12: false });
    }

    // 按稳定 ID 查找 Item Wiki 条目。
    function findItemEntry(id) {
        const key = String(id || "").toLowerCase();
        return state.entries.find(entry => entry.id.toLowerCase() === key) || null;
    }

    // 提取 Health 声明中的生命与四类防御。
    function buildHealthStats(health) {
        const defense = health.defense || {};
        return [
            ["可受伤", health.hasHp, { sourceType: "health", path: "hasHp" }],
            ["生命值", health.hp, { sourceType: "health", path: "hp" }],
            ["最大生命值", health.maxHp, { sourceType: "health", path: "maxHp" }],
            ["切割防御", defense.cutting, { sourceType: "health", path: "defense.cutting" }],
            ["穿刺防御", defense.piercing, { sourceType: "health", path: "defense.piercing" }],
            ["劈砍防御", defense.chopping, { sourceType: "health", path: "defense.chopping" }],
            ["钝击防御", defense.blunt, { sourceType: "health", path: "defense.blunt" }]
        ].filter(([, value]) => value !== undefined && value !== null);
    }

    // 把少量关键字段做成快速扫读卡片。
    function renderStatGrid(stats, entry = null) {
        const grid = document.createElement("div");
        grid.className = "stat-grid";
        if (!stats.length) {
            grid.innerHTML = `<div class="no-data">当前物品没有这一组数值。</div>`;
            return grid;
        }
        for (const [label, value, editTarget] of stats) {
            const card = document.createElement("div");
            card.className = "stat-card";
            const labelElement = document.createElement("span");
            labelElement.textContent = label;
            const valueRow = document.createElement("div");
            valueRow.className = "stat-card-value-row";
            const valueElement = document.createElement("strong");
            valueElement.textContent = formatValue(value);
            valueRow.appendChild(valueElement);
            card.append(labelElement, valueRow);
            if (entry && editTarget) {
                attachInlineEditor(card, entry, { ...editTarget, value, label });
            }
            if (entry && editTarget?.path === "visual.spriteAddress") {
                appendAtlasQuickView(valueRow, entry, value);
            }
            grid.appendChild(card);
        }
        return grid;
    }

    // 图集子 Sprite 在地址旁提供来源图集快捷入口，方便开发者核对素材位置与整体画风。
    function appendAtlasQuickView(valueRow, entry, spriteAddress) {
        const source = parseUnitySpriteAddress(spriteAddress);
        if (!source?.subObjectName) return;

        const button = document.createElement("button");
        button.type = "button";
        button.className = "atlas-quick-button";
        button.textContent = "↗ 查看图集";
        button.title = `打开来源图集：${getLikelyAtlasAssetPath(source.assetPath)}`;
        button.addEventListener("click", event => {
            event.preventDefault();
            event.stopPropagation();
            openAtlasQuickView(entry, spriteAddress);
        });
        button.addEventListener("dblclick", event => event.stopPropagation());
        valueRow.appendChild(button);
    }

    // 打开来源图集；切片文件按“图集名_序号.png”导出时优先回溯到同目录原图集。
    async function openAtlasQuickView(entry, spriteAddress) {
        const source = parseUnitySpriteAddress(spriteAddress);
        if (!source?.subObjectName) return;

        const candidate = getLikelyAtlasAssetPath(source.assetPath);
        let atlasAddress = source.assetPath;
        if (candidate !== source.assetPath) {
            try {
                const atlas = await loadSprite(candidate);
                if (atlas) atlasAddress = candidate;
            } catch {
                // 没有对应原图集时退回当前 Sprite 图片，避免快捷入口直接失效。
            }
        }

        openImageZoom(entry, {
            address: atlasAddress,
            title: `${entry.displayName} · 来源图集`,
            context: `子 Sprite：${source.subObjectName}`
        });
    }

    // 识别项目中常见的“图集名_序号.png”切片命名，推导同目录原始图集路径。
    function getLikelyAtlasAssetPath(assetPath) {
        return String(assetPath || "").replace(/_\d+(?=\.(?:png|jpg|jpeg|webp)$)/i, "");
    }

    // 绘制 source.parent 链，最终条目放在末尾。
    function renderInheritance(entry) {
        const wrap = document.createElement("div");
        wrap.className = "inherit-chain";
        const chain = [];
        const seen = new Set();
        let current = entry.source;
        while (current) {
            const id = String(current.id || "");
            if (!id || seen.has(id.toLowerCase())) break;
            seen.add(id.toLowerCase());
            chain.unshift(id);
            const parentId = String(current.parent || "").trim();
            current = parentId ? (entry.kind === "actor" ? state.actors.sources : state.sourceById).get(parentId.toLowerCase()) : null;
        }

        chain.forEach((id, index) => {
            if (index > 0) {
                const arrow = document.createElement("span");
                arrow.className = "inherit-arrow";
                arrow.textContent = "→";
                wrap.appendChild(arrow);
            }
            const node = document.createElement("span");
            node.className = "inherit-node";
            node.textContent = id;
            wrap.appendChild(node);
        });
        if (!chain.length) wrap.innerHTML = `<span class="no-data">无继承信息</span>`;
        return wrap;
    }

    // 绘制全部 Module 参数；中文名称与字段释义来自 Wiki 模块术语库。
    function renderModules(entry) {
        const list = document.createElement("div");
        list.className = "module-list";
        const modules = entry.final.modules || {};
        const entries = Object.entries(modules);
        if (!entries.length) {
            list.innerHTML = `<div class="no-data">该物品没有组合模块。</div>`;
            return list;
        }

        for (const [stableName, module] of entries) {
            const glossary = getModuleGlossary(stableName);
            const fragment = els.moduleTemplate.content.cloneNode(true);
            const name = fragment.querySelector(".module-name");
            name.innerHTML = `<strong>${escapeHtml(glossary.name)}</strong><small>${escapeHtml(stableName)}</small>`;
            fragment.querySelector(".module-meta").textContent = [module?.prefab, module?.id]
                .filter(Boolean).join(" · ") || "无 Prefab/ID";

            const body = fragment.querySelector(".module-body");
            const description = document.createElement("p");
            description.className = "module-description";
            description.textContent = glossary.description;
            body.appendChild(description);

            const identity = [
                ["模块 Prefab", module?.prefab],
                ["玩法 ID", module?.id],
                ["启用", module?.enabled]
            ].filter(([, value]) => value !== undefined && value !== null && value !== "");
            if (identity.length) {
                const identityGrid = renderStatGrid(identity);
                identityGrid.classList.add("module-identity-grid");
                body.appendChild(identityGrid);
            }

            if (isPlainObject(module?.parameters) && Object.keys(module.parameters).length) {
                body.appendChild(miniHeading("参数 · PARAMETERS"));
                body.appendChild(renderModuleFieldGrid(module.parameters, entry, stableName, "parameters"));
            }
            if (isPlainObject(module?.data) && Object.keys(module.data).length) {
                body.appendChild(miniHeading("数据 · DATA"));
                body.appendChild(renderModuleFieldGrid(module.data, entry, stableName, "data"));
            }
            list.appendChild(fragment);
        }
        return list;
    }

    // 查询模块术语库；未知 MOD 仍完整显示原始模块数据。
    function getModuleGlossary(stableName) {
        const record = state.moduleGlossary?.modules?.[stableName];
        return {
            name: record?.name || stableName,
            description: record?.description || "未在 Wiki 模块术语库登记；以下仍完整展示原始数据。"
        };
    }

    // 把模块嵌套参数绘制成中文优先、原始字段名辅助的可扫读卡片。
    function renderModuleFieldGrid(object, entry, stableName, sectionName) {
        const rows = [];
        flattenObject(object, "", rows);
        const grid = document.createElement("div");
        grid.className = "module-field-grid";
        for (const [path, value] of rows) {
            const card = document.createElement("div");
            card.className = "module-field-card";
            const formatted = formatValue(value);
            if (formatted.length > 80) card.classList.add("wide");
            const label = document.createElement("div");
            label.className = "module-field-label";
            label.innerHTML = `<strong>${escapeHtml(humanizePath(path))}</strong><small>${escapeHtml(path)}</small>`;
            const data = document.createElement("div");
            data.className = "module-field-value";
            data.textContent = formatted;
            card.append(label, data);
            attachModuleFieldTooltip(card, stableName, sectionName, path, value);
            attachInlineEditor(card, entry, {
                sourceType: "module",
                sourceName: stableName,
                sectionName,
                path,
                value,
                label: humanizePath(path)
            });
            grid.appendChild(card);
        }
        return grid;
    }

    // 给模块参数绑定悬浮说明；说明数据独立维护在 module-glossary.json，避免把业务语义硬编码进页面逻辑。
    function attachModuleFieldTooltip(host, stableName, sectionName, path, value) {
        if (!host) return;
        const help = resolveModuleFieldHelp(stableName, sectionName, path);
        host.classList.add("has-field-help");
        host.addEventListener("mouseenter", () => showModuleFieldTooltip(host, help, stableName, sectionName, path, value));
        host.addEventListener("mouseleave", () => hideModuleFieldTooltip(host));
        host.addEventListener("focus", () => showModuleFieldTooltip(host, help, stableName, sectionName, path, value));
        host.addEventListener("blur", () => hideModuleFieldTooltip(host));
    }

    // 按“模块+分区+完整路径 → 完整路径 → 叶字段”查找说明，允许同名字段在不同模块拥有不同语义。
    function resolveModuleFieldHelp(stableName, sectionName, path) {
        const dictionary = state.moduleGlossary?.fieldHelp || {};
        const leaf = String(path).split(".").pop();
        const candidates = [
            `${stableName}.${sectionName}.${path}`,
            `${stableName}.${path}`,
            `${sectionName}.${path}`,
            path,
            leaf
        ];
        let record = null;
        for (const key of candidates) {
            if (Object.prototype.hasOwnProperty.call(dictionary, key)) {
                record = dictionary[key];
                break;
            }
        }

        if (typeof record === "string") {
            return { description: record };
        }
        if (isPlainObject(record)) {
            return record;
        }
        return {
            description: `用于配置“${humanizePath(path)}”。该字段尚未登记更细的运行时说明，可通过原始字段路径继续定位实现。`
        };
    }

    // 显示固定定位的参数说明浮层，避免被书页滚动容器裁切。
    function showModuleFieldTooltip(host, help, stableName, sectionName, path, value) {
        if (!host || host.classList.contains("inline-editing")) return;
        const tooltip = ensureModuleFieldTooltip();
        const qualifiedPath = `${stableName}.${sectionName}.${path}`;
        const meta = [
            help?.unit ? `<span><b>单位</b>${escapeHtml(help.unit)}</span>` : "",
            help?.range ? `<span><b>范围</b>${escapeHtml(help.range)}</span>` : "",
            `<span><b>类型</b>${escapeHtml(describeValueType(value))}</span>`
        ].filter(Boolean).join("");

        tooltip.innerHTML = `
            <div class="field-tooltip-kicker">PARAMETER GUIDE</div>
            <div class="field-tooltip-title">${escapeHtml(humanizePath(path))}</div>
            <div class="field-tooltip-path">${escapeHtml(qualifiedPath)}</div>
            <div class="field-tooltip-description">${escapeHtml(help?.description || "暂无说明")}</div>
            ${help?.effect ? `<div class="field-tooltip-effect"><b>影响</b>${escapeHtml(help.effect)}</div>` : ""}
            <div class="field-tooltip-meta">${meta}</div>
            <div class="field-tooltip-current"><b>当前值</b><code>${escapeHtml(formatValue(value))}</code></div>
            <div class="field-tooltip-edit-hint">双击该词条可直接编辑并自动保存</div>
        `;
        tooltip.hidden = false;
        tooltip.dataset.hostId = qualifiedPath;
        tooltip._fieldTooltipHost = host;
        positionModuleFieldTooltip(host, tooltip);
    }

    // 懒创建全局参数说明浮层。
    function ensureModuleFieldTooltip() {
        let tooltip = document.getElementById("moduleFieldTooltip");
        if (tooltip) return tooltip;
        tooltip = document.createElement("div");
        tooltip.id = "moduleFieldTooltip";
        tooltip.className = "field-tooltip";
        tooltip.setAttribute("role", "tooltip");
        tooltip.hidden = true;
        document.body.appendChild(tooltip);
        return tooltip;
    }

    // 把浮层限制在视口内，优先显示在词条上方，空间不足时自动翻到下方。
    function positionModuleFieldTooltip(host, tooltip) {
        const margin = 10;
        const viewportPadding = 10;
        const hostRect = host.getBoundingClientRect();
        const tooltipRect = tooltip.getBoundingClientRect();
        let left = hostRect.left + (hostRect.width - tooltipRect.width) / 2;
        left = Math.max(viewportPadding, Math.min(left, window.innerWidth - tooltipRect.width - viewportPadding));

        let top = hostRect.top - tooltipRect.height - margin;
        if (top < viewportPadding) top = hostRect.bottom + margin;
        if (top + tooltipRect.height > window.innerHeight - viewportPadding) {
            top = Math.max(viewportPadding, window.innerHeight - tooltipRect.height - viewportPadding);
        }

        tooltip.style.left = `${Math.round(left)}px`;
        tooltip.style.top = `${Math.round(top)}px`;
    }

    // 隐藏当前参数说明；传入 host 时只关闭属于该词条的浮层，避免焦点切换误关新说明。
    function hideModuleFieldTooltip(host = null) {
        const tooltip = document.getElementById("moduleFieldTooltip");
        if (!tooltip || tooltip.hidden) return;
        if (host && tooltip._fieldTooltipHost !== host) return;
        tooltip.hidden = true;
        tooltip.dataset.hostId = "";
        tooltip._fieldTooltipHost = null;
    }

    // 把 JSON 值类型翻译成开发者可直接理解的类型提示。
    function describeValueType(value) {
        if (value === null) return "null";
        if (Array.isArray(value)) return "数组";
        if (isPlainObject(value)) return "对象";
        if (typeof value === "boolean") return "布尔开关";
        if (typeof value === "number") return "数值";
        if (typeof value === "string") return "文本";
        return typeof value;
    }

    // 把当前词条变成可直接双击编辑的字段；编辑完成后立即写回权威 JSON。
    function attachInlineEditor(host, entry, target) {
        if (!host || !entry || !target) return;
        if (entry.kind === "actor" || state.publicReadOnly) return;
        host.classList.add("inline-editable");
        host.tabIndex = 0;
        host.setAttribute("role", "button");
        host.setAttribute("aria-label", `${target.label || target.path}，双击编辑并自动保存`);
        if (!host.classList.contains("has-field-help")) {
            host.title = "双击直接编辑并自动保存到 JSON";
        }
        host.addEventListener("dblclick", event => {
            event.preventDefault();
            event.stopPropagation();
            beginInlineEdit(host, entry, target);
        });
        host.addEventListener("keydown", event => {
            if (event.key !== "F2") return;
            event.preventDefault();
            beginInlineEdit(host, entry, target);
        });
    }

    // 进入内联编辑态；Enter/失焦保存，Esc 取消，复杂 JSON 使用 Ctrl+Enter 保存。
    async function beginInlineEdit(host, entry, target) {
        if (entry.kind === "actor") return;
        if (host.classList.contains("inline-editing")) return;
        hideModuleFieldTooltip();
        if (!state.writable) {
            await checkWriteCapability();
            if (!state.writable) {
                setStatus("error", "当前 Wiki 没有写入能力，请使用“打开物品Wiki.cmd”启动本地服务。");
                return;
            }
        }
        if (entry.kind === "buff" && !state.buffWritable) {
            await checkWriteCapability();
            if (!state.buffWritable) {
                setStatus("error", "当前服务尚未启用 BUFF 保存，请重启“打开物品Wiki.cmd”后重试。");
                return;
            }
        }

        const currentEntry = findItemEntry(entry.id) || entry;
        const valueHost = getInlineValueHost(host);
        const originalNodes = Array.from(valueHost.childNodes).map(node => node.cloneNode(true));
        const originalValue = deepClone(target.value);
        const editor = createInlineEditorControl(originalValue, target);
        const hint = document.createElement("small");
        hint.className = "inline-edit-hint";
        hint.textContent = editor.tagName === "TEXTAREA" ? "Ctrl+Enter 保存 · Esc 取消" : "Enter/失焦保存 · Esc 取消";

        host.classList.add("inline-editing");
        valueHost.textContent = "";
        valueHost.append(editor, hint);
        editor.focus();
        if (editor.select) editor.select();

        let settled = false;
        const restore = () => {
            host.classList.remove("inline-editing", "inline-saving", "inline-error");
            valueHost.replaceChildren(...originalNodes.map(node => node.cloneNode(true)));
        };
        const cancel = () => {
            if (settled) return;
            settled = true;
            restore();
        };
        const commit = async () => {
            if (settled) return;
            let nextValue;
            try {
                nextValue = target.nullableNumber
                    ? parseNullableNumber(editor.value, target.label || target.path)
                    : parseInlineEditorValue(editor, originalValue, target.label || target.path);
            } catch (error) {
                host.classList.add("inline-error");
                setStatus("error", error.message);
                editor.focus();
                return;
            }

            if (JSON.stringify(nextValue) === JSON.stringify(originalValue)) {
                cancel();
                return;
            }

            settled = true;
            editor.disabled = true;
            host.classList.add("inline-saving");
            const scrollTop = els.detailContent.scrollTop;
            try {
                const source = deepClone(currentEntry.source);
                applyInlineValueToSource(source, target, nextValue);
                const payload = await saveItemSourceRequest(currentEntry, source);
                applySavedItemSource(currentEntry, source, payload.hash, payload.modifiedAt, scrollTop);
                setStatus("ready", `${target.label || target.path} 已保存到 ${currentEntry.package.path}。`);
            } catch (error) {
                settled = false;
                editor.disabled = false;
                host.classList.remove("inline-saving");
                host.classList.add("inline-error");
                setStatus("error", `保存失败：${error.message}`);
                editor.focus();
            }
        };

        editor.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                event.preventDefault();
                cancel();
                return;
            }
            const isTextarea = editor.tagName === "TEXTAREA";
            if (event.key === "Enter" && (!isTextarea || event.ctrlKey || event.metaKey)) {
                event.preventDefault();
                commit();
            }
        });
        editor.addEventListener("blur", () => {
            window.setTimeout(() => {
                if (!settled) commit();
            }, 0);
        });
        if (editor.tagName === "SELECT") editor.addEventListener("change", commit);
    }

    // 获取词条中真正显示值的节点，标题/说明本身就是值节点。
    function getInlineValueHost(host) {
        return host.querySelector?.(".module-field-value") || host.querySelector?.("strong") || host;
    }

    // 按原始 JSON 类型创建最小编辑控件，数组/对象直接编辑 JSON。
    function createInlineEditorControl(value, target) {
        if (target.nullableNumber) {
            const input = document.createElement("input");
            input.className = "inline-edit-control";
            input.type = "text";
            input.value = value == null ? "null" : String(value);
            return input;
        }
        if (typeof value === "boolean") {
            const select = document.createElement("select");
            select.className = "inline-edit-control";
            select.innerHTML = `<option value="true">是 / true</option><option value="false">否 / false</option>`;
            select.value = value ? "true" : "false";
            return select;
        }
        if (Array.isArray(value) || isPlainObject(value) || target.multiline) {
            const textarea = document.createElement("textarea");
            textarea.className = "inline-edit-control inline-edit-textarea";
            textarea.spellcheck = false;
            textarea.value = Array.isArray(value) || isPlainObject(value)
                ? JSON.stringify(value, null, 2)
                : String(value ?? "");
            return textarea;
        }
        const input = document.createElement("input");
        input.className = "inline-edit-control";
        input.type = typeof value === "number" ? "number" : "text";
        if (input.type === "number") input.step = "any";
        input.value = String(value ?? "");
        return input;
    }

    // 根据原值类型把输入还原成 JSON 值，避免数字被错误写成字符串。
    function parseInlineEditorValue(editor, originalValue, label) {
        if (typeof originalValue === "boolean") return editor.value === "true";
        if (typeof originalValue === "number") {
            const value = Number(editor.value);
            if (!Number.isFinite(value)) throw new Error(`${label} 必须是有效数字。`);
            return value;
        }
        if (Array.isArray(originalValue) || isPlainObject(originalValue)) {
            let value;
            try {
                value = JSON.parse(editor.value);
            } catch {
                throw new Error(`${label} 必须是合法 JSON。`);
            }
            if (Array.isArray(originalValue) && !Array.isArray(value)) throw new Error(`${label} 必须保持数组类型。`);
            if (isPlainObject(originalValue) && !isPlainObject(value)) throw new Error(`${label} 必须保持对象类型。`);
            return value;
        }
        return editor.value;
    }

    function parseNullableNumber(raw, label) {
        if (raw.trim().toLowerCase() === "null") return null;
        if (!raw.trim() || !Number.isFinite(Number(raw))) throw new Error(`${label} 必须是有限数字或 null。`);
        return Number(raw);
    }

    // 把一个内联字段作为当前 Item 的差异覆盖写入原始 source。
    function applyInlineValueToSource(source, target, value) {
        if (target.sourceType === "root") {
            setNestedValue(source, target.path, value);
            return;
        }
        if (target.sourceType === "health") {
            if (!isPlainObject(source.health)) source.health = {};
            setNestedValue(source.health, target.path, value);
            return;
        }
        if (target.sourceType === "module") {
            if (!isPlainObject(source.modules)) source.modules = {};
            if (!isPlainObject(source.modules[target.sourceName])) source.modules[target.sourceName] = {};
            const moduleSource = source.modules[target.sourceName];
            const sectionName = target.sectionName === "data" ? "data" : "parameters";
            if (!isPlainObject(moduleSource[sectionName])) moduleSource[sectionName] = {};
            setNestedValue(moduleSource[sectionName], target.path, value);
            return;
        }
        throw new Error(`不支持的编辑目标：${target.sourceType}`);
    }

    // 发送受限 Item 写入请求；调用方决定保存后的界面行为。
    async function saveItemSourceRequest(entry, source) {
        if (entry.kind !== "item" && entry.kind !== "buff") throw new Error("生物定义只读，不能通过 Item 接口保存。");
        const isBuff = entry.kind === "buff";
        const expectedHash = (isBuff ? state.buffHashes : state.packageHashes).get(entry.package.id);
        if (!expectedHash) throw new Error("当前分包缺少文件指纹，请先重新读取 JSON。");
        const response = await fetch(isBuff ? BUFF_SAVE_API : ITEM_SAVE_API, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                itemId: isBuff ? entry.final.id : entry.id,
                definitionKind: entry.kind,
                packagePath: entry.package.path,
                expectedHash,
                source
            })
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok || payload.ok !== true) {
            throw new Error(payload.error || `${response.status} ${response.statusText}`);
        }
        return payload;
    }

    // 使用服务端确认后的 source 刷新本地解析结果，同时保持开发者当前阅读位置。
    function applySavedItemSource(entry, source, packageHash, modifiedAt, scrollTop) {
        if (entry.kind === "buff") {
            state.buffSources.set(entry.final.id.toLowerCase(), deepClone(source));
            state.buffHashes.set(entry.package.id, packageHash);
        } else {
            state.sourceById.set(entry.id.toLowerCase(), deepClone(source));
            state.packageHashes.set(entry.package.id, packageHash);
        }
        if (modifiedAt) {
            if (!isPlainObject(state.itemMetadata)) state.itemMetadata = { schemaVersion: 1, items: {} };
            if (!isPlainObject(state.itemMetadata.items)) state.itemMetadata.items = {};
            state.itemMetadata.items[entry.id] = modifiedAt;
        }
        state.resolvedById = resolveDefinitions(state.sourceById);
        rebuildEntries();
        renderList();
        renderOverview();
        const refreshed = findItemEntry(entry.id);
        if (refreshed) {
            state.selectedId = refreshed.id;
            renderDetail(refreshed);
            els.detailContent.scrollTop = scrollTop;
        }
    }

    // 按点分路径写入嵌套 JSON 对象。
    function setNestedValue(root, path, value) {
        const parts = String(path).split(".");
        if (parts.some(key => ["__proto__", "prototype", "constructor"].includes(key)))
            throw new Error("无效字段路径");
        let cursor = root;
        for (let index = 0; index < parts.length - 1; index++) {
            const key = parts[index];
            if (!isPlainObject(cursor[key]) && !Array.isArray(cursor[key]))
                cursor[key] = /^\d+$/.test(parts[index + 1]) ? [] : {};
            cursor = cursor[key];
        }
        cursor[parts[parts.length - 1]] = value;
    }

    // 绘制源配置、最终配置和直接源 JSON 链接。
    function renderDeveloperPanels(entry) {
        const wrap = document.createElement("div");
        const directory = entry.kind === "actor" ? "Actors" : "Items";
        const packagePath = `../GameConfig/${directory}/${entry.package.path}`;
        const projectPath = `Assets/StreamingAssets/GameConfig/${directory}/${entry.package.path}`;

        const sourcePanel = document.createElement("details");
        sourcePanel.className = "dev-panel";
        sourcePanel.open = true;
        sourcePanel.innerHTML = `<summary>来源与原始定义</summary>`;
        const sourceBody = document.createElement("div");
        sourceBody.className = "dev-panel-body";

        const sourceRow = document.createElement("div");
        sourceRow.className = "source-row";
        const link = document.createElement("a");
        link.className = "source-link";
        link.href = packagePath;
        link.target = "_blank";
        link.rel = "noopener";
        link.textContent = `打开 ${entry.package.path}`;
        const copy = document.createElement("button");
        copy.type = "button";
        copy.className = "copy-button";
        copy.textContent = "复制项目路径";
        copy.addEventListener("click", () => copyText(projectPath, copy));
        sourceRow.append(link, copy);
        sourceBody.appendChild(sourceRow);
        sourceBody.appendChild(jsonPre(entry.source));
        sourcePanel.appendChild(sourceBody);

        const resolvedPanel = document.createElement("details");
        resolvedPanel.className = "dev-panel";
        resolvedPanel.innerHTML = `<summary>继承解析后的完整 JSON</summary>`;
        const resolvedBody = document.createElement("div");
        resolvedBody.className = "dev-panel-body";
        resolvedBody.appendChild(jsonPre(entry.final));
        resolvedPanel.appendChild(resolvedBody);

        const visualPanel = document.createElement("details");
        visualPanel.className = "dev-panel";
        visualPanel.innerHTML = `<summary>Visual / ItemData 明细</summary>`;
        const visualBody = document.createElement("div");
        visualBody.className = "dev-panel-body";
        const combined = { visual: entry.final.visual || {}, itemData: entry.final.itemData || {} };
        visualBody.appendChild(renderObjectTable(combined));
        visualPanel.appendChild(visualBody);

        wrap.append(sourcePanel, resolvedPanel, visualPanel);
        return wrap;
    }

    // 把嵌套对象展平为“路径 -> 值”表，保证所有数值都可见。
    function renderObjectTable(object) {
        const rows = [];
        flattenObject(object, "", rows);
        return renderKeyValueTable(rows);
    }

    // 递归展平 JSON 对象，数组保持为单个值显示。
    function flattenObject(value, prefix, rows) {
        if (isPlainObject(value)) {
            const entries = Object.entries(value);
            if (!entries.length && prefix) rows.push([prefix, {}]);
            for (const [key, child] of entries) {
                const path = prefix ? `${prefix}.${key}` : key;
                flattenObject(child, path, rows);
            }
            return;
        }
        rows.push([prefix || "value", value]);
    }

    // 创建通用键值表。
    function renderKeyValueTable(rows) {
        const table = document.createElement("table");
        table.className = "kv-table";
        const tbody = document.createElement("tbody");
        for (const [key, value] of rows) {
            const tr = document.createElement("tr");
            const th = document.createElement("th");
            const td = document.createElement("td");
            th.textContent = humanizePath(key);
            td.textContent = formatValue(value);
            tr.append(th, td);
            tbody.appendChild(tr);
        }
        table.appendChild(tbody);
        return table;
    }

    // 小标题用于区分 Module 的 Parameters/Data。
    function miniHeading(text) {
        const heading = document.createElement("div");
        heading.className = "mini-heading";
        heading.textContent = text;
        return heading;
    }

    // 生成格式化 JSON 代码块。
    function jsonPre(value) {
        const pre = document.createElement("pre");
        pre.className = "json-block";
        pre.textContent = JSON.stringify(value, null, 2);
        return pre;
    }

    // SpriteAddress 支持普通图片和 Unity 多 Sprite PNG 子资源。
    async function fillSpriteIcon(host, spriteAddress, displayName) {
        if (!spriteAddress || typeof spriteAddress !== "string") return;
        try {
            const sprite = await loadSprite(spriteAddress);
            if (!host.isConnected || !sprite) return;
            host.textContent = "";
            if (sprite.kind === "image") {
                const img = document.createElement("img");
                img.alt = displayName;
                img.src = sprite.url;
                host.appendChild(img);
            } else if (sprite.kind === "cropped") {
                const canvas = document.createElement("canvas");
                canvas.width = sprite.width;
                canvas.height = sprite.height;
                const ctx = canvas.getContext("2d");
                ctx.imageSmoothingEnabled = false;
                ctx.drawImage(
                    sprite.image,
                    sprite.x,
                    sprite.image.height - sprite.y - sprite.height,
                    sprite.width,
                    sprite.height,
                    0,
                    0,
                    sprite.width,
                    sprite.height
                );
                host.appendChild(canvas);
            }
        } catch (error) {
            console.debug(`Sprite 预览失败：${spriteAddress}`, error);
        }
    }

    // 所有物品图标都支持单击打开检查器；列表、详情、总览和战利品弹窗共用这一入口。
    function attachItemIconZoom(host, entry) {
        if (!host || !entry || !entry.final?.visual?.spriteAddress) return;
        host.classList.add("zoomable-item-icon");
        host.title = `${entry.displayName} · 单击查看大图`;
        host.addEventListener("click", event => {
            event.preventDefault();
            event.stopPropagation();
            openImageZoom(entry);
        });
    }

    // 读取原始 Sprite，并提供滚轮缩放、按钮缩放和拖拽平移；也可直接检查其来源图集。
    async function openImageZoom(entry, options = {}) {
        const address = options.address || entry?.final?.visual?.spriteAddress;
        if (!address) return;
        const title = options.title || entry.displayName;
        const context = options.context ? ` · ${options.context}` : "";
        els.imageZoomTitle.textContent = title;
        els.imageZoomMeta.textContent = `${entry.id}${context} · ${address}`;
        els.imageZoomBody.innerHTML = `<div class="image-zoom-loading">正在读取 Sprite…</div>`;
        els.imageZoomModal.hidden = false;
        try {
            const sprite = await loadSprite(address);
            if (!sprite || els.imageZoomModal.hidden) {
                if (!sprite) els.imageZoomBody.innerHTML = `<div class="no-data">无法读取这个 Sprite。</div>`;
                return;
            }
            els.imageZoomBody.innerHTML = "";
            const toolbar = document.createElement("div");
            toolbar.className = "image-zoom-toolbar";
            const zoomOut = document.createElement("button");
            zoomOut.type = "button";
            zoomOut.textContent = "−";
            zoomOut.title = "缩小";
            const zoomLabel = document.createElement("span");
            zoomLabel.textContent = "100%";
            const zoomIn = document.createElement("button");
            zoomIn.type = "button";
            zoomIn.textContent = "+";
            zoomIn.title = "放大";
            const zoomReset = document.createElement("button");
            zoomReset.type = "button";
            zoomReset.textContent = "重置";
            zoomReset.title = "恢复 100% 并居中";
            const help = document.createElement("small");
            help.textContent = "滚轮缩放 · 按住拖动查看局部";
            toolbar.append(zoomOut, zoomLabel, zoomIn, zoomReset, help);

            const stage = document.createElement("div");
            stage.className = "image-zoom-stage";
            const content = document.createElement("div");
            content.className = "image-zoom-content";
            let mediaElement;
            if (sprite.kind === "image") {
                const img = document.createElement("img");
                img.alt = entry.displayName;
                img.src = sprite.url;
                img.draggable = false;
                mediaElement = img;
            } else if (sprite.kind === "cropped") {
                const canvas = document.createElement("canvas");
                canvas.width = sprite.width;
                canvas.height = sprite.height;
                const ctx = canvas.getContext("2d");
                ctx.imageSmoothingEnabled = false;
                ctx.drawImage(
                    sprite.image,
                    sprite.x,
                    sprite.image.height - sprite.y - sprite.height,
                    sprite.width,
                    sprite.height,
                    0,
                    0,
                    sprite.width,
                    sprite.height
                );
                mediaElement = canvas;
            }
            content.appendChild(mediaElement);
            stage.appendChild(content);

            let scale = 1;
            let panX = 0;
            let panY = 0;
            let baseWidth = Math.max(1, Number(sprite.width) || 1);
            let baseHeight = Math.max(1, Number(sprite.height) || 1);
            let dragging = false;
            let dragX = 0;
            let dragY = 0;

            // 以 Sprite 自身尺寸作为平移/缩放主体，避免缩放整个 viewport 后拖动出现巨幅错位。
            const updateBaseSize = () => {
                const naturalWidth = Math.max(1, Number(sprite.width) || mediaElement.naturalWidth || mediaElement.width || 1);
                const naturalHeight = Math.max(1, Number(sprite.height) || mediaElement.naturalHeight || mediaElement.height || 1);
                const rect = stage.getBoundingClientRect();
                const maxWidth = Math.max(120, rect.width * 0.62);
                const maxHeight = Math.max(120, rect.height * 0.72);
                const fitScale = Math.min(maxWidth / naturalWidth, maxHeight / naturalHeight);
                const baseScale = fitScale >= 1 ? Math.max(1, Math.floor(fitScale)) : fitScale;
                baseWidth = Math.max(1, naturalWidth * baseScale);
                baseHeight = Math.max(1, naturalHeight * baseScale);
                content.style.width = `${baseWidth}px`;
                content.style.height = `${baseHeight}px`;
            };

            // 限制平移范围，保证放大后的 Sprite 不会被无限拖离视口。
            const clampPan = () => {
                const rect = stage.getBoundingClientRect();
                const margin = 28;
                const maxPanX = Math.max(0, (baseWidth * scale - rect.width) / 2 + margin);
                const maxPanY = Math.max(0, (baseHeight * scale - rect.height) / 2 + margin);
                panX = Math.min(maxPanX, Math.max(-maxPanX, panX));
                panY = Math.min(maxPanY, Math.max(-maxPanY, panY));
            };

            const applyTransform = () => {
                clampPan();
                content.style.transform = `translate(-50%, -50%) translate(${panX}px, ${panY}px) scale(${scale})`;
                zoomLabel.textContent = `${Math.round(scale * 100)}%`;
            };
            const setScale = (nextScale, clientX = null, clientY = null) => {
                const clamped = Math.min(16, Math.max(0.25, nextScale));
                if (clamped === scale) return;
                if (clientX !== null && clientY !== null) {
                    const rect = stage.getBoundingClientRect();
                    const cursorX = clientX - rect.left - rect.width / 2;
                    const cursorY = clientY - rect.top - rect.height / 2;
                    const localX = (cursorX - panX) / scale;
                    const localY = (cursorY - panY) / scale;
                    panX = cursorX - localX * clamped;
                    panY = cursorY - localY * clamped;
                }
                scale = clamped;
                applyTransform();
            };
            zoomIn.addEventListener("click", () => setScale(scale * 1.25));
            zoomOut.addEventListener("click", () => setScale(scale / 1.25));
            zoomReset.addEventListener("click", () => {
                scale = 1;
                panX = 0;
                panY = 0;
                applyTransform();
            });
            stage.addEventListener("wheel", event => {
                event.preventDefault();
                setScale(scale * (event.deltaY < 0 ? 1.15 : 1 / 1.15), event.clientX, event.clientY);
            }, { passive: false });
            stage.addEventListener("pointerdown", event => {
                if (event.button !== 0) return;
                event.preventDefault();
                dragging = true;
                dragX = event.clientX - panX;
                dragY = event.clientY - panY;
                stage.classList.add("dragging");
                stage.setPointerCapture(event.pointerId);
            });
            stage.addEventListener("pointermove", event => {
                if (!dragging) return;
                panX = event.clientX - dragX;
                panY = event.clientY - dragY;
                applyTransform();
            });
            const stopDrag = event => {
                if (!dragging) return;
                dragging = false;
                stage.classList.remove("dragging");
                if (stage.hasPointerCapture(event.pointerId)) stage.releasePointerCapture(event.pointerId);
            };
            stage.addEventListener("pointerup", stopDrag);
            stage.addEventListener("pointercancel", stopDrag);
            stage.addEventListener("dragstart", event => event.preventDefault());
            window.addEventListener("resize", () => {
                if (els.imageZoomModal.hidden) return;
                updateBaseSize();
                applyTransform();
            }, { once: true });
            const caption = document.createElement("div");
            caption.className = "image-zoom-caption";
            caption.innerHTML = `<strong>${escapeHtml(title)}</strong>` +
                `<span>${escapeHtml(entry.id)}</span>` +
                (options.context ? `<span>${escapeHtml(options.context)}</span>` : "") +
                `<small>${escapeHtml(address)}</small>`;
            els.imageZoomBody.append(toolbar, stage, caption);
            updateBaseSize();
            applyTransform();
        } catch (error) {
            els.imageZoomBody.innerHTML = `<div class="no-data">大图加载失败：${escapeHtml(error.message)}</div>`;
        }
    }

    // 关闭物品 Sprite 放大预览。
    function closeImageZoom() {
        els.imageZoomModal.hidden = true;
        els.imageZoomBody.innerHTML = "";
    }

    // 拆分 Unity Sprite Address，统一识别普通贴图与图集子 Sprite。
    function parseUnitySpriteAddress(spriteAddress) {
        if (!spriteAddress || typeof spriteAddress !== "string") return null;
        const match = spriteAddress.match(/^(Assets\/.+?\.(?:png|jpg|jpeg|webp))(?:\[([^\]]+)\])?$/i);
        if (!match) return null;
        return {
            assetPath: match[1],
            subObjectName: match[2] || null,
            url: `../../../${match[1]}`
        };
    }

    // 解析 Unity Sprite Address；对子 Sprite 读取同名 .meta 获取裁剪矩形。
    function loadSprite(spriteAddress) {
        if (state.spriteCache.has(spriteAddress)) return state.spriteCache.get(spriteAddress);
        const promise = (async () => {
            const source = parseUnitySpriteAddress(spriteAddress);
            if (!source) return null;
            const { assetPath, subObjectName, url } = source;
            // Sprite 仍位于项目 Assets 下；当前页面从 StreamingAssets/ItemWiki 相对回到仓库根目录读取。
            if (!subObjectName) {
                const image = await loadImage(url);
                return {
                    kind: "image",
                    url,
                    assetPath,
                    width: image.naturalWidth || image.width,
                    height: image.naturalHeight || image.height
                };
            }

            const [metaResponse, image] = await Promise.all([
                fetch(`${url}.meta`, { cache: "no-store" }),
                loadImage(url)
            ]);
            if (!metaResponse.ok) return null;
            const meta = await metaResponse.text();
            const rect = findUnitySpriteRect(meta, subObjectName);
            if (!rect) {
                // Unity 的 Single Sprite 仍可能以 `Texture.png[SpriteName]` 作为 Addressables 子对象地址，
                // 但其 .meta 不会生成 spriteSheet.sprites 裁剪矩形；这种情况应直接显示整张贴图。
                if (/^\s*spriteMode:\s*1\s*$/m.test(meta)) {
                    return {
                        kind: "image",
                        url,
                        assetPath,
                        subObjectName,
                        width: image.naturalWidth || image.width,
                        height: image.naturalHeight || image.height
                    };
                }
                return null;
            }
            const previewUrl = await createCroppedSpritePngUrl(image, rect);
            return {
                kind: "image",
                url: previewUrl,
                sourceUrl: url,
                assetPath,
                subObjectName,
                width: rect.width,
                height: rect.height
            };
        })();
        state.spriteCache.set(spriteAddress, promise);
        return promise;
    }

    // Unity 的双引号 YAML 标量可能把中文写成 \uXXXX；先还原为真实 Sprite 名再比较。
    function parseUnityYamlScalar(rawValue) {
        const value = String(rawValue || "").trim();
        if (!value) return "";
        if (value.startsWith('"') && value.endsWith('"')) {
            try {
                return JSON.parse(value);
            } catch {
                return value.slice(1, -1);
            }
        }
        if (value.startsWith("'") && value.endsWith("'")) {
            return value.slice(1, -1).replace(/''/g, "'");
        }
        return value;
    }

    // 从 Unity TextureImporter YAML 中读取指定 Sprite 的 rect。
    function findUnitySpriteRect(meta, spriteName) {
        const lines = String(meta || "").split(/\r?\n/);
        for (let index = 0; index < lines.length; index++) {
            const nameMatch = lines[index].match(/^\s*name:\s*(.*?)\s*$/);
            if (!nameMatch || parseUnityYamlScalar(nameMatch[1]) !== spriteName) continue;

            const section = lines.slice(index + 1, Math.min(lines.length, index + 12)).join("\n");
            const match = section.match(
                /^\s*rect:\s*$\n\s*serializedVersion:\s*\d+\s*$\n\s*x:\s*([\d.-]+)\s*$\n\s*y:\s*([\d.-]+)\s*$\n\s*width:\s*([\d.-]+)\s*$\n\s*height:\s*([\d.-]+)\s*$/m
            );
            if (!match) return null;
            return {
                x: Number(match[1]),
                y: Number(match[2]),
                width: Number(match[3]),
                height: Number(match[4])
            };
        }
        return null;
    }

    // 按 Unity Sprite.rect 以 1:1 像素裁剪，并编码为保留 Alpha 的 PNG Blob URL。
    function createCroppedSpritePngUrl(image, rect) {
        const canvas = document.createElement("canvas");
        canvas.width = rect.width;
        canvas.height = rect.height;
        const ctx = canvas.getContext("2d");
        ctx.imageSmoothingEnabled = false;
        const sourceHeight = image.naturalHeight || image.height;
        ctx.drawImage(
            image,
            rect.x,
            sourceHeight - rect.y - rect.height,
            rect.width,
            rect.height,
            0,
            0,
            rect.width,
            rect.height
        );
        return new Promise((resolve, reject) => {
            canvas.toBlob(blob => {
                if (!blob) {
                    reject(new Error("Sprite PNG 编码失败"));
                    return;
                }
                resolve(URL.createObjectURL(blob));
            }, "image/png");
        });
    }

    // 浏览器加载项目图片资源。
    function loadImage(url) {
        return new Promise((resolve, reject) => {
            const image = new Image();
            image.onload = () => resolve(image);
            image.onerror = () => reject(new Error(`无法加载图片：${url}`));
            image.src = url;
        });
    }

    // 复制开发者路径并提供短暂反馈。
    async function copyText(text, button) {
        try {
            await navigator.clipboard.writeText(text);
            const old = button.textContent;
            button.textContent = "已复制";
            setTimeout(() => button.textContent = old, 900);
        } catch {
            window.prompt("复制路径：", text);
        }
    }

    // 更新页面状态条。
    function setStatus(kind, text) {
        els.statusBar.classList.remove("ready", "error");
        if (kind === "ready") els.statusBar.classList.add("ready");
        if (kind === "error") els.statusBar.classList.add("error");
        els.statusText.textContent = text;
    }

    // 没有可显示条目时只给出紧凑提示，不再保留大面积空白占位。
    function showEmpty() {
        els.detailContent.innerHTML = `<div class="detail-empty-line">当前没有可显示的 Item。</div>`;
    }

    // 深拷贝纯 JSON 数据。
    function deepClone(value) {
        return value === undefined ? undefined : JSON.parse(JSON.stringify(value));
    }

    // 判断值是否为普通 JSON 对象。
    function isPlainObject(value) {
        return value !== null && typeof value === "object" && !Array.isArray(value);
    }

    // 将字段路径逐段翻译成中文；模块卡片另行保留完整原始路径作为开发者对照。
    function humanizePath(path) {
        const parts = String(path).split(".");
        const dictionary = state.moduleGlossary?.fields || {};
        return parts.map(part => dictionary[part] || FIELD_LABELS[part] || part).join(" · ");
    }

    // 按开发者读数习惯显示布尔、数组和对象。
    function formatValue(value) {
        if (value === true) return "是 / true";
        if (value === false) return "否 / false";
        if (value === null) return "null";
        if (value === undefined) return "—";
        if (Array.isArray(value)) return value.length ? value.map(formatScalar).join(", ") : "[]";
        if (isPlainObject(value)) return JSON.stringify(value);
        return String(value);
    }

    // 格式化数组中的简单值。
    function formatScalar(value) {
        return isPlainObject(value) || Array.isArray(value) ? JSON.stringify(value) : String(value);
    }

    // 使用名称首字作为缺失 Sprite 的占位图形。
    function fallbackGlyph(entry) {
        const source = entry.displayName || entry.id || "?";
        return source.trim().charAt(0).toUpperCase() || "?";
    }

    // HTML 文本转义。
    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    // HTML 属性转义。
    function escapeAttr(value) {
        return escapeHtml(value);
    }

    // 正则表达式文本转义。
    init();
})();
