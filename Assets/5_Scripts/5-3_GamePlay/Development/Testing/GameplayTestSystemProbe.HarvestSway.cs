using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 采摘视觉规则
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterManualHarvestVisualRule() =>
        Register("agriculture", "manual_harvest_shader", CaptureManualHarvestShader);

    private static JObject CaptureManualHarvestShader(JObject arguments)
    {
        using var fixture = new UnifiedItemRenderingRuleFixture();
        GameObject fruitNode = new("HarvestIndicator");
        fruitNode.transform.SetParent(fixture.Root.transform, false);
        fruitNode.transform.localPosition = new Vector3(.4f, .6f, 0f);
        SpriteRenderer fruit = fruitNode.AddComponent<SpriteRenderer>();
        fruit.sprite = fixture.Sprite.sprite;
        Shader mainShader = Shader.Find("Game/2D/Sprite-Lit-Master");
        Shader plantShader = Shader.Find("FlatWorld/2D/Grass Sway Lit");
        int shakeId = Shader.PropertyToID("_HarvestShake");
        int waterId = Shader.PropertyToID("_WaterEnabled");
        MaterialPropertyBlock block = new();
        block.SetFloat(waterId, .37f);
        fruit.SetPropertyBlock(block);
        WorldPresentationProjection projection = WorldPresentationProjection.Create(fixture.Root.transform);
        projection.Synchronize(Matrix4x4.Translate(new Vector3(24f, -12f, 0f)), true);
        projection.TryGetProjectedTransform(fixture.Sprite.transform, out Transform mirroredBody);
        projection.TryGetProjectedTransform(fruit.transform, out Transform mirroredFruit);
        SpriteRenderer displayBody = mirroredBody != null ? mirroredBody.GetComponent<SpriteRenderer>() : null;
        SpriteRenderer displayFruit = mirroredFruit != null ? mirroredFruit.GetComponent<SpriteRenderer>() : null;

        Vector3 bodyPosition = fixture.Sprite.transform.position;
        Vector3 fruitPosition = fruit.transform.position;
        Bounds bodyBounds = fixture.Sprite.localBounds;
        Bounds fruitBounds = fruit.localBounds;
        ManualHarvestShake.Play(fixture.Item);
        fixture.Sprite.GetPropertyBlock(block);
        Vector4 bodyShake = block.GetVector(shakeId);
        fruit.GetPropertyBlock(block);
        Vector4 fruitShake = block.GetVector(shakeId);
        Vector3 bodyRoot = fixture.Sprite.transform.TransformPoint(new Vector3(bodyShake.x, bodyShake.y, 0f));
        Vector3 fruitRoot = fruit.transform.TransformPoint(new Vector3(fruitShake.x, fruitShake.y, 0f));
        bool projectedSharedRoot = false;
        if (displayBody != null && displayFruit != null)
        {
            displayBody.GetPropertyBlock(block);
            Vector4 displayBodyShake = block.GetVector(shakeId);
            displayFruit.GetPropertyBlock(block);
            Vector4 displayFruitShake = block.GetVector(shakeId);
            Vector3 mirroredRootA = displayBody.transform.TransformPoint(
                new Vector3(displayBodyShake.x, displayBodyShake.y, 0f));
            Vector3 mirroredRootB = displayFruit.transform.TransformPoint(
                new Vector3(displayFruitShake.x, displayFruitShake.y, 0f));
            projectedSharedRoot = displayBodyShake.w > .5f && displayFruitShake.w > .5f &&
                Vector3.Distance(mirroredRootA, mirroredRootB) < .0001f &&
                Vector3.Distance(mirroredRootA, bodyRoot + new Vector3(24f, -12f, 0f)) < .0001f;
        }
        bool unrelatedPropertyKept = Mathf.Approximately(block.GetFloat(waterId), .37f);
        bool boundsExpanded = fixture.Sprite.localBounds.size.x > bodyBounds.size.x &&
            fruit.localBounds.size.x > fruitBounds.size.x;
        Bounds firstBodyBounds = fixture.Sprite.localBounds;
        Bounds firstFruitBounds = fruit.localBounds;

        // 连点复用同一组件和原始边界，结束时两份参数必须同时复位。
        ManualHarvestShake first = EnableGameplayTestProbeLifecycleCallbacks(fixture.Item.GetComponent<ManualHarvestShake>());
        ManualHarvestShake.Play(fixture.Item);
        bool boundsStable = fixture.Sprite.localBounds.Equals(firstBodyBounds) &&
            fruit.localBounds.Equals(firstFruitBounds);
        bool sameComponent = first == fixture.Item.GetComponent<ManualHarvestShake>();
        first.enabled = false;
        fixture.Sprite.GetPropertyBlock(block);
        bool bodyCleared = block.GetVector(shakeId) == Vector4.zero;
        fruit.GetPropertyBlock(block);
        bool fruitCleared = block.GetVector(shakeId) == Vector4.zero;
        bool projectedCleared = displayBody != null && displayFruit != null;
        if (projectedCleared)
        {
            displayBody.GetPropertyBlock(block);
            projectedCleared = block.GetVector(shakeId) == Vector4.zero;
            displayFruit.GetPropertyBlock(block);
            projectedCleared &= block.GetVector(shakeId) == Vector4.zero;
        }
        bool originalBoundsRestored = fixture.Sprite.localBounds.Equals(bodyBounds) &&
            fruit.localBounds.Equals(fruitBounds);
        bool transformsUntouched = fixture.Sprite.transform.position == bodyPosition &&
            fruit.transform.position == fruitPosition;
        projection.Release();

        return new JObject
        {
            ["mainShaderSupported"] = mainShader != null && mainShader.FindPropertyIndex("_HarvestShake") >= 0,
            ["plantShaderSupported"] = plantShader != null && plantShader.FindPropertyIndex("_HarvestShake") >= 0,
            ["rootShared"] = bodyShake.w > .5f && fruitShake.w > .5f &&
                Mathf.Approximately(bodyShake.z, fruitShake.z) && Vector3.Distance(bodyRoot, fruitRoot) < .0001f,
            ["projectedSharedRoot"] = projectedSharedRoot,
            ["projectedCleared"] = projectedCleared,
            ["boundsExpanded"] = boundsExpanded,
            ["boundsStable"] = boundsStable,
            ["sameComponent"] = sameComponent,
            ["transformsUntouched"] = transformsUntouched,
            ["unrelatedPropertyKept"] = unrelatedPropertyKept,
            ["cleared"] = bodyCleared && fruitCleared,
            ["boundsRestored"] = originalBoundsRestored
        };
    }
    #endregion
}
