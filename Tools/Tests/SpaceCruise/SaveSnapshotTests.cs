using FlatWorld.Spaceflight;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static partial class Program
{
    #region 散落物与太空会话快照回归
    private static void LooseItemScaleSnapshot()
    {
        Vector3[] scales = { Vector3.one, default, new(-2.5f, .125f, 3f), new(1e-6f, 100000f, -7.75f) };
        foreach (Vector3 scale in scales)
        {
            var item = new SpaceLooseItemState { Id = "scale-item", Scale = scale };
            string json = JsonConvert.SerializeObject(item);
            var saved = (JObject)JObject.Parse(json)["Scale"];
            Check(saved.Count == 3 && saved["x"] != null && saved["y"] != null && saved["z"] != null,
                "scale must contain only x, y and z, without normalized or magnitude");
            var restored = JsonConvert.DeserializeObject<SpaceLooseItemState>(json);
            CheckScale(scale, restored.Scale);
        }
        var defaultScale = JsonConvert.DeserializeObject<SpaceLooseItemState>("{\"Id\":\"default-scale\"}");
        CheckScale(Vector3.one, defaultScale.Scale);
    }

    private static void LooseItemSessionSnapshot()
    {
        var state = new SpaceSessionState
        {
            Sectors = new List<UniverseState> { new() { TemplateId = "save-fixture", Seed = 42, SimulationSeconds = 123.456d } },
            NavigationSequence = 17
        };
        state.LooseItems.Add(new SpaceLooseItemState
        {
            Id = "space-item", Snapshot = "opaque-item-snapshot", WorldKey = "SpaceScene", InSpace = true,
            RotationDegrees = 37.5d, Scale = new Vector3(-1.25f, 2f, .5f),
            Motion = new SpaceMotionState
            {
                PositionMeters = new SpaceVector2(123456789.125d, -987654321.75d),
                VelocityMetersPerSecond = new SpaceVector2(-120.25d, 35.125d), RadiusMeters = .15d
            }
        });
        state.LooseItems.Add(new SpaceLooseItemState
        {
            Id = "surface-item", Snapshot = "surface-item-snapshot", WorldKey = "earth", InSpace = false,
            SurfaceHeight = 7.5d, SurfaceVerticalSpeed = -2.25d, Scale = default
        });
        for (int cycle = 0; cycle < 3; cycle++)
        {
            state = JsonConvert.DeserializeObject<SpaceSessionState>(JsonConvert.SerializeObject(state));
            Check(state.Version == 3 && state.NavigationSequence == 17 && state.LooseItems.Count == 2,
                "session version, navigation sequence and loose item count must survive repeated saves");
            Check(state.Sectors[0].TemplateId == "save-fixture" && state.Sectors[0].Seed == 42,
                "universe identity must survive saves containing loose items");
            Near(123.456d, state.Sectors[0].SimulationSeconds, "universe clock");
            SpaceLooseItemState space = state.LooseItems[0], surface = state.LooseItems[1];
            Check(space.Id == "space-item" && space.Snapshot == "opaque-item-snapshot" && space.WorldKey == "SpaceScene" && space.InSpace,
                "space item identity, payload and location must survive");
            CheckScale(new Vector3(-1.25f, 2f, .5f), space.Scale);
            Near(37.5d, space.RotationDegrees, "item rotation");
            Near(123456789.125d, space.Motion.PositionMeters.X, "double precision position x");
            Near(-987654321.75d, space.Motion.PositionMeters.Y, "double precision position y");
            Near(-120.25d, space.Motion.VelocityMetersPerSecond.X, "velocity x");
            Near(35.125d, space.Motion.VelocityMetersPerSecond.Y, "velocity y");
            Near(.15d, space.Motion.RadiusMeters, "contact radius");
            Check(surface.Id == "surface-item" && surface.Snapshot == "surface-item-snapshot" && surface.WorldKey == "earth" && !surface.InSpace,
                "surface item identity, payload and world must survive");
            CheckScale(default, surface.Scale);
            Near(7.5d, surface.SurfaceHeight, "surface height");
            Near(-2.25d, surface.SurfaceVerticalSpeed, "surface descent velocity");
        }
    }

    private static void CheckScale(Vector3 expected, Vector3 actual)
        => Check(expected.x == actual.x && expected.y == actual.y && expected.z == actual.z,
            $"scale must survive without normalization: expected ({expected.x}, {expected.y}, {expected.z}), actual ({actual.x}, {actual.y}, {actual.z})");
    #endregion
}
