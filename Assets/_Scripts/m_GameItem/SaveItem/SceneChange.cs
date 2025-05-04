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
    public UltEvent Ontp = new();

    [Tooltip("是否允许传送")]
    public bool _canTeleport = false;

    [Tooltip("指定玩家对象的名称（必须完全匹配）")]
    public string PlayerObjectName = "Player";

    private bool _playerInside = false;
    private float _stayTimer = 0f;
    private float _requiredTime = 3f;
    private GameObject _player;
    private bool _teleportTriggered = false; // 🛡️ 防止重复传送

    void Update()
    {
        if (_canTeleport && _playerInside && !_teleportTriggered)
        {
            _stayTimer += Time.deltaTime;

            if (_stayTimer >= _requiredTime)
            {
                Debug.Log("[SceneChange] 倒计时完成，触发传送");
                TriggerTeleport();
            }
        }
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.name != PlayerObjectName) return;

        // 设置 _player 引用（供 Interact_Start 使用）
        _player = collision.gameObject;
        Debug.Log("[SceneChange] 玩家进入传送门区域，将触发 Interact_Start()");
    }


    void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.name != PlayerObjectName) return;

        _playerInside = false;
        _stayTimer = 0f;

        Debug.Log("[SceneChange] 玩家离开传送门区域，计时重置");
    }

    public void EnableTeleport()
    {
        _canTeleport = true;
        _teleportTriggered = false;
        Debug.Log("[SceneChange] 启用传送功能");
    }

    public void DisableTeleport()
    {
        _canTeleport = false;
        _playerInside = false;
        _stayTimer = 0f;
        _teleportTriggered = false;
        Debug.Log("[SceneChange] 禁用传送功能");
    }

    public void Interact_Start(IInteracter interacter = null)
    {
        Ontp.Invoke();

        if (_teleportTriggered || !_canTeleport)
        {
            Debug.LogWarning("[SceneChange] 传送未启用或已触发，忽略 Interact_Start");
            return;
        }

        // 设置玩家位置
        if (_player != null)
        {
            _player.transform.position = TeleportPosition;
            Debug.Log($"[SceneChange] 玩家位置设置为 {TeleportPosition}");
        }

      
        SaveAndLoad.Instance.ChangeScene(TPTOSceneName);
        Debug.Log($"[SceneChange] 场景传送至：{TPTOSceneName}");

        _teleportTriggered = true;
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

    private void TriggerTeleport()
    {
        Ontp.Invoke();

        // 🧍 将玩家移动到指定位置（传送前）
        if (_player != null)
        {
            _player.transform.position = TeleportPosition;
            Debug.Log($"[SceneChange] 玩家位置已设置为: {TeleportPosition}");
        }
        else
        {
            Debug.LogWarning("[SceneChange] 无法设置玩家位置，_player 为 null");
        }

        // 🚪 调用场景切换
        SaveAndLoad.Instance.ChangeScene(TPTOSceneName);

        Debug.Log($"[SceneChange] 玩家被传送到场景: {TPTOSceneName}");

        _teleportTriggered = true;
        _canTeleport = false;
        _playerInside = false;
        _stayTimer = 0f;
    }

}
