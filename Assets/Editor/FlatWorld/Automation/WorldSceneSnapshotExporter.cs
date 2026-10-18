using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FlatWorld.AIECS.Gameplay;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Converters;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Automation
{
    /// <summary>按需采集已加载世界，给离线性能分析提供可读现场，不调用游戏保存流程。</summary>
    public sealed class WorldSceneSnapshotExporter
    {
        #region 导出入口与关联信息

        private readonly JArray items = new();
        private readonly JArray itemData = new();
        private readonly JArray chunks = new();
        private readonly Dictionary<ItemData, string> dataRefs = new();
        private readonly Dictionary<int, JObject> itemsByGuid = new();
        private readonly JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new DiagnosticFieldsResolver(),
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
            TypeNameHandling = TypeNameHandling.None
        });

        private WorldSceneSnapshotExporter()
        {
            serializer.Converters.Add(new ItemReferenceConverter(this));
            serializer.Converters.Add(new InventoryConverter(this));
            serializer.Converters.Add(new StringEnumConverter());
        }

        public static string[] Export(string directory, string snapshotId, int selectedDisplayFrame)
        {
            var exporter = new WorldSceneSnapshotExporter();
            JObject report = exporter.Capture(snapshotId, selectedDisplayFrame);
            using var file = new StreamWriter(Path.Combine(directory, "world-scene.json"), false, new UTF8Encoding(false));
            using var writer = new JsonTextWriter(file) { Formatting = Formatting.Indented };
            report.WriteTo(writer);
            return new[] { "world-scene.json" };
        }

        private JObject Capture(string snapshotId, int selectedDisplayFrame)
        {
            DateTimeOffset started = DateTimeOffset.UtcNow;
            Scene scene = SceneManager.GetActiveScene();
            ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
            ItemMgr itemManager = ItemMgr.GetInstance();
            SaveDataMgr saveManager = UnityEngine.Object.FindFirstObjectByType<SaveDataMgr>();
            var scenes = new JArray();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene loaded = SceneManager.GetSceneAt(index);
                scenes.Add(new JObject { ["name"] = loaded.name, ["path"] = loaded.path, ["loaded"] = loaded.isLoaded });
            }

            if (EditorApplication.isPlaying)
            {
                CaptureGameObjects(itemManager);
                CaptureMachines(chunkManager);
                var natural = new List<(ItemData Data, bool Suspended, bool Dead)>();
                NaturalEntityEcsService.CopyDiagnosticItems(natural);
                foreach (var resource in natural.OrderBy(resource => resource.Data.Guid))
                    AddItem(resource.Data, "natural-ecs", resource.Data.transform.position)["naturalRuntime"] = new JObject
                    {
                        ["suspended"] = resource.Suspended, ["dead"] = resource.Dead
                    };
                foreach (DroppedItemSaveRecord drop in DroppedItemService.CaptureDiagnosticItems())
                    AddItem(drop.Data, "lightweight-drop", drop.Data.transform.position)["motion"] = Token(drop);
                CaptureResidents();
                CaptureChunks(chunkManager);
            }

            WeatherMgr weather = WeatherMgr.ExistingInstance;
            DayTimeSystem time = DayTimeSystem.GetInstance();
            Camera camera = Camera.main;
            return new JObject
            {
                ["schemaVersion"] = 1,
                ["snapshotId"] = snapshotId,
                ["selectedDisplayFrame"] = selectedDisplayFrame,
                ["capturedAtUtc"] = started.ToString("o", CultureInfo.InvariantCulture),
                ["captureCompletedAtUtc"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["source"] = "local-editor-loaded-world-at-export",
                ["status"] = EditorApplication.isPlaying ? "captured" : "not-in-play-mode",
                ["editorPlayingAtExport"] = EditorApplication.isPlaying,
                ["editorPausedAtExport"] = EditorApplication.isPaused,
                ["unityFrameAtExport"] = Time.frameCount,
                ["activeScene"] = scene.name,
                ["worldSeed"] = saveManager?.SaveData != null ? new JValue(saveManager.SaveData.Seed) : JValue.CreateNull(),
                ["loadedScenes"] = scenes,
                ["environment"] = new JObject
                {
                    ["timeScale"] = Time.timeScale,
                    ["gameTimeSeconds"] = time != null ? new JValue(time.GetCurrentTime(scene.name)) : JValue.CreateNull(),
                    ["weather"] = weather != null ? new JValue(weather.CurrentWeather.ToString()) : JValue.CreateNull(),
                    ["weatherIntensity"] = weather != null ? new JValue(weather.CurrentWeatherIntensity) : JValue.CreateNull()
                },
                ["camera"] = camera == null ? null : new JObject
                {
                    ["position"] = Token(camera.transform.position), ["orthographic"] = camera.orthographic,
                    ["orthographicSize"] = camera.orthographicSize, ["pixelWidth"] = camera.pixelWidth,
                    ["pixelHeight"] = camera.pixelHeight
                },
                ["summary"] = new JObject
                {
                    ["worldItemCount"] = items.Count, ["itemDataCountIncludingContents"] = itemData.Count,
                    ["loadedChunkCount"] = chunks.Count,
                    ["bySource"] = Counts("sources", true), ["byDefinition"] = Counts("definitionId", false)
                },
                ["chunks"] = chunks,
                ["items"] = items,
                ["itemData"] = itemData,
                ["notes"] = new JArray
                {
                    "这是导出时本地 Editor 的已加载现场，不是 Profiler 所选历史帧的回放，也不代表远程 Player。",
                    "只采集已存在的运行态和已加载区块，不加载其它区块，不触发保存、暂停、录制或资源刷新。",
                    "items 是世界实体，itemData 还包含容器内物品；dataRef 指向 itemData[].ref，内容不重复计入世界实体数量。",
                    "同一 GUID 的机器、自然物或表现外壳合并为一条，sources 保留数据来源；生成点仅作为补充基线，不等于正在模拟或显示。",
                    "模块 JSON 状态直接展开，未通用解码的二进制模块保留 Base64；普通 Item 的模块持久状态可能尚未同步，runtimeScalars 和 liveInventories 记录当前运行值。",
                    "自然物内置热状态写入独立副本；扩展模块不执行保存回调，保留其最近已有状态。这份诊断 JSON 不提供直接加载还原存档的功能。"
                }
            };
        }

        #endregion

        #region 已加载实体与区块

        private void CaptureGameObjects(ItemMgr manager)
        {
            if (manager == null) return;
            foreach (Item item in manager.WorldRunTimeItems.Values.OrderBy(item => item != null ? item.GetInstanceID() : 0))
            {
                if (item == null || item.itemData == null || !item.gameObject.scene.isLoaded) continue;
                JObject entry = AddItem(item.itemData, "item-gameobject", item.transform.position);
                entry["gameObject"] = new JObject
                {
                    ["instanceId"] = item.GetInstanceID(), ["name"] = item.name,
                    ["scene"] = item.gameObject.scene.name, ["active"] = item.gameObject.activeInHierarchy,
                    ["enabled"] = item.enabled, ["initialized"] = item.IsInitialized,
                    ["updateInterval"] = item.updateInterval,
                    ["ownerGuid"] = item.Owner != null ? new JValue(item.Owner.itemData?.Guid) : JValue.CreateNull(),
                    ["rotation"] = Token(item.transform.rotation), ["scale"] = Token(item.transform.lossyScale)
                };
                var modules = new JArray();
                foreach (var pair in item.Mods.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    Module module = pair.Value;
                    if (module == null) continue;
                    var state = new JObject
                    {
                        ["name"] = pair.Key, ["type"] = module.GetType().FullName,
                        ["enabled"] = module.enabled, ["tickMode"] = module.TickMode.ToString(),
                        ["fixedTickInterval"] = module.FixedTickInterval,
                        ["runtimeScalars"] = RuntimeScalars(module)
                    };
                    if (module is Mod_Inventory inventory)
                    {
                        var live = new JObject();
                        foreach (var container in inventory.InventoryRefDic.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                            live[container.Key] = Inventory(container.Value?.Data);
                        state["liveInventories"] = live;
                    }
                    modules.Add(state);
                }
                entry["runtimeModules"] = modules;
            }
        }

        private void CaptureMachines(ChunkMgr manager)
        {
            var nodes = new List<MachineEntity>();
            MachineWorld.CollectDiagnosticNodes(nodes);
            foreach (MachineEntity node in nodes.OrderBy(node => node.Id))
            {
                if (node.Snapshot == null) continue;
                if (!node.Active && node.View == null &&
                    (manager == null || !manager.Chunks.ContainsKey(manager.ResolveWorldAddress(node.Position)))) continue;
                JObject entry = AddItem(node.Snapshot, "data-machine", node.Position);
                entry["machine"] = new JObject
                {
                    ["scope"] = node.ScopeKey, ["cell"] = Token(node.Cell), ["active"] = node.Active,
                    ["rpm"] = node.Rpm, ["engaged"] = node.Engaged, ["state"] = Token(node.State),
                    ["electricalSuppliedWatts"] = node.ElectricalSuppliedWatts,
                    ["electricalRequestedWatts"] = node.ElectricalRequestedWatts,
                    ["processing"] = node.Processor == null ? null : new JObject
                    {
                        ["station"] = node.Processor.Station, ["state"] = Token(node.Processor.State),
                        ["progress01"] = node.Processor.Progress01,
                        ["input"] = Inventory(node.Processor.Input?.Data), ["output"] = Inventory(node.Processor.Output?.Data)
                    }
                };
            }
        }

        private void CaptureResidents()
        {
            var residents = new List<AiecsResidentSaveData>();
            AiecsEcologyRuntimeHost.Active?.CopyDiagnosticResidents(residents);
            foreach (AiecsResidentSaveData resident in residents.OrderBy(data => data.Guid))
            {
                JObject entry = WorldItem(resident.Guid, resident.SpeciesId, "actor-ecs", new Vector3(resident.X, resident.Y));
                entry["actorState"] = Token(resident);
            }
        }

        private void CaptureChunks(ChunkMgr manager)
        {
            if (manager == null) return;
            foreach (ChunkRuntime chunk in manager.Chunks.Values.OrderBy(chunk => chunk.Address))
            {
                int present = 0;
                foreach (NaturalItemPlacement placement in chunk.Ecology.Placements)
                {
                    if (manager.IsNaturalItemRemoved(chunk.Address, placement.Guid)) continue;
                    present++;
                    // 生成基线只补未在运行集合里出现的身份，不能覆盖移动后的真实位置。
                    if (itemsByGuid.ContainsKey(placement.Guid)) continue;
                    var position = new Vector3(chunk.Address.ChunkOrigin.X + placement.LocalX + placement.OffsetX,
                        chunk.Address.ChunkOrigin.Y + placement.LocalY + placement.OffsetY);
                    WorldItem(placement.Guid, placement.ItemId, "loaded-chunk-placement", position)["placement"] = new JObject
                    {
                        ["chunk"] = chunk.Address.ToString(), ["ruleId"] = placement.RuleId,
                        ["hostGuid"] = placement.HostGuid, ["state"] = "generated-baseline-only"
                    };
                }
                chunks.Add(new JObject
                {
                    ["address"] = chunk.Address.ToString(), ["dimension"] = chunk.Address.DimensionId,
                    ["origin"] = new JArray(chunk.Address.ChunkOrigin.X, chunk.Address.ChunkOrigin.Y),
                    ["dataStatus"] = chunk.DataStatus.ToString(), ["simulationStatus"] = chunk.SimulationStatus.ToString(),
                    ["presentationStatus"] = chunk.PresentationStatus.ToString(),
                    ["simulationLeases"] = chunk.SimulationLeaseCount, ["presentationLeases"] = chunk.PresentationLeaseCount,
                    ["remainingPlacements"] = present
                });
            }
        }

        private JObject AddItem(ItemData data, string source, Vector3 position)
        {
            JObject entry = WorldItem(data.Guid, data.IDName, source, position);
            entry["dataRef"] = DataRef(data);
            return entry;
        }

        private JObject WorldItem(int guid, string definition, string source, Vector3 position)
        {
            if (guid != 0 && itemsByGuid.TryGetValue(guid, out JObject existing))
            {
                var sources = (JArray)existing["sources"];
                if (!sources.Values<string>().Contains(source)) sources.Add(source);
                existing["position"] = Token(position);
                return existing;
            }
            var entry = new JObject
            {
                ["guid"] = guid, ["definitionId"] = definition, ["sources"] = new JArray(source), ["position"] = Token(position)
            };
            items.Add(entry);
            if (guid != 0) itemsByGuid.Add(guid, entry);
            return entry;
        }

        #endregion

        #region 可读物品和库存状态

        private string DataRef(ItemData data)
        {
            if (data == null) return null;
            if (dataRefs.TryGetValue(data, out string reference)) return reference;
            reference = "data-" + (dataRefs.Count + 1);
            dataRefs.Add(data, reference);
            var entry = new JObject
            {
                ["ref"] = reference, ["guid"] = data.Guid, ["definitionId"] = data.IDName,
                ["name"] = data.GameName, ["type"] = data.GetType().FullName,
                ["amount"] = data.Stack?.Amount ?? 1f, ["durability"] = data.Durability,
                ["maxDurability"] = data.MaxDurability, ["inHand"] = data.inHand,
                ["faction"] = data.FactionId, ["tags"] = Token(data.Tags), ["matter"] = Token(data.MatterState),
                ["specialData"] = JsonOrText(data.ItemSpecialData)
            };
            itemData.Add(entry);
            var modules = new JArray();
            if (data.ModuleDataDic != null)
                foreach (var pair in data.ModuleDataDic.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    ModuleData module = pair.Value;
                    if (module == null) continue;
                    var state = new JObject
                    {
                        ["name"] = pair.Key, ["moduleId"] = module.ModuleId,
                        ["type"] = module.GetType().FullName, ["enabled"] = module.Enabled
                    };
                    switch (module)
                    {
                        case Inventory_ModuleData inventories:
                            var contents = new JObject();
                            if (inventories.Data != null)
                                foreach (var inventory in inventories.Data.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                                    contents[inventory.Key] = Inventory(inventory.Value);
                            state["inventories"] = contents;
                            break;
                        case Ex_ModData json:
                            state["state"] = JsonOrText(json.BitData);
                            break;
                        case Ex_ModData_MemoryPackable binary:
                            state["binaryStateBase64"] = binary.BitData == null ? null : Convert.ToBase64String(binary.BitData);
                            break;
                        default:
                            state["state"] = Token(module);
                            break;
                    }
                    modules.Add(state);
                }
            entry["modules"] = modules;
            return reference;
        }

        private JObject Inventory(Inventory_Data data)
        {
            if (data == null) return null;
            var slots = new JArray();
            if (data.itemSlots != null)
                for (int index = 0; index < data.itemSlots.Count; index++)
                {
                    ItemSlot slot = data.itemSlots[index];
                    if (slot?.itemData != null)
                        slots.Add(new JObject { ["index"] = index, ["dataRef"] = DataRef(slot.itemData) });
                }
            return new JObject { ["name"] = data.Name, ["slotCount"] = data.itemSlots?.Count ?? 0, ["occupiedSlots"] = slots };
        }

        private JObject RuntimeScalars(Module module)
        {
            var result = new JObject();
            foreach (FieldInfo field in module.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                Type type = field.FieldType;
                if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
                    type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector2Int))
                    result[field.Name] = Token(field.GetValue(module));
            }
            return result;
        }

        private JArray Counts(string field, bool array)
        {
            IEnumerable<string> keys = items.Cast<JObject>().SelectMany(item => array
                ? item[field].Values<string>() : new[] { (string)item[field] ?? "" });
            return new JArray(keys.GroupBy(key => key).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new JObject { ["name"] = group.Key, ["count"] = group.Count() }));
        }

        private JToken Token(object value) => value == null ? JValue.CreateNull() : JToken.FromObject(value, serializer);

        private static JToken JsonOrText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value == null ? JValue.CreateNull() : new JValue(value);
            try { return JToken.Parse(value); }
            catch (JsonReaderException) { return new JValue(value); }
        }

        // 仅展开纯数据字段，避免 Vector 的计算属性递归或意外读取 Unity 对象和事件。
        private sealed class DiagnosticFieldsResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization) =>
                type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                    .Where(field => !field.IsNotSerialized && !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType) &&
                        !typeof(Delegate).IsAssignableFrom(field.FieldType) &&
                        (field.FieldType.Namespace == null || !field.FieldType.Namespace.StartsWith("UltEvents", StringComparison.Ordinal)))
                    .Select(field => base.CreateProperty(field, MemberSerialization.Fields)).ToList();
        }

        private sealed class ItemReferenceConverter : JsonConverter
        {
            private readonly WorldSceneSnapshotExporter owner;
            public ItemReferenceConverter(WorldSceneSnapshotExporter owner) => this.owner = owner;
            public override bool CanConvert(Type objectType) => typeof(ItemData).IsAssignableFrom(objectType);
            public override bool CanRead => false;
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer jsonSerializer) =>
                writer.WriteValue(owner.DataRef((ItemData)value));
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer jsonSerializer) =>
                throw new NotSupportedException();
        }

        private sealed class InventoryConverter : JsonConverter
        {
            private readonly WorldSceneSnapshotExporter owner;
            public InventoryConverter(WorldSceneSnapshotExporter owner) => this.owner = owner;
            public override bool CanConvert(Type objectType) => typeof(Inventory_Data).IsAssignableFrom(objectType);
            public override bool CanRead => false;
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer jsonSerializer) =>
                owner.Inventory((Inventory_Data)value).WriteTo(writer);
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer jsonSerializer) =>
                throw new NotSupportedException();
        }

        #endregion
    }
}
