using Newtonsoft.Json.Linq;

internal static partial class Program
{
    #region 局部船体伤害正式接口回归

    private static double DamageNumber(JToken token) => token.Value<double>();
    private static bool DamageFlag(JToken token) => token.Value<bool>();
    private static JToken DamagePart(JToken pieces, string id)
        => pieces.Children().Single(part => part["id"].Value<string>() == id);

    private static void ShipDamageLocalDurability()
    {
        JObject data = GameplayTestShipDamageProbe.Capture("ship_local_damage");
        Check(DamageFlag(data["firstAccepted"]) && DamageFlag(data["secondAccepted"]), "两发引擎伤害应被接受");
        Near(81d, DamageNumber(data["first"]["health"]), "引擎第一发结构耐久");
        Near(1d, DamageNumber(data["first"]["disableHealth"]), "引擎第一发瘫痪耐久");
        Check(DamageFlag(data["first"]["operational"]), "第一发后引擎仍可工作");
        Near(62d, DamageNumber(data["second"]["health"]), "引擎第二发结构耐久");
        Near(-18d, DamageNumber(data["second"]["disableHealth"]), "引擎第二发瘫痪耐久");
        Check(DamageFlag(data["second"]["alive"]) && !DamageFlag(data["second"]["operational"]), "瘫痪不能摧毁引擎结构");
        Check(!DamageFlag(data["disabledEngineAvailable"]) && !DamageFlag(data["disabledGeometryRebuilt"]), "瘫痪必须停工且保留碰撞");
        Check(JToken.DeepEquals(data["beforeGeometry"], data["disabledGeometry"]), "瘫痪不能改变船体几何");
        Near(100d, DamageNumber(data["floor"]["health"]), "局部伤害不连带扣地板");
        Check(data["events"].Count() == 2 && data["events"].All(item => DamageNumber(item["damage"]) == 19d), "两条耐久只能各发出一次实际伤害事件");
        Check(data["invalidAccepted"].Count() == 4 && data["invalidAccepted"].All(value => !DamageFlag(value)), "非法伤害拒绝");
        Check(!DamageFlag(data["armorAccepted"]), "防御抵消全部伤害");
        Near(100d, DamageNumber(data["armor"]["health"]), "抵消后不得扣血");
        Near(10d, DamageNumber(data["wallEffective"]), "墙防御减法");
        Check(DamageFlag(data["destroyAccepted"]) && !DamageFlag(data["repeatDestroyAccepted"]) && data["destroyCallbacks"].Value<int>() == 1,
            "结构摧毁通知幂等");
        Check(DamageFlag(data["threshold"]["alive"]) && !DamageFlag(data["threshold"]["operational"]), "零瘫痪耐久必须停工并留形");
        Near(0d, DamageNumber(data["threshold"]["disableHealth"]), "瘫痪阈值零边界");
        Check(!DamageFlag(data["destroyedGeometry"]["rectangles"][0]["blocksPassage"]), "摧毁后恢复下面地板碰撞");
    }

    private static void ShipDamageHitTargeting()
    {
        JObject data = GameplayTestShipDamageProbe.Capture("ship_hit_targeting");
        Check(data["firstTarget"].Value<string>() == "target-engine", "先命中设备而非地板");
        Check(DamageFlag(data["engineAfterFirst"]["destroyed"]), "第一发摧毁设备");
        Near(100d, DamageNumber(data["floorAfterFirst"]["health"]), "摧毁设备同发不穿透地板");
        Check(data["nextTarget"].Value<string>() == "target-floor", "下一发才能命中地板");
        Near(81d, DamageNumber(data["floorAfterNext"]["health"]), "下一发计算地板自己的防御");
        Check(data["emptyTarget"].Value<string>() == string.Empty && data["invalidTarget"].Value<string>() == string.Empty,
            "空位置和无效坐标拒绝");
        Check(data["rotatedInside"].Value<string>() == "rotated-engine" && data["rotatedOutside"].Value<string>() == string.Empty,
            "负坐标与旋转后的真实占地");
        Check(data["overlays"].Count() == 2, "电线和光纤优先级样本完整");
        foreach (JToken overlay in data["overlays"])
        {
            Check(overlay["firstTarget"].Value<string>() == "overlay-engine" && DamageFlag(overlay["engineDestroyed"]), "引擎优先于高Layer线路");
            Check(overlay["nextTarget"].Value<string>() == "overlay-line", "设备被毁后下一发命中线路");
            Near(100d, DamageNumber(overlay["floorHealth"]), "同发不穿透地板");
            Near(100d, DamageNumber(overlay["lineHealth"]), "同发不穿透线路");
        }
        Check(data["rightEdgeTarget"].Value<string>() == "edge-engine" && data["leftEdgeTarget"].Value<string>() == "edge-wall",
            "边线接触必须内缩实际命中占地，不能误选邻格高Layer部件");
        Check(data["invalidContact"].Value<string>() == string.Empty, "无效接触坐标拒绝");
    }

