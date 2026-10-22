using UnityEngine;

/// <summary>短暂采摘反馈只旋转可见节点，不移动碰撞与逻辑根。</summary>
public sealed class ManualHarvestShake : MonoBehaviour
{
    #region 采摘抖动
    private Quaternion restRotation;
    private float startedAt;
    private bool active;

    public static void Play(Item target)
    {
        if (target?.Sprite == null || target.Sprite.transform == target.transform) return;
        var visual = target.Sprite.gameObject;
        var shake = visual.GetComponent<ManualHarvestShake>() ?? visual.AddComponent<ManualHarvestShake>();
        if (!shake.active) shake.restRotation = shake.transform.localRotation;
        shake.startedAt = Time.time;
        shake.active = true;
        shake.enabled = true;
    }

    private void LateUpdate()
    {
        float elapsed = Time.time - startedAt;
        transform.localRotation = restRotation * Quaternion.Euler(0f, 0f, ManualHarvestWork.ShakeAngle(elapsed));
        if (elapsed >= .24f) { active = false; enabled = false; }
    }

    private void OnDisable()
    {
        if (active) transform.localRotation = restRotation;
        active = false;
    }
    #endregion
}
