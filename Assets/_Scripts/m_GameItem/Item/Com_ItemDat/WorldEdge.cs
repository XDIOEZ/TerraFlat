using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement; // 导入场景管理命名空间

[RequireComponent(typeof(BoxCollider2D))]
public class WorldEdge : Item,ISave_Load
{
    public WorldEdgeData Data;
    public override ItemData Item_Data { get { return Data; } set { Data = value as WorldEdgeData; } }

    // 碰撞后传送到什么场景
    public string TPTOSceneName { get => Data.TeleportScene; set => Data.TeleportScene = value; }
    public UltEvent onSave { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public UltEvent onLoad { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    public Vector2 TeleportPosition { get => Data.TeleportPosition; set => Data.TeleportPosition = value; }
    [Tooltip("触发传送前的等待时间（受 timeScale 影响）")]
    public float teleportDelay = 5f;

    [Tooltip("传送后向地图中心的偏移量")]
    public float centerOffset = 2f;

    private bool _canTeleport = false;

    public void Start()
    {
        // 启动协程，等待指定时间（受 timeScale 影响）
        StartCoroutine(EnableTeleportAfterDelay(teleportDelay));
    }

    IEnumerator EnableTeleportAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay); // 会受 Time.timeScale 影响
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
                Vector3 newPosition = Vector3.zero; // 默认设为原点

                // 如果 ItemSpecialData 不为空，则使用它作为传送点
                if (TeleportPosition!= Vector2.zero)
                {
                    newPosition = TeleportPosition; // 直接设为原点
                }
                else
                {
                    // 否则按原来逻辑处理边缘反弹
                    if (Mathf.Abs(transform.position.y) > Mathf.Abs(transform.position.x))
                    {
                        // 垂直方向边缘，反转Y并向中心偏移
                        newPosition.y = -newPosition.y + Mathf.Sign(-newPosition.y) * centerOffset;
                    }
                    else
                    {
                        // 水平方向边缘，反转X并向中心偏移
                        newPosition.x = -newPosition.x + Mathf.Sign(-newPosition.x) * centerOffset;
                    }
                }

                playerTransform.position = newPosition;
                SaveAndLoad.Instance.ChangeScene(TPTOSceneName);
            }
            else
            {
                Debug.LogWarning("传送场景名称为空！");
            }
        }
    }
    public override void Act()
    {
        throw new System.NotImplementedException();
    }

    public void Save()
    {
        //将实例的transform信息保存到ItemData中
        Data.SetTransformValue(transform.position, transform.rotation, transform.localScale);
        Data.ItemSpecialData = TeleportPosition.ToString();
    }

    public void Load()
    {
        // 将 ItemData 中的 transform 信息应用到实例上
        transform.position = Data._transform.Position;
        transform.rotation = Data._transform.Rotation;
        transform.localScale = Data._transform.Scale;

        // 设置 TeleportPosition
        TeleportPosition = Data.TeleportPosition;

        string sceneName = SceneManager.GetActiveScene().name;

        // 解析 $$...$$ 中的内容，并确保只有两个 $$，否则报错
        string extracted = ExtractContentBetweenDollars(sceneName);
        if (string.IsNullOrEmpty(extracted))
        {
            Debug.LogError($"场景名称 [{sceneName}] 格式错误：必须且仅有两个 \"$\" 符号");
            return;
        }

        // 示例：你可以在这里使用 extracted 字符串
        Debug.Log("提取的内容为: " + extracted);

        TPTOSceneName = extracted;
        // 其他逻辑
        TeleportPosition = House.housePos[SceneManager.GetActiveScene().name];
    }
    public string ExtractContentBetweenDollars(string input)
    {
        int firstIndex = input.IndexOf("$");
        if (firstIndex == -1) return null;

        int secondIndex = input.IndexOf("$", firstIndex + 1);
        if (secondIndex == -1 || input.LastIndexOf("$") != secondIndex)
        {
            return null; // 必须只有两个 $
        }

        int startIndex = firstIndex + 1;
        int length = secondIndex - startIndex;

        if (length <= 0) return null;

        return input.Substring(startIndex, length);
    }
}
