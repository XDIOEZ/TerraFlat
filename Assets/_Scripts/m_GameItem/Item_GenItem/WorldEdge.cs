using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UltEvents;

/// <summary>
/// ��ͼ�߽���������������ɱ߽硢�����͡�������ص��߼�
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class WorldEdge : Item, ISave_Load, IInteract
{
    #region ����������

    public Data_Boundary Data;
    public override ItemData Item_Data { get => Data; set => Data = value as Data_Boundary; }

    public string TPTOSceneName { get => Data.TP_SceneName; set => Data.TP_SceneName = value; }

    public Vector2 TeleportPosition { get => Data.TP_Position; set => Data.TP_Position = value; }

    [Tooltip("���ͺ����ͼ���ĵ�ƫ����")]
    public float centerOffset = 2f;

    public UltEvent onSave { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public UltEvent onLoad { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }


    #endregion

    #region ��������

    private void Start()
    {
        // TODO: ��ʼ����ⳡ�����ƻ���λ���߼�
    }

    public override void Act()
    {
        throw new NotImplementedException();
    }

    #endregion

    #region ���������

    public void Save()
    {
        this.SyncPosition();
    }

    public void Load()
    {
        // ���ر߽�λ�á���ת������
        transform.position = Data._transform.Position;
        transform.rotation = Data._transform.Rotation;
        transform.localScale = Data._transform.Scale;

        // ����Ŀ�괫�ͳ���
        string sceneName = SceneManager.GetActiveScene().name;
        string Target_SceneName = ExtractTargetSceneName(sceneName);

        if (string.IsNullOrEmpty(Target_SceneName))
        {
           // Debug.LogError($"[WorldEdge] ��������ʽ����ȷ: {sceneName}");
            return;
        }

        TPTOSceneName = Target_SceneName;
    }

    #endregion

    #region ��ͼ�߽�����

    /// <summary>
    /// ���õ�ǰ�߽�����ڵ�ͼ�е�λ������״
    /// </summary>
    /// <summary>
    /// ���ݵ�ͼ�������ñ߽�λ�á���С��Ŀ�괫����Ϣ
    /// </summary>
    /// <param name="direction">�߽緽���������ң�</param>
    public void SetupMapEdge(Vector2Int direction)
    {
        Data.Boundary_Position = direction;
        // ��ȡ��ǰ��ͼ��Ϣ
        var saveData = SaveAndLoad.Instance.SaveData;
        Vector2Int activeMapPos = saveData.ActiveMapPos;  // ��ǰ��ͼ��������
        Vector2Int mapSize = saveData.MapSize;            // ��ǰ��ͼ��С����λΪ��������

        // �����ͼ���ĵ㣨���ڶ�λ�߽磩
        Vector2 mapCenter = new Vector2(
            activeMapPos.x + mapSize.x / 2f,
            activeMapPos.y + mapSize.y / 2f
        );

        // Ŀ�곡���� = ��ǰ��ͼλ�� + ����ƫ��
        Vector2Int targetMapPos = activeMapPos + direction * mapSize;
        TPTOSceneName = targetMapPos.ToString();

        // �߽��ȣ����ڱ߽���ײ�п�Ȼ�߶ȣ�
        float edgeThickness = 1f;

        Vector3 position = Vector3.zero;   // ���ձ߽�λ��
        Vector3 scale = Vector3.one;       // ���ձ߽�����

        // ����߽磨���£�
        if (direction == Vector2Int.up || direction == Vector2Int.down)
        {
            position = new Vector3(
                mapCenter.x,
                mapCenter.y + direction.y * (mapSize.y / 2f),
                transform.position.z
            );
            scale = new Vector3(mapSize.x + edgeThickness * 2, edgeThickness, 1);
        }
        // ����߽磨���ң�
        else
        {
            position = new Vector3(
                mapCenter.x + direction.x * (mapSize.x / 2f),
                mapCenter.y,
                transform.position.z
            );
            scale = new Vector3(edgeThickness, mapSize.y + edgeThickness * 2, 1);
        }

        // Ӧ�ñ߽�λ�á����š��Ƕ�
        transform.position = position;
        transform.localScale = scale;
        transform.rotation = Quaternion.identity; // ��֤����ͳһ


        Debug.Log($"[WorldEdge] ���õ�ͼ�߽�: {direction}, Ŀ�곡��: {TPTOSceneName}, λ��: {position}");
    }


    /// <summary>
    /// �����ĸ�����ı߽����
    /// </summary>
    public void GenerateMapEdges()
    {
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        Vector2Int[] directions = new[]
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

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
                Debug.LogError("ʵ�����Ķ����� WorldEdge ����!");
            }
        }

        Debug.Log("��ͼ�߽��������");
    }

    #endregion

    #region ����������

    /// <summary>
    /// �ӳ���������ȡĿ�곡�������Ӧ�Ĵ���λ��
    /// </summary>
    public string ExtractTargetSceneName(string Current_SceneName)
    {
        if (!SaveAndLoad.Instance.SaveData.Scenen_Building_Name.ContainsKey(Current_SceneName))
            return null;

        TeleportPosition = SaveAndLoad.Instance.SaveData.Scenen_Building_Pos[Current_SceneName];
        return SaveAndLoad.Instance.SaveData.Scenen_Building_Name[Current_SceneName];
    }

    #endregion

    #region �����߼�

    /// <summary>
    /// ��ʼ�������� - ������ҵ�Ŀ�곡��
    /// </summary>
    /// <param name="interacter">���������󣬿�Ϊnull</param>
    public void Interact_Start(IInteracter interacter = null)
    {
        // ���Ŀ�곡�������Ƿ�Ϊ�ջ�null
        if (string.IsNullOrEmpty(TPTOSceneName))
        {
            Debug.LogWarning("[WorldEdge] ���棺Ŀ�곡������Ϊ�գ�");
            return;
        }

        // �ӽ������л�ȡ��Ҷ���
        GameObject player = null;
        if (interacter != null)
        {
            player = interacter.User;
        }

        // ��֤��Ҷ����Ƿ����
        if (player == null)
        {
            Debug.LogError("[WorldEdge] �����޷���ȡ��Ч����Ҷ���");
            return;
        }

        // ȷ������λ��
        Vector3 newPosition;
        if (TeleportPosition != Vector2.zero)
        {
            // ʹ��ָ���Ĵ���λ��
            newPosition = new Vector3(TeleportPosition.x, TeleportPosition.y, 0);
        }
        else
        {
            // ʹ��Ĭ�ϵķ���λ��
            newPosition = GetDefaultReboundPosition();
        }

        // �������λ��
        player.transform.position += newPosition;

        // ��¼������־
        Debug.Log("[WorldEdge] ��Ϣ����ұ�������������" + TPTOSceneName + "��λ�ã�" + newPosition.ToString());

        // ���ó����л�����
        SaveAndLoad.Instance.ChangeScene(TPTOSceneName);
    }

    public void Interact_Update(IInteracter interacter = null)
    {
        throw new NotImplementedException();
    }

    public void Interact_Cancel(IInteracter interacter = null)
    {
        // ����ʵ��
    }

    #endregion

    #region ��������

    /// <summary>
    /// ���δָ�����͵㣬����ݱ߽練��һ��Ĭ��λ��
    /// </summary>
    private Vector3 GetDefaultReboundPosition()
    {
        //���� Data.Boundary_Position �ķ��� ȷ���߽�λ��ʲô����

        return Data.Boundary_Position * 2f;
    }

    #endregion
}
