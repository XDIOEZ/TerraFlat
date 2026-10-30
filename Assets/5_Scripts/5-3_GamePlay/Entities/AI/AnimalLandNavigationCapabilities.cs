using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>外部推进目标及途径攻击选项，目标失效后继续前往最后坐标。</summary>
public sealed class AnimalAdvanceCapability : AnimalBehaviorTreeCapability
{
    #region 持久推进指令
    private readonly AnimalBehaviorTreeContext context;
    private int targetGuid;
    private Vector3 targetPosition;
    public bool HasCommand { get; private set; }
    public bool AttackActorsOnRoute { get; private set; }
    public float ArrivalDistance { get; private set; } = 1.25f;
    public Vector3 TargetPosition
    {
        get
        {
            Item target = targetGuid != 0 ? ItemMgr.Instance?.GetItemByGuid(targetGuid) : null;
            if (target != null) targetPosition = target.transform.position;
            return targetPosition;
        }
    }

    public AnimalAdvanceCapability(AnimalBehaviorTreeContext context, JObject config, JObject saved)
    {
        this.context = context;
        HasCommand = saved?["hasCommand"]?.Value<bool>() ?? false;
        targetGuid = saved?["targetGuid"]?.Value<int>() ?? 0;
        targetPosition = new Vector3(saved?["x"]?.Value<float>() ?? 0f, saved?["y"]?.Value<float>() ?? 0f,
            saved?["z"]?.Value<float>() ?? 0f);
        ArrivalDistance = saved?["arrivalDistance"]?.Value<float>() ?? 1.25f;
        AttackActorsOnRoute = saved?["attackActorsOnRoute"]?.Value<bool>() ?? false;
    }
    public void BeginAdvance(AIAdvanceCommand command)
    {
        if (!IsFinite(command.TargetPosition))
            throw new System.ArgumentException("推进目标必须是有限坐标。", nameof(command));
        targetGuid = command.TargetItemGuid;
        targetPosition = command.TargetPosition;
        ArrivalDistance = Mathf.Max(.05f, command.ArrivalDistance);
        AttackActorsOnRoute = command.AttackActorsOnRoute;
        HasCommand = true;
        if (context.TryGetCapability("pack", out AnimalPackCapability pack)) pack.ForgetGroupThreat();
        else if (context.TryGetCapability("memory", out AnimalTargetMemoryCapability memory)) memory.Forget();
    }
    public void CancelAdvance()
    {
        HasCommand = false;
        AttackActorsOnRoute = false;
        targetGuid = 0;
        if (context.TryGetCapability("pack", out AnimalPackCapability pack)) pack.ForgetGroupThreat();
        else if (context.TryGetCapability("memory", out AnimalTargetMemoryCapability memory)) memory.Forget();
        context.Mod_Mover?.StopMovement();
    }
    public override JObject CaptureSaveData() => new()
    {
        ["hasCommand"] = HasCommand, ["targetGuid"] = targetGuid,
        ["x"] = targetPosition.x, ["y"] = targetPosition.y, ["z"] = targetPosition.z,
        ["arrivalDistance"] = ArrivalDistance, ["attackActorsOnRoute"] = AttackActorsOnRoute
    };
    public static bool IsFinite(Vector3 position) => float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z);
    #endregion
}

/// <summary>维护最近的可见威胁，不决定动物应逃跑或反击。</summary>
public sealed class AnimalPerceivedThreatCapability : AnimalBehaviorTreeCapability
{
    #region 检测快照
    private readonly AnimalBehaviorTreeContext context;
    private readonly string[] tags;
    private readonly bool includePlayers;
    private readonly float radius;
    private float refreshRemaining;
    public Item Target { get; private set; }

    public AnimalPerceivedThreatCapability(AnimalBehaviorTreeContext context, JObject config, JObject saved)
    {
        this.context = context;
        tags = AnimalLandTargetRules.Strings(config, "tags");
        includePlayers = AnimalLandTargetRules.Flag(config, "includePlayers", true);
        radius = AnimalLandTargetRules.Number(config, "radius", 36f);
        if (context.Detector != null) context.Detector.DetectionRadius = Mathf.Max(context.Detector.DetectionRadius, radius);
    }
    public override void Tick(float deltaTime)
    {
        refreshRemaining -= deltaTime;
        if (refreshRemaining > 0f) return;
        refreshRemaining = .15f;
        Target = context.FindClosestThreat(radius, tags, includePlayers);
    }
    public override void Dispose() => Target = null;
    #endregion
}
