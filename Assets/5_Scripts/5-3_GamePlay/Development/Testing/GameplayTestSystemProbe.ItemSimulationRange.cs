using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 物品距离暂停刚体回归

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterItemSimulationRangeRigidbodyPauseRule()
        => Register("item_runtime", "simulation_range_rigidbody_pause", CaptureItemSimulationRangeRigidbodyPauseAndRestore);

    // 使用正式 Item 入口验证三种刚体类型暂停时无物理日志且恢复原模拟状态。
    private static JObject CaptureItemSimulationRangeRigidbodyPauseAndRestore(JObject args)
    {
        var samples = new JArray();
        foreach (RigidbodyType2D bodyType in new[]
                 { RigidbodyType2D.Static, RigidbodyType2D.Kinematic, RigidbodyType2D.Dynamic })
        foreach (bool initiallySimulated in new[] { true, false })
        {
            samples.Add(WithEntityProbeItem(item =>
            {
                item.BindData(EntityProbeData("probe:simulation-range-body", 1f));
                var body = item.gameObject.AddComponent<Rigidbody2D>();
                body.bodyType = bodyType;
                body.gravityScale = 0f;
                item.gameObject.SetActive(true);
                item.Load();
                if (bodyType != RigidbodyType2D.Static)
                {
                    body.velocity = new Vector2(3f, -2f);
                    body.angularVelocity = 12f;
                }
                body.simulated = initiallySimulated;

                bool velocitySeeded = bodyType != RigidbodyType2D.Static &&
                    body.velocity != Vector2.zero && !Mathf.Approximately(body.angularVelocity, 0f);
                int issueCount = 0;
                void CountPhysicsLog(string condition, string stackTrace, LogType type)
                {
                    if (type != LogType.Log) issueCount++;
                }

                Application.logMessageReceived += CountPhysicsLog;
                try
                {
                    item.SetSimulationRangePaused(true);
                    int pauseIssueCount = issueCount;
                    issueCount = 0;
                    bool pausedSimulationDisabled = !body.simulated;
                    bool velocitiesCleared = bodyType == RigidbodyType2D.Static ||
                        (body.velocity == Vector2.zero && Mathf.Approximately(body.angularVelocity, 0f));
                    item.SetSimulationRangePaused(false);
                    int resumeIssueCount = issueCount;
                    bool initialized = item.IsInitialized;
                    bool simulationRestored = body.simulated == initiallySimulated;
                    bool poolReuseVelocitySeeded = false;
                    if (bodyType != RigidbodyType2D.Static && initiallySimulated)
                    {
                        body.velocity = new Vector2(-4f, 2f);
                        body.angularVelocity = -9f;
                        poolReuseVelocitySeeded = body.velocity != Vector2.zero &&
                            !Mathf.Approximately(body.angularVelocity, 0f);
                    }
                    issueCount = 0;
                    item.PrepareForPoolReuse();
                    bool poolReuseVelocitiesCleared = bodyType == RigidbodyType2D.Static ||
                        (body.velocity == Vector2.zero && Mathf.Approximately(body.angularVelocity, 0f));
                    return new JObject
                    {
                        ["bodyType"] = body.bodyType.ToString(),
                        ["initiallySimulated"] = initiallySimulated,
                        ["initialized"] = initialized,
                        ["velocitySeeded"] = velocitySeeded,
                        ["pauseIssueCount"] = pauseIssueCount,
                        ["pausedSimulationDisabled"] = pausedSimulationDisabled,
                        ["velocitiesCleared"] = velocitiesCleared,
                        ["resumeIssueCount"] = resumeIssueCount,
                        ["simulationRestored"] = simulationRestored,
                        ["poolReuseIssueCount"] = issueCount,
                        ["poolReuseVelocitySeeded"] = poolReuseVelocitySeeded,
                        ["poolReuseVelocitiesCleared"] = poolReuseVelocitiesCleared
                    };
                }
                finally { Application.logMessageReceived -= CountPhysicsLog; }
            }));
        }
        return new JObject { ["samples"] = samples };
    }

    #endregion
}
