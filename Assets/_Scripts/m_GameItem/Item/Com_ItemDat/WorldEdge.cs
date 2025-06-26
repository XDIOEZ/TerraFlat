using System;
using System.Collections;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(BoxCollider2D))]
public class WorldEdge : Item, ISave_Load, IInteract
{
    #region ����
    public Data_Boundary Data;
    public override ItemData Item_Data { get => Data; set => Data = value as Data_Boundary; }
    #endregion

    #region ����
    public string TPTOSceneName { get => Data.TeleportScene; set => Data.TeleportScene = value; }
    public UltEvent onSave { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public UltEvent onLoad { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public Vector2 TeleportPosition { get => Data.TeleportPosition; set => Data.TeleportPosition = value; }

    [Tooltip("���ͺ����ͼ���ĵ�ƫ����")]
    public float centerOffset = 2f;
    #endregion

    #region �̳з���ʵ��

    public  void Start()
    {
        //��⵱ǰ������ĵ�ַ��������Ƿ����(x,y) ��ʽ ������ϴ˸�ʽ��ʾ��
    }
    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public void Save()
    {
        this.SyncPosition();
    }

    public void SetupMapEdge(Vector2Int direction)
    {
        Vector2Int activeMapPos = SaveAndLoad.Instance.SaveData.ActiveMapPos;
        Vector2Int mapSize = SaveAndLoad.Instance.SaveData.MapSize;

        // �����ͼ���ĵ� (���½� + ��/2, ��/2)
        Vector2 mapCenter = new Vector2(
            activeMapPos.x + mapSize.x / 2f,
            activeMapPos.y + mapSize.y / 2f
        );

        // ���㴫��Ŀ�곡����
        Vector2Int targetMapPos = activeMapPos + direction;
        TPTOSceneName = targetMapPos.ToString();

        // �߽���
        float edgeThickness = 1f;

        // ����߽�λ�ú�����
        Vector3 position = Vector3.zero;
        Vector3 scale = Vector3.one;

        if (direction == Vector2Int.up || direction == Vector2Int.down)
        {
            // �����͵ײ��߽�
            position = new Vector3(
                mapCenter.x,
                mapCenter.y + (direction.y * mapSize.y / 2f),
                transform.position.z
            );
            scale = new Vector3(mapSize.x + edgeThickness * 2, edgeThickness, 1);
        }
        else
        {
            // �����Ҳ�߽�
            position = new Vector3(
                mapCenter.x + (direction.x * mapSize.x / 2f),
                mapCenter.y,
                transform.position.z
            );
            scale = new Vector3(edgeThickness, mapSize.y + edgeThickness * 2, 1);
        }

        transform.position = position;
        transform.localScale = scale;

        // ����ͳһ�����Ƴ���ת�߼�
        transform.rotation = Quaternion.identity;

        // ���ô���λ�ã����ڵ�ͼ�ı߽��ڲࣩ
        Data.TeleportPosition = new Vector2(
            mapCenter.x + (direction.x * (mapSize.x / 2f - 1)),
            mapCenter.y + (direction.y * (mapSize.y / 2f - 1))
        );

        Debug.Log($"���õ�ͼ�߽�: {direction}, Ŀ�곡��: {TPTOSceneName}, λ��: {position}");
    }

    public void GenerateMapEdges()
    {
        // ������б߽�
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        // �����ĸ�����
        Vector2Int[] directions = new Vector2Int[]
        {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
        };

        // Ϊÿ���������ɱ߽�
        foreach (Vector2Int direction in directions)
        {
            Item edgeItem = RunTimeItemManager.Instance.InstantiateItem("MapEdges");
            if (edgeItem is WorldEdge worldEdge)
            {
                worldEdge.SetupMapEdge(direction);
                worldEdge.transform.SetParent(transform);
            }
            else
            {
                Debug.LogError("ʵ�����Ķ�����WorldEdge����!");
            }
        }

        Debug.Log("��ͼ�߽��������");
    }

    public void Load()
    {
        transform.position = Data._transform.Position;
        transform.rotation = Data._transform.Rotation;
        transform.localScale = Data._transform.Scale;

        string sceneName = SceneManager.GetActiveScene().name;
        string targetScene = ExtractTargetSceneName(sceneName);

        if (string.IsNullOrEmpty(targetScene))
        {
            Debug.LogError($"[WorldEdge] ��������ʽ����ȷ: {sceneName}");
            return;
        }

        TPTOSceneName = targetScene;
    }
    #endregion

    #region ����������
    /// <summary>
    /// ��Ƕ�׳���������ȡ��Ŀ�곡������
    /// ʾ������ "A=>B=>C" ����ȡ "B"
    /// </summary>
    public string ExtractTargetSceneName(string input)
    {
        /* var parts = input.Split(new[] { "=>" }, StringSplitOptions.RemoveEmptyEntries);
         if (parts.Length < 2) return null; // ������Ҫ A=>B
         return parts[parts.Length - 2]; // �����ڶ�������Ŀ�곡����*/
        TeleportPosition = SaveAndLoad.Instance.SaveData.Scenen_Building_Pos[input];
        return SaveAndLoad.Instance.SaveData.Scenen_Building_Name[input];
    }
    #endregion

    #region ����ʵ��
    public void Interact_Start(IInteracter interacter = null)
    {
        if (string.IsNullOrEmpty(TPTOSceneName))
        {
            Debug.LogWarning($"[WorldEdge] Ŀ�곡��Ϊ�գ�: {TPTOSceneName}");
            return;
        }

        GameObject player = interacter.User;
        if (player == null)
        {
            Debug.LogError("[WorldEdge] δ�ҵ���Ҷ���");
            return;
        }

        // ���ô���λ��
        Vector3 newPosition = TeleportPosition != Vector2.zero
            ? (Vector3)TeleportPosition
            : GetDefaultReboundPosition();

        player.transform.position = newPosition;

        Debug.Log($"[WorldEdge] ��ұ�����������: {TPTOSceneName}��λ��: {newPosition}");
        SaveAndLoad.Instance.ChangeScene(TPTOSceneName);
    }

    public void Interact_Update(IInteracter interacter = null) => throw new System.NotImplementedException();

    public void Interact_Cancel(IInteracter interacter = null)
    {
        // ����ʵ��
    }
    #endregion

    #region ��������
    private Vector3 GetDefaultReboundPosition()
    {
        Vector3 pos = Vector3.zero;
        if (Mathf.Abs(transform.position.y) > Mathf.Abs(transform.position.x))
            pos.y = -pos.y + Mathf.Sign(-pos.y) * centerOffset;
        else
            pos.x = -pos.x + Mathf.Sign(-pos.x) * centerOffset;
        return pos;
    }
    #endregion
}