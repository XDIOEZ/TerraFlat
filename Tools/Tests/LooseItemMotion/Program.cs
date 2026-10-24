using Newtonsoft.Json.Linq;

int checks = 0;
void Check(bool condition, string label)
{
    checks++;
    if (!condition) throw new InvalidOperationException(label);
}
void Near(JObject result, string key, float expected, string label)
    => Check(Math.Abs(result[key].Value<float>() - expected) < .00002f, label + " / " + key + ": " + result);
JObject Run(params (string, object)[] values)
{
    var args = new JObject();
    foreach (var (key, value) in values) args[key] = JToken.FromObject(value);
    return GameplayTestSystemProbe.RunMotion(args);
}
foreach (bool belt in new[] { false, true })
{
    foreach (var (force, expected) in new[] { (0f, .2f), (1f, .4f), (-1f, 0f), (-2f, -.2f) })
    {
        JObject value = Run(("belt", belt), ("water", !belt), ("forceX", force));
        Near(value, "x", expected, "统一叠加/抵消/反向");
        Near(value, "splitX", expected, "分段时间一致");
        Near(value, "y", 0f, "平行力不偏转");
    }
    JObject side = Run(("belt", belt), ("water", !belt), ("forceY", 3f), ("seconds", .1f));
    Check(side["y"].Value<float>() > .1f, "横向外力未作用于水流或带面");
    foreach (var bad in new[] { ("seconds", (object)0f), ("seconds", (object)(-.2f)), ("invalidTime", (object)true) })
    {
        JObject value = Run(("belt", belt), bad);
        Near(value, "x", 0f, "无效时间不得移动");
        Near(value, "moves", 0f, "无效时间不得查询阻挡");
    }
    Near(Run(("belt", belt), ("invalidForce", true)), "x", .2f, "无效外力不得污染载体运动");
    Near(Run(("belt", belt), ("missing", true)), "x", 0f, "未加载区域停步");
    JObject blocked = Run(("belt", belt), ("speed", 5f), ("wall", .35f));
    Check(blocked["x"].Value<float>() <= .35f && blocked["x"].Value<float>() >= .2f, "高速穿墙或提前停步");
}
JObject shore = Run(("water", true), ("shore", .15f));
Check(shore["x"].Value<float>() < .15f, "水流自己走上陆地");
JObject ashore = Run(("water", true), ("shore", .15f), ("forceX", 2f));
Check(ashore["x"].Value<float>() >= .15f, "外力无法推动物品离水");
Near(Run(("water", true), ("submerged", true), ("forceX", 10f)), "x", .2f, "沉没不受表面外力");
Near(Run(("wrap", 1f), ("x", .95f)), "x", .15f, "环绕跨缝");
JObject curve = Run(("belt", true), ("curve", true), ("x", -.5f));
Near(curve, "x", -.5f + MathF.Sin(.4f) * .5f, "弯带实际路径");
Near(curve, "y", .5f - MathF.Cos(.4f) * .5f, "弯带实际路径");
Near(curve, "splitX", curve["x"].Value<float>(), "弯带分步一致");
JObject reversed = Run(("belt", true), ("curve", true), ("y", .5f), ("speed", -1f));
Near(reversed, "x", -.5f + MathF.Sin(MathF.PI / 2 - .4f) * .5f, "反转弯带");
Near(reversed, "y", .5f - MathF.Cos(MathF.PI / 2 - .4f) * .5f, "反转弯带");
JObject sources = GameplayTestSystemProbe.RunSources();
Near(sources, "combinedX", 1f, "多来源"); Near(sources, "combinedY", 2f, "多来源");
Near(sources, "removedX", 1f, "释放指定来源"); Near(sources, "removedY", 0f, "释放指定来源");
Near(sources, "cleared", 0f, "全部撤销");
Check(sources["duplicate"].Value<string>() == "InvalidOperationException", "重复标识未拒绝");
Near(sources, "selfOnceY", 1f, "回调内撤销"); Near(sources, "selfTwice", 0f, "回调内撤销后不再执行");
Check(!LooseItemMotion.HasVelocitySources, "测试泄漏了全局推动来源");
Console.WriteLine($"PASS: {checks} assertions against production LooseItemMotion core, ConveyorPath and shared Lua probe.");
