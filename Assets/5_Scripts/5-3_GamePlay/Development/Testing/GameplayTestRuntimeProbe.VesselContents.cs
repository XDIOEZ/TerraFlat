using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class GameplayTestRuntimeProbe
{
    #region 坩埚真实固体出料采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterVesselContentsRuntimeProbes()
        => Register("containers", "contents_spill", CaptureVesselContentsSpill);

    // 独立坩埚绑定正式模块，出料和失败撤销均走生产事务，不在夹具里扣量。
    private static JObject CaptureVesselContentsSpill(Player player, JObject args)
    {
        using IDisposable scope = GameplayWorldRegistry.UseWorld(player.gameObject.scene.name);
        var root = new GameObject("Lua Contents Crucible") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, player.gameObject.scene);
        root.transform.position = player.transform.position;
        GameItem item = root.AddComponent<GameItem>();
        Mod_WaterVessel vessel = root.AddComponent<Mod_WaterVessel>();
        Mod_Mortar mortar = root.AddComponent<Mod_Mortar>();
        var drops = new GameplayVesselDropCapture(player);
        using IDisposable registration = DroppedItemService.RegisterSpawnProvider(drops);
        try
        {
            item.BindData(CreateDragItem("Crucible"));
            item.itemData.MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = 500f };
            var liquidStorage = (Ex_ModData_MemoryPackable)item.itemData.ModuleDataDic["vessel"];
            var solidsStorage = (Ex_ModData_MemoryPackable)item.itemData.ModuleDataDic["mortar"];
            ItemData sand = ThermalMaterial("Sand", 3f, 700f);
            solidsStorage.WriteData(new MortarState { Bowl = CreateDragInventory("lua.crucible.solids", sand).Data });
            vessel.ModuleInit(item, liquidStorage);
            mortar.ModuleInit(item, solidsStorage);
            item.itemMods.AddMod(vessel);
            item.itemMods.AddMod(mortar);
            vessel.Load(); mortar.Load();
            vessel.Save(); mortar.Save();
            bool candidateCreated = Mod_Building.TryCreatePlacementCandidateData(item.itemData,
                player.transform.position, out ItemData candidate, out _, out string placementError);
            if (!candidateCreated) throw new InvalidOperationException(placementError);
            MortarState placedContents = MachineModuleState.Read<MortarState>(candidate, Mod_Mortar.ModuleId);
            ItemData placedSand = placedContents.Bowl.itemSlots[0].itemData;
            ItemSlot slot = mortar.Contents.Data.itemSlots[0];
            int guid = slot.itemData.Guid;
            bool wrongIdentity = VesselContentsOperations.TrySpill(vessel, player, 0, guid + 1, 1);
            float afterRejected = slot.Amount;
            bool spilled = VesselContentsOperations.TrySpill(vessel, player, 0, guid, 1);
            bool repeat = VesselContentsOperations.TrySpill(vessel, player, 0, guid, 1);
            bool alive = drops.Handles.Count == 1 && drops.Handles[0].IsValid;

            Inventory changed = CreateDragInventory("lua.crucible.changed", CreateDragItem("Sand", 2));
            ItemSlot changedSlot = changed.Data.itemSlots[0];
            drops.AfterSpawn = () => changedSlot.itemData.Stack.Amount = 3f;
            bool changedAccepted = InventoryWorldDrop.TryDrop(changedSlot, 2, player,
                player.transform.position, (Vector2)player.transform.position + Vector2.right, .5f,
                inventory: changed.Data);
            bool rollback = drops.Handles.Count == 2 && !drops.Handles[1].IsValid;
            return new JObject
            {
                ["wrongIdentity"] = wrongIdentity, ["afterRejected"] = afterRejected,
                ["spilled"] = spilled, ["repeat"] = repeat, ["remaining"] = slot.Amount,
                ["worldDropAlive"] = alive, ["dropAmount"] = drops.FirstAmount,
                ["dropTemperature"] = drops.FirstTemperature,
                ["playerDiscard"] = drops.FirstReason == DroppedItemSpawnReason.PlayerDiscard,
                ["sourceIsPlayer"] = drops.FirstSource == player,
                ["world"] = drops.FirstWorld, ["expectedWorld"] = player.gameObject.scene.name,
                ["changedAccepted"] = changedAccepted, ["changedAmount"] = changedSlot.Amount,
                ["candidateRolledBack"] = rollback,
                ["gameObjectBackend"] = MachineCatalog.Get("Crucible_Building") == null,
                ["candidateCreated"] = candidateCreated,
                ["placedSolidAmount"] = placedSand.Stack.Amount,
                ["placedSolidTemperature"] = placedSand.MatterState.TemperatureCelsius,
                ["placedShellTemperature"] = candidate.MatterState.TemperatureCelsius,
                ["shellStateIndependent"] = !ReferenceEquals(candidate.MatterState, item.itemData.MatterState)
            };
        }
        finally
        {
            foreach (DroppedItemHandle handle in drops.Handles) DroppedItemService.Remove(handle);
            try { mortar.Unload(); }
            finally { try { vessel.Unload(); } finally { UnityEngine.Object.DestroyImmediate(root); } }
        }
    }

    private sealed class GameplayVesselDropCapture : IDroppedItemSpawnProvider
    {
        private readonly Player actor;
        private bool spawning;
        public readonly List<DroppedItemHandle> Handles = new();
        public Action AfterSpawn;
        public float FirstAmount, FirstTemperature;
        public DroppedItemSpawnReason FirstReason;
        public Item FirstSource;
        public string FirstWorld;
        public GameplayVesselDropCapture(Player actor) { this.actor = actor; }
        public bool CanHandle(in DroppedItemSpawnContext context) => !spawning && context.SourceItem == actor;
        public DroppedItemHandle SpawnDrop(ItemData source, in DroppedItemSpawnContext context, float rotation, Vector3 scale)
        {
            spawning = true;
            try
            {
                if (Handles.Count == 0)
                {
                    FirstAmount = source.Stack.Amount; FirstTemperature = source.MatterState.TemperatureCelsius;
                    FirstReason = context.Reason; FirstSource = context.SourceItem;
                    FirstWorld = GameplayWorldRegistry.ExecutionWorldKey;
                }
                DroppedItemHandle handle = DroppedItemService.Spawn(source, context.Start, context.End,
                    context.Duration, rotation: rotation, spawnReason: context.Reason, sourceItem: context.SourceItem);
                Handles.Add(handle);
                AfterSpawn?.Invoke();
                return handle;
            }
            finally { spawning = false; }
        }
    }

    #endregion
}
