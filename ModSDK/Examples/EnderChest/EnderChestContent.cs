using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Example.EnderChest
{
    /// <summary>复用本体木箱外壳与模块，只创建新的内容身份和独立模板。</summary>
    internal static class EnderChestContent
    {
        #region 内容注册
        internal static void Register(ManagedModContext context)
        {
            GameRes resources = GameRes.ExistingInstance
                ?? throw new InvalidOperationException("末影箱注册时游戏资源尚未就绪。");
            var root = new GameObject("Example.EnderChest.Templates");
            root.SetActive(false);
            root.transform.SetParent(resources.transform, false);
            var lease = context.Track(new DefinitionLease(resources, root));
            Sprite sprite = LoadSprite(context.PackagePath, out Texture2D texture);
            lease.SetArt(sprite, texture);
            lease.Add(Clone(resources, "Chest_Wood", ModEntry.ChestId, false, root.transform, sprite));
            lease.Add(Clone(resources, "Chest_Wood_Summoner", ModEntry.SummonerId, true, root.transform, sprite));
        }

        private static RuntimeItemDefinition Clone(GameRes resources, string sourceId, string id,
            bool summoner, Transform root, Sprite sprite)
        {
            if (!resources.TryGetItemDefinition(sourceId, out RuntimeItemDefinition source))
                throw new InvalidOperationException("缺少木箱定义：" + sourceId);
            GameObject shell = UnityEngine.Object.Instantiate(source.ShellPrefab, root, false);
            shell.name = id;
            SpriteRenderer renderer = shell.GetComponentInChildren<SpriteRenderer>(true);
            if (renderer != null) { renderer.sprite = sprite; renderer.color = Color.white; }
            ItemData data = source.CreateItemData();
            data.IDName = id;
            data.GameName = summoner ? "末影箱" : "末影箱-建筑";
            data.Description = "同一名玩家的所有末影箱共用一份私有库存。";
            data.Stack.Stackable = false;
            if (!Mod_Building.TryReadBuildingData(data, out Ex_ModData module, out var building))
                throw new InvalidOperationException("木箱模板缺少建筑数据：" + sourceId);
            building.Version = Mod_Building.CurrentBuildingDataVersion;
            building.BuildingPrefabId = ModEntry.ChestId;
            building.SummonerPrefabId = ModEntry.SummonerId;
            building.Role = summoner ? BuildingRole.Summoner : BuildingRole.PlacedBuilding;
            module.WriteData(building);

            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            var prefabs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (RuntimeItemModuleDefinition entry in source.ModuleDefinitions)
            {
                parameters.Add(entry.StableName, entry.ParametersJson);
                prefabs.Add(entry.StableName, entry.PrefabId);
            }

            ItemVisualDefinitionDto visual = CopyVisual(source.Visual);
            return new RuntimeItemDefinition(id, id, shell, data, visual, source.Health,
                source.LootTableId, source.WaterEntryTransformItemId, sprite,
                parameters, prefabs, null, null, source.AnimatorController,
                source.IsActor, source.Material, null, source.IsGroundCover,
                source.WorldGridOccupancy, source.RequiredGroundSupport, source.ActorEcs,
                source.EntityRuntime);
        }

        private static ItemVisualDefinitionDto CopyVisual(ItemVisualDefinitionDto source)
        {
            if (source == null) return null;
            return new ItemVisualDefinitionDto
            {
                RendererPath = source.RendererPath,
                SpriteAddress = null,
                SpriteStates = source.SpriteStates,
                LiquidSurface = source.LiquidSurface,
                MaterialAddress = source.MaterialAddress,
                SpriteBundle = source.SpriteBundle,
                SpriteAsset = source.SpriteAsset,
                AnimatorPath = source.AnimatorPath,
                AnimatorControllerAddress = source.AnimatorControllerAddress,
                AnimationState = source.AnimationState,
                AnimatorControllerBundle = source.AnimatorControllerBundle,
                AnimatorControllerAsset = source.AnimatorControllerAsset,
                RendererLocalPosition = source.RendererLocalPosition,
                RendererLocalEulerAngles = source.RendererLocalEulerAngles,
                RendererLocalScale = source.RendererLocalScale,
                Color = Color.white,
                FlipX = source.FlipX,
                FlipY = source.FlipY,
                SortingLayerName = source.SortingLayerName,
                SortingOrder = source.SortingOrder,
                Shadows = source.Shadows,
                Collider = source.Collider
            };
        }

        private static Sprite LoadSprite(string packagePath, out Texture2D texture)
        {
            string path = Path.Combine(packagePath, "Art", "EnderChest_Closed.png");
            byte[] bytes = File.ReadAllBytes(path);
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes, false) || texture.width != 16 || texture.height != 16)
            {
                UnityEngine.Object.Destroy(texture);
                throw new InvalidDataException("末影箱贴图必须是 16×16 PNG：" + path);
            }
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(texture, new Rect(0, 0, 16, 16),
                new Vector2(.5f, 0f), 16f, 0, SpriteMeshType.FullRect);
        }

        private sealed class DefinitionLease : IDisposable
        {
            private readonly GameRes resources;
            private readonly GameObject root;
            private readonly List<RuntimeItemDefinition> definitions = new();
            private Sprite sprite;
            private Texture2D texture;
            public DefinitionLease(GameRes resources, GameObject root)
            { this.resources = resources; this.root = root; }
            public void SetArt(Sprite sprite, Texture2D texture)
            { this.sprite = sprite; this.texture = texture; }
            public void Add(RuntimeItemDefinition definition)
            {
                resources.RegisterItemDefinition(definition);
                definitions.Add(definition);
            }
            public void Dispose()
            {
                foreach (RuntimeItemDefinition definition in definitions)
                {
                    if (resources.ItemDefinitions.TryGetValue(definition.Id, out var registered) &&
                        ReferenceEquals(registered, definition))
                    {
                        resources.ItemDefinitions.Remove(definition.Id);
                        if (resources.AllPrefabs.TryGetValue(definition.Id, out GameObject prefab) &&
                            ReferenceEquals(prefab, definition.ShellPrefab))
                            resources.AllPrefabs.Remove(definition.Id);
                        resources.LoadedCount = Math.Max(0, resources.LoadedCount - 1);
                    }
                }
                if (root != null) UnityEngine.Object.Destroy(root);
                if (sprite != null) UnityEngine.Object.Destroy(sprite);
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
        }
        #endregion
    }
}
