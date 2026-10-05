using HarmonyLib;

namespace Example.HarmonyMachines
{
    /// <summary>示例只修改炉温上限；卸载只撤销本 MOD 自己的补丁。</summary>
    public sealed class ModEntry : IManagedGameMod
    {
        #region MOD 生命周期
        private Harmony harmony;
        public void Initialize(ManagedModContext context)
        {
            harmony = new Harmony(context.ModId);
            harmony.PatchAll(typeof(ModEntry).Assembly);
        }
        public void ContentReady() { }
        public void Dispose()
        {
            if (harmony == null) return;
            harmony.UnpatchAll(harmony.Id);
            harmony = null;
        }
        #endregion
    }

    [HarmonyPatch(typeof(FurnaceLogic), nameof(FurnaceLogic.CalculateMaximumTemperature))]
    public static class ExtraFurnaceTemperature
    {
        #region 炉温补丁
        private static void Postfix(ref float __result) => __result += 100f;
        #endregion
    }
}
