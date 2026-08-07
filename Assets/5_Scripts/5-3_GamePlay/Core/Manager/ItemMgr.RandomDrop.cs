using UnityEngine;

public partial class ItemMgr
{
    [Tooltip("随机空投")]
    public void RandomDropInMap(GameObject dropObject, Chunk map = null, Vector2Int quadrant = default)
    {
        Vector2 defaultPosition;
        if (map == null)
        {
            defaultPosition = Vector2.zero;
        }
        else
        {
            defaultPosition = map.MapSave.MapPosition;
        }

        // 地图格子的实际世界尺寸（单位：世界单位，例如每格宽100高120）
        int tileSizeX = 1; // 根据你的逻辑替换
        int tileSizeY = 1;

        // 整个地图的大小
        float mapWidth = ChunkMgr.GetChunkSize().x * tileSizeX;
        float mapHeight = ChunkMgr.GetChunkSize().y * tileSizeY;

        // 随机数生成器
        System.Random rng = new System.Random();

        // 在 [0, mapWidth] 范围内取随机值
        float randX = (float)rng.NextDouble() * mapWidth;
        float randY = (float)rng.NextDouble() * mapHeight;

        // 确定象限，默认(1,1)就是第一象限
        if (quadrant == default) quadrant = new Vector2Int(1, 1);

        randX *= Mathf.Sign(quadrant.x);
        randY *= Mathf.Sign(quadrant.y);

        // 设置空投对象位置（相对 map 的位置）
        dropObject.transform.position = defaultPosition + new Vector2(randX, randY);
    }
}
