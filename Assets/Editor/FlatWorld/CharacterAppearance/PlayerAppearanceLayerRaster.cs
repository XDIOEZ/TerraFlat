using System;
using System.Collections.Generic;

/// <summary>把已标定的独立素材归到公共角色画布，像素采样不依赖 Unity 编辑器状态。</summary>
public static class PlayerAppearanceLayerRaster
{
    #region 公共画布与源图标定
    public const int Width = 32;
    public const int Height = 48;
    public const int SourceWidth = 1448;
    public const int SourceHeight = 1086;
    public static readonly string[] ItemIds =
    {
        "Traveler_Hood", "Traveler_Vest", "Traveler_Gloves", "Traveler_Trousers", "Traveler_Boots",
        "IronGuard_Helmet", "IronGuard_Cuirass", "IronGuard_Gauntlets", "IronGuard_Greaves", "IronGuard_Boots"
    };

    public struct Area
    {
        public int X, Y, Width, Height;
        public Area(int x, int y, int width, int height)
        { X = x; Y = y; Width = width; Height = height; }
    }
    #endregion

    #region 独立图层归位
    public static Dictionary<string, uint[]> ReadLayers(uint[] source, int width, int height)
    {
        if (width != SourceWidth || height != SourceHeight || source.Length != width * height)
            throw new ArgumentException("分层源母版必须使用已标定的 1448×1086 透明图；新母版需重新标定锚点。");
        var result = new Dictionary<string, uint[]>();
        Add(result, source, "Bodies/Base_Male", new Area(80, 35, 235, 340), new Area(2, 4, 28, 42));
        Add(result, source, "Bodies/Base_Female", new Area(430, 35, 235, 340), new Area(2, 4, 28, 42));
        // 统一肩腰与四肢的实际像素，脸和发型仍由角色自身选择。
        for (int y = 23; y < Height; y++)
        for (int x = 0; x < Width; x++)
            result["Bodies/Base_Female"][y * Width + x] = result["Bodies/Base_Male"][y * Width + x];
        Add(result, source, "Hair/Male", new Area(805, 40, 205, 165), new Area(5, 0, 24, 16));
        Add(result, source, "Hair/Female", new Area(1140, 40, 220, 185), new Area(5, 0, 26, 19));
        Add(result, source, "Wearables/Traveler_Hood", new Area(95, 405, 210, 220), new Area(4, 0, 26, 27));
        Add(result, source, "Wearables/Traveler_Vest", new Area(440, 440, 215, 160), new Area(3, 22, 27, 13));
        AddPair(result, source, "Wearables/Traveler_Gloves",
            new Area(780, 510, 90, 110), new Area(935, 510, 95, 110), 29, 8, 9);
        Add(result, source, "Wearables/Traveler_Trousers", new Area(1145, 460, 210, 170), new Area(5, 33, 23, 10));
        AddPair(result, source, "Wearables/Traveler_Boots",
            new Area(85, 730, 95, 110), new Area(205, 730, 100, 110), 40, 10, 6);
        Add(result, source, "Wearables/IronGuard_Helmet", new Area(450, 640, 200, 195), new Area(5, 1, 24, 22));
        Add(result, source, "Wearables/IronGuard_Cuirass", new Area(780, 655, 250, 150), new Area(3, 22, 27, 13));
        AddPair(result, source, "Wearables/IronGuard_Gauntlets",
            new Area(1130, 710, 110, 125), new Area(1280, 710, 100, 125), 29, 8, 9);
        Add(result, source, "Wearables/IronGuard_Greaves", new Area(95, 860, 210, 160), new Area(5, 33, 23, 10));
        AddPair(result, source, "Wearables/IronGuard_Boots",
            new Area(420, 930, 110, 115), new Area(565, 930, 110, 115), 40, 10, 6);
        var hairMask = new uint[Width * Height];
        for (int i = 0; i < hairMask.Length; i++) hairMask[i] = 0xFFFFFFFF;
        result.Add("Masks/Headgear_Hair", hairMask);
        foreach (string itemId in ItemIds)
            result.Add("Icons/" + itemId, MakeIcon(result["Wearables/" + itemId]));
        return result;
    }

    private static void Add(Dictionary<string, uint[]> result, uint[] source, string name, Area sourceArea, Area targetArea)
    {
        var target = new uint[Width * Height];
        Copy(source, SourceWidth, FindBounds(source, SourceWidth, sourceArea), target, Width, targetArea);
        result.Add(name, target);
    }

    private static void AddPair(Dictionary<string, uint[]> result, uint[] source, string name,
        Area left, Area right, int top, int pieceWidth, int pieceHeight)
    {
        var target = new uint[Width * Height];
        int leftX = pieceWidth == 10 ? 5 : 1;
        int rightX = pieceWidth == 10 ? 20 : 24;
        Copy(source, SourceWidth, FindBounds(source, SourceWidth, left), target, Width,
            new Area(leftX, top, pieceWidth, pieceHeight));
        Copy(source, SourceWidth, FindBounds(source, SourceWidth, right), target, Width,
            new Area(rightX, top, pieceWidth, pieceHeight));
        result.Add(name, target);
    }
    #endregion

    #region 最近邻采样与独立物品图标
    public static Area FindBounds(uint[] pixels, int width, Area area)
    {
        int minX = area.X + area.Width, minY = area.Y + area.Height, maxX = -1, maxY = -1;
        for (int y = area.Y; y < area.Y + area.Height; y++)
        for (int x = area.X; x < area.X + area.Width; x++)
        {
            if ((pixels[y * width + x] >> 24) <= 128) continue;
            minX = Math.Min(minX, x); minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        if (maxX < minX || maxY < minY) throw new ArgumentException("素材标定区域没有有效像素。");
        return new Area(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static void Copy(uint[] source, int sourceWidth, Area from, uint[] target, int targetWidth, Area to)
    {
        for (int y = 0; y < to.Height; y++)
        for (int x = 0; x < to.Width; x++)
        {
            int sx = from.X + Math.Min(from.Width - 1, (int)((x + 0.5f) * from.Width / to.Width));
            int sy = from.Y + Math.Min(from.Height - 1, (int)((y + 0.5f) * from.Height / to.Height));
            uint pixel = source[sy * sourceWidth + sx];
            target[(to.Y + y) * targetWidth + to.X + x] = (pixel >> 24) > 128 ? pixel | 0xFF000000 : 0;
        }
    }

    private static uint[] MakeIcon(uint[] equipment)
    {
        Area bounds = FindBounds(equipment, Width, new Area(0, 0, Width, Height));
        float fit = Math.Min(28f / bounds.Width, 28f / bounds.Height);
        int drawWidth = Math.Max(1, (int)Math.Round(bounds.Width * fit));
        int drawHeight = Math.Max(1, (int)Math.Round(bounds.Height * fit));
        var result = new uint[32 * 32];
        Copy(equipment, Width, bounds, result, 32,
            new Area((32 - drawWidth) / 2, (32 - drawHeight) / 2, drawWidth, drawHeight));
        return result;
    }
    #endregion
}
