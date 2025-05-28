using System;
using System.Collections;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(BoxCollider2D))]
public class WorldEdge : Item, ISave_Load, IInteract
{
    public Data_Boundary Data;
    public override ItemData Item_Data { get => Data; set => Data = value as Data_Boundary; }

    public string TPTOSceneName { get => Data.TeleportScene; set => Data.TeleportScene = value; }
    public UltEvent onSave { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public UltEvent onLoad { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public Vector2 TeleportPosition { get => Data.TeleportPosition; set => Data.TeleportPosition = value; }

    [Tooltip("传送后向地图中心的偏移量")]
    public float centerOffset = 2f;

    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public void Save()
    {
        this.SyncPosition();
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
            Debug.LogError($"[WorldEdge] 场景名格式不正确: {sceneName}");
            return;
        }

        TPTOSceneName = targetScene;
       
    }


    /// <summary>
    /// 从嵌套场景名中提取“目标场景名”
    /// 示例：从 "A=>B=>C" 中提取 "B"
    /// </summary>
    public string ExtractTargetSceneName(string input)
    {
        /* var parts = input.Split(new[] { "=>" }, StringSplitOptions.RemoveEmptyEntries);
         if (parts.Length < 2) return null; // 至少需要 A=>B
         return parts[parts.Length - 2]; // 倒数第二个就是目标场景名*/
        TeleportPosition = SaveAndLoad.Instance.SaveData.Scenen_Building_Pos[input];
        return SaveAndLoad.Instance.SaveData.Scenen_Building_Name[input];
    }


    public void Interact_Start(IInteracter interacter = null)
    {
        if (string.IsNullOrEmpty(TPTOSceneName))
        {
            Debug.LogWarning("[WorldEdge] 目标场景为空！:"+ TPTOSceneName);
            return;
        }

        GameObject player = interacter.User ;
        if (player == null)
        {
            Debug.LogError("[WorldEdge] 未找到玩家对象！");
            return;
        }

        // 设置传送位置
        Vector3 newPosition = TeleportPosition != Vector2.zero
            ? (Vector3)TeleportPosition
            : GetDefaultReboundPosition();

        player.transform.position = newPosition;

        Debug.Log($"[WorldEdge] 玩家被传送至场景: {TPTOSceneName}，位置: {newPosition}");
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
