using UnityEngine;
using Newtonsoft.Json;

namespace FlatWorld.Spaceflight
{
    public sealed class ShipWreckView : GameItem
    {
        #region 一整块残骸的冷载荷与拼装外观
        public ShipState Composition;
        public void Initialize(SpaceSession session, ShipState ship)
        {
            Composition = ship;
            ItemData payload = GameRes.ExistingInstance.CreateItemData("ShipWreck");
            payload.Guid = ship.WreckGuid;
            payload.Stack.CanBePickedUp = false;
            payload.ItemSpecialData = JsonConvert.SerializeObject(ship);
            BindData(payload);
            PersistenceOwner = session;
            foreach (ShipPieceState piece in ship.Pieces)
            {
                if (!piece.IsAlive || !GameRes.ExistingInstance.TryGetItemDefinition(piece.ItemId, out RuntimeItemDefinition definition)) continue;
                int width = (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height;
                int height = (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width;
                GameObject child = new("FragmentSprite_" + piece.ItemId);
                child.transform.SetParent(transform, false);
                child.transform.localPosition = new Vector3((float)((piece.CellX + width * .5d) * ship.CellSizeMeters - ship.CenterOfMassLocalX),
                    (float)((piece.CellY + height * .5d) * ship.CellSizeMeters - ship.CenterOfMassLocalY), 0f);
                child.transform.localRotation = Quaternion.Euler(0, 0, piece.QuarterTurns * 90f);
                child.transform.localScale = definition.Visual?.RendererLocalScale ?? Vector3.one;
                SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
                renderer.sprite = definition.Sprite;
                renderer.color = (definition.Visual?.Color ?? Color.white) * new Color(.8f, .8f, .8f, 1f);
                renderer.sharedMaterial = definition.Material;
                WorldSortingManager.GetInstance().ApplyRenderer(renderer, piece.Kind == ShipPieceKind.Floor
                    ? WorldSortingManager.VehicleCategory : WorldSortingManager.BuildingCategory, piece.Kind == ShipPieceKind.Floor ? -2 : 0);
                Sprite ??= renderer;
            }
            // 整块只挂一个 Item，子对象只有贴图，没有原机器、管网和建筑模块。
        }
        public void RefreshPayload()
        {
            itemData.Durability = (float)Composition.WreckHealth;
            itemData.MaxDurability = (float)Composition.WreckMaxHealth;
            itemData.ItemSpecialData = JsonConvert.SerializeObject(Composition);
        }
        #endregion
    }
}
