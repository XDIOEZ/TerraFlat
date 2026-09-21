using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlatWorld.DroppedItems;
using MemoryPack;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>掉落物离线回归：只创建独立 ECS World 和预览场景，不进入游戏、不读写玩家存档。</summary>
public static class DroppedItemEcsDiagnostics
{
    private const string ReportPath = "Temp/DroppedItemEcsValidation.txt";
    private static readonly List<string> Results = new();

    [MenuItem("FlatWorld/诊断/验证掉落物 ECS")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("掉落物隔离诊断只允许在编辑模式运行。");
        Results.Clear();
        int originalWorldCount = Unity.Entities.World.All.Count;
        try
        {
            CheckSimulation();
            CheckPersistence();
            CheckPickup();
            CheckPresentation();
            Check(Unity.Entities.World.All.Count == originalWorldCount, "全部诊断完成后没有遗留 ECS World");
            Material material = Resources.Load<Material>("DroppedItems/DroppedItemLit");
            Check(material != null && material.shader != null && material.shader.isSupported,
                "掉落物共享材质存在，Shader 受当前编辑器支持");
            Check(!ShaderUtil.GetShaderMessages(material.shader).Any(message =>
                message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error), "共享 Shader 无编译错误");
            Directory.CreateDirectory("Temp");
            File.WriteAllLines(ReportPath, Results);
            Debug.Log($"[DroppedItemEcsValidation] PASS {Results.Count} assertions; report={ReportPath}");
        }
        catch (Exception exception)
        {
            Results.Add("FAIL " + exception);
            Directory.CreateDirectory("Temp");
            File.WriteAllLines(ReportPath, Results);
            Debug.LogError($"[DroppedItemEcsValidation] FAIL {exception}");
            throw;
        }
    }

    #region 实体与运动

    private static DroppedBody Body(int id, float2 position) => new()
        { Id = id, Position = position, Scale = new float2(1f), Amount = 7f };

    private static void CheckSimulation()
    {
        using DroppedItemSimulation simulation = new();
        List<DroppedChange> changes = new();
        for (int i = 1; i <= 10000; i++) simulation.Create(Body(i, new float2(i % 100, i / 100)));
        for (int frame = 0; frame < 60; frame++)
        {
            simulation.Step(1f / 60f, default, changes);
            if (changes.Count != 0) throw new InvalidOperationException("静止实体产生了运动事件。");
        }
        Check(simulation.Count == 10000 && simulation.Get(100).Amount == 7f,
            "10000 个静止实体连续 60 步无运动事件，数量保持不变");
        Check(!simulation.TryGetFlight(100, out _) && !simulation.TryGetWater(100, out _),
            "静止实体不携带短期运动组件");
        bool duplicateRejected = false;
        try { simulation.Create(Body(100, default)); }
        catch (ArgumentException) { duplicateRejected = true; }
        Check(duplicateRejected && simulation.Count == 10000, "重复 ID 被拒绝且不残留实体");
        bool invalidRejected = false;
        DroppedBody invalid = Body(10001, default); invalid.Amount = float.NaN;
        try { simulation.Create(invalid); }
        catch (ArgumentException) { invalidRejected = true; }
        Check(invalidRejected && simulation.Count == 10000, "非有限数量被拒绝且不残留实体");

        WorldTopologyDomain topology = new(new int2(0), new int2(100), true);
        simulation.Create(Body(10001, new float2(99f, 5f)), new DroppedFlight
        { Start = new float2(99f, 5f), End = new float2(101f, 5f), Control = new float2(100f, 6f),
            Duration = 1f, ArcHeight = 1f, RotationSpeed = 360f });
        simulation.Step(0.5f, topology, changes);
        DroppedBody moving = simulation.Get(10001);
        Check(math.distance(moving.Position, new float2(0f, 5f)) < 0.001f && moving.VisualHeight > 1f && moving.Pickable == 0,
            "抛掷使用接缝最短路径，地面位置与视觉高度分离，途中禁止拾取");
        simulation.Step(0.5f, topology, changes);
        Check(changes.Count(change => change.Id == 10001 && change.Kind == 1) == 1 &&
            !simulation.TryGetFlight(10001, out _) && simulation.Get(10001).VisualHeight == 0f,
            "落地事件仅交付一次，移除轨迹组件并清零视觉高度");
        simulation.Step(0.5f, topology, changes);
        Check(changes.Count == 0, "落地后恢复静止，不再产生运动事件");

        DroppedBody floating = Body(10002, new float2(10f)); floating.WaterKind = 1;
        simulation.Create(floating, water: new DroppedWaterTransition
            { StartDepth = 0.48f, TargetDepth = 0.2f, Duration = 0.8f });
        simulation.Step(0.8f, default, changes);
        Check(Mathf.Abs(simulation.Get(10002).LiquidDepth - 0.2f) < 0.001f &&
            !simulation.TryGetWater(10002, out _) && changes.Any(change => change.Id == 10002 && change.Kind == 2),
            "漂浮水线到达目标后移除过渡组件");
        DroppedBody sinking = Body(10003, new float2(10f)); sinking.WaterKind = 2;
        simulation.Create(sinking, water: new DroppedWaterTransition { TargetDepth = 1f, Duration = 2f });
        simulation.Step(2f, default, changes);
        Check(changes.Any(change => change.Id == 10003 && change.Kind == 3), "沉没完成交付回收事件");
        simulation.Remove(10003);
        Check(!simulation.Contains(10003), "回收从权威实体集合移除物品");
        simulation.Create(Body(10004, default), new DroppedFlight
            { Start = default, End = new float2(2f), Control = new float2(1f), Duration = 1f });
        DroppedBody concurrentWater = Body(10005, default); concurrentWater.WaterKind = 1;
        simulation.Create(concurrentWater, water: new DroppedWaterTransition
            { StartDepth = 0f, TargetDepth = 0.4f, Duration = 1f });
        simulation.Step(0.5f, default, changes);
        Check(changes.Count == 2 && changes.Any(change => change.Id == 10004) &&
            changes.Any(change => change.Id == 10005) && Mathf.Abs(simulation.Get(10005).LiquidDepth - 0.2f) < 0.001f,
            "抛掷与浮沉同帧执行时，固定变更缓冲分区不相互覆盖");
    }