    private static void ShipDamageLineBudget()
    {
        JObject data = GameplayTestShipDamageProbe.Capture("ship_damage_lines");
        Check(data["selected"].Count() == 10 && data["selected"].Values<string>().Distinct().Count() == 10, "损伤线十个独立活部位");
        Near(63d, DamageNumber(data["loss"]), "先均分100再抵消每层防御");
        for (int x = 0; x < 10; x++)
        {
            JToken part = DamagePart(data["parts"], "line-" + x);
            Near(x >= 4 && x < 7 ? 100d : 91d, DamageNumber(part["health"]), "部位实际损伤");
            if (x >= 7) Near(11d, DamageNumber(part["disableHealth"]), "引擎功能损伤相同");
        }
        Near(100d, DamageNumber(DamagePart(data["parts"], "line-outside")["health"]), "线外部位不受伤也不分摊");
        Near(0d, DamageNumber(data["zeroLoss"]), "零预算无伤害");
        Near(76d, DamageNumber(data["filteredLoss"]), "先筛选四块地板再均分80");
        for (int x = 0; x < 10; x++)
            Near(x < 4 ? 72d : x < 7 ? 100d : 91d, DamageNumber(DamagePart(data["filtered"], "line-" + x)["health"]), "筛选预算实际部位损伤");
        Near(11d, DamageNumber(data["fractionalLoss"]), "小数均分的结构伤害守恒");
        Check(data["fractional"].All(part => DamageNumber(part["health"]) == 94.5d), "小数份额不应取整");
        Near(51d, DamageNumber(data["changingLoss"]), "薄弱部位的过量伤害不能重分配");
        Near(50d, DamageNumber(data["changingSurvivor"]["health"]), "摧毁回调移除部位后仍使用冻结份额");
        Check(data["changingCallbacks"].Value<int>() == 1 && data["changingPieces"].Value<int>() == 1, "真实回调移除沿线部位");
    }

    private static void ShipDamageRecursiveFragments()
    {
        JObject data = GameplayTestShipDamageProbe.Capture("ship_damage_fragmentation");
        Check(data["afterWall"].Count() == 1 && !DamageFlag(data["afterWall"][0]["wreck"]), "只毁腰墙不分船");
        Near(40d, DamageNumber(data["floorAfterWall"]["health"]), "同一发腰墙攻击不伤地板");
        Check(data["firstSplit"].Count() == 2, "毁腰部地板拆成船首与船尾");
        JToken head = data["firstSplit"].Single(part => !DamageFlag(part["wreck"]));
        JToken tail = data["firstSplit"].Single(part => DamageFlag(part["wreck"]));
        Check(head["id"].Value<string>() == data["originalId"].Value<string>() && DamageFlag(head["console"]) && DamageFlag(head["engine"]),
            "船首保留原身份与控制台动力");
        Check(head["pieces"].Count() == 5 && tail["pieces"].Count() == 6, "第一次分裂部件守恒");
        Near(83d, DamageNumber(DamagePart(head["pieces"], "long-floor-2")["health"]), "船首局部血量不重新分配");
        Near(67d, DamageNumber(DamagePart(tail["pieces"], "long-floor-5")["health"]), "船尾局部血量不重置");
        foreach (JToken fragments in new[] { data["tailSplit"], data["restoredTailSplit"] })
        {
            Check(fragments.Count() == 2 && fragments.All(part => DamageFlag(part["wreck"])), "残骸递归拆分与保存不复活");
            JToken[] parts = fragments.SelectMany(fragment => fragment["pieces"]).ToArray();
            Check(parts.Length == 5 && parts.All(part => part["id"].Value<string>() != "long-floor-6"), "已毁部件移除且碎块归属守恒");
            Near(67d, DamageNumber(DamagePart(new JArray(parts), "long-floor-5")["health"]), "再次拆分保留原部位血量");
            JToken cargo = DamagePart(new JArray(parts), "long-cargo");
            Near(74d, DamageNumber(cargo["health"]), "再次拆分保留承载设备结构耐久");
            Check(cargo["snapshot"].Value<string>() == data["cargoSnapshot"].Value<string>(), "递归拆分和JSON往返保留库存快照");
        }
        Check(data["diagonalSplit"].Count() == 2, "地板对角相连仍算两个结构");
    }

    #endregion
}
