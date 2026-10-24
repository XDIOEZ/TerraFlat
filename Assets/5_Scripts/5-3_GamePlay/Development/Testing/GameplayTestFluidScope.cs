using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class MachineWorld
{
    #region 流体测试的独立作用域
    // 测试夹具仅开放既有调度阶段，不复制输送算法，也不清空其他世界的机器或注册表。
    internal sealed class GameplayFluidScope : IDisposable
    {
        private static readonly JsonSerializerSettings cloneSettings = new()
        {
            Converters =
            {
                new Vector4FieldsConverter(), new ColorFieldsConverter(),
                new FlatWorld.Spaceflight.SpaceLooseItemState.ScaleJsonConverter()
            }
        };

        // Color 只复制 RGBA 原值，避免 linear 与 gamma 等计算属性递归进入克隆。
        private sealed class ColorFieldsConverter : JsonConverter<Color>
        {
            public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("r"); writer.WriteValue(value.r);
                writer.WritePropertyName("g"); writer.WriteValue(value.g);
                writer.WritePropertyName("b"); writer.WriteValue(value.b);
                writer.WritePropertyName("a"); writer.WriteValue(value.a);
                writer.WriteEndObject();
            }
            public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue,
                bool hasExistingValue, JsonSerializer serializer)
            {
                JObject fields = JObject.Load(reader);
                return new Color(fields.Value<float>("r"), fields.Value<float>("g"), fields.Value<float>("b"), fields.Value<float>("a"));
            }
        }

        // Unity Vector4 只复制四个实际分量，防止 normalized 属性在定义克隆时无限自引用。
        private sealed class Vector4FieldsConverter : JsonConverter<Vector4>
        {
            public override void WriteJson(JsonWriter writer, Vector4 value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("x"); writer.WriteValue(value.x);
                writer.WritePropertyName("y"); writer.WriteValue(value.y);
                writer.WritePropertyName("z"); writer.WriteValue(value.z);
                writer.WritePropertyName("w"); writer.WriteValue(value.w);
                writer.WriteEndObject();
            }
            public override Vector4 ReadJson(JsonReader reader, Type objectType, Vector4 existingValue,
                bool hasExistingValue, JsonSerializer serializer)
            {
                JObject fields = JObject.Load(reader);
                return new Vector4(fields.Value<float>("x"), fields.Value<float>("y"), fields.Value<float>("z"), fields.Value<float>("w"));
            }
        }
        private readonly GameSaveData save;
        private readonly string planetId;
        private readonly List<string> keys = new();
        private readonly HashSet<string> ownedKeys = new(StringComparer.Ordinal);
        private readonly List<string> shipIds = new();
        private readonly List<MachineEntity> created = new();
        private readonly Vector2Int origin;
        private readonly bool previousBackground;
        private ScopeLease lease;
        private bool disposed;
        public string Key { get; }
        public float Seconds => MachineCatalog.Settings.TickSeconds;
        public AtmosphereState Atmosphere { get; }

        public GameplayFluidScope(bool surface = false)
        {
            if (!GameNetwork.HasStateAuthority) throw new InvalidOperationException("流体测试要求权威端。");
            save = SaveDataMgr.Instance?.SaveData ?? throw new InvalidOperationException("测试世界尚未就绪。");
            if (surface)
            {
                Key = SceneManager.GetActiveScene().name;
                if (!WorldAddress.FromWorldKey(Key).IsSurface || !save.PlanetData_Dict.ContainsKey(Key))
                    throw new InvalidOperationException("地表流体测试要求隔离地表测试世界。");
                lease = UseScope(Key); previousBackground = scope.BackgroundSimulation;
                scope.BackgroundSimulation = true; keys.Add(Key);
                Atmosphere = GetCurrentAtmosphere();
                try { origin = FindSurfaceCells(); }
                catch { scope.BackgroundSimulation = previousBackground; lease.Dispose(); throw; }
                return;
            }
            planetId = "fluid_test_" + Guid.NewGuid().ToString("N");
            origin = new Vector2Int(-2048, -2048);
            Key = new WorldAddress(planetId, "fluid_test").WorldKey;
            Atmosphere = new AtmosphereState { CapacityMoles = 100m, ReferenceMoles = 100m,
                ReferencePressureKPa = 100, TemperatureKelvin = 293.15,
                Gases = new List<AtmosphereGasAmount> { new() { FluidId = FluidIds.Oxygen, Moles = 50m } } };
            save.PlanetData_Dict.Add(planetId, new PlanetData { Name = planetId, Atmosphere = Atmosphere });
            try { lease = UseScope(Key); scope.BackgroundSimulation = true; keys.Add(Key); ownedKeys.Add(Key); }
            catch { save.PlanetData_Dict.Remove(planetId); throw; }
        }

        public MachineEntity Add(string itemId, int x = 0, int y = 0, int rotation = 0)
        {
            if (!GameRes.ExistingInstance.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition))
                throw new InvalidOperationException("流体测试缺少实际物品定义：" + itemId);
            ItemData data = definition.CreateItemData();
            data.Guid = ItemMgr.GetInstance().GenerateGuid();
            data.transform ??= new ItemTransform();
            data.transform.position = new Vector3(x + origin.x + .5f, y + origin.y + .5f);
            data.transform.rotation = Quaternion.identity; data.transform.scale = Vector3.one; data.inHand = false;
            Mod_Building.SetInstalledDataState(data);
            WriteMachineState(data, new MachineState { RotationQuarterTurns = rotation });
            MachineEntity node = Place(data);
            created.Add(node);
            MachineDefinition original = node.Definition;
            node.Definition = JsonConvert.DeserializeObject<MachineDefinition>(JsonConvert.SerializeObject(original, cloneSettings), cloneSettings);
            node.Definition.Content = original.Content;
            TopologyChanged(node);
            return node;
        }

        public void Rebuild() => RebuildGraphsIfDirty();
        public FluidNetworkGraph Graph { get { Rebuild(); return fluidGraph; } }
        public List<FluidTankGroup> Groups()
        { var result = new List<FluidTankGroup>(); CollectFluidTankGroups(result); return result; }
        public void Begin()
        {
            Rebuild(); TransportStep++; BeginFluidStep(Seconds); Rebuild();
        }
        public void Complete() => CompleteFluidStep(Seconds);
        public void Step() { Begin(); Complete(); }
        public void Tick() => MachineWorld.Tick(Seconds);
        public bool Budget(FluidInventory inventory, out FluidInventoryState stock, out double liters)
            => TryGetFluidTransferBudget(inventory, out stock, out liters);
        public bool GasBudget(FluidInventory inventory, out FluidInventoryState stock, out double liters)
            => TryGetGasTransferBudget(inventory, out stock, out liters);
        public void SetEngineWork(MachineEntity node, double watts) => SetFluidMechanicalPower(node, watts);
        public void ForgetDetached(MachineEntity node) => ForgetDetachedFluidNode(node);
        public Vector2 SurfacePoint(int x = 0, int y = 0) => new(origin.x + x + .5f, origin.y + y + .5f);
        private static Vector2Int FindSurfaceCells()
        {
            Player player = ItemMgr.Instance.User_Player ?? throw new InvalidOperationException("测试玩家不存在。");
            Vector2Int center = CellOf(player.transform.position);
            for (int y = -3; y <= 3; y++)
            for (int x = -3; x <= 3; x++)
            {
                var first = center + new Vector2Int(x, y); bool free = true;
                for (int offset = 0; offset < 3; offset++)
                {
                    Vector2Int cell = first + Vector2Int.right * offset;
                    if (!ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile((Vector2)cell + Vector2.one * .5f, out var sample) ||
                        sample.Cell.BlockingTileId != 0 || TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y) != 0 ||
                        IsOccupied(cell, 0) || IsOccupied(cell, 2) || BuildingOccupancyRegistry.IsOccupied(cell, layer: 0)) free = false;
                }
                if (free) return first;
            }
            throw new InvalidOperationException("玩家附近没有已加载的三个连续测试格。");
        }

        public string NewShip()
        {
            string id = planetId + "_ship_" + shipIds.Count;
            shipIds.Add(id); string key = GetShipScopeKey(id); keys.Add(key); ownedKeys.Add(key); return id;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                foreach (string key in keys)
                {
                    using (UseScope(key))
                    {
                        foreach (MachineEntity node in created)
                        {
                            if (node.ScopeKey != key || !nodes.ContainsKey(node.Id)) continue;
                            DiscardPlacementCandidate(node.Id); ForgetDetachedFluidNode(node);
                        }
                        if (!ownedKeys.Contains(key)) { scope.BackgroundSimulation = previousBackground; continue; }
                        save.Mechanical?.Worlds.Remove(key);
                        save.Mechanical?.RemovedGenerated.Remove(key);
                        save.Mechanical?.PendingPressureLiquidSpills.RemoveAll(spill => spill.WorldKey == key);
                        save.Mechanical?.PressureExplosions.RemoveAll(entry => entry.WorldKey == key);
                        save.Mechanical?.RecentPressureExplosionSources.RemoveAll(id => id.StartsWith(key + ":", StringComparison.Ordinal));
                        if (save.Mechanical != null)
                        {
                            var batches = new List<string>();
                            foreach (var pair in save.Mechanical.FluidRuptures)
                                if (pair.Value.WorldKey == key) batches.Add(pair.Key);
                            foreach (string id in batches) save.Mechanical.FluidRuptures.Remove(id);
                        }
                    }
                }
                foreach (string key in ownedKeys) scopes.Remove(key);
                foreach (string id in shipIds) shipScopeKeys.Remove(id);
                foreach (MachineEntity node in created)
                    if (node.Definition.Fluid != null) fluidBehaviors.Remove(node.Definition.Fluid);
                FluidDeviceOperations.ClearCompiledReactions();
            }
            finally { if (planetId != null) save.PlanetData_Dict.Remove(planetId); lease.Dispose(); }
        }
    }
    #endregion
}
