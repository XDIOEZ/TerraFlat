using UnityEngine;
using UnityEngine.Tilemaps;

public class ShowBox : MonoBehaviour
{
    // 在检查器中显示并可编辑的变量
    [SerializeField]
    private int minX = -100;
    [SerializeField]
    private int maxX = 99;
    [SerializeField]
    private int minY = -100;
    [SerializeField]
    private int maxY = 99;
    [ContextMenu("CutTiles")]
    public void CutTiles()
    {
        Tilemap tilemap = GetComponent<Tilemap>();
        if (tilemap == null)
        {
            Debug.LogError("Tilemap component not found!");
            return;
        }

        // 获取所有瓦片的位置范围
        BoundsInt bounds = tilemap.cellBounds;

        // 遍历所有可能包含瓦片的位置
        foreach (Vector3Int pos in bounds.allPositionsWithin)
        {
            if (tilemap.HasTile(pos))
            {
                // 检查是否在目标区域内
                bool shouldKeep = pos.x >= minX && pos.x <= maxX
                               && pos.y >= minY && pos.y <= maxY;

                // 删除区域外的瓦片
                if (!shouldKeep)
                {
                    tilemap.SetTile(pos, null);
                }
            }
        }

        Debug.Log("瓦片清理完成");
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;
        // 绘制加载区域
        Gizmos.DrawWireCube(transform.position, new Vector3(maxX - minX, maxY - minY, 0));
    }
}
