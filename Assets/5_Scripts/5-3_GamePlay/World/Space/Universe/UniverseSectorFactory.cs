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
            for (int index = 0; index < state.Bodies.Count; index++)
            {
                InitializeBodyInstanceIdentity(state.Bodies[index], state.Seed, sector, randomizePlacements);
            }
            _ = new UniverseSimulation(state); return state;
        }

        public static BodyState CreateBodyInstanceFromTemplate(BodyState definition, int universeSeed,
            SpaceSectorId sector, bool randomizePlacements)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            BodyState body = JsonConvert.DeserializeObject<BodyState>(JsonConvert.SerializeObject(definition));
            InitializeBodyInstanceIdentity(body, universeSeed, sector, randomizePlacements);
            return body;
        }

        private static void InitializeBodyInstanceIdentity(BodyState body, int universeSeed,
            SpaceSectorId sector, bool randomizePlacements)
        {
            // 新建星区和新增目录内容共用同一身份与初相位，加载顺序不会重抽矿区位置。
            string templateId = body.BodyId;
            body.TemplateBodyId = templateId; body.Sector = sector;
            body.BodyId = SpaceSectorCoordinates.BodyId(sector, templateId);
            body.PlanetId = SpaceSectorCoordinates.PlanetId(sector, body.PlanetId);
            if (!string.IsNullOrWhiteSpace(body.ParentBodyId)) body.ParentBodyId = SpaceSectorCoordinates.BodyId(sector, body.ParentBodyId);
            body.Seed = DeriveSeed(universeSeed, templateId);
            if (randomizePlacements && !string.IsNullOrWhiteSpace(body.ParentBodyId))
                body.PlacementAngleRadians = ((uint)body.Seed / (double)uint.MaxValue) * Math.PI * 2d;
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
