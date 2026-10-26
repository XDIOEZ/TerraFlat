using System.Text.Json.Nodes;

internal static class BearProvocationHarness
{
    #region 正式仇恨账本离线采样

    // 只提供输入序列和原始输出，实际断言复用项目 Lua，业务规则直接编译生产源文件。
    private static void Main(string[] argumentsList)
    {
        if (argumentsList.Contains("--credit"))
        {
            BearHitCreditHarness.Run();
            return;
        }
        JsonObject arguments = JsonNode.Parse(Console.In.ReadToEnd())!.AsObject();
        var ledger = new AIProximityHatredLedger<long>();
        ledger.Configure(60f, 120f, 1f, 30f);
        var identities = new HashSet<long>();
        var samples = new JsonArray();
        foreach (JsonNode value in arguments["steps"]!.AsArray())
        {
            JsonObject step = value!.AsObject();
            if (step["clear"]?.GetValue<bool>() ?? false) ledger.Clear();
            float delta = step["deltaTime"]?.GetValue<float>() ?? 0f;
            if (step["invalidDelta"]?.GetValue<bool>() ?? false) delta = float.NaN;
            bool deltaRejected = false;
            try { ledger.BeginUpdate(delta); }
            catch (ArgumentOutOfRangeException) { deltaRejected = true; }
            if (!deltaRejected)
            {
                foreach (long identity in ReadIdentities(step, "nearby"))
                {
                    identities.Add(identity);
                    ledger.ObserveNearbyPlayer(identity);
                }
                ledger.EndUpdate();
                foreach (long identity in ReadIdentities(step, "hits"))
                {
                    identities.Add(identity);
                    ledger.RecordHit(identity);
                }
                foreach (long identity in ReadIdentities(step, "remove")) ledger.Remove(identity);
            }
            var hatred = new JsonObject();
            var presence = new JsonObject();
            foreach (long identity in identities)
            {
                string key = identity.ToString(System.Globalization.CultureInfo.InvariantCulture);
                hatred[key] = ledger.GetHatred(identity);
                presence[key] = ledger.GetContinuousPresenceSeconds(identity);
            }
            var ineligible = new HashSet<long>(ReadIdentities(step, "ineligible"));
            bool hasTarget = ledger.TryGetHighestHatredTarget(out long highestTarget);
            bool hasEligible = ledger.TryGetHighestHatredTarget(identity => !ineligible.Contains(identity), out long eligibleTarget);
            samples.Add(new JsonObject
            {
                ["hatred"] = hatred, ["presence"] = presence, ["deltaRejected"] = deltaRejected,
                ["highestTarget"] = hasTarget ? highestTarget : 0, ["eligibleTarget"] = hasEligible ? eligibleTarget : 0
            });
        }
        Console.WriteLine(new JsonObject { ["samples"] = samples }.ToJsonString());
    }

    private static IEnumerable<long> ReadIdentities(JsonObject step, string field)
    {
        if (step[field] is not JsonArray values) yield break;
        foreach (JsonNode value in values) yield return value!.GetValue<long>();
    }

    #endregion
}
