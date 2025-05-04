using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement; // ���볡�����������ռ�

[RequireComponent(typeof(BoxCollider2D))]
public class WorldEdge : Item,ISave_Load
{
    public WorldEdgeData Data;
    public override ItemData Item_Data { get { return Data; } set { Data = value as WorldEdgeData; } }

    // ��ײ���͵�ʲô����
    public string TPTOSceneName { get => Data.TeleportScene; set => Data.TeleportScene = value; }
    public UltEvent onSave { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public UltEvent onLoad { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public Vector2 TeleportPosition { get => Data.TeleportPosition; set => Data.TeleportPosition = value; }
    [Tooltip("��������ǰ�ĵȴ�ʱ�䣨�� timeScale Ӱ�죩")]
    public float teleportDelay = 5f;

    [Tooltip("���ͺ����ͼ���ĵ�ƫ����")]
    public float centerOffset = 2f;

    private bool _canTeleport = false;

    public void Start()
    {
        // ����Э�̣��ȴ�ָ��ʱ�䣨�� timeScale Ӱ�죩
        StartCoroutine(EnableTeleportAfterDelay(teleportDelay));
    }

    IEnumerator EnableTeleportAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay); // ���� Time.timeScale Ӱ��
        _canTeleport = true;
    }
    public void OnTriggerEnter2D(Collider2D collision)
    {
        if (!_canTeleport) return;

        if (collision.gameObject.name == "Player")
        {
            if (!string.IsNullOrEmpty(TPTOSceneName))
            {
                Transform playerTransform = collision.GetComponent<Player>().transform;
                Vector3 newPosition = Vector3.zero; // Ĭ����Ϊԭ��

                // ��� ItemSpecialData ��Ϊ�գ���ʹ������Ϊ���͵�
                if (TeleportPosition!= Vector2.zero)
                {
                    newPosition = TeleportPosition; // ֱ����Ϊԭ��
                }
                else
                {
                    // ����ԭ���߼������Ե����
                    if (Mathf.Abs(transform.position.y) > Mathf.Abs(transform.position.x))
                    {
                        // ��ֱ�����Ե����תY��������ƫ��
                        newPosition.y = -newPosition.y + Mathf.Sign(-newPosition.y) * centerOffset;
                    }
                    else
                    {
                        // ˮƽ�����Ե����תX��������ƫ��
                        newPosition.x = -newPosition.x + Mathf.Sign(-newPosition.x) * centerOffset;
                    }
                }

                playerTransform.position = newPosition;
                SaveAndLoad.Instance.ChangeScene(TPTOSceneName);
            }
            else
            {
                Debug.LogWarning("���ͳ�������Ϊ�գ�");
            }
        }
    }
    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public void Save()
    {
        //��ʵ����transform��Ϣ���浽ItemData��
        Data.SetTransformValue(transform.position, transform.rotation, transform.localScale);
        Data.ItemSpecialData = TeleportPosition.ToString();
    }

    public void Load()
    {
        // �� ItemData �е� transform ��ϢӦ�õ�ʵ����
        transform.position = Data._transform.Position;
        transform.rotation = Data._transform.Rotation;
        transform.localScale = Data._transform.Scale;

        // ���� TeleportPosition
        TeleportPosition = Data.TeleportPosition;

        string sceneName = SceneManager.GetActiveScene().name;

        // ���� $$...$$ �е����ݣ���ȷ��ֻ������ $$�����򱨴�
        string extracted = ExtractContentBetweenDollars(sceneName);
        if (string.IsNullOrEmpty(extracted))
        {
            Debug.LogError($"�������� [{sceneName}] ��ʽ���󣺱����ҽ������� \"$\" ����");
            return;
        }

        // ʾ���������������ʹ�� extracted �ַ���
        Debug.Log("��ȡ������Ϊ: " + extracted);

        TPTOSceneName = extracted;
        // �����߼�
        TeleportPosition = House.housePos[SceneManager.GetActiveScene().name];
    }
    public string ExtractContentBetweenDollars(string input)
    {
        int firstIndex = input.IndexOf("$");
        if (firstIndex == -1) return null;

        int secondIndex = input.IndexOf("$", firstIndex + 1);
        if (secondIndex == -1 || input.LastIndexOf("$") != secondIndex)
        {
            return null; // ����ֻ������ $
        }

        int startIndex = firstIndex + 1;
        int length = secondIndex - startIndex;

        if (length <= 0) return null;

        return input.Substring(startIndex, length);
    }
}
