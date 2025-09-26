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
    [Header("Tag配方 标签列表")]
    public List<string> ADBLabels_CraftingRecipe_Tag = new List<string>();
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
    public Dictionary<string, Recipe_Tag> recipeTagDict = new Dictionary<string, Recipe_Tag>();
    [ShowInInspector]
    public Dictionary<string, TileBase> tileBaseDict = new Dictionary<string, TileBase>();
    [ShowInInspector]
    public Dictionary<string, Buff_Data> BuffData_Dict = new Dictionary<string, Buff_Data>();
    [ShowInInspector]
    public Dictionary<string, Inventoryinit> InventoryInitDict = new Dictionary<string, Inventoryinit>();

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

    #endregion

    #region 协程加载资源（带进度）
    
    private System.Collections.IEnumerator LoadResourcesWithProgress()
    {
        // 记录上次加载的资源数量
        int previousLoadedCount = LoadedCount;
        
        // 清空现有字典并重置计数器
        ClearAllDictionaries();
        LoadedCount = 0;
        loadedAssetsCount = 0;

        // 默认标签
        if (ADBLabels_Prefab.Count == 0)
        {
            ADBLabels_Prefab.Add("ItemPrefab");
            ADBLabels_Prefab.Add("Prefab");
            ADBLabels_CraftingRecipe.Add("CraftingRecipe");
            ADBLabels_CraftingRecipe_Tag.Add("CraftingRecipe_Tag");
            ADBLabels_TileBase.Add("TileBase");
            ADBLabels_BuffData.Add("Buff");
            ADBLabels_InventoryInit.Add("InventoryInit");
        }

        // 估算总资源数量（用于进度条）
        totalAssetsToLoad = EstimateTotalAssets();
        loadingProgress = 0f;

        // 分别同步加载
        yield return StartCoroutine(SyncLoadAssetsWithProgress<GameObject>(
            ADBLabels_Prefab,
            AllPrefabs,
            HandlePrefab,
            "加载预制体"));
            
        yield return StartCoroutine(SyncLoadAssetsWithProgress<Recipe>(
            ADBLabels_CraftingRecipe,
            recipeDict,
            null,
            "加载配方"));
            
        yield return StartCoroutine(SyncLoadAssetsWithProgress<Recipe_Tag>(
            ADBLabels_CraftingRecipe_Tag,
            recipeTagDict,
            null,
            "加载Tag配方"));
            
        yield return StartCoroutine(SyncLoadAssetsWithProgress<TileBase>(
            ADBLabels_TileBase,
            tileBaseDict,
            null,
            "加载TileBase"));
            
        // 额外处理：BuffData
        yield return StartCoroutine(SyncLoadAssetsWithProgress<Buff_Data>(
            ADBLabels_BuffData,
            BuffData_Dict,
            null,
            "加载Buff数据"));
            
        // 新增：加载InventoryInit资源
        yield return StartCoroutine(SyncLoadAssetsWithProgress<Inventoryinit>(
            ADBLabels_InventoryInit,
            InventoryInitDict,
            null,
            "加载初始库存"));

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
        return (ADBLabels_Prefab.Count + 
                ADBLabels_CraftingRecipe.Count + 
                ADBLabels_CraftingRecipe_Tag.Count +
                ADBLabels_TileBase.Count + 
                ADBLabels_BuffData.Count + 
                ADBLabels_InventoryInit.Count) * 100;
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
                Recipe recipe => recipe.inputs.ToString(),
                Recipe_Tag recipeTag => recipeTag.GetRecipeKey(),
                TileBase tile => tile.name,
                Buff_Data buff => buff.name,
                Inventoryinit inventoryInit => inventoryInit.name,
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

    #endregion

    #region 同步加载资源（原有方法保持不变）
    
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
        ADBLabels_CraftingRecipe_Tag.Add("CraftingRecipe_Tag");
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
    SyncLoadAssetsByLabels<Recipe_Tag>(
        ADBLabels_CraftingRecipe_Tag,
        recipeTagDict,
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
    recipeTagDict.Clear();
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
                Recipe_Tag recipeTag => recipeTag.GetRecipeKey(),
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
        if (AllPrefabs.TryGetValue(prefabName, out var go))
        {
            return go;
        }
        else
        {
            // 输出错误日志，包含关键信息便于调试
            Debug.LogError($"找不到名为 [{prefabName}] 的预制体！请检查AllPrefabs字典中是否正确注册了该预制体", this);
            return null;
        }
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
    
    public Recipe_Tag GetRecipeTag(string recipeTagName)
    {
        recipeTagDict.TryGetValue(recipeTagName, out var recipeTag);
        return recipeTag;
    }
    
    // 新增：获取InventoryInit资源
    public Inventoryinit InventoryInitGet(string inventoryInitName, out Inventoryinit inventoryInit)
    {
        InventoryInitDict.TryGetValue(inventoryInitName, out inventoryInit);
        return inventoryInit;
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