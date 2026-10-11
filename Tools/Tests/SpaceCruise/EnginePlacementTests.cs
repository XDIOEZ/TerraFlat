using System.Text.Json;
using FlatWorld.Spaceflight;

internal static partial class Program
{
    #region 引擎外缘与喷口通道回归
    private static ShipPieceState EnginePiece(int x, int y, int turns = 0) => new()
        { Kind = ShipPieceKind.Engine, CellX = x, CellY = y, QuarterTurns = turns };
    private static ShipState FloorRectangle(int x, int y, int width, int height)
    {
        var ship = new ShipState();
        for (int row = y; row < y + height; row++)
        for (int col = x; col < x + width; col++)
            ship.Pieces.Add(new ShipPieceState { Kind = ShipPieceKind.Floor, Layer = -1, CellX = col, CellY = row });
        return ship;
    }
    private static void ExpectEngine(ShipState ship, ShipPieceState engine, bool expected, string message)
    {
        bool actual = ShipEnginePlacement.TryValidateEngine(ship, engine, out string reason);
        Check(actual == expected, $"{message}: accepted={actual}, reason={reason}");
        Check(actual || !string.IsNullOrWhiteSpace(reason), "invalid engine must explain its placement failure");
    }
    private static void ExpectEngineAddition(ShipState ship, ShipPieceState proposal, bool expected, string message)
    {
        bool actual = ShipEnginePlacement.TryValidateAddition(ship, proposal, out string reason);
        Check(actual == expected, $"{message}: accepted={actual}, reason={reason}");
        Check(actual || !string.IsNullOrWhiteSpace(reason), "blocked addition must explain its placement failure");
    }
    private static void EnginePlacementRules()
    {
        var square = FloorRectangle(-4, -4, 9, 9);
        var edges = new[] { EnginePiece(0, -4, 0), EnginePiece(4, 0, 1), EnginePiece(0, 4, 2), EnginePiece(-4, 0, 3) };
        foreach (var engine in edges)
        {
            square.Pieces.Add(engine);
            ExpectEngine(square, engine, true, "all four outward edges work and ignore the engine itself");
            engine.QuarterTurns += 2;
            ExpectEngine(square, engine, false, "edge engine cannot face inward");
            square.Pieces.Remove(engine);
        }
        for (int turns = 0; turns < 4; turns++)
            ExpectEngine(square, EnginePiece(0, 0, turns), false, "interior placement is rejected in every direction");
        var malformed = EnginePiece(0, -4); malformed.Height = 2;
        ExpectEngine(square, malformed, false, "one by two engine is rejected");
        malformed.Height = 1; malformed.Width = 0;
        ExpectEngine(square, malformed, false, "zero width cannot be normalized to one for engine validation");
        ExpectEngine(square, EnginePiece(0, -5), false, "unsupported engine is rejected");
        malformed = EnginePiece(0, -4); malformed.Destroyed = true;
        ExpectEngine(square, malformed, false, "destroyed engine is rejected");
        malformed.Destroyed = false; malformed.Health = 0;
        ExpectEngine(square, malformed, false, "engine with zero health is rejected");
        malformed.Health = 100; malformed.Kind = ShipPieceKind.Equipment;
        ExpectEngine(square, malformed, false, "non-engine cannot use engine validation");

        square.Pieces.RemoveAll(piece => piece.CellX == 0 && piece.CellY == 1);
        ExpectEngine(square, EnginePiece(0, 0, 2), false, "a one-cell hole does not turn the opposite hull into outside space");
        var concave = FloorRectangle(-2, -3, 5, 4);
        for (int y = 1; y <= 3; y++)
        foreach (int x in new[] { -2, 2 })
            concave.Pieces.Add(new ShipPieceState { Kind = ShipPieceKind.Floor, CellX = x, CellY = y });
        ExpectEngine(concave, EnginePiece(-2, 2, 1), false, "opposite arm of concave hull blocks exhaust across a gap");
        ExpectEngine(concave, EnginePiece(-2, 2, 3), true, "negative-coordinate outer arm may face away from the ship");
        ExpectEngine(concave, EnginePiece(0, 0, 2), true, "open concavity with an unobstructed ray reaches outside");

        var rotated = new ShipState();
        var support = new ShipPieceState { Kind = ShipPieceKind.Floor, CellX = -5, CellY = -7, Width = 2, Height = 3, QuarterTurns = 1 };
        rotated.Pieces.Add(support);
        ExpectEngine(rotated, EnginePiece(-3, -6, 1), true, "rotated rectangular floor supports its actual right edge");
        ExpectEngine(rotated, EnginePiece(-3, -6, 3), false, "rotated rectangular floor blocks inward exhaust");
        support.Destroyed = true;
        ExpectEngine(rotated, EnginePiece(-3, -6, 1), false, "destroyed floor cannot support the engine");
    }
    private static void EnginePlacementAdditions()
    {
        var ship = FloorRectangle(-5, -8, 1, 1);
        var engine = EnginePiece(-5, -8); ship.Pieces.Add(engine);
        var obstacle = new ShipPieceState { Kind = ShipPieceKind.Wall, CellX = -5, CellY = -11 };
        ExpectEngineAddition(ship, obstacle, false, "new wall cannot block exhaust after an empty gap");
        obstacle.Kind = ShipPieceKind.Floor;
        ExpectEngineAddition(ship, obstacle, false, "new floor cannot extend into the exhaust ray");
        obstacle.Kind = ShipPieceKind.Equipment; obstacle.CellX = -6; obstacle.Width = 2; obstacle.Height = 3; obstacle.QuarterTurns = 1;
        ExpectEngineAddition(ship, obstacle, false, "rotated equipment footprint intersects exhaust ray");
        obstacle.CellX = -8;
        ExpectEngineAddition(ship, obstacle, true, "rotated equipment beside exhaust is allowed");
        obstacle.CellX = -6;
        foreach (var kind in new[] { ShipPieceKind.Wire, ShipPieceKind.Pipe, ShipPieceKind.Fiber })
        {
            obstacle.Kind = kind;
            ExpectEngineAddition(ship, obstacle, true, "formal connection-line kinds do not obstruct exhaust");
        }
        obstacle.Kind = ShipPieceKind.Wall; obstacle.Destroyed = true;
        ExpectEngineAddition(ship, obstacle, true, "destroyed proposal is not an exhaust obstacle");
        obstacle.Destroyed = false; obstacle.Health = 0;
        ExpectEngineAddition(ship, obstacle, true, "zero-health proposal is not an exhaust obstacle");
        obstacle.Health = 100; engine.Destroyed = true;
        ExpectEngineAddition(ship, obstacle, true, "destroyed existing engine does not reserve an exhaust ray");
        engine.Destroyed = false;
        ship.Pieces.Add(obstacle);
        ExpectEngine(ship, engine, false, "existing live equipment after a gap disables engine eligibility");
        obstacle.Destroyed = true;
        ExpectEngine(ship, engine, true, "destroyed structure no longer obstructs engine eligibility");
        var invalid = EnginePiece(-5, -8, 2);
        ship.Pieces.Add(new ShipPieceState { Kind = ShipPieceKind.Floor, CellX = -5, CellY = -7 });
        ExpectEngineAddition(ship, invalid, false, "new engine uses the same outward-facing rule");
    }
    #endregion

