using System.Collections.Generic;
using System;
using Newtonsoft.Json.Linq;

public sealed partial class Mod_WaterVessel
{
    #region 容器端口
    public List<ContainerPortConfiguration> ContainerPorts = new()
    { new ContainerPortConfiguration { Id = "liquids", Type = "core:liquid_vessel" } };
    private List<IContainerPort> livePorts;
    private uint portsGeneration;
    private long portRegistryGeneration;
    private List<ContainerPortConfiguration> observedPortConfiguration;
    public void CollectContainerPorts(List<IContainerPort> ports)
    {
        if (!IsRuntimeLoaded || !Enabled) return;
        if (livePorts == null || portsGeneration != RuntimeGeneration || portRegistryGeneration != ContainerPortFactoryRegistry.Generation || !ReferenceEquals(observedPortConfiguration, ContainerPorts))
        {
            livePorts = new(); portsGeneration = RuntimeGeneration; observedPortConfiguration = ContainerPorts;
            portRegistryGeneration = ContainerPortFactoryRegistry.Generation;
            var configurations = ContainerPorts;
            uint generation = RuntimeGeneration; Item owner = item; uint ownerGeneration = owner.RuntimeGeneration;
            foreach (var configuration in ContainerPorts)
                livePorts.Add(ContainerPortFactoryRegistry.Create(configuration.Type,
                    new ContainerPortFactoryContext(owner, this, null, this, configuration,
                        () => this != null && IsRuntimeLoaded && RuntimeGeneration == generation && item == owner && owner != null &&
                              !owner.DestructionHandled && owner.RuntimeGeneration == ownerGeneration && ReferenceEquals(ContainerPorts, configurations))));
        }
        ports.AddRange(livePorts);
    }
    public override void ApplyResourceConfiguration(string itemId, string moduleName, string json)
    { base.ApplyResourceConfiguration(itemId, moduleName, json); livePorts = null; }
    public static List<ContainerPortConfiguration> ResolvePortConfigurations(ItemData data)
    {
        if (data == null || GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out var definition)) return new();
        foreach (var declaration in definition.ModuleDefinitions)
        {
            if (!declaration.Enabled || declaration.ModuleId != ModuleId) continue;
            if (!string.IsNullOrWhiteSpace(declaration.ParametersJson))
            {
                JToken token = JObject.Parse(declaration.ParametersJson).GetValue(nameof(ContainerPorts), StringComparison.OrdinalIgnoreCase);
                if (token != null) return ContainerPortConfigurationValidator.Read(token);
            }
            if (definition.TryGetModuleAssembly(declaration.StableName, out var assembly))
            {
                var authoring = assembly.ModulePrefab != null ? assembly.ModulePrefab.GetComponentInChildren<Mod_WaterVessel>(true) :
                    definition.ShellPrefab != null ? assembly.ResolveEmbeddedModule(definition.ShellPrefab.transform) as Mod_WaterVessel : null;
                if (authoring != null) return authoring.ContainerPorts;
            }
            return new() { new ContainerPortConfiguration { Id = "liquids", Type = "core:liquid_vessel" } };
        }
        return new();
    }
    #endregion
}
