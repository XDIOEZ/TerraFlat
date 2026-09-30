using System;
using TMPro;

namespace Example.EnderChest
{
    /// <summary>复用木箱面板，但每次打开都绑定当前角色的末影箱库存。</summary>
    internal sealed class EnderChestPanelSession : IMachinePanelSession
    {
        #region 面板绑定
        private readonly MachineEntity entity;
        private BasePanel panel;
        private Inventory inventory;
        private bool disposed;

        public EnderChestPanelSession(MachineEntity entity)
            => this.entity = entity ?? throw new ArgumentNullException(nameof(entity));

        public bool IsAlive => !disposed;
        public bool IsOpen => !disposed && panel != null && panel.IsOpen();

        public void Toggle(Item item)
        {
            if (disposed) return;
            if (IsOpen) { Close(); return; }
            Player actor = item as Player ?? item?.GetComponentInParent<Player>();
            Inventory hand = item?.GetComponentInChildren<Mod_Hand>()?.HandInventory;
            Inventory vault = ModEntry.Current?.Vault.Resolve(actor, entity);
            if (actor == null || hand == null || vault == null)
                throw new InvalidOperationException("末影箱交互缺少角色、手部库存或私有库存。");
            if (panel == null || !ReferenceEquals(inventory, vault)) Bind(vault);
            MachineWorld.RequestOperation(entity, "inventory.private-open", ModEntry.VaultKey, actor);
            panel.Open();
            inventory.DefaultTarget_Inventory = hand;
            inventory.SyncQuickTransferTarget(panel);
            inventory.RefreshUI();
        }

        private void Bind(Inventory vault)
        {
            if (panel != null) DisposePanel();
            if (vault.basePanel != null) vault.basePanel.Close();
            vault.MachineOwner = entity;
            if (vault.InventoryPanel_Prefab == null)
                throw new InvalidOperationException("末影箱缺少木箱面板 Prefab。");
            BasePanel created = UIManager.Instance.CreatePanelFromGameObject(vault.InventoryPanel_Prefab);
            panel = created;
            inventory = vault;
            try
            {
                panel.InitClosed();
                panel.SetGameplayInputBlocking(false);
                panel.PrepareForGamepadNavigation(closeOnCancel: true);
                InventoryPanelLayout.ApplyDefaultCraftingPosition(
                    panel.Dragger != null ? panel.Dragger.rectTransform : panel.rectTransform);
                if (panel.TryGetText("FWUI_标题", out TextMeshProUGUI title)) title.text = "末影箱";
                panel.Closed += OnClosed;
                inventory.basePanel = panel;
                inventory.InitUI();
            }
            catch { DisposePanel(); throw; }
        }

        private void OnClosed() => ClearBinding();

        private void ClearBinding()
        {
            if (inventory == null || !ReferenceEquals(inventory.basePanel, panel)) return;
            inventory.DefaultTarget_Inventory = null;
            inventory.SyncQuickTransferTarget(null);
            if (ReferenceEquals(inventory.MachineOwner, entity)) inventory.MachineOwner = null;
        }

        public void Refresh()
        {
            if (IsOpen) inventory?.RefreshUI();
        }

        public void Close()
        {
            if (panel != null && panel.IsOpen()) panel.Close();
            ClearBinding();
        }

        public void Dispose()
        {
            if (disposed) return;
            Close();
            disposed = true;
            DisposePanel();
        }

        private void DisposePanel()
        {
            if (panel == null) return;
            panel.Closed -= OnClosed;
            if (inventory != null && ReferenceEquals(inventory.basePanel, panel))
            {
                inventory.itemSlot_UI.Clear();
                inventory.basePanel = null;
            }
            UIManager.ExistingInstance?.DestroyPanel(panel);
            panel = null;
            inventory = null;
        }
        #endregion
    }
}
