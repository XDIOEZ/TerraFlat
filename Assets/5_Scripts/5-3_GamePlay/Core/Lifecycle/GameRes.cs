using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine.Tilemaps;
using UnityEngine;
using UnityEngine.InputSystem;
using System.IO;
using System.Linq;

/// <summary>游戏资源目录门面；加载计划、句柄所有权与启动校验由独立组件负责，Ready 前禁止进入世界。</summary>
public partial class GameRes : SingletonAutoMono<GameRes>
{
    #region 字段

    /// <summary>只返回当前已存在的资源管理器，不会因查询而自动创建实例。</summary>
    public static GameRes ExistingInstance => instance;

    public int LoadedCount = 0;
    
    [Header("所有预制体字典")]
    [ShowInInspector]
    public Dictionary<string, GameObject> AllPrefabs = new Dictionary<string, GameObject>();

    [Header("JSON 物品定义字典")]
    [ShowInInspector]
    public Dictionary<string, RuntimeItemDefinition> ItemDefinitions =
        new Dictionary<string, RuntimeItemDefinition>(System.StringComparer.OrdinalIgnoreCase);

    [Header("JSON Actor 定义字典")]
    [ShowInInspector]
    public Dictionary<string, RuntimeItemDefinition> ActorDefinitions =
        new Dictionary<string, RuntimeItemDefinition>(System.StringComparer.OrdinalIgnoreCase);

