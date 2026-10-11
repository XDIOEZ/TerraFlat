using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed class SpaceDropView : GameItem
    {
        #region 只显示冷载荷的掉落外壳
        public void Initialize(ItemData payload, object owner)
        {
            BindData(payload);
            PersistenceOwner = owner;
            DroppedItemVisual visual = DroppedItemVisual.Resolve(payload, DroppedItemVisual.ResolveSprite(payload));
            GameObject child = new("Render");
            child.transform.SetParent(transform, false);
            child.transform.localPosition = visual.LocalPosition;
            child.transform.localRotation = visual.LocalRotation;
            child.transform.localScale = visual.LocalScale;
            Sprite = child.AddComponent<SpriteRenderer>();
            Sprite.sprite = visual.Sprite;
            Sprite.color = visual.Color;
            Sprite.sharedMaterial = visual.SpriteMaterial;
            Sprite.sortingLayerID = visual.Layer;
            Sprite.sortingOrder = visual.Order;
            // 不调用 Load 或 ModuleInit，气罐、机器和建筑能力只保存在冷数据里。
        }
        #endregion
    }
}
