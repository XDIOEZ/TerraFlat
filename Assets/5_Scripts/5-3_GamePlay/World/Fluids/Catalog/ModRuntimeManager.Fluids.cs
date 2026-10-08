using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed partial class ModRuntimeManager
{
    #region 工业流体与大气内容
    private sealed class PendingFluidContent
    {
        public string OwnerModId;
        public string File;
        public List<FluidDefinitionDto> Fluids = new();
        public List<AtmosphereDefinitionDto> Atmospheres = new();
    }
    private List<PendingFluidContent> pendingFluidContents = new();
    private List<IDisposable> registeredFluidContent = new();
    private void QueueFluidDefinitions(ModPackage package, string file, JObject document)
    {
        if (document["fluids"] == null && document["atmospheres"] == null) return;
        var pending = new PendingFluidContent { OwnerModId = package.Manifest.Id, File = file };
        if (document["fluids"] != null)
        {
            if (document["fluids"] is not JArray fluidArray) throw new InvalidDataException($"MOD {pending.OwnerModId} 的 fluids 必须为数组：{file}");
            foreach (var token in fluidArray)
            {
                var dto = FluidCatalogLoader.DeserializeDefinition(token);
                if (!FluidDefinition.ValidateId(dto.Id).StartsWith(pending.OwnerModId + ":", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"MOD {pending.OwnerModId} 的流体必须使用自己的命名空间：{dto.Id}");
                pending.Fluids.Add(dto);
            }
        }
        if (document["atmospheres"] != null)
        {
            if (document["atmospheres"] is not JArray atmosphereArray) throw new InvalidDataException($"MOD {pending.OwnerModId} 的 atmospheres 必须为数组：{file}");
            foreach (var token in atmosphereArray)
            {
                var dto = token.ToObject<AtmosphereDefinitionDto>(JsonSerializer.Create(FluidCatalogLoader.Settings));
                if (dto == null || !FluidDefinition.ValidateId(dto.Id).StartsWith(pending.OwnerModId + ":", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"MOD {pending.OwnerModId} 的大气必须使用自己的命名空间：{dto?.Id}");
                pending.Atmospheres.Add(dto);
            }
        }
        pendingFluidContents.Add(pending);
    }
    private void ProcessFluidDefinitions(GameRes resources)
    {
        try
        {
            // 同包的多个文件先合并，避免文件顺序导致大气引用不到自身气体。
            var byMod = new Dictionary<string, PendingFluidContent>(StringComparer.OrdinalIgnoreCase);
            var order = new List<PendingFluidContent>();
            foreach (var pending in pendingFluidContents)
            {
                if (!byMod.TryGetValue(pending.OwnerModId, out var merged))
                { merged = new PendingFluidContent { OwnerModId = pending.OwnerModId }; byMod.Add(merged.OwnerModId, merged); order.Add(merged); }
                merged.Fluids.AddRange(pending.Fluids); merged.Atmospheres.AddRange(pending.Atmospheres);
            }
            foreach (var pending in order)
                registeredFluidContent.Add(FluidContentRegistry.Register(pending.Fluids, pending.Atmospheres, pending.OwnerModId, resources));
        }
        catch
        {
            UnloadFluidDefinitions(); throw;
        }
    }
    private void UnloadFluidDefinitions()
    {
        for (int i = registeredFluidContent.Count - 1; i >= 0; i--) registeredFluidContent[i].Dispose();
        registeredFluidContent.Clear(); pendingFluidContents.Clear();
    }
    private void ConfigureFluidResourceReload(ResourceReloadContext context)
    {
        context.AddList(() => pendingFluidContents, value => pendingFluidContents = value);
        context.AddList(() => registeredFluidContent, value => registeredFluidContent = value);
    }
    #endregion
}