    [Header("JSON 战利品表字典")]
    [ShowInInspector]
    public Dictionary<string, RuntimeLootTable> LootTables =
        new Dictionary<string, RuntimeLootTable>(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>仅供尚未迁移 CraftingService 的熔炼流程按输入签名查询；普通合成使用 recipeCatalog 多候选目录。</summary>
    [Header("旧熔炼输入签名字典")]
    [ShowInInspector]
    public Dictionary<string, RuntimeRecipe> recipeDict = new Dictionary<string, RuntimeRecipe>();

    [ShowInInspector]
    public IReadOnlyDictionary<string, RuntimeRecipe> recipeById => recipeCatalog.RecipesById;

    /// <summary>运行时配方的权威索引；注册时构建类型候选顺序。</summary>
    private readonly CraftingRecipeCatalog recipeCatalog = new CraftingRecipeCatalog();

    [Header("TileBase字典")]
    [ShowInInspector]
    public Dictionary<string, TileBase> tileBaseDict = new Dictionary<string, TileBase>();

    [Header("Tile地块逻辑SO字典")]
    [ShowInInspector]
    public Dictionary<string, Tile_Block> TileBlockDict = new Dictionary<string, Tile_Block>();

    [Header("Buff数据字典")]
    [ShowInInspector]
    public Dictionary<string, BuffDefinition> BuffDefinitions =
        new Dictionary<string, BuffDefinition>(System.StringComparer.OrdinalIgnoreCase);

    [Header("污染定义字典")]
    [ShowInInspector]
    public Dictionary<string, ContaminationDefinition> ContaminationDefinitions =
        new Dictionary<string, ContaminationDefinition>(System.StringComparer.OrdinalIgnoreCase);

    [System.NonSerialized]
    private TextLibraryService textLibraryService = TextLibraryService.Empty;

    /// <summary>文字库访问接口；调用方不需要依赖具体 JSON 加载实现。</summary>
    public ITextLibraryService TextLibraries => textLibraryService;

    [Header("初始库存字典")]
    [ShowInInspector]
    public Dictionary<string, Inventoryinit> InventoryInitDict = new Dictionary<string, Inventoryinit>();

    [Header("技能字典")]
    [ShowInInspector]
    public Dictionary<string, BaseSkill> SkillDict = new Dictionary<string, BaseSkill>();

    /// <summary>全部阶段成功后发布的就绪状态。</summary>
    public bool isLoadFinish => LoadState == ResourceLoadState.Ready;
    
    // 进度条相关字段
    private bool showLoadingGUI = false;
    private string loadingText = "";
    private float loadingProgress = 0f;
    private bool resourceLoadFailed = false;

    #endregion

    #region Unity 生命周期

    protected override void Awake()
    {
        base.Awake();

        // 返回主菜单时场景会再次带入 WorldManager Prefab；重复实例已由基类安排销毁，
        // 不能再启动一条会清空正式资源目录、随后又因对象销毁而中断的加载协程。
        if (instance != this)
            return;

        // 初始化时显示加载界面
        showLoadingGUI = true;
        loadingText = "正在加载资源...";
        InitializeResourceLoadingPresentation();
        RefreshResourceLoadingPresentation();
        TryReloadResources();
    }

    /// <summary>刷新资源加载界面，并保留 F5 热重载入口。</summary>
    public void Update()
    {
        RefreshResourceLoadingPresentation();

        // 资源热重载是主菜单阶段也需生效的全局开发快捷键。
        if (Keyboard.current?.f5Key.wasPressedThisFrame == true)
        {
            TryReloadResources();
        }
    }

    #endregion

    #region Prefab 别名

    /// <summary>收集 Item 主别名与独立模块的候选别名。</summary>
    private void CollectPrefabAliases(GameObject prefab, IDictionary<string, GameObject> moduleAliasCandidates)
    {
        var item = prefab.GetComponent<Item>();
        if (item != null)
        {
            RegisterPrefabAlias(item.itemData?.IDName, prefab);
            return;
        }

        foreach (Module module in prefab.GetComponentsInChildren<Module>(true))
        {
            if (module == null)
                continue;

            CollectModuleAlias(moduleAliasCandidates, module.CanonicalModuleId, prefab);
            CollectModuleAlias(moduleAliasCandidates, module._Data?.ID, prefab);
        }
    }

    /// <summary>记录唯一模块别名；多个 Prefab 共用玩法 ID 时将该别名标记为不可直接实例化。</summary>
    private static void CollectModuleAlias(
        IDictionary<string, GameObject> moduleAliasCandidates,
        string key,
        GameObject prefab)
    {
        if (prefab == null || string.IsNullOrWhiteSpace(key))
            return;

        string normalizedKey = key.Trim();
        if (!moduleAliasCandidates.TryGetValue(normalizedKey, out GameObject existingPrefab))
        {
            moduleAliasCandidates[normalizedKey] = prefab;
            return;
        }

        if (existingPrefab != prefab)
            moduleAliasCandidates[normalizedKey] = null;
    }

    /// <summary>只登记能够唯一解析且不会遮蔽 Prefab 主地址的模块别名。</summary>
    private void RegisterUniqueModuleAliases(IReadOnlyDictionary<string, GameObject> moduleAliasCandidates)
    {
        foreach (KeyValuePair<string, GameObject> pair in moduleAliasCandidates)
        {
            if (pair.Value == null)
                continue;
            if (AllPrefabs.TryGetValue(pair.Key, out GameObject primaryPrefab) && primaryPrefab != pair.Value)
                continue;

            RegisterPrefabAlias(pair.Key, pair.Value);
        }
    }

    internal void RegisterPrefabAlias(string key, GameObject prefab)
    {
        if (prefab == null || string.IsNullOrWhiteSpace(key))
            return;

        string normalizedKey = key.Trim();
        if (AllPrefabs.TryGetValue(normalizedKey, out GameObject existingPrefab) &&
            existingPrefab != null && existingPrefab != prefab)
        {
            throw new InvalidDataException(
                $"Prefab 别名冲突：{normalizedKey} -> {existingPrefab.name} / {prefab.name}");
        }

        AllPrefabs[normalizedKey] = prefab;
    }

    /// <summary>批量注册失败时，仅移除仍指向本次外壳的内部别名。</summary>
    internal void UnregisterPrefabAlias(string key, GameObject prefab)
    {
        if (prefab == null || string.IsNullOrWhiteSpace(key))
            return;

        string normalizedKey = key.Trim();
        if (AllPrefabs.TryGetValue(normalizedKey, out GameObject registered) && registered == prefab)
            AllPrefabs.Remove(normalizedKey);
    }

    #endregion

    #region 外部接口

    public GameObject InstantiatePrefab(string prefab, Vector3? position = null, Quaternion? rotation = null, Vector3? scale = null, Transform parent = null)
    {
        if (AllPrefabs.TryGetValue(prefab, out var go))
        {
            var obj = Instantiate(go, parent);

            // 设置位置、旋转和缩放
            obj.transform.position = position ?? Vector3.zero;
            obj.transform.rotation = rotation ?? Quaternion.identity;

            // 修复缩放为0的问题 - 如果未指定缩放或缩放为零向量，使用Vector3.one
            obj.transform.localScale = scale ?? Vector3.one;

            // 额外检查：确保缩放的每个分量都不为0
            if (obj.transform.localScale.x == 0) obj.transform.localScale = new Vector3(1, obj.transform.localScale.y, obj.transform.localScale.z);
            if (obj.transform.localScale.y == 0) obj.transform.localScale = new Vector3(obj.transform.localScale.x, 1, obj.transform.localScale.z);
            if (obj.transform.localScale.z == 0) obj.transform.localScale = new Vector3(obj.transform.localScale.x, obj.transform.localScale.y, 1);

            if (ModRuntimeManager.Instance != null && ModRuntimeManager.Instance.IsRuntimeTemplate(go))
                obj.SetActive(true);

            if (ItemDefinitions.TryGetValue(prefab, out RuntimeItemDefinition definition) &&
                obj.TryGetComponent(out Item item))
            {
                ItemDefinitionRuntime.ConfigureInstance(this, definition, item, definition.CreateItemData());
            }

            return obj;
        }
        Debug.LogError($"预制件不存在:{prefab}");
        return null;
    }
    
    public GameObject GetPrefab(string prefabName, bool logError = true)
    {
        if (AllPrefabs.TryGetValue(prefabName, out var go))
        {
            return go;
        }
        else
        {
            // 输出错误日志，包含关键信息便于调试
            if (logError)
                Debug.LogError($"找不到名为 [{prefabName}] 的预制体！请检查AllPrefabs字典中是否正确注册了该预制体", this);
            return null;
        }
    }

    public TileBase GetTileBase(string tileBaseName)
    {
        tileBaseDict.TryGetValue(tileBaseName, out var tile);
        return tile;
    }
    
    /// <summary>
    /// 获取 Tile_Block 逻辑 ScriptableObject（通过 tileItemName 或资源名）
    /// </summary>
    public Tile_Block GetTileBlock(string key)
    {
        TileBlockDict.TryGetValue(key, out var block);
        return block;
    }
    
    public BuffDefinition GetBuffDefinition(string buffId)
    {
        if (string.IsNullOrWhiteSpace(buffId))
            return null;

        BuffDefinitions.TryGetValue(buffId.Trim(), out BuffDefinition definition);
        return definition;
    }

    /// <summary>按稳定 ID 查询污染指标定义。</summary>
    public ContaminationDefinition GetContaminationDefinition(string contaminationId)
    {
        if (string.IsNullOrWhiteSpace(contaminationId))
            return null;
        ContaminationDefinitions.TryGetValue(contaminationId.Trim(), out ContaminationDefinition definition);
        return definition;
    }

    public void RegisterItemDefinition(RuntimeItemDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || definition.ShellPrefab == null)
            throw new InvalidDataException("注册的 ItemDefinition、ID 或外壳为空");
        if (ItemDefinitions.ContainsKey(definition.Id))
            throw new InvalidDataException($"ItemDefinition ID 冲突：{definition.Id}");
        if (AllPrefabs.TryGetValue(definition.Id, out GameObject existing) && existing != definition.ShellPrefab)
            throw new InvalidDataException($"物品 ID 与 Prefab 名称/别名冲突：{definition.Id} -> {existing.name} / {definition.ShellPrefab.name}");

        ItemDefinitions.Add(definition.Id, definition);
        // 物品 ID 与实例化目录使用同一个权威外壳。
        AllPrefabs[definition.Id] = definition.ShellPrefab;
        LoadedCount++;
    }

