#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>隔离世界实走自行车上下车、骑行、涉水与碰撞推动，结束截图后归还现场。</summary>
public static class GameplayTestBicycleRuntimeProbe
{
    #region 自行车现场登记与采样
    private static BicycleScope active;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register() => GameplayTestRuntimeProbe.Register("player", "bicycle_lifecycle", CaptureBicycleLifecycle);

    private static JObject CaptureBicycleLifecycle(Player player, JObject args)
    {
        string action = args.Value<string>("action") ?? "begin";
        if (action == "begin")
        {
            if (active != null) throw new InvalidOperationException("自行车现场尚未清理。");
            active = new BicycleScope(player);
            GameplayTestRuntimeProbe.RegisterCleanup(active);
            active.CreateAndMount();
        }
        BicycleScope scope = active ?? throw new InvalidOperationException("自行车现场尚未建立。");
        if (action == "wall_mark") scope.MarkBlockedWheelPose();
        JObject result = scope.Sample();
        if (action == "wall") scope.AddBlockingWall();
        if (action == "water") scope.FillWater();
        if (action == "push") scope.BeginPush();
        if (action == "finish") scope.MountForScreenshot();
        result["mountedAfterAction"] = scope.Carrier.Rider == scope.Mover;
        return result;
    }
    #endregion

    #region 临时车体、液体与玩家位置租约
    private sealed class BicycleScope : IDisposable
    {
        private readonly Player player;
        private readonly Vector3 originalPosition;
        private readonly List<(Vector2 position, string liquid, int level)> liquids = new();
        private Item bicycle;
        private GameObject wall;
        private Vector2 startPosition, pushStart;
        private Vector2 blockedPosition;
        private float blockedWheelAngle;
        private bool disposed;
        public Mod_Mover Mover { get; }
        public Mod_Carrier Carrier { get; private set; }

        public BicycleScope(Player actor)
        {
            player = actor;
            originalPosition = actor.transform.position;
            Mover = actor.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
            if (Mover == null || Mover.CarrierSource != null) throw new InvalidOperationException("自行车现场需要未乘车的隔离玩家。");
        }

        public void CreateAndMount()
        {
            Vector2 site = FindClearRidingSite();
            SetPlayerPosition(site + Vector2.down);
            ItemData data = GameRes.ExistingInstance.CreateItemData("Bicycle_Building");
            Ex_ModData module = (Ex_ModData)data.ModuleDataDic[ModText.Building];
            Mod_Building.Building_Data state = module.GetData<Mod_Building.Building_Data>();
            state.State = BuildingState.Installed;
            module.WriteData(state);
            bicycle = ItemMgr.Instance.InstantiateItem(data, site);
            bicycle.Load();
            Carrier = bicycle.GetComponentInChildren<Mod_Carrier>(true);
            if (Carrier == null || !Carrier.IsAvailable) throw new InvalidOperationException("自行车正式载具未就绪。");
            startPosition = Carrier.Body.position;
            Carrier.OnInteractStart(player);
        }

        private Vector2 FindClearRidingSite()
        {
            Vector2 center = player.transform.position;
            for (int y = -8; y <= 8; y += 4)
            for (int x = -8; x <= 8; x += 4)
            {
                Vector2 first = new(Mathf.Floor(center.x) + x + .5f, Mathf.Floor(center.y) + y + .5f);
                bool clear = true;
                for (int row = -2; row <= 2 && clear; row++)
                for (int column = -2; column <= 10; column++)
                {
                    Vector2 position = first + new Vector2(column, row);
                    if (!ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(position, out RuntimeTerrainTileSample tile) ||
                        tile.LiquidDepth > 0f || !tile.Terrain.IsWalkable(tile.LocalCell.x, tile.LocalCell.y) ||
                        tile.Cell.BlockingTileId != 0 || tile.Cell.BackTileId != 0 || BuildingOccupancyRegistry.IsOccupied(tile.WorldCell))
                    { clear = false; break; }
                }
                if (clear) return first;
            }
            throw new InvalidOperationException("隔离玩家附近没有13×5格干燥骑行区域。");
        }

