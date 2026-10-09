using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class Planet : MonoBehaviour
{
    #region 星体表现数据
    [ShowInInspector]
    public PlanetData planetData; // 行星运行数据

    [SerializeField]
    public Transform OrbitCenter; // 该行星自己的公转中心

    private Vector3 renderScale;
    private bool initializedPresentation;

    public void ApplyUniverse(FlatWorld.Spaceflight.SpaceSession session, FlatWorld.Spaceflight.BodyState body)
    {
        if (session == null || body == null) return;
        if (!initializedPresentation)
        {
            SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
            float diameter = sprite == null ? 1f : Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            renderScale = transform.localScale * ((float)(body.RadiusMeters * 2d) / Mathf.Max(0.001f, diameter));
            initializedPresentation = true;
        }
        // 星体和飞船使用同一浮动原点，表现不再次推进公转。
        Vector2 position = session.ProjectPosition(body.PositionMeters);
        transform.position = new Vector3(position.x, position.y, 0f);
        transform.localScale = renderScale;
        transform.rotation = Quaternion.Euler(0f, 0f, (float)(session.Universe.GetRotationRadians(body.BodyId) * 180d / System.Math.PI));
    }

    public Vector3 GetOrbitCenterPosition()
    {
        if (OrbitCenter == null)
        {
            throw new System.InvalidOperationException($"[Planet] OrbitCenter 为空，行星对象: {name}");
        }

        return OrbitCenter.position;
    }
    #endregion
}
