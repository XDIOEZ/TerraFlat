using System.Text.Json;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    passed++;
}
void Near(float value, float expected, string name) => Check(Math.Abs(value - expected) < .00001f, name);
var settings = new ShieldActionSettings();
var clock = new ShieldActionClock();
Check(clock.TryPress(settings), "primary press starts");
Near(clock.Defense, 0, "press is not a parry");
clock.Advance(.1f);
Check(clock.Release() && clock.Kind == ShieldActionKind.Bash, "tap bashes on release");
Near(clock.Defense, 5, "bash defense");
Near(clock.SpeedMultiplier, 1, "bash not slowed");
Check(!clock.TryParry(settings) && !clock.TryPress(settings), "actions cannot overlap");
clock.Advance(.3f);
Check(clock.Kind == ShieldActionKind.Idle, "bash expires");
Near(clock.Defense, 0, "bash no permanent defense");
Check(clock.TryPress(settings), "press again");
clock.Advance(.199f);
Check(clock.Kind == ShieldActionKind.Pressing, "under hold threshold");
clock.Advance(.0011f);
Check(clock.Kind == ShieldActionKind.Guard, "hold starts guard");
Near(clock.Defense, 10, "guard defense");
Near(clock.SpeedMultiplier, .5f, "guard halves speed");
clock.Advance(100);
Check(clock.Kind == ShieldActionKind.Guard, "guard persists while held");
Check(!clock.Release() && clock.Kind == ShieldActionKind.Idle, "guard release is not a bash");
Near(clock.Defense, 0, "guard release removes defense");
Near(clock.SpeedMultiplier, 1, "guard release restores speed");
Check(clock.TryParry(settings), "parry begins");
Near(clock.Defense, 10, "parry defense is ten, not twenty");
Near(clock.SpeedMultiplier, 1, "parry does not use guard slowdown");
Check(!clock.TryParry(settings), "repeat parry cannot stack");
clock.Advance(.2f);
Check(clock.Kind == ShieldActionKind.Idle && !clock.TryParry(settings), "window expires before cooldown");
clock.Advance(.4001f);
Check(clock.TryParry(settings), "cooldown recovers");
clock.Cancel();
Check(!clock.TryParry(settings), "cancel cannot reset cooldown");
Near(clock.Defense, 0, "cancel releases parry defense");
clock.Advance(float.NaN); clock.Advance(float.PositiveInfinity); clock.Advance(-1);
Check(clock.ParryCooldownRemaining > 0, "invalid elapsed cannot advance cooldown");
clock.Advance(1);
Check(clock.TryPress(settings), "restart after cooldown");
settings.GuardDefense = 999;
clock.Advance(.2f);
Near(clock.Defense, 10, "active action snapshots configuration");
clock.Cancel();
Near(clock.SpeedMultiplier, 1, "cancel clears guard speed");
var unsafeSettings = new ShieldActionSettings { GuardSpeedMultiplier = 0, HoldThreshold = float.NaN, BashDefense = -1 };
Check(clock.TryPress(unsafeSettings), "invalid settings normalized");
clock.Advance(.2f);
Near(clock.SpeedMultiplier, .5f, "invalid guard speed default");
clock.Cancel();

// Production defense lease: no saved-base mutation, composable sources, stale leases inert.
var receiver = new Mod_DamageReceiver();
var shieldLease = receiver.AcquireTemporaryDefense(10);
Near(receiver.Defense.Physical, 3, "base defense remains serializable without shield");
Near(receiver.GetEffectiveDefense().Physical, 13, "temporary physical defense applied once");
var otherLease = receiver.AcquireTemporaryDefense(2);
Near(receiver.GetEffectiveDefense().Physical, 15, "independent sources compose");
shieldLease.Dispose(); shieldLease.Dispose();
Near(receiver.GetEffectiveDefense().Physical, 5, "double dispose cannot remove another source");
receiver.EndLifetime();
Near(receiver.TemporaryPhysicalDefense, 0, "death clears temporary defense");
receiver.BeginNewLifetime();
var newLease = receiver.AcquireTemporaryDefense(5);
otherLease.Dispose();
Near(receiver.GetEffectiveDefense().Physical, 8, "old lifetime dispose cannot remove new defense");
newLease.Dispose();
Near(receiver.GetEffectiveDefense().Physical, 3, "all leases leave original defense");
foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
{
    bool rejected = false;
    try { receiver.AcquireTemporaryDefense(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "invalid defense rejected");
}
for (int i = 0; i < 1000; i++)
{
    var c = new ShieldActionClock();
    c.TryPress(new ShieldActionSettings());
    c.Advance(i % 2 == 0 ? .1f : .3f);
    c.Release();
    c.Advance(1);
    Check(c.Kind == ShieldActionKind.Idle && c.Defense == 0 && c.SpeedMultiplier == 1, "tap/hold repeated cleanup");
}
Console.WriteLine(JsonSerializer.Serialize(new { passed, status = "PASS", scope = "production shield clock and temporary defense leases; Unity runtime not invoked" }));
