using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChange : MonoBehaviour
{
    [Tooltip("Ҫ���͵�Ŀ�곡������")]
    public string TPTOSceneName;

    [Tooltip("��ҽ����ᱻ���͵����λ��")]
    public Vector2 TeleportPosition;

    [Tooltip("���봫�����¼�")]
    public UltEvent Ontp = new();

    public bool _canTeleport = false;

    [Tooltip("ָ����Ҷ�������ƣ�������ȫƥ�䣩")]
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

                Debug.Log($"������ҵ�����: {TPTOSceneName}��λ��: {TeleportPosition}");

                // ��ֹ�ظ�����
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

        Debug.Log("��ҽ��봫�������򣬿�ʼ��ʱ...");
    }

    void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.name != PlayerObjectName) return;

        _playerInside = false;
        _stayTimer = 0f;

        Debug.Log("����뿪���������򣬼�ʱ����");
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
