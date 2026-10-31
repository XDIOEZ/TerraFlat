using System.Collections.Generic;
using UnityEngine;

/// <summary>采摘只向主体与附属 Sprite 提交一次共享摇摆参数，不再逐帧旋转 Transform。</summary>
public sealed class ManualHarvestShake : MonoBehaviour
{
    #region GPU采摘反馈
    private const float MaximumRotationDisplacement = .13f;
    private static readonly int HarvestShakeId = Shader.PropertyToID("_HarvestShake");
    private readonly List<SpriteRenderer> renderers = new();
    private readonly MaterialPropertyBlock block = new();
    private Item ownerItem;
    private bool active;

    public static void Play(Item target)
    {
        if (target?.Sprite == null) return;
        var shake = target.GetComponent<ManualHarvestShake>() ?? target.gameObject.AddComponent<ManualHarvestShake>();
        shake.enabled = true;
        shake.Restart(target);
    }

    private void Restart(Item target)
    {
        Clear();
        ownerItem = target;
        target.GetComponentsInChildren(true, renderers);
        Vector3 root = target.Sprite.transform.position;
        foreach (SpriteRenderer renderer in renderers)
        {
            if (renderer == null) continue;
            // 每个附属 Sprite 保存同一根点的自身局部坐标，世界投影时也能正确跟随。
            Vector3 pivot = renderer.transform.InverseTransformPoint(root);
            Vector4 state = new(pivot.x, pivot.y, Time.time, 1f);
            renderer.GetPropertyBlock(block);
            block.SetVector(HarvestShakeId, state);
            renderer.SetPropertyBlock(block);

            // 扩大 CPU 裁剪边界覆盖 GPU 旋转；重复点击先恢复原边界，避免不断累加。
            renderer.ResetLocalBounds();
            Bounds bounds = renderer.localBounds;
            float radius = new Vector2(Mathf.Abs(bounds.center.x - pivot.x) + bounds.extents.x,
                Mathf.Abs(bounds.center.y - pivot.y) + bounds.extents.y).magnitude;
            float padding = radius * MaximumRotationDisplacement;
            bounds.Expand(new Vector3(padding * 2f, padding * 2f, 0f));
            renderer.localBounds = bounds;
        }
        WorldPresentationProjection.RefreshItemVisualProperties(target, renderers);
        active = true;
        Invoke(nameof(Clear), ManualHarvestWork.ShakeDuration);
    }

    private void Clear()
    {
        CancelInvoke(nameof(Clear));
        if (active)
        {
            foreach (SpriteRenderer renderer in renderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetVector(HarvestShakeId, Vector4.zero);
                renderer.SetPropertyBlock(block);
                renderer.ResetLocalBounds();
            }
            WorldPresentationProjection.RefreshItemVisualProperties(ownerItem, renderers);
        }
        active = false;
        ownerItem = null;
        renderers.Clear();
    }

    private void OnDisable() => Clear();
    #endregion
}
