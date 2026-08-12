using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.Tilemaps;
using UnityEngine;
using System.IO;
using System.Linq;
using FlatWorld.Gameplay.Quests;

public class GameRes : SingletonAutoMono<GameRes>
{
    #region 字段
    public int LoadedCount = 0;
    
    [Header("资源标签列表")]
    public List<string> ADBLabels = new List<string>();

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

    private readonly Dictionary<string, ItemData> legacyItemDataTemplates =
        new Dictionary<string, ItemData>(System.StringComparer.OrdinalIgnoreCase);

    [Header("配方字典")]
    [ShowInInspector]
    public Dictionary<string, RuntimeRecipe> recipeDict = new Dictionary<string, RuntimeRecipe>();

    [Header("配方ID字典")]
    [ShowInInspector]
    public Dictionary<string, RuntimeRecipe> recipeById = new Dictionary<string, RuntimeRecipe>();

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

    [Header("初始库存字典")]
    [ShowInInspector]
    public Dictionary<string, Inventoryinit> InventoryInitDict = new Dictionary<string, Inventoryinit>();

    [Header("技能字典")]
    [ShowInInspector]
    public Dictionary<string, BaseSkill> SkillDict = new Dictionary<string, BaseSkill>();

    public bool isLoadFinish = false;
    
    // 进度条相关字段
    private bool showLoadingGUI = false;
    private string loadingText = "";
    private float loadingProgress = 0f;
    private int totalAssetsToLoad = 0;
    private int loadedAssetsCount = 0;

    #endregion

    #region Unity 生命周期

    new void Awake()
    {
        base.Awake();
        // 初始化时显示加载界面
        showLoadingGUI = true;
        loadingText = "正在加载资源...";
        StartCoroutine(LoadResourcesWithProgress());
    }

    public void Update()
    {
            // 这里可以添加一些调试输入，例如按下某个键可以重新加载资源
            if (Input.GetKeyDown(KeyCode.F5))
            {
                Debug.Log("F5键被按下，开始热更新资源...");
                showLoadingGUI = true;
                StartCoroutine(LoadResourcesWithProgress());
            }
    }

    #endregion

    #region 协程加载资源（带进度）
    
