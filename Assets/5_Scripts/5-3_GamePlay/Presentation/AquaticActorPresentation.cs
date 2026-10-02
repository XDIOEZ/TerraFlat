using System;
using UnityEngine;

/// <summary>整条鱼绘制在海床之后、水面之前；不使用陆地动物的半身水线裁切。</summary>
public sealed class AquaticActorPresentation : IDisposable
{
    public const string SortingCategory = "underwater-creature";
    private readonly Item owner;
    private readonly SpriteRenderer renderer;
    private readonly Material originalMaterial;
    private readonly Material underwaterMaterial;
    private readonly WorldSortingMember sorting;
    private bool? isUnderwater;

    public AquaticActorPresentation(Item owner, SpriteRenderer renderer)
    {
        this.owner = owner;
        this.renderer = renderer;
        originalMaterial = renderer.sharedMaterial;
        if (originalMaterial == null) throw new InvalidOperationException("水生动物缺少主体材质。");
        var config = WorldRenderingConfigCatalog.Default.sorting;
        int waterQueue = -1;
        foreach (var profile in config.batchProfiles)
            if (profile.visualLayer == "Water") waterQueue = config.batchRenderQueueBase + profile.renderQueueOffset;
        if (waterQueue < 0) throw new InvalidOperationException("世界渲染配置缺少 Water 批次。");
        underwaterMaterial = new Material(originalMaterial)
        {
            name = "AquaticActor_Submerged",
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = waterQueue - 1
        };
        sorting = owner.GetComponent<WorldSortingMember>() ?? owner.gameObject.AddComponent<WorldSortingMember>();
    }

    public void SetUnderwater(bool value)
    {
        if (isUnderwater == value || renderer == null) return;
        isUnderwater = value;
        renderer.sharedMaterial = value ? underwaterMaterial : originalMaterial;
        sorting.Bind(value ? SortingCategory : WorldSortingManager.CreatureCategory, renderer, owner);
    }

    public void Dispose()
    {
        if (renderer != null) renderer.sharedMaterial = originalMaterial;
        if (sorting != null && renderer != null) sorting.Bind(WorldSortingManager.CreatureCategory, renderer, owner);
        if (underwaterMaterial != null) UnityEngine.Object.Destroy(underwaterMaterial);
    }
}
