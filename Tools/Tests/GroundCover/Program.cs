using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlatWorld.WorldModel;

#region 读取正式配置
var root = Path.GetFullPath(args[0]);
var yaml = File.ReadAllText(Path.Combine(root, "Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset"));
var sections = yaml.Split("  textParameters:");
var numbers = new Dictionary<string, double>();
foreach (Match m in Regex.Matches(sections[0], @"- Id: ([^\r\n]+)\r?\n    Value: ([^\r\n]+)"))
    numbers[m.Groups[1].Value] = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
var texts = new Dictionary<string, string>();
foreach (Match m in Regex.Matches(sections[1], @"- Id: ([^\r\n]+)\r?\n    Value: ([^\r\n]+)"))
    texts[m.Groups[1].Value] = m.Groups[2].Value;
using var tiles = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Assets/StreamingAssets/GameConfig/Tiles/terrain.json")));
texts["ecology.naturalPlantableGroundTileIds"] = string.Join(",", tiles.RootElement.GetProperty("tiles")
    .EnumerateArray().Where(t => t.TryGetProperty("naturalPlantable", out var capable) && capable.GetBoolean())
    .Select(t => t.GetProperty("runtimeTileId").GetInt32()).Where(id => id > 0).OrderBy(id => id));
numbers["biome.peat.spawnChance"] = 0;
numbers["climate.temperature.blendRadius"] = 0;
var generatorType = typeof(DeterministicChunkGenerator);
var climateType = generatorType.GetNestedType("SurfaceClimateSample", BindingFlags.NonPublic)!;
var batchType = generatorType.GetNestedType("SurfaceChunkBatch", BindingFlags.NonPublic)!;
var build = generatorType.GetMethod("BuildSurfaceCell", BindingFlags.Static | BindingFlags.NonPublic)!;
var results = new Dictionary<string, object>();

#endregion

