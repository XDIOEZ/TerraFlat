using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Tilemaps;
using UnityEngine;
using System.IO;

public class GameRes : SingletonAutoMono<GameRes>
{
    #region 字段
    public int LoadedCount = 0;
    [Header("Prefab 标签列表")]
    public List<string> ADBLabels_Prefab = new List<string>();
    [Header("配方 标签列表")]
    public List<string> ADBLabels_CraftingRecipe = new List<string>();
    [Header("TileBase 标签列表")]
    public List<string> ADBLabels_TileBase = new List<string>();
    [Header("BuffData 标签列表")]
    public List<string> ADBLabels_BuffData = new List<string>();
    [Header("InventoryInit 标签列表")]
    public List<string> ADBLabels_InventoryInit = new List<string>();

    [ShowInInspector]
    public Dictionary<string, GameObject> AllPrefabs = new Dictionary<string, GameObject>();
    [ShowInInspector]
    public Dictionary<string, Recipe> recipeDict = new Dictionary<string, Recipe>();
    [ShowInInspector]
    public Dictionary<string, TileBase> tileBaseDict = new Dictionary<string, TileBase>();
    [ShowInInspector]
    public Dictionary<string, Buff_Data> BuffData_Dict = new Dictionary<string, Buff_Data>();
    [ShowInInspector]
    public Dictionary<string, Inventoryinit> InventoryInitDict = new Dictionary<string, Inventoryinit>();

    public bool isLoadFinish = false;
    #endregion

    #region Unity 生命周期

    new void Awake()
    {
        base.Awake();
        LoadResourcesSync();
    }

    #endregion

    #region 同步加载资源
public void LoadResourcesSync()
{
    // 记录上次加载的资源数量
    int previousLoadedCount = LoadedCount;
    
    // 清空现有字典并重置计数器
    ClearAllDictionaries();
    LoadedCount = 0;

    // 默认标签
    if (ADBLabels_Prefab.Count == 0)
    {
        ADBLabels_Prefab.Add("ItemPrefab");
        ADBLabels_Prefab.Add("Prefab");
        ADBLabels_CraftingRecipe.Add("CraftingRecipe");
        ADBLabels_TileBase.Add("TileBase");
        ADBLabels_BuffData.Add("Buff");
        ADBLabels_InventoryInit.Add("InventoryInit");
    }

    // 分别同步加载
    SyncLoadAssetsByLabels<GameObject>(
        ADBLabels_Prefab,
        AllPrefabs,
        HandlePrefab);
    SyncLoadAssetsByLabels<Recipe>(
        ADBLabels_CraftingRecipe,
        recipeDict,
        null);
    SyncLoadAssetsByLabels<TileBase>(
        ADBLabels_TileBase,
        tileBaseDict,
        null);
    // 额外处理：BuffData
    SyncLoadAssetsByLabels<Buff_Data>(
        ADBLabels_BuffData,
        BuffData_Dict,
        null);
    // 新增：加载InventoryInit资源
    SyncLoadAssetsByLabels<Inventoryinit>(
        ADBLabels_InventoryInit,
        InventoryInitDict,
        null);

    isLoadFinish = true;
    
    // 计算本次加载的资源数量
    int currentLoadedCount = LoadedCount;
    int difference = currentLoadedCount - previousLoadedCount;
    
    string differenceText = difference > 0 ? $"(比上次多加载 {difference} 个)" : 
                           difference < 0 ? $"(比上次少加载 {Mathf.Abs(difference)} 个)" : 
                           "(与上次加载数量相同)";
    
    Debug.Log($"所有资源同步加载完成！共加载 {currentLoadedCount} 个资源 {differenceText}");
}

// 清空所有字典
private void ClearAllDictionaries()
{
    AllPrefabs.Clear();
    recipeDict.Clear();
    tileBaseDict.Clear();
    BuffData_Dict.Clear();
    InventoryInitDict.Clear();
}

[Button("热更新所有资源")]
public void HotReloadAllResources()
{
    Debug.Log("开始热更新所有资源...");
    LoadResourcesSync();
    Debug.Log("热更新完成！");
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
                Recipe recipe => recipe.inputs.ToString(),
                TileBase tile => tile.name,
                Buff_Data buff => buff.name,
                Inventoryinit inventoryInit => inventoryInit.name,
                _ => asset.ToString()
            };

            dict[key] = asset;
            LoadedCount++;
        }

      //  Debug.Log($"同步加载 {typeof(T).Name} 完成，数量：{assets.Count}");
    }

    // 专门处理 Prefab 的额外逻辑：把 Item ID 也加入字典
    private void HandlePrefab(GameObject prefab)
    {
        var item = prefab.GetComponent<Item>();
        if (item != null && !string.IsNullOrEmpty(item.itemData.IDName))
        {
            AllPrefabs[item.itemData.IDName] = prefab;
        }
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

            return obj;
        }
        Debug.LogError($"预制件不存在:{prefab}");
        return null;
    }

    public GameObject GetPrefab(string prefabName)
    {
        AllPrefabs.TryGetValue(prefabName, out var go);
        return go;
    }
    public void GetPrefab(string prefabName, out GameObject go)
    {
        AllPrefabs.TryGetValue(prefabName, out go);
    }

  public Item GetItem(string prefabName)
{
    GameObject prefab = GetPrefab(prefabName);
    if (prefab == null)
    {
        Debug.LogError($"无法获取预制体: {prefabName}，返回null");
        return null;
    }
    
    Item item = prefab.GetComponent<Item>();
    if (item == null)
    {
        Debug.LogError($"预制体 {prefabName} 上没有找到 Item 组件");
        return null;
    }
    
    return item;
}

    public TileBase GetTileBase(string tileBaseName)
    {
        tileBaseDict.TryGetValue(tileBaseName, out var tile);
        return tile;
    }
    
    public Buff_Data GetBuffData(string buffName)
    {
        BuffData_Dict.TryGetValue(buffName, out var buff);
        return buff;
    }
    
    public Recipe GetRecipe(string recipeName)
    {
        recipeDict.TryGetValue(recipeName, out var recipe);
        return recipe;
    }
    
    // 新增：获取InventoryInit资源
    public Inventoryinit InventoryInitGet(string inventoryInitName, out Inventoryinit inventoryInit)
    {
        InventoryInitDict.TryGetValue(inventoryInitName, out inventoryInit);
        return inventoryInit;
    }

    #endregion
}