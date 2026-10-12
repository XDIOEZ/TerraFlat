using FlatWorld.Spaceflight;

internal static partial class Program
{
    #region 场景租约行为
    private static void SectorStreaming()
    {
        var policy = new SpaceSectorStreaming();
        var solar = new SpaceSectorId(0, 0);
        var sirius = new SpaceSectorId(-1, 0);
        void Observe(string name, SpaceSectorId sector, double x, double vx)
            => policy.Observe(name, new SpaceSectorPosition(sector, new SpaceVector2(x, 0)), new SpaceVector2(vx, 0));
        policy.BeginFrame(); Observe("a", solar, 0, -100); policy.EndFrame();
        Check(policy.Required.Count == 1 && policy.Required.Contains(solar), "interior loads only own sector");
        policy.BeginFrame(); Observe("a", solar, -121000, -100); policy.EndFrame();
        Check(policy.Required.Count == 2 && policy.Required.Contains(sirius), "approach preloads actual left neighbor");
        policy.BeginFrame(); Observe("a", sirius, 127000, -100); policy.EndFrame();
        Check(policy.Required.Contains(solar) && policy.Required.Contains(sirius), "crossing retains source");
        policy.BeginFrame(); Observe("a", solar, -127000, 100); policy.EndFrame();
        Check(policy.Required.Contains(sirius), "reversing inside overlap reuses neighbor");
        policy.BeginFrame(); Observe("a", sirius, 123000, -100); Observe("b", solar, 0, 0); policy.EndFrame();
        Check(policy.Required.Contains(solar), "another player keeps source loaded");
        policy.BeginFrame(); Observe("a", sirius, 123000, -100); policy.EndFrame();
        Check(policy.Required.Count == 1 && policy.Required.Contains(sirius), "source releases after leaving overlap and last viewer");
        policy.BeginFrame(); Observe("a", sirius, 127000, 100); policy.EndFrame();
        Check(policy.Required.Contains(solar), "turning toward source starts prefetch again");
        policy.BeginFrame(); Observe("a", new SpaceSectorId(1000000000000000, 0), 0, 1000000); policy.EndFrame();
        Check(policy.Required.Count == 1, "fast multi-sector crossing has no load barrier or stale source lease");
        policy.BeginFrame(); policy.EndFrame();
        Check(policy.Required.Count == 0, "no players require no scene resources");
    }
    #endregion
}
