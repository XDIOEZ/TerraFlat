#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.NaturalEntities;
using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameplayTestWorldScopeRuntimeProbe
{
    #region 多世界正式索引与物理采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif
    private static void Register() => GameplayTestRuntimeProbe.RegisterAsync("networking", "world_scope", Capture);

    private sealed class Fixture : IDisposable
    {
        public readonly List<ShipGroundBuildingStore.Entry> Buildings = new();
        public readonly List<NaturalEntityHandle> Resources = new();
        public readonly List<GameObject> Bodies = new();
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (NaturalEntityHandle handle in Resources) NaturalEntityEcsService.Remove(handle);
            foreach (ShipGroundBuildingStore.Entry building in Buildings) ShipGroundBuildingStore.RollbackCreated(building);
            foreach (GameObject body in Bodies) if (body != null) UnityEngine.Object.Destroy(body);
        }
    }

    // 同坐标使用真实地面工厂、自然实体入口和独立 PhysicsScene，不重算各系统规则。
    private static IEnumerator Capture(Player player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        Scene initialScene = SceneManager.GetActiveScene();
        string first = player.Data.CurrentSceneName;
        global::WorldAddress address = global::WorldAddress.FromWorldKey(first);
        string second = address.WithDimension(address.IsSurface ? global::WorldAddress.CaveDimensionId : global::WorldAddress.SurfaceDimensionId).WorldKey;
        Vector2 point = WorldLocalPresentation.ToLogical((Vector2)player.transform.position) + Vector2.right * 24f;
        var own = new Fixture();
        GameplayTestRuntimeProbe.RegisterCleanup(own);
        using (own)
        {
            yield return GameplayWorldRegistry.PrepareWorld(first, point);
            yield return GameplayWorldRegistry.PrepareWorld(second, point);
            var result = new JObject { ["firstWorld"] = first, ["secondWorld"] = second };
            Vector2Int cell = new(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y));
            foreach (string world in new[] { first, second })
                using (GameplayWorldRegistry.UseWorld(world))
                {
                    ItemData data = GameRes.Instance.CreateItemData("Chest_Wood");
                    data.Guid = ItemMgr.Instance.GenerateGuid();
                    Mod_Building.SetInstalledDataState(data);
                    own.Buildings.Add(ShipGroundBuildingStore.Place(data, cell, 0, world));
                }
            float deadline = Time.realtimeSinceStartup + 15f;
            while (own.Buildings.Exists(value => value.Item != null && !value.Item.IsInitialized))
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("多世界自有建筑初始化超时。");
                yield return null;
            }
            int guid = ItemMgr.Instance.GenerateGuid();
            object firstEcs = null, secondEcs = null;
            foreach (string world in new[] { first, second })
                using (GameplayWorldRegistry.UseWorld(world))
                {
                    var definition = GameRes.Instance.ItemDefinitions["Tree_Pine"];
                    ItemData data = definition.CreateItemData();
                    data.Guid = guid;
                    data.transform ??= new ItemTransform();
                    data.transform.position = new Vector3(cell.x + 3.5f, cell.y + .5f);
                    string dimension = ChunkMgr.Instance.ResolveWorldAddress(data.transform.position).DimensionId;
                    if (!NaturalEntityEcsService.TryRegister(definition, guid, data.transform.position, 26f, 0f,
                        dimension, data, out NaturalEntityHandle handle)) throw new InvalidOperationException("多世界自然实体未创建。");
                    own.Resources.Add(handle);
                    if (world == first) firstEcs = WorldEntityRuntime.Current; else secondEcs = WorldEntityRuntime.Current;
                }
            result["distinctEcsWorlds"] = !ReferenceEquals(firstEcs, secondEcs);
            result["distinctResourceHandles"] = own.Resources[0] != own.Resources[1];
            using (GameplayWorldRegistry.UseWorld(first)) result["firstOccupied"] = BuildingOccupancyRegistry.IsOccupied(cell);
            using (GameplayWorldRegistry.UseWorld(second)) result["secondOccupied"] = BuildingOccupancyRegistry.IsOccupied(cell);
            ShipGroundBuildingStore.RollbackCreated(own.Buildings[0]);
            own.Buildings.RemoveAt(0);
            NaturalEntityEcsService.Remove(own.Resources[0]);
            result["firstResourceRemoved"] = !NaturalEntityEcsService.Contains(own.Resources[0]);
            result["secondResourceAlive"] = NaturalEntityEcsService.Contains(own.Resources[1]);
            using (GameplayWorldRegistry.UseWorld(second)) result["secondStillOccupied"] = BuildingOccupancyRegistry.IsOccupied(cell);
            var colliders = new List<Collider2D>();
            Vector2 physicsPoint = point + Vector2.up * 128f;
            foreach (string world in new[] { first, second })
            {
                var body = new GameObject("Lua自有世界物理体");
                own.Bodies.Add(body);
                SceneManager.MoveGameObjectToScene(body, GameplayWorldRegistry.GetOrCreate(world).Scene);
                body.layer = 31;
                body.transform.position = physicsPoint;
                colliders.Add(body.AddComponent<BoxCollider2D>());
            }
            Physics2D.SyncTransforms();
            var hits = new Collider2D[16];
            foreach (string world in new[] { first, second })
                using (GameplayWorldRegistry.UseWorld(world))
                {
                    int index = world == first ? 0 : 1;
                    int count = GameplayPhysics2D.OverlapCircleNonAlloc(physicsPoint, .4f, hits, 1 << 31);
                    bool ownHit = false, otherHit = false;
                    for (int i = 0; i < count; i++) { ownHit |= hits[i] == colliders[index]; otherHit |= hits[i] == colliders[1 - index]; }
                    result[index == 0 ? "firstPhysicsOwn" : "secondPhysicsOwn"] = ownHit;
                    result[index == 0 ? "firstPhysicsOther" : "secondPhysicsOther"] = otherHit;
                }
            WeatherMgr a = WeatherMgr.GetForWorld(first), b = WeatherMgr.GetForWorld(second);
            result["distinctWeatherManagers"] = a != null && b != null && a != b && a.WorldKey == first && b.WorldKey == second;
            result["sceneUnchanged"] = SceneManager.GetActiveScene() == initialScene;
            result["localPlayerUnchanged"] = ReferenceEquals(player, ItemMgr.Instance.User_Player);
            completed(result);
        }
    }
    #endregion
}
#endif