        public JObject Sample()
        {
            CarrierWheelPresentation wheels = bicycle.GetComponent<CarrierWheelPresentation>();
            return new JObject
            {
                ["available"] = Carrier.IsAvailable, ["mounted"] = Carrier.Rider == Mover,
                ["riderBodySimulated"] = Mover.rb.simulated, ["bodySimulated"] = Carrier.Body.simulated,
                ["distance"] = WorldTopologyRuntime.Distance(startPosition, Carrier.Body.position),
                ["pushDistance"] = WorldTopologyRuntime.Distance(pushStart, Carrier.Body.position),
                ["speed"] = Carrier.CurrentVelocity.magnitude, ["drivenSpeed"] = Carrier.DrivenVelocity.magnitude,
                ["wheelAngle"] = wheels.WheelAngle, ["sameWheelRotation"] = wheels.RearWheel.localRotation == wheels.FrontWheel.localRotation,
                ["blockedDistance"] = WorldTopologyRuntime.Distance(blockedPosition, Carrier.Body.position),
                ["blockedWheelTurn"] = Mathf.Abs(Mathf.DeltaAngle(blockedWheelAngle, wheels.WheelAngle)),
                ["pusherIsPlayer"] = Carrier.PushSource == Mover,
                ["seatError"] = WorldTopologyRuntime.Distance(player.transform.position, Carrier.SeatPosition),
                ["inWater"] = ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(Carrier.Body.position, out RuntimeTerrainTileSample tile) && tile.LiquidDepth > 0f
            };
        }

        public void FillWater()
        {
            Carrier.StopMotion();
            if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
            Vector2Int center = Vector2Int.FloorToInt(Carrier.Body.position);
            var boundary = new List<Vector2Int>();
            for (int y = -3; y <= 3; y++)
            for (int x = -3; x <= 4; x++)
            {
                if (y is -3 or 3 || x is -3 or 4) { boundary.Add(center + new Vector2Int(x, y)); continue; }
                Vector2 position = (Vector2)(center + new Vector2Int(x, y)) + Vector2.one * .5f;
                if (!ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(position, out RuntimeTerrainTileSample tile))
                    throw new InvalidOperationException("自行车涉水区域未加载。");
                liquids.Add((position, tile.Terrain.GetLiquidId(tile.LocalCell.x, tile.LocalCell.y),
                    tile.Terrain.GetLiquidLevel(tile.LocalCell.x, tile.LocalCell.y)));
                if (!WorldLiquidSystem.TrySet(position, "core:dirty_water", 5))
                    throw new InvalidOperationException("自行车涉水现场设置失败。");
            }
            WorldLiquidFlowObstacles.Register(this, boundary);
        }

        private void RestoreWater()
        {
            foreach (var entry in liquids) WorldLiquidSystem.TrySet(entry.position, entry.liquid, entry.level);
            liquids.Clear();
            WorldLiquidFlowObstacles.Unregister(this);
        }

        public void AddBlockingWall()
        {
            wall = new GameObject("[BicycleCollisionWall]");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wall, player.gameObject.scene);
            wall.transform.position = Carrier.Body.position + Vector2.right * 1.2f;
            wall.AddComponent<BoxCollider2D>().size = new Vector2(.2f, 3f);
            Physics2D.SyncTransforms();
        }

        public void MarkBlockedWheelPose()
        {
            blockedPosition = Carrier.Body.position;
            blockedWheelAngle = bicycle.GetComponent<CarrierWheelPresentation>().WheelAngle;
        }

        public void BeginPush()
        {
            Carrier.StopMotion();
            RestoreWater();
            Carrier.OnInteractStart(player);
            SetPlayerPosition(Carrier.Body.position + Vector2.left * 1.35f + Vector2.down * .22f);
            pushStart = Carrier.Body.position;
        }

        public void MountForScreenshot()
        {
            Carrier.StopMotion();
            if (Carrier.Rider == null) Carrier.OnInteractStart(player);
        }

        private void SetPlayerPosition(Vector2 position)
        {
            Mover.ResetMotion();
            Mover.rb.position = position;
            player.transform.position = new Vector3(position.x, position.y, originalPosition.z);
            player.itemData.transform.position = WorldTopologyRuntime.NormalizePosition(position);
            Physics2D.SyncTransforms();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (ReferenceEquals(Mover.CarrierSource, Carrier)) Mover.DetachCarrier(null);
            if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
            if (bicycle != null) ItemMgr.Instance.DespawnItem(bicycle, saveData: false);
            RestoreWater();
            SetPlayerPosition(originalPosition);
            if (ReferenceEquals(active, this)) active = null;
        }
    }
    #endregion
}
#endif