    private System.Collections.IEnumerator LoadResourcesWithProgress()
    {
        isLoadFinish = false;

        // 记录上次加载的资源数量
        int previousLoadedCount = LoadedCount;
        
        // 清空现有字典并重置计数器
        ClearAllDictionaries();
        LoadedCount = 0;
        loadedAssetsCount = 0;

        // 默认标签
        if (ADBLabels.Count == 0)
        {
            ADBLabels.Add("ItemPrefab");
            ADBLabels.Add("Prefab");
            ADBLabels.Add("TileBase");
            ADBLabels.Add("TileBlock");
            ADBLabels.Add("InventoryInit");
            ADBLabels.Add("Skill");
        }

        // 估算总资源数量（用于进度条）
        totalAssetsToLoad = EstimateTotalAssets();
        loadingProgress = 0f;

        HashSet<string> redundantItemPrefabPaths;
        try
        {
            redundantItemPrefabPaths = ItemDefinitionCatalogLoader.GetRedundantBuiltInPrefabPaths();
        }
        catch (System.Exception exception)
        {
            loadingText = $"物品定义预检失败：{exception.Message}";
            loadingProgress = 1f;
            Debug.LogError(loadingText);
            Debug.LogException(exception);
            yield break;
        }

        // 先解析 Addressables 位置，再过滤已迁入 JSON 的旧变体 Prefab。
        yield return StartCoroutine(SyncLoadPrefabsWithProgress(
            new List<string> { "ItemPrefab", "Prefab" },
            redundantItemPrefabPaths,
            "加载运行时预制体"));

        loadingText = "加载 JSON 物品定义";
        int loadedItemDefinitionCount = 0;
        System.Exception itemDefinitionError = null;
        yield return StartCoroutine(ItemDefinitionCatalogLoader.LoadBuiltInAsync(
            this,
            count => loadedItemDefinitionCount = count,
            exception => itemDefinitionError = exception,
            progress => loadingProgress = Mathf.Clamp01(progress)));
        if (itemDefinitionError != null)
        {
            loadingText = $"物品定义加载失败：{itemDefinitionError.Message}";
            loadingProgress = 1f;
            Debug.LogError(loadingText);
            Debug.LogException(itemDefinitionError);
            yield break;
        }
        loadedAssetsCount += loadedItemDefinitionCount;
        loadingProgress = Mathf.Clamp01((float)loadedAssetsCount / totalAssetsToLoad);

        loadingText = "加载 JSON Actor 定义";
        int loadedActorDefinitionCount = 0;
        System.Exception actorDefinitionError = null;
        yield return StartCoroutine(ActorDefinitionCatalogLoader.LoadBuiltInAsync(
            this,
            count => loadedActorDefinitionCount = count,
            exception => actorDefinitionError = exception,
            progress => loadingProgress = Mathf.Clamp01(progress)));
        if (actorDefinitionError != null)
        {
            loadingText = $"Actor 定义加载失败：{actorDefinitionError.Message}";
            loadingProgress = 1f;
            Debug.LogError(loadingText);
            Debug.LogException(actorDefinitionError);
            yield break;
        }
        loadedAssetsCount += loadedActorDefinitionCount;
        loadingProgress = Mathf.Clamp01((float)loadedAssetsCount / totalAssetsToLoad);
            
        loadingText = "加载JSON配方";
        int loadedRecipeCount = 0;
        System.Exception recipeLoadError = null;
        yield return StartCoroutine(RecipeCatalogLoader.LoadBuiltInAsync(
            this,
            count => loadedRecipeCount = count,
            exception => recipeLoadError = exception));
        if (recipeLoadError != null)
        {
            loadingText = $"配方加载失败：{recipeLoadError.Message}";
            loadingProgress = 1f;
            Debug.LogError(loadingText);
            Debug.LogException(recipeLoadError);
            yield break;
        }
        loadedAssetsCount += loadedRecipeCount;
        loadingProgress = Mathf.Clamp01((float)loadedAssetsCount / totalAssetsToLoad);

        loadingText = "加载JSON Buff";
        int loadedBuffCount = 0;
        System.Exception buffLoadError = null;
        yield return StartCoroutine(BuffCatalogLoader.LoadBuiltInAsync(
            this,
            count => loadedBuffCount = count,
            exception => buffLoadError = exception));
        if (buffLoadError != null)
        {
            loadingText = $"Buff 加载失败：{buffLoadError.Message}";
            loadingProgress = 1f;
            Debug.LogError(loadingText);
            Debug.LogException(buffLoadError);
            yield break;
        }
        loadedAssetsCount += loadedBuffCount;
        loadingProgress = Mathf.Clamp01((float)loadedAssetsCount / totalAssetsToLoad);

        loadingText = "加载 JSON 任务";
        int loadedQuestCount = 0;
        System.Exception questLoadError = null;
        yield return StartCoroutine(QuestCatalogLoader.LoadBuiltInAsync(
            count => loadedQuestCount = count,
            exception => questLoadError = exception));
        if (questLoadError != null)
        {
            loadingText = $"任务加载失败：{questLoadError.Message}";
            loadingProgress = 1f;
            Debug.LogError(loadingText);
            Debug.LogException(questLoadError);
            yield break;
        }
        loadedAssetsCount += loadedQuestCount;
        loadingProgress = Mathf.Clamp01((float)loadedAssetsCount / totalAssetsToLoad);
            
        yield return StartCoroutine(SyncLoadAssetsWithProgress<TileBase>(
            new List<string> { "TileBase" },
            tileBaseDict,
            null,
            "加载TileBase"));

        // 新增：加载 Tile_Block 资源（地块逻辑 ScriptableObject）
        yield return StartCoroutine(SyncLoadAssetsWithProgress<Tile_Block>(
            new List<string> { "TileBlock" },
            TileBlockDict,
            null,
            "加载TileBlock SO"));
            
        // 新增：加载InventoryInit资源
        yield return StartCoroutine(SyncLoadAssetsWithProgress<Inventoryinit>(
            new List<string> { "InventoryInit" },
            InventoryInitDict,
            null,
            "加载初始库存"));

        // 新增：加载Skill资源
        yield return StartCoroutine(SyncLoadAssetsWithProgress<BaseSkill>(
            new List<string> { "Skill" },
            SkillDict,
            null,
            "加载技能"));

        // 内建资源完成后再加载 MOD，确保 MOD 可以引用游戏本体内容。
        ModRuntimeManager modRuntime = ModRuntimeManager.Ensure(gameObject);
        yield return StartCoroutine(modRuntime.LoadEnabledMods(this, ReportModLoadingProgress));
        if (!modRuntime.IsReady)
        {
            ClearAllDictionaries();
            loadingText = $"MOD 加载失败：{modRuntime.FailureReason}";
            loadingProgress = 1f;
            Debug.LogError(loadingText);
            yield break;
        }

        isLoadFinish = true;
        showLoadingGUI = false; // 隐藏加载界面
        
        // 计算本次加载的资源数量
        int currentLoadedCount = LoadedCount;
        int difference = currentLoadedCount - previousLoadedCount;
        
        string differenceText = difference > 0 ? $"(比上次多加载 {difference} 个)" : 
                               difference < 0 ? $"(比上次少加载 {Mathf.Abs(difference)} 个)" : 
                               "(与上次加载数量相同)";
        
        Debug.Log($"所有资源同步加载完成！共加载 {currentLoadedCount} 个资源 {differenceText}");
    }

