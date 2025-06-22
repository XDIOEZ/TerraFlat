using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Tilemaps;
using UnityEngine;
using System.Threading;

public class GameRes : SingletonAutoMono<GameRes>
{
    #region 字段
    public int LoadedCount = 0; // 当前已加载的资源数量
    [Header("Prefab标签列表")]
    public List<string> ADBLabels_Prefab = new List<string>();
    [Header("合成配方标签列表")]
    public List<string> ADBLabels_CraftingRecipe = new List<string>();
    [Header("TileBase标签列表")]
    public List<string> ADBLabels_TileBase = new List<string>();

    [ShowInInspector]
    public Dictionary<string, GameObject> AllPrefabs = new Dictionary<string, GameObject>();
    [ShowInInspector]
    public Dictionary<string, Recipe> recipeDict = new Dictionary<string, Recipe>();
    [ShowInInspector]
    public Dictionary<string, TileBase> tileBaseDict = new Dictionary<string, TileBase>();

    public bool isLoadFinish = false;
    private bool prefabLoaded = false;
    private bool recipeLoaded = false;
    private bool tileBaseLoaded = false;

    #endregion

    #region Unity生命周期方法

    float loadStartTime;

    public void LoadResources()
    {
        loadStartTime = Time.realtimeSinceStartup; // 记录加载开始时间
        //如果为空，则添加默认标签
        if (ADBLabels_Prefab.Count == 0)
        {
            ADBLabels_Prefab.Add("ItemPrefab");
            ADBLabels_CraftingRecipe.Add("CraftingRecipe");
            ADBLabels_TileBase.Add("TileBase");
            ADBLabels_Prefab.Add("Prefab");
        }

        LoadPrefabByLabels(ADBLabels_Prefab);
        LoadRecipeByLabels(ADBLabels_CraftingRecipe);
        LoadTileBaseByLabels(ADBLabels_TileBase);
    }


    new void Awake()
    {
        base.Awake();
        LoadResources();

        // 资源加载完成后
        Debug.Log("所有资源加载完成！");
    }

    #endregion
    #region 外部接口


    [Button]
    public GameObject InstantiatePrefab(string prefab, Vector3 position = default)
    {
        if (AllPrefabs.ContainsKey(prefab))
        {
            GameObject obj = Instantiate(AllPrefabs[prefab]);

            if (position == Vector3.zero)
                obj.transform.position = new Vector3(0, 0, 0);
            else
                obj.transform.position = position;

            return obj;
        }
        else
        {
            Debug.LogError($"预制件不存在: {prefab}");
            return null;
        }
    }

    // 获取指定名称的预制体
    public GameObject GetPrefab(string prefabName)
    {
        if (AllPrefabs.ContainsKey(prefabName))
        {
            return AllPrefabs[prefabName];
        }
        else
        {
            Debug.LogWarning($"未找到名为 \"{prefabName}\" 的预制体！");
            return null;
        }
    }


    // 获取指定名称的TileBase
    public TileBase GetTileBase(string tileBaseName)
    {
        if (tileBaseDict.ContainsKey(tileBaseName))
        {
            return tileBaseDict[tileBaseName];
        }
        else
        {
            Debug.LogWarning($"未找到名为 \"{tileBaseName}\" 的TileBase！");
            return null;
        }
    }

    #endregion



    #region 通过标签加载资源的通用方法




    // 通用资源加载方法
    private void LoadAssetsByLabels<T>(List<string> labels, System.Action<AsyncOperationHandle<IList<T>>> onLoadCompleted)
    {
        if (labels == null || labels.Count == 0)
        {
            Debug.LogWarning("标签列表为空或未提供。");
            return;
        }

        Addressables.LoadAssetsAsync<T>(labels, null, Addressables.MergeMode.Union).Completed += onLoadCompleted;
    }

    // 通用加载完成回调
    private void OnLoadCompleted<T>(AsyncOperationHandle<IList<T>> handle, Dictionary<string, T> assetDict, ref bool loadFlag, string assetType)
    {
        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            foreach (var asset in handle.Result)
            {
                if (asset == null)
                {
                    Debug.LogError($"{assetType} 为空。");
                    continue;
                }

                string assetName = asset.ToString();

                // Specifically handle GameObject type
                if (asset is GameObject prefab)
                {
                    assetName = prefab.name;

                    // Handle adding prefab to the dictionary
                    Item item = prefab.GetComponent<Item>();
                    if (item != null)
                    {
                        if (assetDict is Dictionary<string, GameObject> gameObjectDict)
                        {
                            gameObjectDict[item.Item_Data.IDName] = prefab;
                        }
                    }

                    if (assetDict is Dictionary<string, GameObject> gameObjectDict2)
                    {
                        gameObjectDict2[prefab.name] = prefab;
                    }
                }
                else if (asset is Recipe recipe)
                {
                    // Handle adding Recipe to the dictionary
                    assetName = recipe.inputs.ToString();
                    assetDict[assetName] = (T)(object)recipe;
                }
                else if (asset is TileBase tileBase)
                {
                    // Handle adding TileBase to the dictionary
                    assetName = tileBase.name;
                    assetDict[assetName] = (T)(object)tileBase;
                }

                LoadedCount++;
            }

            loadFlag = true;
            CheckLoadFinish();
            Debug.Log($"{assetType} 资源加载完成，总耗时: {(Time.realtimeSinceStartup - loadStartTime) * 1000f:F0} ms");
        }
        else
        {
            Debug.LogError($"{assetType} 加载失败。");
        }
    }

    #endregion

    #region 通过标签加载Prefab的方法

    public void LoadPrefabByLabels(List<string> labels)
    {
        LoadAssetsByLabels<GameObject>(labels, (handle) => OnLoadCompleted(handle, AllPrefabs, ref prefabLoaded, "预制体"));
    }

    #endregion

    #region 通过标签加载配方的方法

    public void LoadRecipeByLabels(List<string> labels)
    {
        LoadAssetsByLabels<Recipe>(labels, (handle) => OnLoadCompleted(handle, recipeDict, ref recipeLoaded, "配方"));
    }

    #endregion

    #region 通过标签加载TileBase的方法

    public void LoadTileBaseByLabels(List<string> labels)
    {
        LoadAssetsByLabels<TileBase>(labels, (handle) => OnLoadCompleted(handle, tileBaseDict, ref tileBaseLoaded, "TileBase"));
    }

    #endregion

    private void CheckLoadFinish()
    {
        if (prefabLoaded && recipeLoaded && tileBaseLoaded)
        {
            isLoadFinish = true;
            Debug.Log("所有资源加载完成！");
        }
    }
}
