using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 地表重力移动采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterMovementRules()
    {
        Register("player", "surface_gravity_movement", PlayerSurfaceGravityMovement);
        Register("player", "motion_reset", PlayerMotionReset);
    }

    // 独立角色只调用正式速度过渡，不切换当前世界、不启动物理模拟。
    private static JObject PlayerSurfaceGravityMovement(JObject args)
    {
        var root = new GameObject("[LuaSurfaceGravityMovement]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            Item actor = Flag(args, "player", true)
                ? root.AddComponent<Player>() : root.AddComponent<GameItem>();
            Mod_Mover mover = root.AddComponent<Mod_Mover>();
            mover.ModuleInit(actor, new Ex_ModData_MemoryPackable { ID = ModText.Mod_Mover, Enabled = true });
            mover.Speed = new GameValue_float(10f);
            SpaceGameplaySettings settings = SpaceGameplaySettings.Current;
            JObject Sample(double gravity)
            {
                Vector2 current = new(10f, 0f);
                Vector2 turn = mover.SmoothSurfaceVelocity(current, new Vector2(0f, 10f), .01f, gravity);
                Vector2 target = new((float)(10d * settings.WalkingMultiplier(gravity)), 0f);
                Vector2 settled = Vector2.zero;
                for (int i = 0; i < 1200; i++)
                    settled = mover.SmoothSurfaceVelocity(settled, target, .02f, gravity);
                float settledSpeed = settled.magnitude;
                for (int i = 0; i < 1200; i++)
                    settled = mover.SmoothSurfaceVelocity(settled, Vector2.zero, .02f, gravity);
                Vector2 zeroTime = mover.SmoothSurfaceVelocity(current, Vector2.zero, 0f, gravity);
                Vector2 negativeTime = mover.SmoothSurfaceVelocity(current, Vector2.zero, -1f, gravity);
                return new JObject
                {
                    ["speedMultiplier"] = settings.WalkingMultiplier(gravity),
                    ["responseMultiplier"] = settings.WalkingResponseMultiplier(gravity),
                    ["start"] = mover.SmoothSurfaceVelocity(Vector2.zero, current, .01f, gravity).x,
                    ["stop"] = mover.SmoothSurfaceVelocity(current, Vector2.zero, .01f, gravity).x,
                    ["reverse"] = mover.SmoothSurfaceVelocity(current, -current, .01f, gravity).x,
                    ["turnX"] = turn.x, ["turnY"] = turn.y,
                    ["settledSpeed"] = settledSpeed, ["settledStopSpeed"] = settled.magnitude,
                    ["zeroTime"] = zeroTime.x, ["negativeTime"] = negativeTime.x
                };
            }
            var result = new JObject
            {
                ["normal"] = Sample(settings.EarthGravity),
                ["low"] = Sample(settings.EarthGravity / 6d),
                ["high"] = Sample(settings.EarthGravity * 2d),
                ["zero"] = Sample(0d),
                ["minimum"] = Sample(settings.MinimumGravityForWalking)
            };
            mover.SetSurfaceMovementResponse(.5f, .25f);
            result["ice"] = Sample(settings.EarthGravity / 6d);
            return result;
        }
        finally { Object.DestroyImmediate(root); }
    }

    #endregion

    #region 运动重置与异常输入采样

    // 独立刚体调用正式移动和租约接口，Lua 检查状态与回调，不运行物理模拟。
    private static JObject PlayerMotionReset(JObject args)
    {
        var root = new GameObject("[LuaMotionReset]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            GameItem actor = root.AddComponent<GameItem>();
            actor.BindData(new Data_GeneralItem { IDName = "test:motion_reset" });
            Rigidbody2D body = root.AddComponent<Rigidbody2D>();
            body.simulated = false;
            Mod_Mover mover = root.AddComponent<Mod_Mover>();
            mover.ModuleInit(actor, new Ex_ModData_MemoryPackable { ID = ModText.Mod_Mover, Enabled = true });
            mover.rb = body;
            root.SetActive(true);
            mover.SetRunState(true);
            int starts = 0, ends = 0, resets = 0;
            bool acquiredInReset = false;
            bool resetInEnd = true;
            mover.OnMoveStart += () => starts++;
            mover.OnMoveEnd += () => { ends++; if (resetInEnd) mover.ResetMotion(); };
            mover.MotionReset += () =>
            {
                resets++;
                mover.ResetMotion();
                acquiredInReset |= mover.TryAcquireDrivenMotion(Vector2.left, out _);
                mover.MoveByInput(Vector2.left, 1f);
            };
            JObject Sample() => new()
            {
                ["x"] = body.position.x, ["y"] = body.position.y,
                ["velocity"] = body.velocity.magnitude, ["angularVelocity"] = body.angularVelocity,
                ["driven"] = mover.DrivenVelocity.magnitude, ["external"] = mover.ExternalVelocity.magnitude,
                ["input"] = mover.RequestedMoveInput.magnitude, ["moving"] = mover.IsMoving,
                ["running"] = mover.IsRunning, ["speed"] = mover.Speed.Value,
                ["lease"] = mover.HasDrivenMotion, ["starts"] = starts, ["ends"] = ends,
                ["resets"] = resets, ["acquiredInReset"] = acquiredInReset
            };
            mover.MoveByInput(Vector2.right, .3f);
            JObject before = Sample();
            body.position = new Vector2(17f, 23f);
            body.angularVelocity = 45f;
            mover.ResetMotion();
            JObject reset = Sample();
            mover.ResetMotion();
            JObject repeated = Sample();
            resetInEnd = false;
            mover.MoveByInput(Vector2.up, .3f);
            JObject resumed = Sample();
            mover.IsLock = true;
            mover.MoveByInput(Vector2.right, .3f);
            JObject locked = Sample();
            mover.IsLock = false;

            bool acquired = mover.TryAcquireDrivenMotion(new Vector2(3f, 0f), out var stale);
            mover.MoveByInput(Vector2.up, .3f);
            mover.Move(body.position + Vector2.up, .3f);
            mover.ApplyNavigationVelocity(Vector2.up * 8f, .3f);
            JObject owned = Sample();
            mover.ResetMotion();
            JObject leaseReset = Sample();
            bool acquiredAgain = mover.TryAcquireDrivenMotion(new Vector2(2f, 0f), out var current);
            stale?.SetVelocity(Vector2.up * 100f);
            stale?.Dispose();
            JObject afterStale = Sample();
            current?.Dispose();

            var invalid = new JArray();
            var smooth = new JArray();
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Vector2 invalidCurrent = mover.SmoothSurfaceVelocity(new Vector2(value, 0f), Vector2.right, .1f);
                Vector2 invalidTarget = mover.SmoothSurfaceVelocity(Vector2.right, new Vector2(0f, value), .1f);
                Vector2 invalidTime = mover.SmoothSurfaceVelocity(Vector2.right, Vector2.zero, value);
                smooth.Add(new JObject
                {
                    ["currentX"] = invalidCurrent.x, ["currentY"] = invalidCurrent.y,
                    ["targetX"] = invalidTarget.x, ["targetY"] = invalidTarget.y,
                    ["timeX"] = invalidTime.x, ["timeY"] = invalidTime.y
                });
                for (int entry = 0; entry < 4; entry++)
                {
                    mover.MoveByInput(Vector2.right, .3f);
                    switch (entry)
                    {
                        case 0: mover.MoveByInput(new Vector2(value, 1f), .1f); break;
                        case 1: mover.Move(new Vector2(1f, value), .1f); break;
                        case 2: mover.ApplyNavigationVelocity(new Vector2(value, 1f), .1f); break;
                        case 3: mover.MoveByInput(Vector2.right, value); break;
                    }
                    invalid.Add(Sample());
                }
            }
            resetInEnd = true;
            mover.OnMoveStart += mover.ResetMotion;
            mover.MoveByInput(Vector2.right, .3f);
            JObject reentrant = Sample();
            mover.MoveByInput(Vector2.right, .3f);
            JObject nextReentrant = Sample();
            mover.rb = null;
            mover.ResetMotion();
            JObject missingBody = Sample();
            return new JObject
            {
                ["before"] = before, ["reset"] = reset, ["repeated"] = repeated,
                ["resumed"] = resumed, ["locked"] = locked, ["acquired"] = acquired,
                ["owned"] = owned, ["leaseReset"] = leaseReset, ["acquiredAgain"] = acquiredAgain,
                ["afterStale"] = afterStale, ["invalid"] = invalid, ["smooth"] = smooth,
                ["reentrant"] = reentrant, ["nextReentrant"] = nextReentrant, ["missingBody"] = missingBody
            };
        }
        finally { Object.DestroyImmediate(root); }
    }

    #endregion
}
