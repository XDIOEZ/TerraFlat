using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    /// <summary>复用正式光纤的接头和半臂，不新增纹理，按四邻连接组合十六种外形。</summary>
    public static class ShipFiberConnectionVisual
    {
        #region 共享光纤切片
        private static readonly Dictionary<Sprite, Sprite[]> slices = new();
        static ShipFiberConnectionVisual() => SharedSpriteMeshCache.Clearing += Clear;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Clear()
        {
            foreach (Sprite[] parts in slices.Values)
                foreach (Sprite sprite in parts) Destroy(sprite);
            slices.Clear();
        }

        // 原图中心接头约占六分之一格，右半臂连续延伸到格边，切片保持原图像素与比例。
        private static Sprite[] GetSlices(Sprite source)
        {
            if (slices.TryGetValue(source, out Sprite[] result) && result[0] != null) return result;
            Rect rect = source.rect;
            float centerStart = Mathf.Round(rect.width * .416f);
            float centerEnd = Mathf.Round(rect.width * .584f);
            float armBottom = Mathf.Floor(rect.height * .459f);
            float armTop = Mathf.Ceil(rect.height * .541f);
            result = new[]
            {
                Sprite.Create(source.texture, new Rect(rect.x + centerStart, rect.y + Mathf.Round(rect.height * .416f),
                    centerEnd - centerStart, Mathf.Round(rect.height * .168f)), new Vector2(.5f, .5f),
                    rect.width, 0, SpriteMeshType.FullRect),
                Sprite.Create(source.texture, new Rect(rect.x + centerEnd, rect.y + armBottom,
                    rect.width - centerEnd, armTop - armBottom), new Vector2(0f, .5f),
                    rect.width, 0, SpriteMeshType.FullRect)
            };
            for (int i = 0; i < result.Length; i++)
            {
                result[i].name = source.name + "_FiberPart_" + i;
                result[i].hideFlags = HideFlags.DontSave;
            }
            slices[source] = result;
            return result;
        }
        #endregion

        #region 地表与船内组合
        public static void Update(SpriteRenderer source, ref SpriteRenderer[] parts, int mask, double cellSize = 1d)
        {
            if (source == null || source.sprite == null) return;
            Sprite[] sprites = GetSlices(source.sprite);
            parts ??= new SpriteRenderer[5];
            source.enabled = false;
            source.transform.localScale = new Vector3((float)cellSize, (float)cellSize, 1f);
            float armStart = Mathf.Round(source.sprite.rect.width * .584f) / source.sprite.rect.width - .5f;
            for (int i = 0; i < parts.Length; i++)
            {
                SpriteRenderer part = parts[i];
                if (part == null)
                {
                    var child = new GameObject("FiberPart_" + i);
                    child.transform.SetParent(source.transform, false);
                    part = parts[i] = child.AddComponent<SpriteRenderer>();
                    part.spriteSortPoint = SpriteSortPoint.Pivot;
                }
                part.sprite = sprites[i == 0 ? 0 : 1];
                part.color = source.color; part.sharedMaterial = source.sharedMaterial;
                part.sortingLayerID = source.sortingLayerID; part.sortingOrder = source.sortingOrder;
                part.flipX = false; part.flipY = false;
                float angle = i == 0 ? 0f : 90f - (i - 1) * 90f;
                Quaternion rotation = Quaternion.Euler(0f, 0f, angle);
                part.transform.localRotation = rotation;
                part.transform.localPosition = i == 0 ? Vector3.zero : rotation * new Vector3(armStart, 0f);
                part.transform.localScale = Vector3.one;
                part.enabled = i == 0 || (mask & (1 << (i - 1))) != 0;
            }
        }

        public static void Release(ref SpriteRenderer[] parts)
        {
            if (parts == null) return;
            foreach (SpriteRenderer part in parts)
                if (part != null)
                {
                    SpriteRenderer source = part.transform.parent != null
                        ? part.transform.parent.GetComponent<SpriteRenderer>() : null;
                    if (source != null) source.enabled = true;
                    part.enabled = false;
                    Destroy(part.gameObject);
                }
            parts = null;
        }

        private static void Destroy(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
        #endregion
    }
}
