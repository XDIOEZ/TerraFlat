using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChange : MonoBehaviour, IInteract
{
    [Tooltip("要传送的目标场景名称")]
    public string TPTOSceneName;

    [Tooltip("玩家进入后会被传送到这个位置（跨场景传送后将使用）")]
    public Vector2 TeleportPosition;

    [Tooltip("进入传送门事件")]
    public UltEvent OnTp = new();

    [Tooltip("是否允许传送")]
    public bool _canTeleport = false;

    public void Interact_Start(IInteracter interacter = null)
    {
        OnTp.Invoke();

        interacter.User.transform.position = TeleportPosition + new Vector2(0, 0.1f);

        print($"[SceneChange] 玩家位置传送至：{TeleportPosition}");
        print($"[SceneChange] 玩家位置传送至：{interacter.User.transform.position}");
        print($"[SceneChange] 玩家：{interacter.User.name}");

        SaveLoadManager.Instance.ChangeScene(TPTOSceneName);

        Debug.Log($"[SceneChange] 场景传送至：{TPTOSceneName}");

        _canTeleport = false;
    }


    public void Interact_Update(IInteracter interacter = null)
    {
        throw new System.NotImplementedException();
    }

    public void Interact_Cancel(IInteracter interacter = null)
    {
        //throw new System.NotImplementedException();
    }

}
