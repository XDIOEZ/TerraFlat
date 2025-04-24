using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(PolygonCollider2D))]
public class PixelColliderGenerator : MonoBehaviour
{
    public SpriteRenderer spriteRenderer; // �󶨵ľ�����Ⱦ��
    public float pixelPerUnit = 100f; // ÿ��λ�����������ڿ������ɾ���
    public int simplificationFactor = 2; // ��������ӣ�ֵԽ�����ɵĶ���Խ��

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
            Debug.LogError("Sprite δ�ҵ�����ȷ�� SpriteRenderer ���а󶨵�ͼ��");
            return;
        }

        Texture2D texture = sprite.texture;

        // ��ȡ��������ر߽�
        Rect spriteRect = sprite.textureRect;
        int xStart = Mathf.RoundToInt(spriteRect.x);
        int yStart = Mathf.RoundToInt(spriteRect.y);
        int width = Mathf.RoundToInt(spriteRect.width);
        int height = Mathf.RoundToInt(spriteRect.height);

        List<Vector2> outline = GetOutline(texture, xStart, yStart, width, height);

        // �򻯶���
        outline = SimplifyOutline(outline, simplificationFactor);

        // Ӧ�õ� PolygonCollider2D
        PolygonCollider2D polygonCollider = GetComponent<PolygonCollider2D>();
        polygonCollider.pathCount = 1;
        polygonCollider.SetPath(0, outline.ToArray());

        Debug.Log($"��ײ��������ɣ���������{outline.Count}");
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

                if (pixelColor.a > 0.1f) // �жϷ�͸������
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
