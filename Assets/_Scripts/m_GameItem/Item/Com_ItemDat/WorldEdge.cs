using System.Collections;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(BoxCollider2D))]
public class WorldEdge : Item, ISave_Load, IInteract
{
    public WorldEdgeData Data;
    public override ItemData Item_Data { get => Data; set => Data = value as WorldEdgeData; }

    public string TPTOSceneName { get => Data.TeleportScene; set => Data.TeleportScene = value; }
    public UltEvent onSave { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public UltEvent onLoad { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public Vector2 TeleportPosition { get => Data.TeleportPosition; set => Data.TeleportPosition = value; }

    [Tooltip("���ͺ����ͼ���ĵ�ƫ����")]
    public float centerOffset = 2f;

    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public void Save()
    {
        Data.SetTransformValue(transform.position, transform.rotation, transform.localScale);
    }

    public void Load()
    {
        transform.position = Data._transform.Position;
        transform.rotation = Data._transform.Rotation;
        transform.localScale = Data._transform.Scale;
        TeleportPosition = Data.TeleportPosition;

        string sceneName = SceneManager.GetActiveScene().name;
        string extracted = ExtractContentBetweenDollars(sceneName);

        if (string.IsNullOrEmpty(extracted))
        {
         //   Debug.LogError($"[WorldEdge] �������Ƹ�ʽ������Ҫ���� '$'��: {sceneName}");
            return;
        }

        TPTOSceneName = extracted;
        TeleportPosition = House.housePos[sceneName];
    }

    public string ExtractContentBetweenDollars(string input)
    {
        int firstIndex = input.IndexOf('$');
        int secondIndex = input.IndexOf('$', firstIndex + 1);
        if (firstIndex == -1 || secondIndex == -1 || secondIndex != input.LastIndexOf('$'))
            return null;

        return input.Substring(firstIndex + 1, secondIndex - firstIndex - 1);
    }

    public void Interact_Start(IInteracter interacter = null)
    {
        if (string.IsNullOrEmpty(TPTOSceneName))
        {
            Debug.LogWarning("[WorldEdge] Ŀ�곡��Ϊ�գ�");
            return;
        }

        GameObject player = interacter.User ;
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

    private Vector3 GetDefaultReboundPosition()
    {
        Vector3 pos = Vector3.zero;
        if (Mathf.Abs(transform.position.y) > Mathf.Abs(transform.position.x))
            pos.y = -pos.y + Mathf.Sign(-pos.y) * centerOffset;
        else
            pos.x = -pos.x + Mathf.Sign(-pos.x) * centerOffset;
        return pos;
    }

    public void Interact_Update(IInteracter interacter = null) => throw new System.NotImplementedException();
    public void Interact_Cancel(IInteracter interacter = null)
    {

    }
}
