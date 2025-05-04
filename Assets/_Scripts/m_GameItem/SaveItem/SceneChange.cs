using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChange : MonoBehaviour
{
    [Tooltip("要传送的目标场景名称")]
    public string TPTOSceneName;

    [Tooltip("玩家进入后会被传送到这个位置")]
    public Vector2 TeleportPosition;

    [Tooltip("进入传送门事件")]
    public UltEvent Ontp = new();

    public bool _canTeleport = false;

    [Tooltip("指定玩家对象的名称（必须完全匹配）")]
    public string PlayerObjectName = "Player";

    private bool _playerInside = false;
    private float _stayTimer = 0f;
    private float _requiredTime = 3f;
    private GameObject _player;

    void Update()
    {
        if (_canTeleport && _playerInside)
        {
            _stayTimer += Time.deltaTime;

            if (_stayTimer >= _requiredTime)
            {
                Ontp.Invoke();
                SaveAndLoad.Instance.ChangeScene(TPTOSceneName);

                Debug.Log($"传送玩家到场景: {TPTOSceneName}，位置: {TeleportPosition}");

                // 防止重复触发
                _canTeleport = false;
                _playerInside = false;
                _stayTimer = 0f;
            }
        }
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (!_canTeleport) return;
        if (collision.gameObject.name != PlayerObjectName) return;

        _playerInside = true;
        _player = collision.gameObject;
        _stayTimer = 0f;

        Debug.Log("玩家进入传送门区域，开始计时...");
    }

    void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.name != PlayerObjectName) return;

        _playerInside = false;
        _stayTimer = 0f;

        Debug.Log("玩家离开传送门区域，计时重置");
    }

    public void EnableTeleport()
    {
        _canTeleport = true;
    }

    public void DisableTeleport()
    {
        _canTeleport = false;
        _playerInside = false;
        _stayTimer = 0f;
    }
}
