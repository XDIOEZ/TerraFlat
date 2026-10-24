using FlatWorld.WorldModel;
using UnityEngine;

public sealed partial class ChunkTilemapRenderer
{
    #region 耕地水分表现

    internal static bool IsSoilMoistureVisualLayer(string layerId) =>
        layerId == FarmlandSystem.WaterLayer || layerId == FarmlandSystem.SourceLayer || layerId == "moisture";

    private static bool TryGetFarmlandTemplate(int tileId, out TileData_Farmland template)
    {
        template = null;
        if (GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetTileDefinition(tileId, out RuntimeTileDefinition definition))
            return false;
        template = definition.TileDataTemplate as TileData_Farmland;
        return template != null;
    }

    private static Vector4 BuildGroundSurfaceData(ChunkTerrainData terrain, int x, int y, int tileId) =>
        TryGetFarmlandTemplate(tileId, out TileData_Farmland template)
            ? BuildFarmlandMoistureData(terrain, x, y, template) : Vector4.zero;

    // 只读真实水分，不走会自动灌溉、创建快照或写存档的农业查询。
    internal static Vector4 BuildFarmlandMoistureData(ChunkTerrainData terrain, int x, int y,
        TileData_Farmland template)
    {
        int water = template.waterValue;
        if (terrain.TryGetEnvironmentValue(FarmlandSystem.SourceLayer, x, y, out float source) && source > 0f)
            water = FarmlandSystem.ReadWaterLevel(terrain, new Vector2Int(x, y));
        else if (terrain.TryGetEnvironmentValue("moisture", x, y, out float moisture))
            water = Mathf.RoundToInt(Mathf.Clamp01(moisture) * template.maxWater);
        // x 为含水比例，y 为耕地标记；普通地块保持全零并使用同一材质。
        return new Vector4(Mathf.Clamp01((float)water / template.maxWater), 1f, 0f, 0f);
    }

    private void RefreshSoilMoistureVisuals(ChunkTerrainChanged changed)
    {
        ChunkTerrainData terrain = boundChunk.Terrain;
        bool presentationReady = false;
        foreach (int index in changed.GetCellIndices(terrain.Width))
        {
            int x = index % terrain.Width, y = index / terrain.Width;
            if (!TryGetFarmlandTemplate(terrain.GetCell(x, y).GroundTileId, out TileData_Farmland template))
                continue;
            if (!presentationReady)
            {
                if (!EnsureBatchPresentationForIncrementalRefresh("SoilMoisture")) return;
                presentationReady = true;
            }
            // 初绑仍由原有分帧预算补写，已完成绑定时只上传变化格的水分参数。
            if (batchBindingInProgress)
                liquidVisualDirty.Add(index);
            else
                groundMesh?.SetCellSurfaceData(x, y, BuildFarmlandMoistureData(terrain, x, y, template));
        }
    }

    #endregion
}
