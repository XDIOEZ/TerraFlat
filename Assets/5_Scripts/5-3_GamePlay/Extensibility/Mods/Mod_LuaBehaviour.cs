using UnityEngine;

/// <summary>
/// 可由 MOD AssetBundle 预制体直接挂载的通用 Lua 行为模块。
/// Lua 文件需返回包含 OnLoad、OnUpdate、OnAct、OnSave 的 table，函数均可选。
/// </summary>
public sealed class Mod_LuaBehaviour : Module
{
    public const string ModuleId = "Mod_LuaBehaviour";

    #region 配置

    [SerializeField]
    private string modId;

    [SerializeField]
    private string scriptPath;

    [SerializeField]
    private ModuleTickMode tickMode = ModuleTickMode.FixedInterval;

    [SerializeField, Min(0.01f)]
    private float fixedTickInterval = 0.25f;

    [SerializeField]
    private Ex_ModData data = new();

    #endregion

    #region Module

    public override ModuleData _Data
    {
        get => data;
        set => data = value as Ex_ModData ?? new Ex_ModData();
    }

    public override ModuleTickMode TickMode => tickMode;
    public override float FixedTickInterval => Mathf.Max(0.01f, fixedTickInterval);
    public override string CanonicalModuleId => ModuleId;

    public override void Awake()
    {
        data ??= new Ex_ModData();
        data.ID = ModuleId;
        base.Awake();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        data ??= new Ex_ModData();
        data.ID = ModuleId;
        fixedTickInterval = Mathf.Max(0.01f, fixedTickInterval);
    }
#endif

    public override void Load()
    {
        if (ModRuntimeManager.Instance == null)
            return;

        data.BitData = ModRuntimeManager.Instance.InvokeItemLua(
            modId,
            scriptPath,
            "OnLoad",
            item,
            data.BitData);
    }

    public override void Save()
    {
        if (ModRuntimeManager.Instance == null)
            return;

        data.BitData = ModRuntimeManager.Instance.InvokeItemLua(
            modId,
            scriptPath,
            "OnSave",
            item,
            data.BitData);
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!data.isRunning || ModRuntimeManager.Instance == null)
            return;

        data.BitData = ModRuntimeManager.Instance.InvokeItemLua(
            modId,
            scriptPath,
            "OnUpdate",
            item,
            data.BitData,
            deltaTime);
    }

    public override void Act()
    {
        base.Act();
        if (ModRuntimeManager.Instance == null)
            return;

        data.BitData = ModRuntimeManager.Instance.InvokeItemLua(
            modId,
            scriptPath,
            "OnAct",
            item,
            data.BitData);
    }

    #endregion
}