    #region GM模板配置静态检查
    private static void EnginePlacementTemplate()
    {
        string root = FindEngineFixtureRoot();
        var items = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (string file in new[] { "space_ships", "space_devices", "electrical", "chemical_machines" })
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, $"Assets/StreamingAssets/GameConfig/Items/shells/{file}.json")));
            foreach (var item in document.RootElement.GetProperty("items").EnumerateArray()) items.Add(item.GetProperty("id").GetString(), item.Clone());
        }
        using var machines = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Assets/Resources/Config/Mechanical/mechanical-catalog.json")));
        var layers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in machines.RootElement.GetProperty("Nodes").EnumerateArray())
            layers.Add(node.GetProperty("Id").GetString(), JsonInt(node, "PlacementLayer", JsonString(node, "Kind") == "bridge" ? 1 : 0));
        using var structures = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Assets/StreamingAssets/GameConfig/Structures/fixed-structures.json")));
        var template = structures.RootElement.GetProperty("structures").EnumerateArray().Single(value => value.GetProperty("id").GetString() == "gm:small-test-ship");
        var ship = new ShipState();
        foreach (var stamp in template.GetProperty("items").EnumerateArray())
        {
            string itemId = stamp.GetProperty("itemId").GetString();
            var kindValue = EngineItemValue(items, itemId, "modules", "ship-part", "parameters", "Configuration", "Kind");
            var kind = kindValue.ValueKind == JsonValueKind.String ? Enum.Parse<ShipPieceKind>(kindValue.GetString()) : ShipPieceKind.Equipment;
            var layerValue = EngineItemValue(items, itemId, "modules", "ship-part", "parameters", "Configuration", "Layer");
            int layer = layerValue.ValueKind == JsonValueKind.Number ? layerValue.GetInt32() : layers.GetValueOrDefault(itemId);
            var buildingValue = EngineItemValue(items, itemId, "modules", "建筑模块", "data", "BitData");
            using var building = JsonDocument.Parse(buildingValue.GetString());
            int width = JsonInt(building.RootElement, "FootprintWidth", 1), height = JsonInt(building.RootElement, "FootprintHeight", 1);
            var shipWidth = EngineItemValue(items, itemId, "modules", "ship-part", "parameters", "Configuration", "FootprintWidth");
            var shipHeight = EngineItemValue(items, itemId, "modules", "ship-part", "parameters", "Configuration", "FootprintHeight");
            if (shipWidth.ValueKind == JsonValueKind.Number) width = Math.Max(width, shipWidth.GetInt32());
            if (shipHeight.ValueKind == JsonValueKind.Number) height = Math.Max(height, shipHeight.GetInt32());
            // 模板矩形表示重复成员，实际占地仍读取每件物品定义。
            for (int y = 0; y < JsonInt(stamp, "height", 1); y++)
            for (int x = 0; x < JsonInt(stamp, "width", 1); x++)
                ship.Pieces.Add(new ShipPieceState { PieceId = $"{JsonString(stamp, "memberId")}:{x}:{y}", ItemId = itemId,
                    Kind = kind, Layer = layer, CellX = stamp.GetProperty("x").GetInt32() + x, CellY = stamp.GetProperty("y").GetInt32() + y,
                    QuarterTurns = JsonInt(stamp, "quarterTurns", 0), Width = width, Height = height });
        }
        var occupied = new Dictionary<ShipCell, string>();
        foreach (var piece in ship.Pieces)
        foreach (var cell in ShipGeometry.Footprint(piece))
        {
            Check(cell.X >= 0 && cell.Y >= 0 && cell.X < template.GetProperty("width").GetInt32() && cell.Y < template.GetProperty("height").GetInt32(), "template actual footprint remains in bounds");
            if (piece.Layer == 0)
                Check(occupied.TryAdd(cell, piece.PieceId), $"template structure overlap at {cell.X},{cell.Y}: {piece.PieceId}");
        }
        var engines = ship.Pieces.Where(piece => piece.Kind == ShipPieceKind.Engine).ToArray();
        Check(engines.Length == 6, "GM template must have six engines");
        foreach (var engine in engines)
        {
            ExpectEngine(ship, engine, true, $"GM template engine {engine.PieceId}");
            var sealedValue = EngineItemValue(items, engine.ItemId, "modules", "ship-part", "parameters", "Configuration", "SealsAtmosphere");
            Check(sealedValue.ValueKind == JsonValueKind.True, "edge engines must seal the wall openings");
        }
        Console.WriteLine($"CONTENT GM template: {engines.Length} outward engines, {occupied.Count} distinct structure-layer cells");
    }
    private static string FindEngineFixtureRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Assets/StreamingAssets/GameConfig/Structures/fixed-structures.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("Cannot locate production GM template relative to the test binary.");
    }
    private static JsonElement EngineItemValue(Dictionary<string, JsonElement> items, string itemId, params string[] path)
    {
        while (items.TryGetValue(itemId, out var item))
        {
            var value = item; bool found = true;
            foreach (string member in path)
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(member, out value)) { found = false; break; }
            if (found) return value;
            if (!item.TryGetProperty("parent", out var parent)) break;
            itemId = parent.GetString();
        }
        return default;
    }
    private static int JsonInt(JsonElement value, string key, int fallback) => value.TryGetProperty(key, out var member) ? member.GetInt32() : fallback;
    private static string JsonString(JsonElement value, string key) => value.TryGetProperty(key, out var member) ? member.GetString() : null;
    #endregion
}
