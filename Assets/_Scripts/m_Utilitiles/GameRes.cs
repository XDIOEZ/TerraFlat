using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Tilemaps;
using OfficeOpenXml;
using Sirenix.OdinInspector;

public class GameRes : SingletonAutoMono<GameRes>
{
    #region �ֶ�
    // ���ؽ���
    public int LoadedCount = 0; // ��ǰ�Ѽ��ص���Դ����
    [Header("Prefab��ǩ�б�")]
    public List<string> ADBLabels_Prefab = new List<string>();
    [Header("�ϳ��䷽��ǩ�б�")]
    public List<string> ADBLabels_CraftingRecipe = new List<string>();
    [Header("TileBase��ǩ�б�")]
    public List<string> ADBLabels_TileBase = new List<string>();

    // �ϲ����Ԥ�����ֵ�
    [ShowInInspector]
    public Dictionary<string, GameObject> AllPrefabs = new Dictionary<string, GameObject>(); // ֻ����Ԥ����

    // ��Ϊ�洢�䷽�����ֵ�
    [ShowInInspector]
    public Dictionary<string, Recipe> recipeDict = new Dictionary<string, Recipe>();
    // ��Ϊ�洢TileBase�����ֵ�
    [ShowInInspector]
    public Dictionary<string, TileBase> tileBaseDict = new Dictionary<string, TileBase>();


    #endregion

    #region Unity�������ڷ���

    public void Awake()
    {
        ADBLabels_Prefab.Add("Prefab");
        ADBLabels_CraftingRecipe.Add("CraftingRecipe");
        ADBLabels_TileBase.Add("TileBase");

        //����Ԥ�Ƽ�
        LoadPrefabByLabels(ADBLabels_Prefab);
        //�����䷽
        LoadRecipeByLabels(ADBLabels_CraftingRecipe);
        //����TileBase
        LoadTileBaseByLabels(ADBLabels_TileBase);
    }
    void Start()
    {
        print("GameResManager Start");
    }
    #endregion

    [Button]
    public GameObject InstantiatePrefab(string prefab, Vector3 position = default)
    {
        if (AllPrefabs.ContainsKey(prefab))
        {
            GameObject obj = Instantiate(AllPrefabs[prefab]);
            if(position == Vector3.zero)
            obj.transform.position = new Vector3(0, 0, 0);
            else
            obj.transform.position = position;

            return obj;
        }
        else
        {
            Debug.LogError($"Ԥ�Ƽ�������: {prefab}");
            return null;
        }
    }

    GameObject GetPrefab(string prefabName)
    {
        if (AllPrefabs.ContainsKey(prefabName))
        {
            return AllPrefabs[prefabName];
        }
        else
        {
            Debug.LogWarning($"δ�ҵ���Ϊ \"{prefabName}\" ��Ԥ���壡");
            return null;
        }
    }

    public TileBase GetTileBase(string tileBaseName)
    {
        return tileBaseDict.ContainsKey(tileBaseName)? tileBaseDict[tileBaseName] : null;
    }

    #region ͨ����ǩ����Prefab�ķ���
    /// <summary>
    /// ͨ����ǩ�б����Ԥ�Ƽ�
    /// </summary>
    public void LoadPrefabByLabels(List<string> labels)
    {
        if (labels == null || labels.Count == 0)
        {
            Debug.LogWarning("��ǩ�б�Ϊ�ջ�δ�ṩ��");
            return;
        }

        // ʹ�ñ�ǩ�б������Դ
        Addressables.LoadAssetsAsync<GameObject>(labels, null, Addressables.MergeMode.Union).Completed += OnLoadCompleted;

    }

