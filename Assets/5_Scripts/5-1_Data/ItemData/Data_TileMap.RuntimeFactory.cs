using System;
using System.Collections.Generic;

public partial class Data_TileMap
{
    #region 地图纯内存实例工厂

    internal Func<Data_TileMap> CompileRuntimeMapFactory()
    {
        TileStackCell[,] cells = CloneCells(_tileCells);
        EnvironmentLayers environment = CloneEnvironment(EnvironmentLayers);
        GrassLayerData grass = CloneGrass(GrassLayer);
        var mapPosition = position;
        bool loaded = TileLoaded;
        return () => new Data_TileMap
        {
            _tileCells = CloneCells(cells), position = mapPosition, TileLoaded = loaded,
            EnvironmentLayers = CloneEnvironment(environment), GrassLayer = CloneGrass(grass),
            _nonEmptyCellCount = -1
        };
    }

    private static TileStackCell[,] CloneCells(TileStackCell[,] source)
    {
        if (source == null) return null;
        var result = new TileStackCell[source.GetLength(0), source.GetLength(1)];
        for (int x = 0; x < source.GetLength(0); x++)
            for (int y = 0; y < source.GetLength(1); y++)
            {
                TileStackCell cell = source[x, y];
                var copy = new TileStackCell { BaseTile = CloneTile(cell.BaseTile), OverlayTile = CloneTile(cell.OverlayTile) };
                if (cell.OverflowLayers != null)
                {
                    copy.OverflowLayers = new List<TileData>(cell.OverflowLayers.Count);
                    foreach (TileData tile in cell.OverflowLayers) copy.OverflowLayers.Add(CloneTile(tile));
                }
                result[x, y] = copy;
            }
        return result;
    }

    private static TileData CloneTile(TileData source)
    {
        if (source == null) return null;
        TileData result = source.Clone();
        if (result == null || result.GetType() != source.GetType() || ReferenceEquals(source, result))
            throw new InvalidOperationException($"地块运行态复制失败：{source.GetType().FullName}");
        return result;
    }

    private static EnvironmentLayers CloneEnvironment(EnvironmentLayers source) => source == null ? new EnvironmentLayers() : new EnvironmentLayers
    {
        Temperature = CloneLayer(source.Temperature), TemperatureCelsius = CloneLayer(source.TemperatureCelsius),
        Precipitation = CloneLayer(source.Precipitation), Height = CloneLayer(source.Height),
        WindX = CloneLayer(source.WindX), WindY = CloneLayer(source.WindY), Light = CloneLayer(source.Light)
    };

    private static float[,] CloneLayer(float[,] source) => source == null ? null : (float[,])source.Clone();

    // 地形、环境及草层都属于实例，二维层和溢出列表不能与默认计划共用。
    private static GrassLayerData CloneGrass(GrassLayerData source) => source == null ? new GrassLayerData() : new GrassLayerData
    {
        Width = source.Width, Height = source.Height,
        Cells = source.Cells == null ? null : (byte[])source.Cells.Clone()
    };

    #endregion
}
