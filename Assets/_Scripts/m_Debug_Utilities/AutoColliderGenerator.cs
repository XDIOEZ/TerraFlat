using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(PolygonCollider2D))]
public class PixelColliderGenerator : MonoBehaviour
{
    public SpriteRenderer spriteRenderer; // 绑定的精灵渲染器
    public float pixelPerUnit = 100f; // 每单位像素数，用于控制生成精度
    public int simplificationFactor = 2; // 顶点简化因子，值越高生成的顶点越少

    private void Start()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        GenerateColliderFromSprite();
    }

    private void GenerateColliderFromSprite()
    {
        Sprite sprite = spriteRenderer.sprite;
        if (sprite == null)
        {
            Debug.LogError("Sprite 未找到，请确保 SpriteRenderer 中有绑定的图像！");
            return;
        }

        Texture2D texture = sprite.texture;

        // 获取精灵的像素边界
        Rect spriteRect = sprite.textureRect;
        int xStart = Mathf.RoundToInt(spriteRect.x);
        int yStart = Mathf.RoundToInt(spriteRect.y);
        int width = Mathf.RoundToInt(spriteRect.width);
        int height = Mathf.RoundToInt(spriteRect.height);

        List<Vector2> outline = GetOutline(texture, xStart, yStart, width, height);

        // 简化顶点
        outline = SimplifyOutline(outline, simplificationFactor);

        // 应用到 PolygonCollider2D
        PolygonCollider2D polygonCollider = GetComponent<PolygonCollider2D>();
        polygonCollider.pathCount = 1;
        polygonCollider.SetPath(0, outline.ToArray());

        Debug.Log($"碰撞器生成完成，顶点数：{outline.Count}");
    }

    private List<Vector2> GetOutline(Texture2D texture, int xStart, int yStart, int width, int height)
    {
        List<Vector2> outline = new List<Vector2>();
        bool[,] visited = new bool[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (visited[x, y]) continue;

                Color pixelColor = texture.GetPixel(xStart + x, yStart + y);

                if (pixelColor.a > 0.1f) // 判断非透明像素
                {
                    outline.Add(new Vector2(x / pixelPerUnit, y / pixelPerUnit));
                    visited[x, y] = true;
                }
            }
        }

        return outline;
    }

    private List<Vector2> SimplifyOutline(List<Vector2> outline, int factor)
    {
        if (factor <= 1) return outline;

        List<Vector2> simplified = new List<Vector2>();
        for (int i = 0; i < outline.Count; i += factor)
        {
            simplified.Add(outline[i]);
        }

        return simplified;
    }
}
