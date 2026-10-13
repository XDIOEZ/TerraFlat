using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 星球引力源登记
        private readonly List<SpaceGravitySource> gravitySources = new();

        private void RegisterGravitySources(UniverseSimulation universe)
        {
            SpaceGameplaySettings settings = SpaceGameplaySettings.Current;
            foreach (BodyState body in universe.State.Bodies)
                if (body.MassKg > 0d) gravitySources.Add(new SpaceGravitySource(universe, body, settings));
        }
        #endregion
    }
}
