using System;
using System.Collections.Generic;
using System.IO;

/// <summary>扩展注册返回资源会话租约，失败和乱序卸载不会留下半份定义。</summary>
public static class FluidContentRegistry
{
    #region MOD 原子注册
    private sealed class Registration : IDisposable
    {
        private readonly FluidCatalog fluids;
        private readonly AtmosphereCatalog atmospheres;
        private readonly List<FluidDefinition> fluidDefinitions;
        private readonly List<AtmosphereDefinition> atmosphereDefinitions;
        private bool disposed;
        public Registration(FluidCatalog fluids, AtmosphereCatalog atmospheres,
            List<FluidDefinition> fluidDefinitions, List<AtmosphereDefinition> atmosphereDefinitions)
        { this.fluids = fluids; this.atmospheres = atmospheres; this.fluidDefinitions = fluidDefinitions; this.atmosphereDefinitions = atmosphereDefinitions; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = atmosphereDefinitions.Count - 1; i >= 0; i--) atmospheres.Unregister(atmosphereDefinitions[i]);
            for (int i = fluidDefinitions.Count - 1; i >= 0; i--) fluids.Unregister(fluidDefinitions[i]);
        }
    }
    public static IDisposable Register(IEnumerable<FluidDefinitionDto> fluidDtos, IEnumerable<AtmosphereDefinitionDto> atmosphereDtos,
        string ownerModId, GameRes resources = null)
    {
        if (string.IsNullOrWhiteSpace(ownerModId)) throw new ArgumentException("MOD 流体注册必须提供所属 MOD ID", nameof(ownerModId));
        var fluidDefinitions = new List<FluidDefinition>();
        foreach (var dto in fluidDtos ?? Array.Empty<FluidDefinitionDto>()) fluidDefinitions.Add(new FluidDefinition(dto));
        foreach (var value in fluidDefinitions)
            if (value.LiquidId != null && resources != null && resources.TryGetLiquidDefinition(value.LiquidId, out var liquid) &&
                value.LitersPerServing != (decimal)liquid.LitersPerServing)
                throw new InvalidDataException($"流体 {value.Id} 与液体 {value.LiquidId} 的每份升数冲突");
        var fluids = FluidCatalog.Default; var atmospheres = AtmosphereCatalog.Default;
        fluids.Register(fluidDefinitions, ownerModId, resources == null ? null : id => resources.TryGetLiquidDefinition(id, out _));
        try
        {
            var atmosphereDefinitions = atmospheres.Register(atmosphereDtos, fluids, ownerModId);
            return new Registration(fluids, atmospheres, fluidDefinitions, atmosphereDefinitions);
        }
        catch
        {
            foreach (var definition in fluidDefinitions) fluids.Unregister(definition);
            throw;
        }
    }
    #endregion
}
