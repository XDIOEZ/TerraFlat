using FlatWorld.Navigation;
using System.Text.Json;
using UnityEngine;

using JsonDocument document = JsonDocument.Parse(File.ReadAllText(args[0]));
JsonElement config = document.RootElement.GetProperty("actors")[0].GetProperty("modules").GetProperty("ai").GetProperty("parameters");
int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    checks++;
}
bool Near(float a, float b) => Math.Abs(a - b) < .0001f;
FishHarness NewFish(float targetX = .9f)
{
    var fish = new FishHarness(config);
    DroppedItemService.Position = new Vector2(targetX, 0f);
    return fish;
}

foreach (float action in new[] { .005f, .1f, .45f, .55f, .9f, 1.2f, 2.1f })
foreach (float preferred in new[] { 0f, .01f, .3f, .5f, 5f, -1f, float.NaN, float.PositiveInfinity })
{
    float stop = AIApproachRange.ResolveStopDistance(preferred, action);
    Check(float.IsFinite(stop) && stop >= 0f && stop <= action * .5f && stop < action,
        $"停止内圈越界：{preferred}/{action}");
}
Check(!AIApproachRange.IsInActionRange(float.NaN, 1f), "NaN 距离误触发动作");
Check(!AIApproachRange.IsInActionRange(0f, 0f), "零动作范围仍触发动作");
Check(Near(AIApproachRange.AdvanceActionTimer(.2f, .1f, .64f, .9f), .3f), "内外圈之间动作计时被清零");
Check(AIApproachRange.AdvanceActionTimer(.7f, .1f, 1f, .9f) == 0f, "范围外仍累计进食");
Check(Near(AIApproachRange.ClampMovementStep(.9f, 10f, .3f), .6f), "低帧率越过停止内圈");

foreach (int fps in new[] { 10, 30, 60, 120 })
foreach (float speed in new[] { 0f, .25f, .54f, 1f })
foreach (float preferred in new[] { .3f, 9f })
{
    var fish = NewFish();
    fish.forageStopDistance = preferred;
    float dt = 1f / fps;
    for (int frame = 0; frame < fps * 8 && fish.food.Eaten == 0; frame++)
    {
        // 食物在鱼 Tick 前移动，模拟最易触发边界重置的更新顺序。
        DroppedItemService.Position += new Vector2(speed * dt, 0f);
        fish.Tick(dt);
    }
    Check(fish.food.Eaten == 1 && Mod_FishingRod.Notifications == 1,
        $"真实鱼觅食方法无法吃到漂移食物：fps={fps}, speed={speed}, stop={preferred}");
    Check(!fish.target.IsValid && fish.eatElapsed == 0f, "消费后旧目标或计时残留");
}

var moving = NewFish(.8f);
moving.Tick(.1f);
Check(moving.item.transform.position.x > 0f && Near(moving.eatElapsed, .1f), "进入进食外圈后仍立即停车/清零");

var slowFrame = NewFish(.8f);
slowFrame.Tick(.4f);
slowFrame.Tick(.4f);
Check(slowFrame.food.Eaten == 1 && Near(slowFrame.item.transform.position.x, .5f), "低帧率内圈位置或摄食错误");

var escaped = NewFish(.8f);
escaped.Tick(.1f);
DroppedItemService.Position = new Vector2(3f, 0f);
escaped.Tick(.1f);
Check(escaped.eatElapsed == 0f && escaped.food.Eaten == 0, "食物脱离范围后仍远程进食");

var fast = NewFish();
for (int frame = 0; frame < 300; frame++)
{
    DroppedItemService.Position += new Vector2(2f / 60f, 0f);
    fast.Tick(1f / 60f);
}
Check(fast.food.Eaten == 0, "比鱼更快且始终在范围外的食物被隔空消费");

var nearby = NewFish(.8f);
nearby.food.Data.nutrition.Rate = .8f;
for (int frame = 0; frame < 120 && nearby.food.Eaten == 0; frame++)
{
    DroppedItemService.Position += new Vector2(.25f / 60f, 0f);
    nearby.Tick(1f / 60f);
}
Check(nearby.food.Eaten == 1, "非主动觅食阶段无法吃到嘴边的移动食物");
var distant = NewFish(2f);
distant.food.Data.nutrition.Rate = .8f;
distant.Tick(.1f);
Check(!distant.target.IsValid && distant.item.transform.position.x == 0f, "非饥饿鱼主动远距离追食");

var blocked = NewFish(.8f);
AquaticHabitat.Traversable = false;
blocked.Tick(.1f);
Check(!blocked.target.IsValid && blocked.food.Eaten == 0 && blocked.item.transform.position.x == 0f,
    "受阻时穿过岸边或隔障进食");
var dry = NewFish(.2f);
AquaticHabitat.Wet = false;
dry.Tick(1f);
Check(!dry.target.IsValid && dry.food.Eaten == 0, "鱼消费了干地上的目标");
var removed = NewFish(.8f);
DroppedItemService.Available = false;
removed.Tick(1f);
Check(!removed.target.IsValid && removed.food.Eaten == 0, "失效掉落物仍被消费");

var wrapped = NewFish(.3f);
WorldTopologyRuntime.Period = 10f;
wrapped.item.transform.position = new Vector3(9.5f, 0f, 0f);
wrapped.body.position = wrapped.item.transform.position;
for (int frame = 0; frame < 120 && wrapped.food.Eaten == 0; frame++)
{
    DroppedItemService.Position = WorldTopologyRuntime.NormalizePosition(
        DroppedItemService.Position + new Vector2(.54f / 60f, 0f));
    wrapped.Tick(1f / 60f);
}
Check(wrapped.food.Eaten == 1, "跨接缝追食没有使用短方向移动");

Console.WriteLine($"PASS: {checks} checks; production fish methods, drifting food, 10/30/60/120 FPS, range safety and invalid targets.");
