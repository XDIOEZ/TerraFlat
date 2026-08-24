using UnityEngine;

/// <summary>
/// 雪地动物的轻量视觉替换组件。复用既有动物的动画和 AI 组件，只替换所有子 SpriteRenderer 的静态精灵，
/// 让兔子和雪豹可以沿用成熟行为代码，同时使用项目已有的统一像素素材。
/// </summary>
[DisallowMultipleComponent]
public sealed class SnowAnimalVisualOverride : MonoBehaviour
{
    [SerializeField] private Sprite overrideSprite;

    /// <summary>设置需要覆盖的动物精灵。</summary>
    public void SetSprite(Sprite sprite)
    {
        overrideSprite = sprite;
        ApplySprite();
    }

    private void LateUpdate()
    {
        ApplySprite();
    }

    private void ApplySprite()
    {
        if (overrideSprite == null)
            return;

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        foreach (SpriteRenderer renderer in renderers)
        {
            if (renderer != null)
                renderer.sprite = overrideSprite;
        }
    }
}
