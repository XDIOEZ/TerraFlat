using System.Collections.Generic;
using UnityEngine;

/// <summary>保存物品外壳或模块的物理默认值，由所属世界按需创建实际组件。</summary>
public sealed class ItemPhysicsTemplate2D : MonoBehaviour
{
    #region 碰撞形状默认值
    public Module owningModule;
    public string colliderType;
    public bool colliderEnabled = true;
    public bool isTrigger;
    public Vector2 offset;
    public Vector2 size = Vector2.one;
    public float radius = 0.5f;
    public float edgeRadius;
    public int capsuleDirection;
    public List<Vector2> points = new();
    public float density = 1f;
    public PhysicsMaterial2D material;
    public bool usedByEffector;
    public bool usedByComposite;
    #endregion

    #region 刚体默认值
    public bool hasBody;
    public RigidbodyType2D bodyType = RigidbodyType2D.Dynamic;
    public bool bodySimulated = true;
    public bool bodyUseAutoMass;
    public float bodyMass = 1f;
    public float bodyLinearDrag;
    public float bodyAngularDrag = 0.05f;
    public float bodyGravityScale = 1f;
    public RigidbodyConstraints2D bodyConstraints;
    public CollisionDetectionMode2D bodyCollisionDetection;
    public RigidbodyInterpolation2D bodyInterpolation;
    public RigidbodySleepMode2D bodySleepingMode = RigidbodySleepMode2D.StartAwake;
    public bool bodyUseFullKinematicContacts;
    #endregion
}

public interface IItemPhysicsComponentsReceiver2D
{
    void OnItemPhysicsComponentsAvailable();
}
