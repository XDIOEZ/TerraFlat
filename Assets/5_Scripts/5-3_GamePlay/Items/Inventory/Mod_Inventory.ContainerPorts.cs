using System;
using System.Collections.Generic;

public partial class Mod_Inventory : IContainerPortProvider
{
    #region 显式库存端口
    public List<ContainerPortConfiguration> ContainerPorts = new();
    private List<IContainerPort> liveContainerPorts;
    private uint portGeneration;
    private long portRegistryGeneration;
    private List<ContainerPortConfiguration> observedPortConfiguration;
    public void CollectContainerPorts(List<IContainerPort> ports)
    {
        if (!IsRuntimeLoaded || !Enabled || ContainerPorts == null || ContainerPorts.Count == 0) return;
        if (liveContainerPorts == null || portGeneration != RuntimeGeneration || portRegistryGeneration != ContainerPortFactoryRegistry.Generation || !ReferenceEquals(observedPortConfiguration, ContainerPorts))
        {
            liveContainerPorts = new(); portGeneration = RuntimeGeneration; observedPortConfiguration = ContainerPorts;
            portRegistryGeneration = ContainerPortFactoryRegistry.Generation;
            var configurations = ContainerPorts;
            uint generation = RuntimeGeneration; Item owner = item; uint ownerGeneration = owner.RuntimeGeneration;
            foreach (var configuration in ContainerPorts)
            {
                if (string.IsNullOrWhiteSpace(configuration.InventoryId) || !InventoryRefDic.TryGetValue(configuration.InventoryId, out Inventory target))
                    throw new InvalidOperationException($"库存端口 {configuration.Id} 引用了不存在的库存：{configuration.InventoryId}");
                liveContainerPorts.Add(ContainerPortFactoryRegistry.Create(configuration.Type,
                    new ContainerPortFactoryContext(owner, this, target, null, configuration,
                        () => this != null && IsRuntimeLoaded && RuntimeGeneration == generation && item == owner && owner != null &&
                              !owner.DestructionHandled && owner.RuntimeGeneration == ownerGeneration && InventoryRefDic.ContainsValue(target) && ReferenceEquals(ContainerPorts, configurations))));
            }
        }
        ports.AddRange(liveContainerPorts);
    }
    public override void ApplyResourceConfiguration(string itemId, string moduleName, string json)
    { base.ApplyResourceConfiguration(itemId, moduleName, json); liveContainerPorts = null; }
    #endregion
}