    #endregion

    #region 快照兼容

    private static Data_GeneralItem Data(float amount = 7f, bool stackable = true) => new()
    {
        IDName = "__DroppedEcsTest", Guid = 1234, Durability = 0.37f, MaxDurability = 1f,
        ItemSpecialData = "stateful-container", Stack = new ItemStack
        { Amount = amount, Weight = 1f, Volume = 1f, Stackable = stackable, CanBePickedUp = true }
    };

    private static void CheckPersistence()
    {
        Data_GeneralItem payload = Data();
        payload.ModuleDataDic.Add("container", new Ex_ModData_MemoryPackable
            { ID = "container", BitData = new byte[] { 3, 7, 11 } });
        DroppedItemArchive archive = new();
        archive.Worlds.Add("world/surface", new List<DroppedItemSaveRecord>
        {
            new() { Data = payload, Position = new Vector2(2, 3), Scale = Vector2.one,
                HasFlight = true, FlightDuration = 1f, FlightElapsed = 0.4f },
        });
        archive.Worlds.Add("world/cave", new List<DroppedItemSaveRecord>());
        DroppedItemArchive copy = MemoryPackSerializer.Deserialize<DroppedItemArchive>(MemoryPackSerializer.Serialize(archive));
        DroppedItemSaveRecord record = copy.Worlds["world/surface"][0];
        Check(record.Data.Stack.Amount == 7f && record.Data.Durability == 0.37f &&
            record.Data.ItemSpecialData == payload.ItemSpecialData &&
            ((Ex_ModData_MemoryPackable)record.Data.ModuleDataDic["container"]).BitData.SequenceEqual(new byte[] { 3, 7, 11 }),
            "快照保留数量、耐久、特殊状态及容器模块字节");
        Check(copy.Worlds.Count == 2 && record.HasFlight && record.FlightElapsed == 0.4f,
            "不同维度隔离保存，并保留未结束的轨迹进度");
        LegacyDroppedEnvelopeFixture old = new() { Version = 16, CoreSaveData = new byte[] { 1, 2 }, ChunkRecords = new() };
        CompactSaveEnvelope upgraded = MemoryPackSerializer.Deserialize<CompactSaveEnvelope>(MemoryPackSerializer.Serialize(old));
        Check(upgraded.Version == 16 && upgraded.DroppedItems == null && upgraded.CoreSaveData.SequenceEqual(old.CoreSaveData),
            "旧三字段存档封装可按新格式读取，新增掉落字段为空");
        upgraded.Version = 17; upgraded.DroppedItems = MemoryPackSerializer.Serialize(archive);
        CompactSaveEnvelope roundTrip = MemoryPackSerializer.Deserialize<CompactSaveEnvelope>(MemoryPackSerializer.Serialize(upgraded));
        Check(roundTrip.DroppedItems.SequenceEqual(upgraded.DroppedItems), "新存档封装保留独立掉落快照");
    }

    #endregion

    #region 共用拾取事务

    private sealed class InventoryFixture : IInventory
    {
        public readonly Inventory Inventory;
        public InventoryFixture(string name, float maxSlot = 100f)
        {
            Inventory = new Inventory { Data = new Inventory_Data(new List<ItemSlot>
                { new(0) { SlotMaxVolume = maxSlot } }, name) };
        }
        public Inventory GetDefaultTargetInventory() => Inventory;
    }