    /// <summary>按稳定 ID 注册战利品表；重复 ID 视为配置错误。</summary>
    public void RegisterLootTable(RuntimeLootTable lootTable)
    {
        if (lootTable == null || string.IsNullOrWhiteSpace(lootTable.Id))
            throw new InvalidDataException("注册的战利品表或 ID 为空");
        if (LootTables.ContainsKey(lootTable.Id))
            throw new InvalidDataException($"战利品表 ID 冲突：{lootTable.Id}");

        LootTables.Add(lootTable.Id, lootTable);
    }

    /// <summary>按稳定 ID 查询战利品表。</summary>
    public bool TryGetLootTable(string lootTableId, out RuntimeLootTable lootTable)
    {
        if (string.IsNullOrWhiteSpace(lootTableId))
        {
            lootTable = null;
            return false;
        }

        return LootTables.TryGetValue(lootTableId.Trim(), out lootTable);
    }

    /// <summary>注册 JSON Actor；同时进入通用 ItemDefinition 管线以复用对象池与存档。</summary>
    public void RegisterActorDefinition(RuntimeItemDefinition definition)
    {
        if (definition == null || !definition.IsActor)
            throw new InvalidDataException("注册的 ActorDefinition 为空或未标记为 Actor");
        if (ActorDefinitions.ContainsKey(definition.Id))
            throw new InvalidDataException($"ActorDefinition ID 冲突：{definition.Id}");
        if (AllPrefabs.TryGetValue(definition.Id, out GameObject existingPrefab) &&
            existingPrefab != null && existingPrefab != definition.ShellPrefab)
        {
            throw new InvalidDataException($"ActorDefinition ID 与已有 Prefab 别名冲突：{definition.Id}");
        }

        // 两张表必须保持原子一致，避免其中一步失败后留下半注册 Actor。
        bool itemRegistered = false;
        try
        {
            RegisterItemDefinition(definition);
            itemRegistered = true;
            ActorDefinitions.Add(definition.Id, definition);
        }
        catch
        {
            ActorDefinitions.Remove(definition.Id);
            if (itemRegistered)
            {
                ItemDefinitions.Remove(definition.Id);
                if (AllPrefabs.TryGetValue(definition.Id, out GameObject prefab) &&
                    prefab == definition.ShellPrefab)
                {
                    AllPrefabs.Remove(definition.Id);
                }
                LoadedCount = Mathf.Max(0, LoadedCount - 1);
            }
            throw;
        }
    }