    // 估算总资源数量
    private int EstimateTotalAssets()
    {
        // 这里可以根据经验数据估算，或者先进行一次异步预加载来获取数量
        // 简单估算：假设每种类型大约有100个资源
        return ADBLabels.Count * 100;
    }

    private void ReportModLoadingProgress(string text, float progress)
    {
        loadingText = text;
        loadingProgress = Mathf.Clamp01(progress);
    }

    // 带进度的同步加载
    private System.Collections.IEnumerator SyncLoadAssetsWithProgress<T>(
        List<string> labels,
        IDictionary<string, T> dict,
        System.Action<T> onLoadedAsset,
        string progressText)
    {
        if (labels == null || labels.Count == 0) yield break;

        var handle = Addressables.LoadAssetsAsync<T>(
            labels,
            null,
            Addressables.MergeMode.Union);

        // 更新进度文本
        loadingText = progressText;
        
        // 等待加载完成，同时更新进度
        while (!handle.IsDone)
        {
            // 更新进度（这里使用handle.PercentComplete，实际可能需要根据具体需求调整）
            loadingProgress = Mathf.Clamp01((float)loadedAssetsCount / totalAssetsToLoad);
            yield return null;
        }

        // 阻塞等待
        IList<T> assets = handle.WaitForCompletion();
        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"同步加载 {typeof(T).Name} 失败");
            yield break;
        }

        foreach (var asset in assets)
        {
            if (asset == null) continue;

            // 回调（直接传入 asset，就不会有类型转换问题）
            onLoadedAsset?.Invoke(asset);

            string key = asset switch
            {
                GameObject go => go.name,
                TileBase tile => tile.name,
                Inventoryinit inventoryInit => inventoryInit.name,
                BaseSkill skill => skill.name,
                Tile_Block tileBlock => tileBlock.name,
                _ => asset.ToString()
            };

            dict[key] = asset;
            LoadedCount++;
            loadedAssetsCount++;
            
            // 每加载一定数量的资源就更新一次进度
            if (loadedAssetsCount % 10 == 0)
            {
                loadingProgress = Mathf.Clamp01((float)loadedAssetsCount / totalAssetsToLoad);
                yield return null; // 让出控制权，避免卡顿
            }
        }

        // 更新进度
        loadingProgress = Mathf.Clamp01((float)loadedAssetsCount / totalAssetsToLoad);
    }

    private System.Collections.IEnumerator SyncLoadPrefabsWithProgress(
        List<string> labels,
        HashSet<string> excludedPaths,
        string progressText)
    {
        if (labels == null || labels.Count == 0)
            yield break;

        AsyncOperationHandle<IList<IResourceLocation>> locationsHandle =
            Addressables.LoadResourceLocationsAsync(labels, Addressables.MergeMode.Union, typeof(GameObject));
        while (!locationsHandle.IsDone)
        {
            loadingText = progressText;
            yield return null;
        }
        if (locationsHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError("无法解析 Prefab Addressables 位置");
            yield break;
        }

        excludedPaths ??= new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        List<IResourceLocation> locations = locationsHandle.Result
            .Where(location => location != null)
            .GroupBy(location => location.PrimaryKey, System.StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(location => !excludedPaths.Contains((location.PrimaryKey ?? string.Empty).Replace('\\', '/')))
            .ToList();
        int skippedCount = locationsHandle.Result.Count - locations.Count;

        AsyncOperationHandle<IList<GameObject>> assetsHandle =
            Addressables.LoadAssetsAsync<GameObject>(locations, null, true);
        while (!assetsHandle.IsDone)
        {
            loadingText = $"{progressText}（已跳过 {skippedCount} 个 JSON 变体）";
            loadingProgress = Mathf.Clamp01((float)loadedAssetsCount / totalAssetsToLoad);
            yield return null;
        }
        if (assetsHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError("加载运行时 Prefab 失败");
            yield break;
        }

        foreach (GameObject prefab in assetsHandle.Result)
        {
            if (prefab == null)
                continue;
            RegisterPrefabAlias(prefab.name, prefab);
            HandlePrefab(prefab);
            LoadedCount++;
            loadedAssetsCount++;
            if (loadedAssetsCount % 10 == 0)
                yield return null;
        }

        Debug.Log($"[GameRes] Prefab 加载计划：加载 {locations.Count}，跳过 JSON 旧变体 {skippedCount}");
        loadingProgress = Mathf.Clamp01((float)loadedAssetsCount / totalAssetsToLoad);
        Addressables.Release(locationsHandle);
    }

    #endregion

    #region 同步加载资源（原有方法保持不变）
    
public void LoadResourcesSync()
{
    showLoadingGUI = true;
    StopAllCoroutines();
    StartCoroutine(LoadResourcesWithProgress());
}

// 清空所有字典
private void ClearAllDictionaries()
{
    AllPrefabs.Clear();
    ItemDefinitions.Clear();
    ActorDefinitions.Clear();
    ActorDefinitionCatalogLoader.ResetRuntimeCatalog();
    legacyItemDataTemplates.Clear();
    recipeDict.Clear();
    recipeById.Clear();
    tileBaseDict.Clear();
    TileBlockDict.Clear();
    BuffDefinitions.Clear();
    QuestCatalog.Reset();
    InventoryInitDict.Clear();
    SkillDict.Clear();
}

[Button("热更新所有资源")]
public void HotReloadAllResources()
{
    Debug.Log("开始热更新所有资源...");
    LoadResourcesSync();
    Debug.Log("已开始重新加载本体资源与 MOD；完成前不可进入世界。");
}

    // 通用同步加载，并填充到字典
    private void SyncLoadAssetsByLabels<T>(
        List<string> labels,
        IDictionary<string, T> dict,
        System.Action<T> onLoadedAsset = null)
    {
        if (labels == null || labels.Count == 0) return;

        var handle = Addressables.LoadAssetsAsync<T>(
            labels,
            null,
            Addressables.MergeMode.Union);

        // 阻塞等待
        IList<T> assets = handle.WaitForCompletion();
        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"同步加载 {typeof(T).Name} 失败");
            return;
        }

        foreach (var asset in assets)
        {
            if (asset == null) continue;

            // 回调（直接传入 asset，就不会有类型转换问题）
            onLoadedAsset?.Invoke(asset);

            string key = asset switch
            {
                GameObject go => go.name,
                TileBase tile => tile.name,
                Inventoryinit inventoryInit => inventoryInit.name,
                BaseSkill skill => skill.name,
                Tile_Block tileBlock => string.IsNullOrEmpty(tileBlock.tileItemName) ? tileBlock.name : tileBlock.tileItemName,
                _ => asset.ToString()
            };

            dict[key] = asset;
            LoadedCount++;
        }

      //  Debug.Log($"同步加载 {typeof(T).Name} 完成，数量：{assets.Count}");
    }

    // 专门处理 Prefab 的额外逻辑：把 Item ID 和独立模块 ID 也加入字典
    private void HandlePrefab(GameObject prefab)
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

            RegisterPrefabAlias(module.CanonicalModuleId, prefab);
            RegisterPrefabAlias(module._Data?.ID, prefab);
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
            Item existingItem = existingPrefab.GetComponent<Item>();
            Item incomingItem = prefab.GetComponent<Item>();

            // JSON shellPrefab 必须稳定指向真实 Item；普通 Prefab 或模块别名不得覆盖同名外壳。
            if (existingItem != null && incomingItem == null)
                return;
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
    
   void OnTriggerEnter2D()
    {
        
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

    public void RegisterItemDefinition(RuntimeItemDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || definition.ShellPrefab == null)
            throw new InvalidDataException("注册的 ItemDefinition、ID 或外壳为空");
        if (ItemDefinitions.ContainsKey(definition.Id))
            throw new InvalidDataException($"ItemDefinition ID 冲突：{definition.Id}");

        ItemDefinitions.Add(definition.Id, definition);
        // 兼容仍以 AllPrefabs.ContainsKey 判断物品是否存在的配方与玩法代码。
        AllPrefabs[definition.Id] = definition.ShellPrefab;
        LoadedCount++;
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

    /// <summary>获取物品界面显示信息；JSON 定义优先，旧物品回退到独立 Prefab。</summary>
    public bool TryGetItemPresentation(string itemId, out string displayName, out Sprite sprite)
    {
        displayName = string.Empty;
        sprite = null;
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        string requestedId = itemId.Trim();
        if (TryGetItemDefinition(requestedId, out RuntimeItemDefinition definition))
        {
            displayName = definition.DisplayName;
            sprite = definition.Sprite;
            return true;
        }

        if (!AllPrefabs.TryGetValue(requestedId, out GameObject prefab) || prefab == null)
            return false;

        Item item = prefab.GetComponent<Item>() ?? prefab.GetComponentInChildren<Item>(true);
        displayName = !string.IsNullOrWhiteSpace(item?.itemData?.GameName)
            ? item.itemData.GameName
            : requestedId;
        sprite = item?.Sprite != null
            ? item.Sprite.sprite
            : prefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite;
        return item != null;
    }

    /// <summary>按物品 ID 创建数据；JSON 定义优先，未迁移物品回退到旧 Prefab。</summary>
    public ItemData CreateItemData(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            throw new System.ArgumentException("物品 ID 不能为空", nameof(itemId));

        string requestedId = itemId.Trim();
        ItemData data;
        if (TryGetItemDefinition(requestedId, out RuntimeItemDefinition definition))
        {
            data = definition.CreateItemData();
        }
        else
        {
            if (!legacyItemDataTemplates.TryGetValue(requestedId, out ItemData template))
            {
                GameObject prefab = GetPrefab(requestedId, false);
                Item item = prefab != null ? prefab.GetComponent<Item>() : null;
                template = item?.Get_NewItemData();
                if (template != null)
                {
                    template.Guid = 0;
                    legacyItemDataTemplates.Add(requestedId, template);
                }
            }

            data = template != null
                ? FastCloner.FastCloner.DeepClone(template)
                : null;
        }

        if (data != null)
        {
            data.IDName = requestedId;
            data.Guid = System.Guid.NewGuid().GetHashCode();
        }
        return data;
    }

    public IReadOnlyList<string> GetAllItemIds()
    {
        var ids = new HashSet<string>(ItemDefinitions.Keys, System.StringComparer.OrdinalIgnoreCase);
        var definitionShells = new HashSet<GameObject>();
        foreach (RuntimeItemDefinition definition in ItemDefinitions.Values)
            definitionShells.Add(definition.ShellPrefab);

        foreach (KeyValuePair<string, GameObject> pair in AllPrefabs)
        {
            if (pair.Value == null || definitionShells.Contains(pair.Value))
                continue;
            Item item = pair.Value.GetComponent<Item>();
            if (item?.itemData != null && item is not Player && item is not Map)
                ids.Add(item.itemData.IDName);
        }
        return new List<string>(ids);
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
    
    public RuntimeRecipe GetRecipe(string recipeName)
    {
        if (recipeById.TryGetValue(recipeName, out RuntimeRecipe recipe))
            return recipe;

        recipeDict.TryGetValue(recipeName, out recipe);
        return recipe;
    }

    /// <summary>
    /// 注册校验后的运行时配方，同时建立 ID 与旧输入签名索引。
    /// </summary>
    public void RegisterRecipe(RuntimeRecipe recipe, bool replaceExistingSignature)
    {
        if (recipe == null || string.IsNullOrWhiteSpace(recipe.Id))
            throw new System.IO.InvalidDataException("注册的运行时配方或配方 ID 为空");
        if (recipeById.ContainsKey(recipe.Id))
            throw new System.IO.InvalidDataException($"配方 ID 冲突：{recipe.Id}");

        string inputKey = recipe.inputs?.ToString();
        if (string.IsNullOrWhiteSpace(inputKey))
            throw new System.IO.InvalidDataException($"配方 {recipe.Id} 无法生成输入签名");
        if (recipeDict.ContainsKey(inputKey) && !replaceExistingSignature)
            throw new System.IO.InvalidDataException($"配方输入签名冲突：{recipe.Id} -> {inputKey}");
        if (recipeDict.TryGetValue(inputKey, out RuntimeRecipe existing))
            Debug.LogWarning($"[RecipeCatalog] 配方 {recipe.Id} 覆盖相同输入签名的配方 {existing.Id}：{inputKey}");

        recipeById.Add(recipe.Id, recipe);
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

    #region GUI进度条显示
    
    private void OnGUI()
    {
        if (!showLoadingGUI) return;

        // 设置GUI样式
        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = MakeTex(2, 2, new Color(0f, 0f, 0f, 0.8f));
        
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 16;
        labelStyle.normal.textColor = Color.white;
        labelStyle.alignment = TextAnchor.MiddleCenter;
        
        GUIStyle progressStyle = new GUIStyle(GUI.skin.box);
        progressStyle.normal.background = MakeTex(2, 2, new Color(0.2f, 0.6f, 1f, 1f));

        // 计算位置和尺寸
        float width = 400;
        float height = 100;
        float x = (Screen.width - width) / 2;
        float y = (Screen.height - height) / 2;

        // 绘制背景框
        GUI.Box(new Rect(x, y, width, height), "", boxStyle);
        
        // 绘制标题
        GUI.Label(new Rect(x, y + 10, width, 20), loadingText, labelStyle);
        
        // 绘制进度条背景
        GUI.Box(new Rect(x + 20, y + 40, width - 40, 20), "", GUI.skin.box);
        
        // 绘制进度条
        GUI.Box(new Rect(x + 22, y + 42, (width - 44) * loadingProgress, 16), "", progressStyle);
        
        // 绘制进度文本
        GUI.Label(new Rect(x, y + 65, width, 20), 
                 $"{Mathf.RoundToInt(loadingProgress * 100)}%", labelStyle);
    }
    
    // 创建纹理的辅助方法
    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; ++i)
        {
            pix[i] = col;
        }
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }
    
    #endregion
}
