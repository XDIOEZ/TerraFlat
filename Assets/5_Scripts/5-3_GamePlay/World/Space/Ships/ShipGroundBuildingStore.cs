using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public static class ShipGroundBuildingStore
    {
        #region 普通地面建筑引用
        public sealed class Entry
        {
            public Item Item;
            public MachineEntity Machine;
            public ItemData Data;
            public Vector2Int Cell;
            public int QuarterTurns;
            public int Guid => Data.Guid;

            // 收集只保留真实引用，被选中的成员才捕获完整库存与模块快照。
            public ItemData Capture()
            {
                if (Machine != null)
                {
                    Data = MachineWorld.CaptureSnapshot(Machine) ??
                        throw new InvalidOperationException("待装配机器已离开地面世界。");
                    Cell = Machine.Cell;
                    QuarterTurns = Machine.RotationQuarterTurns;
                }
                else
                {
                    if (Item == null || Item.DestructionHandled)
                        throw new InvalidOperationException("待装配建筑已离开地面世界。");
                    Item.Save();
                    Data = FastCloner.FastCloner.DeepClone(Item.itemData);
                    Vector2 position = WorldLocalPresentation.ToLogical((Vector2)Item.transform.position);
                    Cell = new Vector2Int(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y));
                    QuarterTurns = Mathf.RoundToInt(Item.transform.eulerAngles.z / 90f) & 3;
                }
                return Data;
            }
        }

        public static List<Entry> Collect(string worldKey)
        {
            if (string.IsNullOrWhiteSpace(worldKey)) throw new ArgumentException("地面世界地址为空。", nameof(worldKey));
            var result = new List<Entry>();
            ItemMgr items = ItemMgr.Instance;
            if (items != null && worldKey == SceneManager.GetActiveScene().name)
                foreach (Item item in items.WorldRunTimeItems.Values)
                {
                    if (item == null || item.DestructionHandled || item.itemData == null || item.InHand ||
                        item.gameObject.scene.name != worldKey ||
                        item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building)?.IsInstalled() != true ||
                        MachineWorld.OwnsWorldItem(item.itemData)) continue;
                    Vector2 position = WorldLocalPresentation.ToLogical((Vector2)item.transform.position);
                    result.Add(new Entry { Item = item, Data = item.itemData,
                        Cell = new Vector2Int(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)),
                        QuarterTurns = Mathf.RoundToInt(item.transform.eulerAngles.z / 90f) & 3 });
                }
            using (MachineWorld.UseScope(worldKey))
            {
                var machines = new List<MachineEntity>();
                MachineWorld.CollectNodes(machines);
                foreach (MachineEntity machine in machines)
                    result.Add(new Entry { Machine = machine, Data = machine.Snapshot,
                        Cell = machine.Cell, QuarterTurns = machine.RotationQuarterTurns });
            }
            return result;
        }
        #endregion

        #region 普通地面建筑创建与卸载
        public static Entry Place(ItemData data, Vector2Int cell, int quarterTurns, string worldKey)
            => PlaceCore(data, cell, quarterTurns, worldKey, null, true);

        public static Entry PlacePrepared(ItemData data, Vector2Int cell, int quarterTurns, string worldKey,
            ItemMgr.PreparedItemHandle prepared)
            => PlaceCore(data, cell, quarterTurns, worldKey, prepared, false);

        private static Entry PlaceCore(ItemData data, Vector2Int cell, int quarterTurns, string worldKey,
            ItemMgr.PreparedItemHandle prepared, bool activate)
        {
            if (!GameNetwork.HasStateAuthority || data == null || data.Guid == 0 ||
                string.IsNullOrWhiteSpace(worldKey)) throw new InvalidOperationException("地面建筑数据无效或没有世界写入权限。");
            bool machine = MachineCatalog.Get(data.IDName) != null;
            if (!machine && worldKey != SceneManager.GetActiveScene().name)
                throw new InvalidOperationException("普通建筑需要在目标地表场景就绪后恢复。");
            quarterTurns &= 3;
            Vector3 logical = new(cell.x + .5f, cell.y + .5f, 0f);
            Quaternion rotation = Quaternion.Euler(0f, 0f, quarterTurns * 90f);
            data.transform ??= new ItemTransform();
            data.transform.position = logical;
            data.transform.rotation = rotation;
            data.transform.scale = Vector3.one;
            data.inHand = false;
            if (data.Stack != null) { data.Stack.Amount = 1f; data.Stack.CanBePickedUp = false; }
            Mod_Building.SetInstalledDataState(data);
            var entry = new Entry { Data = data, Cell = cell, QuarterTurns = quarterTurns };
            try
            {
                using var scope = MachineWorld.UseScope(worldKey);
                if (machine)
                {
                    if (MachineWorld.GetById(data.Guid) != null)
                        throw new InvalidOperationException("落地机器身份已经被使用：" + data.Guid);
                    MachineState state = MachineWorld.ReadMachineState(data);
                    state.RotationQuarterTurns = quarterTurns;
                    MachineWorld.WriteMachineState(data, state);
                    entry.Machine = MachineWorld.RestoreMachine(data);
                    entry.Data = entry.Machine.Snapshot;
                }
                else
                {
                    if (ItemMgr.Instance == null) throw new InvalidOperationException("物品管理器尚未就绪。");
                    if (prepared != null)
                    {
                        entry.Item = ItemMgr.Instance.CommitPreparedItem(prepared, data,
                            WorldLocalPresentation.ProjectPosition(logical), rotation, Vector3.one, activate: activate);
                    }
                    else
                    {
                        // 提前准备失败时沿用原创建链，最终显示仍等整组所有权移交成功。
                        entry.Item = ItemMgr.Instance.InstantiateItem(data, WorldLocalPresentation.ProjectPosition(logical), rotation, Vector3.one);
                        if (!activate) entry.Item.gameObject.SetActive(false);
                        entry.Item.Load();
                    }
                    Mod_Building building = entry.Item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building) ??
                        throw new InvalidOperationException("落地成员缺少普通建筑模块：" + data.IDName);
                    building.SetAsInstalled(initializeHealth: false);
                    entry.Item.transform.rotation = rotation;
                    entry.Item.Save();
                    if (activate) BuildingPlacementLifecycle.NotifyCommitted(entry.Item);
                }
                return entry;
            }
            catch
            {
                RollbackCreated(entry);
                throw;
            }
        }

        public static void RevealPrepared(Entry entry)
        {
            if (entry?.Item == null || entry.Item.DestructionHandled) return;
            entry.Item.gameObject.SetActive(true);
            BuildingPlacementLifecycle.NotifyCommitted(entry.Item);
        }

        public static void RemoveItem(Entry entry)
        {
            if (entry?.Machine != null) throw new InvalidOperationException("机器必须通过作用域迁移移交，不能按建筑卸载。");
            if (entry?.Item == null || entry.Item.DestructionHandled) return;
            Item item = entry.Item;
            if (ItemMgr.Instance == null) throw new InvalidOperationException("物品管理器尚未就绪。");
            ItemMgr manager = ItemMgr.Instance;
            int guid = entry.Guid;
            try
            {
                SaveDataMgr.Instance?.RemoveRuntimeBuildingRecord(item);
                item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building)?.ReleasePlacementOccupancy();
                manager.DespawnItem(item, saveData: false);
            }
            catch (Exception error)
            {
                // 注销后的外部通知失败不撤销已完成的所有权移交。
                if (item == null || item.DestructionHandled ||
                    !manager.WorldRunTimeItems.TryGetValue(guid, out Item current) || !ReferenceEquals(current, item))
                { Debug.LogException(error); return; }
                SaveDataMgr.Instance?.RecordRuntimeBuildingChange(item);
                throw;
            }
        }

        public static void RollbackCreated(Entry entry)
        {
            if (entry == null) return;
            if (entry.Machine != null)
            {
                using var scope = MachineWorld.UseNodeScope(entry.Machine);
                if (!MachineWorld.DiscardPlacementCandidate(entry.Machine.Id))
                    throw new InvalidOperationException("落地机器候选未能撤销。");
            }
            else RemoveItem(entry);
        }
        #endregion
    }
}