    /// <summary>
    /// ��Դ������ɵĻص�
    /// </summary>
    /// <param name="handle">�첽�������</param>
    void OnLoadCompleted(AsyncOperationHandle<IList<GameObject>> handle)
    {
        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            foreach (var prefab in handle.Result)
            {
                if (prefab == null)
                {
                    Debug.LogError("���ص�Ԥ�Ƽ�Ϊ�ա�");
                    continue;
                }

                if (AllPrefabs.ContainsKey(prefab.name))
                {
                    Debug.LogWarning($"Ԥ�Ƽ��Ѵ���: {prefab.name}");
                }
                else
                {
                    AllPrefabs[prefab.name] = prefab;
                    LoadedCount++;
                    //Debug.Log($"�ɹ����ز����Ԥ�Ƽ�: {prefab.name}");
                }
            }
        }
        else
        {
            Debug.LogError("��Դ����ʧ�ܡ�");
        }
    }
    #endregion

    #region ͨ����ǩ�����䷽�ķ���
        /// <summary>
        /// ͨ����ǩ�б�����䷽
        /// </summary>
        public void LoadRecipeByLabels(List<string> labels)
        {
            if (labels == null || labels.Count == 0)
            {
                Debug.LogWarning("��ǩ�б�Ϊ�ջ�δ�ṩ��");
                return;
            }

            // ʹ�ñ�ǩ�б������Դ
            Addressables.LoadAssetsAsync<Recipe>(labels, null, Addressables.MergeMode.Union).Completed += OnRecipeLoadCompleted;

        }
        /// <summary>
        /// �䷽������ɵĻص�
        /// </summary>
        /// <param name="handle">�첽�������</param>
        void OnRecipeLoadCompleted(AsyncOperationHandle<IList<Recipe>> handle)
        {
            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                foreach (var recipe in handle.Result)
                {
                    if (recipe == null)
                    {
                        Debug.LogError("���ص��䷽Ϊ�ա�");
                        continue;
                    }

                    if (recipeDict.ContainsKey(recipe.inputs.ToString()))
                    {
                        Debug.LogWarning($"�䷽�Ѵ���: {recipe.name}");
                    }
                    else

                    {
                        recipeDict[recipe.inputs.ToString()] = recipe;
                       // Debug.Log($"�ɹ����ز�����䷽: {recipe.name}");
                    }
                }
            }
            else
            {
                Debug.LogError("�䷽����ʧ�ܡ�");
            }
        }
    #endregion

    #region ͨ����ǩ����TileBase�ķ���
    /// <summary>
    /// ͨ����ǩ�б����TileBase
    /// </summary>
    public void LoadTileBaseByLabels(List<string> labels)
    {
        if (labels == null || labels.Count == 0)
        {
            Debug.LogWarning("��ǩ�б�Ϊ�ջ�δ�ṩ��");
            return;
        }

        // ʹ�ñ�ǩ�б������Դ
        Addressables.LoadAssetsAsync<TileBase>(labels, null, Addressables.MergeMode.Union).Completed += OnTileBaseLoadCompleted;

    }
    /// <summary>
    /// TileBase������ɵĻص�
    /// </summary>
    /// <param name="handle">�첽�������</param>
    void OnTileBaseLoadCompleted(AsyncOperationHandle<IList<TileBase>> handle)
    {
        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            foreach (var tileBase in handle.Result)
            {
                if (tileBase == null)
                {
                    Debug.LogError("���ص�TileBaseΪ�ա�");
                    continue;
                }

                if (tileBase.name == null)
                {
                    Debug.LogError("TileBase��nameΪ�ա�");
                    continue;
                }

                if (tileBaseDict.ContainsKey(tileBase.name))
                {
                    Debug.LogWarning($"TileBase�Ѵ���: {tileBase.name}");
                }
                else
                {
                    tileBaseDict[tileBase.name] = tileBase;
                    LoadedCount++;
                    //Debug.Log($"�ɹ����ز����TileBase: {tileBase.name}");
                }
            }
        }
        else
        {
            Debug.LogError("TileBase����ʧ�ܡ�");
        }
    }
    #endregion
}