#region 草地生成回归
byte[] Grass(double rain, double celsius = 14.2155, int ground = 1, double height = .657033, int seed = 277168505)
{
    var n = new Dictionary<string, double>(numbers);
    n["biome.grassland.groundTileId"] = ground;
    n["biome.grassland.minimumCelsius"] = -100;
    n["biome.grassland.maximumCelsius"] = 100;
    n["biome.grassland.minimumPrecipitation"] = 0;
    n["biome.grassland.maximumPrecipitation"] = 1;
    n["biome.grassland.priority"] = 9999;
    var profile = new ChunkGenerationProfileSnapshot("grass.regression", 75, 32, 32, n, texts);
    var request = new ChunkGenerationRequest(1, new WorldAddress("surface", new Int2(512,544)), seed, 1, profile);
    var climate = Activator.CreateInstance(climateType)!;
    void Set(string field, object value) => climateType.GetField(field)!.SetValue(climate, value);
    Set("Height", height); Set("TemperatureCelsius", celsius);
    Set("Temperature", profile.Settings.NormalizeTemperatureCelsius(celsius));
    Set("Precipitation", rain); Set("BaseBiome", SurfaceBiomeKind.Grassland); Set("Classified", true);
    var batch = Activator.CreateInstance(batchType, request, profile.Settings)!;
    try
    {
        var samples = (Array)batchType.GetField("samples", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(batch)!;
        for (int i = 0; i < samples.Length; i++) samples.SetValue(climate, i);
        var lavaListType = typeof(List<>).MakeGenericType(generatorType.GetNestedType("LavaBasin", BindingFlags.NonPublic)!);
        var lava = Activator.CreateInstance(lavaListType);
        var grass = new byte[1024];
        for (int y=0; y<32; y++) for (int x=0; x<32; x++)
        {
            var output=build.Invoke(null, new object[]{request, profile.Settings, null!, climate, batch, x,y,512+x,544+y,0,0,0,lava!})!;
            grass[y*32+x]=(byte)output.GetType().GetField("Grass")!.GetValue(output)!;
        }
        return grass;
    }
    finally { ((IDisposable)batch).Dispose(); }
}
int Count(byte[] a)=>a.Count(v=>v==ChunkTerrainData.GrassPresent);
void Check(bool ok, string what) { if(!ok) throw new Exception(what); }
var dry=Grass(0); var originalRain=Grass(.105704); var wet=Grass(.15);
Check(Count(dry)>0,"zero rainfall erased grass");
Check(Count(dry)<Count(originalRain) && Count(originalRain)<Count(wet),"rainfall density not continuous");
Check(dry.Select((v,i)=>v!=2 || originalRain[i]==2).All(v=>v),"dry cells changed as rainfall increased");
Check(originalRain.Select((v,i)=>v!=2 || wet[i]==2).All(v=>v),"rainfall broke deterministic selection");
Check(originalRain.SequenceEqual(Grass(.105704)),"same seed changed grass");
Check(Count(Grass(.15,-38))==0,"extreme cold generated grass");
Check(Count(Grass(.15,60))==0,"extreme heat generated grass");
Check(Count(Grass(.15,14.2,4))==0,"stone generated grass");
Check(Count(Grass(.15,14.2,29))>0,"natural dirt lost grass");
Check(Count(Grass(.15,14.2,28))>0,"natural peat lost grass");
Check(Count(Grass(.15,14.2,1,.1))==0,"underwater generated grass");
results["grassCellsPer1024"]=new {dry=Count(dry),screenshotRain=Count(originalRain),wet=Count(wet)};

#endregion

#region 花朵生态回归
using var flowers=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Assets/StreamingAssets/GameConfig/WorldGeneration/NaturalItems/surface-grassland.json")));
var flower=flowers.RootElement.GetProperty("ecologyRules").EnumerateArray().Single(r=>r.GetProperty("ruleId").GetString()=="surface.grassland.flower");
double N(string key)=>flower.GetProperty(key).GetDouble();
var rule=new EcologySpawnRuleSnapshot("surface.grassland.flower","Flower",1,N("spawnChance"),N("spawnChanceMultiplier"),(int)N("biomeMask"),N("minTemperature"),N("maxTemperature"),N("minPrecipitation"),N("maxPrecipitation"),N("minHeight"),N("maxHeight"),distributionMode:(EcologyDistributionMode)N("distributionMode"),patchSpacing:(int)N("patchSpacing"),patchRadius:N("patchRadius"),patchChance:N("patchChance"),requiredEnvironmentLayer:flower.GetProperty("requiredEnvironmentLayer").GetString(),minimumEnvironmentValue:N("minimumEnvironmentValue"),maximumEnvironmentValue:N("maximumEnvironmentValue"),requireNaturalPlantableGround:flower.GetProperty("requireNaturalPlantableGround").GetBoolean());
var fp=new ChunkGenerationProfileSnapshot("flowers.regression",75,32,32,numbers,texts,ecologyRules:new[]{rule});
int Flowers(double celsius, double rain)
{
    int total=0;
    for(int ox=0;ox<4;ox++) for(int oy=0;oy<4;oy++)
    {
        var request=new ChunkGenerationRequest(1,new WorldAddress("surface",new Int2(512+ox*32,544+oy*32)),277168505,1,fp);
        using var terrain=new ChunkTerrainBuffer(32,32);
        for(int y=0;y<32;y++) for(int x=0;x<32;x++)
        {
            terrain.SetCell(x,y,new TerrainCell(1,0,0,4,1,TerrainCellFlags.Walkable));
            terrain.SetEnvironmentValue("height",x,y,.657033f);
            terrain.SetEnvironmentValue("temperature",x,y,.28431f);
            terrain.SetEnvironmentValue("temperature.celsius",x,y,(float)celsius);
            terrain.SetEnvironmentValue("precipitation",x,y,(float)rain);
        }
        total+=ChunkEcologyGenerator.Generate(request,terrain,1,fp.EcologyRules,CancellationToken.None).Placements.Count;
    }
    return total;
}
int normalFlowers=Flowers(14.2155,.105704),zeroRainFlowers=Flowers(14.2155,0);
Check(normalFlowers>0 && zeroRainFlowers>0,"flower eligibility erased by low rainfall");
Check(Flowers(-38,.105704)==0,"cold flowers generated");
Check(Flowers(60,.105704)==0,"hot flowers generated");
results["flowerCellsPer16384"]=new {screenshotConditions=normalFlowers,zeroRain=zeroRainFlowers};
Console.WriteLine(JsonSerializer.Serialize(results));
Console.WriteLine("PASS: dry/wet density, deterministic seeds, dirt/peat, cold/hot/underwater exclusions, flower eligibility.");

#endregion
