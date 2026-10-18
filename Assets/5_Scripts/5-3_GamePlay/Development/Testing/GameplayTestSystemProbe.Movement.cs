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
        => Register("player", "surface_gravity_movement", PlayerSurfaceGravityMovement);

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
}