    private static void CheckPickup()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = new GameObject("DroppedItemEcsDiagnosticFixture");
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            ItemPicker picker = root.AddComponent<ItemPicker>();
            picker.ModSaveData = new Ex_ModData_MemoryPackable();
            InventoryFixture hotbar = new(ModText.Hotbar, 2f), bag = new(ModText.Bag);
            picker.AddTargetInventories.Add(hotbar); picker.AddTargetInventories.Add(bag);
            ItemData full = Data(5f);
            Check(picker.TryAcceptNetworkPickup(full) && !full.Stack.CanBePickedUp &&
                hotbar.Inventory.Data.itemSlots[0].Amount == 2f && bag.Inventory.Data.itemSlots[0].Amount == 3f,
                "完整拾取先放快捷栏，余量进入背包");
            hotbar.Inventory.Data.itemSlots[0].ClearData(); bag.Inventory.Data.itemSlots[0].ClearData();
            bag.Inventory.Data.SetCarryCapacity(3f, 100f);
            ItemData partial = Data(5f);
            Check(picker.TryAcceptNetworkPickup(partial) && partial.Stack.CanBePickedUp && partial.Stack.Amount == 2f &&
                hotbar.Inventory.Data.itemSlots[0].Amount + bag.Inventory.Data.itemSlots[0].Amount == 3f,
                "重量限制下部分入包，世界保留余量，跨快捷栏与背包合计守恒");
            ItemData blocked = Data(5f);
            Check(!picker.TryAcceptNetworkPickup(blocked) && blocked.Stack.Amount == 5f && blocked.Stack.CanBePickedUp,
                "满包拾取失败不改变源数量与拾取标记");
            hotbar.Inventory.Data.itemSlots[0].ClearData(); bag.Inventory.Data.itemSlots[0].ClearData();
            bag.Inventory.Data.SetCarryCapacity(100f, 1.5f);
            ItemData tool = Data(3f, false);
            Check(picker.TryAcceptNetworkPickup(tool) && tool.Stack.Amount == 2f &&
                hotbar.Inventory.Data.itemSlots[0].Amount == 1f && bag.Inventory.Data.itemSlots[0].itemData == null,
                "不可堆叠工具按整数逐件入包，体积容量不足时不会拆成小数");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    #endregion

    #region 表现批次生命周期

    /// <summary>使用程序化测试精灵验证正式渲染桥，不加载真实 Item，不修改资源或正式场景对象。</summary>
    private static void CheckPresentation()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        using DroppedItemSimulation simulation = new();
        Texture2D texture = new(2, 2);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0f), 2f);
        GameObject cameraObject = new("DroppedRenderDiagnosticCamera");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 3f;
        cameraObject.transform.position = new Vector3(1f, 1f, -10f);
        IDisposable presentation = null;
        try
        {
            // 诊断反射只用于访问隔离的 internal 表现适配器，不加入生产运行路径。
            Type visualType = typeof(DroppedItemService).Assembly.GetType("DroppedItemVisual", true);
            object visual = Activator.CreateInstance(visualType, true);
            void Field(string name, object value) => visualType.GetField(name).SetValue(visual, value);
            Field("Sprite", sprite); Field("Vertices", sprite.vertices); Field("Uvs", sprite.uv);
            Field("Triangles", sprite.triangles); Field("LocalMatrix", Matrix4x4.identity); Field("Color", Color.white);
            var map = (System.Collections.IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(int), visualType));
            for (int i = 1; i <= 1000; i++)
            {
                simulation.Create(Body(i, new float2(1f, 1f))); map.Add(i, visual);
            }
            Type type = typeof(DroppedItemService).Assembly.GetType("DroppedItemPresentation", true);
            presentation = (IDisposable)Activator.CreateInstance(type, scene, simulation, map);
            for (int i = 1; i <= 1000; i++) type.GetMethod("Changed").Invoke(presentation, new object[] { i });
            void Present() => type.GetMethod("Present").Invoke(presentation, new object[] { camera, default(WorldTopologyDomain) });
            Present();
            MeshFilter[] meshes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshFilter>()).ToArray();
            Check(meshes.Length == 1 && meshes[0].sharedMesh.vertexCount == 1000 * sprite.vertices.Length &&
                scene.GetRootGameObjects().All(root => root.GetComponentInChildren<Item>() == null),
                "同批 1000 个掉落物只生成一个网格节点，不生成任何 Item");
            cameraObject.transform.position = new Vector3(10000f, 10000f, -10f); Present();
            Check(scene.GetRootGameObjects().Length == 1, "离开视野释放批次节点，权威 ECS 实体仍保留");
            cameraObject.transform.position = new Vector3(1f, 1f, -10f); Present();
            Check(scene.GetRootGameObjects().Length == 2 && simulation.Count == 1000, "返回视野可从实体数据重建显示");
            presentation.Dispose(); presentation = null;
            Check(scene.GetRootGameObjects().Length == 1, "表现释放不遗留网格节点");
        }
        finally
        {
            presentation?.Dispose();
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(texture);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    #endregion

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Results.Add("PASS " + description);
    }
}

/// <summary>与旧版存档封装完全相同的字段次序，仅用于内存兼容测试。</summary>
[MemoryPackable]
public partial class LegacyDroppedEnvelopeFixture
{
    public int Version;
    public byte[] CoreSaveData;
    public List<ChunkSaveRecord> ChunkRecords;
}
