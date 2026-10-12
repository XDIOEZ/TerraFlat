using System;
using Newtonsoft.Json;

namespace FlatWorld.Spaceflight
{
    public static class UniverseSectorFactory
    {
        #region 星系模板的独立实例
        // 模板只提供规则，每个星区拥有独立身份、种子和可持久化运动状态。
        public static UniverseState Generate(UniverseState template, int worldSeed, SpaceSectorId sector, bool randomizePlacements)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            UniverseState state = JsonConvert.DeserializeObject<UniverseState>(JsonConvert.SerializeObject(template));
            state.Sector = sector; state.Seed = DeriveSeed(worldSeed, sector.ToString());
            state.SimulationSeconds = 0d;
            foreach (BodyState body in state.Bodies)
            {
                string templateId = body.BodyId;
                body.TemplateBodyId = templateId; body.Sector = sector;
                body.BodyId = SpaceSectorCoordinates.BodyId(sector, templateId);
                body.PlanetId = SpaceSectorCoordinates.PlanetId(sector, body.PlanetId);
                if (!string.IsNullOrWhiteSpace(body.ParentBodyId)) body.ParentBodyId = SpaceSectorCoordinates.BodyId(sector, body.ParentBodyId);
                body.Seed = DeriveSeed(state.Seed, templateId);
                if (randomizePlacements && !string.IsNullOrWhiteSpace(body.ParentBodyId))
                    body.PlacementAngleRadians = ((uint)body.Seed / (double)uint.MaxValue) * Math.PI * 2d;
            }
            _ = new UniverseSimulation(state); return state;
        }

        public static int DeriveSeed(int seed, string identity)
        {
            unchecked
            {
                uint hash = (uint)seed ^ 2166136261u;
                foreach (char character in identity) { hash ^= character; hash *= 16777619u; }
                return (int)hash;
            }
        }
        #endregion
    }
}
