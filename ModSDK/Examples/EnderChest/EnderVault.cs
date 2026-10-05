using System;
using System.Collections.Generic;
using FlatWorld.Networking;

namespace Example.EnderChest
{
    /// <summary>按角色身份保存私有库存；同一角色的各个箱体只提供访问入口。</summary>
    internal sealed class EnderVault : IDisposable
    {
        #region 角色库存
        private readonly Dictionary<string, VaultRecord> records = new(StringComparer.Ordinal);
        private GameSaveData currentSave;

        public Inventory Resolve(Player actor, MachineEntity entity)
        {
            if (actor?.Data == null || string.IsNullOrWhiteSpace(actor.ProfileId) ||
                entity?.Definition?.Id != ModEntry.ChestId) return null;
            GameSaveData save = SaveDataMgr.Instance?.SaveData;
            if (save == null) return null;
            if (!ReferenceEquals(currentSave, save))
            {
                DisposeRecords();
                currentSave = save;
            }

            string id = actor.ProfileId;
            if (records.TryGetValue(id, out VaultRecord record) &&
                !ReferenceEquals(record.PlayerData, actor.Data))
            {
                record.Dispose();
                records.Remove(id);
                record = null;
            }
            if (record == null)
            {
                Inventory template = GetTemplate(entity);
                Inventory_Data saved = MachinePersistence.Read<Inventory_Data>(actor.Data, ModEntry.VaultKey);
                Inventory inventory = MachineInventory.Create(template, saved,
                    "末影箱", template.Data.itemSlots.Count);
                inventory.Data.Name = "末影箱";
                record = new VaultRecord(save, id, actor.Data, inventory);
                records.Add(id, record);
            }
            record.Inventory.MachineOwner = entity;
            return record.Inventory;
        }

        private static Inventory GetTemplate(MachineEntity entity)
        {
            if (entity.Definition.Content == null)
            {
                GameRes resources = GameRes.ExistingInstance;
                entity.Definition.Content = new MachineContent(resources.ItemDefinitions[ModEntry.ChestId]);
            }
            var config = entity.Definition.Content.Find<Mod_Inventory>();
            var authoring = config?.Authoring as Mod_Inventory;
            if (authoring?.InventoryInstances == null || authoring.InventoryInstances.Count == 0 ||
                authoring.InventoryInstances[0]?.Data == null)
                throw new InvalidOperationException("末影箱缺少木箱库存模板。");
            return authoring.InventoryInstances[0];
        }

        public void Dispose() => DisposeRecords();

        private void DisposeRecords()
        {
            foreach (VaultRecord record in records.Values) record.Dispose();
            records.Clear();
            currentSave = null;
        }

        private sealed class VaultRecord : IDisposable
        {
            private readonly GameSaveData save;
            private readonly string profileId;
            public readonly Data_Player PlayerData;
            public readonly Inventory Inventory;

            public VaultRecord(GameSaveData save, string profileId, Data_Player playerData, Inventory inventory)
            {
                this.save = save;
                this.profileId = profileId;
                PlayerData = playerData;
                Inventory = inventory;
                Inventory.Data.Event_OnDataChanged += OnChanged;
            }

            private void OnChanged(ItemSlot slot)
            {
                if (!GameNetwork.HasStateAuthority) return;
                MachinePersistence.Write(PlayerData, ModEntry.VaultKey, Inventory.Data);
                if (save.PlayerData_Dict != null) save.PlayerData_Dict[profileId] = PlayerData;
            }

            public void Dispose()
            {
                Inventory.Data.Event_OnDataChanged -= OnChanged;
                Inventory.DefaultTarget_Inventory = null;
                Inventory.SyncQuickTransferTarget(null);
                Inventory.UnbindRuntimeDataEvents();
                Inventory.MachineOwner = null;
                Inventory.itemSlot_UI.Clear();
                Inventory.basePanel = null;
            }
        }
        #endregion
    }
}
