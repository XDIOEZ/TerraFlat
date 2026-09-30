using HarmonyLib;

namespace Example.EnderChest
{
    /// <summary>示例 MOD 的唯一入口，所有注册与 Harmony 补丁随会话释放。</summary>
    public sealed class ModEntry : IManagedGameMod
    {
        #region 生命周期
        internal const string ChestId = "example.enderchest:chest";
        internal const string SummonerId = ChestId + "_Summoner";
        internal const string VaultKey = "example.enderchest:vault";
        internal EnderVault Vault { get; private set; }
        internal static ModEntry Current { get; private set; }
        private Harmony harmony;

        public void Initialize(ManagedModContext context)
        {
            EnderChestContent.Register(context);
            Vault = new EnderVault();
            context.Track(MachineInventoryCommands.RegisterPrivateInventory(VaultKey, Vault.Resolve));
            Current = this;
            harmony = new Harmony(context.ModId);
            harmony.PatchAll(typeof(ModEntry).Assembly);
        }

        public void ContentReady() { }

        public void Dispose()
        {
            harmony?.UnpatchAll(harmony.Id);
            harmony = null;
            if (ReferenceEquals(Current, this)) Current = null;
            Vault?.Dispose();
            Vault = null;
        }
        #endregion
    }

    [HarmonyPatch(typeof(MachineLogicRegistry), nameof(MachineLogicRegistry.Create))]
    internal static class EnderChestLogicPatch
    {
        #region 机器玩法
        private static bool Prefix(MachineEntity entity, ref MachineLogic __result)
        {
            if (entity?.Definition?.Id != ModEntry.ChestId) return true;
            __result = new EnderChestLogic(entity);
            return false;
        }
        #endregion
    }

    [HarmonyPatch(typeof(MachinePanelSession), nameof(MachinePanelSession.Create))]
    internal static class EnderChestPanelPatch
    {
        #region 面板入口
        private static bool Prefix(MachineEntity entity, ref IMachinePanelSession __result)
        {
            if (entity?.Definition?.Id != ModEntry.ChestId) return true;
            __result = new EnderChestPanelSession(entity);
            return false;
        }
        #endregion
    }

    /// <summary>箱体没有公共库存；实体快照只保存放置和耐久等原生状态。</summary>
    internal sealed class EnderChestLogic : MachineLogic
    {
        #region 私有库存入口
        public EnderChestLogic(MachineEntity entity) : base(entity) { }
        public override void Capture() { }
        public override bool Execute(string operation, string argument, Player actor)
            => operation == "inventory.private-open" && argument == ModEntry.VaultKey && actor != null;
        public override bool ApplyRemoteSnapshot(ItemData snapshot) => snapshot?.IDName == ModEntry.ChestId;
        #endregion
    }
}