    public bool TryGetActorDefinition(string actorId, out RuntimeItemDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            definition = null;
            return false;
        }

        return ActorDefinitions.TryGetValue(actorId.Trim(), out definition);
    }

    /// <summary>仅供 MOD 卸载回滚其运行时 Actor 定义。</summary>
    public bool UnregisterExternalActorDefinition(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId) ||
            !ActorDefinitions.Remove(actorId.Trim(), out RuntimeItemDefinition definition))
        {
            return false;
        }

        ItemDefinitions.Remove(actorId.Trim());
        if (AllPrefabs.TryGetValue(actorId.Trim(), out GameObject prefab) &&
            prefab == definition.ShellPrefab)
        {
            AllPrefabs.Remove(actorId.Trim());
        }
        LoadedCount = Mathf.Max(0, LoadedCount - 1);
        return true;
    }

    public bool TryGetItemDefinition(string itemId, out RuntimeItemDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            definition = null;
            return false;
        }
        return ItemDefinitions.TryGetValue(itemId, out definition);
    }

    /// <summary>
    /// 获取 JSON 物品定义中的界面显示信息。
    /// </summary>
    public bool TryGetItemPresentation(string itemId, out string displayName, out Sprite sprite)
    {
        displayName = string.Empty;
        sprite = null;
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        string requestedId = itemId.Trim();
        if (!TryGetItemDefinition(requestedId, out RuntimeItemDefinition definition))
        {
            Debug.LogError($"物品 {requestedId} 没有 JSON 定义");
            return false;
        }

        displayName = definition.DisplayName;
        sprite = definition.Sprite;
        if (sprite == null)
        {
            Debug.LogError($"物品 {requestedId} 的 JSON 定义缺少 visual.spriteAddress");
            return false;
        }

        return true;
    }

    /// <summary>按物品 ID 创建数据；JSON 目录是唯一权威来源。</summary>
    public ItemData CreateItemData(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            throw new System.ArgumentException("物品 ID 不能为空", nameof(itemId));

        string requestedId = itemId.Trim();
        if (!TryGetItemDefinition(requestedId, out RuntimeItemDefinition definition))
            throw new InvalidDataException($"物品 {requestedId} 没有 JSON 定义");

        ItemData data = definition.CreateItemData();
        data.IDName = requestedId;
        data.Guid = System.Guid.NewGuid().GetHashCode();
        return data;
    }

    public IReadOnlyList<string> GetAllItemIds()
    {
        return ItemDefinitions.Keys.OrderBy(id => id, System.StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void ApplyItemModuleConfiguration(string itemId, string moduleName, Module module, ModuleData data)
    {
        if (!TryGetItemDefinition(itemId, out RuntimeItemDefinition definition) ||
            !definition.TryGetModuleParameters(moduleName, out string json))
        {
            return;
        }

        ModuleJsonConfigurator.Apply(module, itemId, moduleName, data?.ID, json);
    }

    public void RegisterBuff(BuffDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            throw new InvalidDataException("注册的 Buff JSON 定义或 ID 为空");

        string id = definition.Id.Trim();
        if (BuffDefinitions.ContainsKey(id))
            throw new InvalidDataException($"Buff ID 冲突：{id}");

        BuffDefinitions[id] = definition;
        LoadedCount++;
    }

    /// <summary>注册一个本体或 MOD 污染定义；重复 ID 直接拒绝。</summary>
    public void RegisterContaminationDefinition(ContaminationDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            throw new InvalidDataException("注册的污染定义或 ID 为空");

        string id = definition.Id.Trim();
        if (ContaminationDefinitions.ContainsKey(id))
            throw new InvalidDataException($"污染定义 ID 冲突：{id}");
        ContaminationDefinitions.Add(id, definition);
        LoadedCount++;
    }

    /// <summary>仅供 MOD 加载失败或资源重载时回滚外部污染定义。</summary>
    public bool UnregisterExternalContaminationDefinition(string contaminationId)
    {
        if (string.IsNullOrWhiteSpace(contaminationId) ||
            !ContaminationDefinitions.Remove(contaminationId.Trim()))
        {
            return false;
        }
        LoadedCount = Mathf.Max(0, LoadedCount - 1);
        return true;
    }
    
    public RuntimeRecipe GetRecipe(string recipeName)
    {
        if (recipeCatalog.TryGet(recipeName, out RuntimeRecipe recipe))
            return recipe;

        recipeDict.TryGetValue(recipeName, out recipe);
        return recipe;
    }

    /// <summary>返回按匹配优先级预排序的指定类型配方。</summary>
    public IReadOnlyList<RuntimeRecipe> GetRecipes(RecipeType recipeType)
    {
        return recipeCatalog.GetByType(recipeType);
    }

    /// <summary>
    /// 注册校验后的运行时配方；普通合成进入多候选目录，旧输入签名索引仅保留给熔炼流程。
    /// </summary>
    public void RegisterRecipe(RuntimeRecipe recipe, bool replaceExistingSignature)
    {
        if (recipe == null || string.IsNullOrWhiteSpace(recipe.Id))
            throw new System.IO.InvalidDataException("注册的运行时配方或配方 ID 为空");
        if (recipeById.ContainsKey(recipe.Id))
            throw new System.IO.InvalidDataException($"配方 ID 冲突：{recipe.Id}");
        if (recipe.inputs == null)
            throw new System.IO.InvalidDataException($"配方 {recipe.Id} 缺少输入定义");

        string inputKey = null;
        if (recipe.inputs.recipeType != RecipeType.Crafting)
        {
            inputKey = recipe.inputs.ToString();
            if (string.IsNullOrWhiteSpace(inputKey))
                throw new System.IO.InvalidDataException($"配方 {recipe.Id} 无法生成输入签名");
            if (recipeDict.ContainsKey(inputKey) && !replaceExistingSignature)
                throw new System.IO.InvalidDataException($"配方输入签名冲突：{recipe.Id} -> {inputKey}");
            if (recipeDict.TryGetValue(inputKey, out RuntimeRecipe existing))
                Debug.LogWarning($"[RecipeCatalog] 配方 {recipe.Id} 覆盖相同输入签名的配方 {existing.Id}：{inputKey}");
        }

        recipeCatalog.Register(recipe);
        if (inputKey != null)
            recipeDict[inputKey] = recipe;
        LoadedCount++;
    }
    
    // 新增：获取InventoryInit资源
    public Inventoryinit InventoryInitGet(string inventoryInitName, out Inventoryinit inventoryInit)
    {
        InventoryInitDict.TryGetValue(inventoryInitName, out inventoryInit);
        return inventoryInit;
    }
    
    // 新增：获取BaseSkill资源
    public BaseSkill GetSkill(string skillName)
    {
        SkillDict.TryGetValue(skillName, out var skill);
        return skill;
    }

    public VFX GetVFX(string vfxName)
    {
        AllPrefabs.TryGetValue(vfxName, out var vfx);
        if (vfx == null)
        {
            Debug.LogError($"VisualEffectMManager: 特效对象为空");
            return null;
        }
        VFX vfxComponent = vfx.GetComponent<VFX>();
        if (vfxComponent == null)
        {
            Debug.LogError($"VisualEffectMManager: 特效对象 {vfxName} 没有VFX组件");
            return null;
        }
        return vfxComponent;
    }

    public void InstantiateVFX(string vfxName, Vector3 position, Quaternion rotation)
    {
        VFX vfx = GetVFX(vfxName);
        if (vfx == null)
        {
            Debug.LogError($"VisualEffectMManager: 特效对象 {vfxName} 为空");
            return;
        }
        GameObject vfxObj = Instantiate(vfx.gameObject, position, rotation);
        vfxObj.transform.SetParent(transform);
    }

    #endregion

}
