using UnityEngine;

/// <summary>世界空间耐力条；每只鸟复用两条线，不创建 Canvas，不参与飞行状态或存档。</summary>
[DisallowMultipleComponent]
public sealed class BirdFlightStaminaBar : MonoBehaviour
{
    #region 绑定与显示
    private AI_Bird bird;
    private Transform anchor;
    private LineRenderer background, fill;
    private static Material sharedMaterial;

    public void Bind(AI_Bird source, Transform liftRoot)
    {
        bird = source;
        anchor = liftRoot;
        if (background == null)
        {
            background = CreateLine("Flight stamina background", new Color(0f, 0f, 0f, 0.75f), 100);
            fill = CreateLine("Flight stamina fill", new Color(0.2f, 0.9f, 1f), 101);
        }
        SetVisible(false);
    }

    private void LateUpdate()
    {
        bool visible = bird != null && bird.isActiveAndEnabled && bird.IsAlive && anchor != null &&
            (bird.IsAirborne || bird.FlightStamina < bird.flightStaminaMax);
        SetVisible(visible);
        if (!visible) return;
        // LiftRoot 已包含飞行高度，不重复叠加，否则条会悬在鸟上方很远处。
        Vector3 left = anchor.position + new Vector3(-0.4f, 0.55f, 0f);
        Vector3 right = left + Vector3.right * 0.8f;
        background.SetPosition(0, left);
        background.SetPosition(1, right);
        fill.SetPosition(0, left);
        fill.SetPosition(1, Vector3.Lerp(left, right, Mathf.Clamp01(bird.FlightStamina / bird.flightStaminaMax)));
    }

    public void SetVisible(bool visible)
    {
        if (background != null) background.enabled = visible;
        if (fill != null) fill.enabled = visible && bird != null && bird.FlightStamina > 0f;
    }
    private void OnDisable() => SetVisible(false);
    #endregion

    #region 共享材质与复用线条
    private LineRenderer CreateLine(string objectName, Color color, int order)
    {
        if (sharedMaterial == null)
            sharedMaterial = new Material(Shader.Find("Sprites/Default"))
            { name = "Bird flight stamina", hideFlags = HideFlags.HideAndDontSave };
        GameObject child = new(objectName);
        child.transform.SetParent(transform, false);
        LineRenderer line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startColor = line.endColor = color;
        line.widthMultiplier = 0.06f;
        line.sortingOrder = order;
        line.sharedMaterial = sharedMaterial;
        return line;
    }
    #endregion
}
