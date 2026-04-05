using DG.Tweening;
using UnityEngine;

public class OreStoneThrowOnAct : MonoBehaviour
{
#region 配置

    [SerializeField]
    private GameObject stoneBulletPrefab;

    [SerializeField]
    private float throwSpeed = 16f;

    [SerializeField]
    private float spawnOffset = 1.1f;

    [SerializeField]
    private float pickupDelay = 0.8f;

    [SerializeField]
    private float projectileLifeTime = 3f;

    [SerializeField]
    private bool ignoreOwnerCollision = true;

    private const string StoneItemId = "Ore_Stone";

#endregion

#region 缓存

    private Item _item;
    private Inventory_HotBar _hotBar;
    private Mod_FocusPoint _focusPoint;

#endregion

#region 生命周期

    private void Awake()
    {
        _item = GetComponent<Item>();
    }

    private void OnEnable()
    {
        if (_item == null)
        {
            _item = GetComponent<Item>();
        }

        if (_item != null)
        {
            _item.OnAct -= HandleAct;
            _item.OnAct += HandleAct;
        }
    }

    private void OnDisable()
    {
        if (_item != null)
        {
            _item.OnAct -= HandleAct;
        }
    }

#endregion

#region 核心逻辑

    private void HandleAct()
    {
        if (_item == null || _item.itemData == null)
            return;

        if (_item.itemData.IDName != StoneItemId)
            return;

        if (!TryResolveOwnerContext())
            return;

        ItemSlot currentSlot = _hotBar.CurrentSelectItemSlot;
        if (currentSlot == null || currentSlot.itemData == null || _hotBar.CurentSelectItem != _item)
            return;

        if (currentSlot.itemData.Stack.Amount <= 0f)
            return;

        Vector2 direction = ResolveShootDirection();
        Vector3 spawnPosition = ResolveSpawnPosition(direction);

        if (stoneBulletPrefab == null)
        {
            Debug.LogError("[OreStoneThrowOnAct] stoneBulletPrefab 未设置，无法发射石头子弹");
            return;
        }

        GameObject projectileObject = Instantiate(stoneBulletPrefab, spawnPosition, Quaternion.identity);
        Item projectileItem = projectileObject.GetComponent<Item>();
        if (projectileItem != null)
        {
            ItemMgr.Instance.InjectRuntimeItem(projectileItem, "OreStoneBullet");
            projectileItem.Owner = _item.Owner;
            projectileItem.Load();
            projectileItem.SetInHand(false);

            if (projectileItem.itemData != null)
            {
                projectileItem.itemData.Stack.Amount = 1f;
                projectileItem.itemData.Stack.CanBePickedUp = false;
            }
        }

        Rigidbody2D rigidbody2D = projectileObject.GetComponent<Rigidbody2D>();
        if (rigidbody2D == null)
        {
            rigidbody2D = projectileObject.AddComponent<Rigidbody2D>();
        }

        if (ignoreOwnerCollision)
        {
            IgnoreProjectileCollisionWithOwner(projectileObject);
        }

        rigidbody2D.gravityScale = 0f;
        rigidbody2D.drag = 0f;
        rigidbody2D.angularDrag = 0.05f;
        rigidbody2D.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rigidbody2D.interpolation = RigidbodyInterpolation2D.Interpolate;
        rigidbody2D.velocity = direction * throwSpeed;
        rigidbody2D.angularVelocity = Random.Range(-720f, 720f);

        ConsumeOneStoneFromCurrentSlot(currentSlot);

        DOVirtual.DelayedCall(pickupDelay, () =>
        {
            if (projectileItem != null && projectileItem.itemData != null)
            {
                projectileItem.itemData.Stack.CanBePickedUp = true;
            }
        }).SetTarget(projectileObject);

        DOVirtual.DelayedCall(projectileLifeTime, () =>
        {
            if (projectileObject == null)
                return;

            if (projectileItem != null && ItemMgr.Instance != null)
            {
                ItemMgr.Instance.DespawnItem(projectileItem);
            }
            else
            {
                Destroy(projectileObject);
            }
        }).SetTarget(projectileObject);
    }

    private void IgnoreProjectileCollisionWithOwner(GameObject projectileObject)
    {
        if (_item == null || _item.Owner == null)
            return;

        Collider2D projectileCollider = projectileObject.GetComponent<Collider2D>();
        if (projectileCollider == null)
            return;

        Collider2D[] ownerColliders = _item.Owner.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < ownerColliders.Length; i++)
        {
            Collider2D ownerCollider = ownerColliders[i];
            if (ownerCollider == null)
                continue;

            Physics2D.IgnoreCollision(projectileCollider, ownerCollider, true);
        }
    }

    private bool TryResolveOwnerContext()
    {
        if (_item.Owner == null || _item.Owner.itemMods == null)
            return false;

        Module hotbarMod = _item.Owner.itemMods.GetMod_ByID(ModText.Hotbar);
        _hotBar = hotbarMod != null ? hotbarMod.GetComponent<Inventory_HotBar>() : null;
        _focusPoint = _item.Owner.itemMods.GetMod_ByID<Mod_FocusPoint>(ModText.FocusPoint);

        return _hotBar != null && _hotBar.Data != null;
    }

    private Vector3 ResolveSpawnPosition(Vector2 direction)
    {
        Vector3 basePosition;
        if (_hotBar != null && _hotBar.spawnLocation != null)
        {
            basePosition = _hotBar.spawnLocation.position;
        }
        else
        {
            basePosition = _item.transform.position;
        }

        return basePosition + (Vector3)(direction * spawnOffset);
    }

    private Vector2 ResolveShootDirection()
    {
        Vector2 from = (_hotBar != null && _hotBar.spawnLocation != null)
            ? (Vector2)_hotBar.spawnLocation.position
            : (Vector2)_item.transform.position;

        if (_focusPoint != null)
        {
            Vector2 to = _focusPoint.Data.DefaultSkill_Point;
            Vector2 direction = (to - from).normalized;
            if (direction.sqrMagnitude > 0.0001f)
            {
                return direction;
            }
        }

        float fallbackDirection = _item.Owner != null && _item.Owner.transform.lossyScale.x < 0f ? -1f : 1f;
        return new Vector2(fallbackDirection, 0f);
    }

    private void ConsumeOneStoneFromCurrentSlot(ItemSlot slot)
    {
        int index = Mathf.Clamp(slot.Index, 0, _hotBar.Data.itemSlots.Count - 1);
        _hotBar.Data.ChangeItemDataAmount(index, -1f);

        if (slot.itemData != null && slot.itemData.Stack.Amount <= 0f)
        {
            _hotBar.Data.RemoveItemAll(slot, index);
        }
        else
        {
            _hotBar.RefreshUI(index);
        }

        _item.OnUIRefresh.Invoke();
    }

#endregion
}
