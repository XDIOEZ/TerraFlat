# 美术参考样张归档

这些文件是画风评审或生成流程用的参考样张，不参与游戏运行时加载。目录按原来的 Environment、Equipment、Farming 分类整理，PNG 与对应 `.meta` 一并保存。

7 张样图的 PNG 与下表正式资源逐项 SHA-256 相同。归档的 `.meta` 保留样张自己的 GUID 和导入设置；正式图仍使用 Assets 中原有的 PNG 与 `.meta`。两者导入设置并不完全相同，尤其最大导入尺寸、Pivot、PPU、Sprite 网格或物理轮廓设置；请勿把归档 `.meta` 覆盖到正式资源上。

| 分类 | 归档样张（GUID） | 对应正式资源 | 已核对的导入差异 |
| --- | --- | --- | --- |
| Environment | [Mine_Coal_Grounded_Tall_Sample.png](Environment/MineralStyleSamples_20261023/Mine_Coal_Grounded_Tall_Sample.png) (`81b212c204bd4fb18d5dc7d048a1554e`) | [Mine_Coal_Idle.png](../../../Assets/6_Art/Generated/Environment/MineralsUnified/Mine_Coal/Mine_Coal_Idle.png) | Sprite alignment、物理轮廓生成 |
| Equipment | [IronGuard_Cuirass_Handcrafted_StyleSample_20261023.png](Equipment/IronGuard_Cuirass/IronGuard_Cuirass_Handcrafted_StyleSample_20261023.png) (`847fb2b3a26249eb84e0980868f6a416`) | [IronGuard_Cuirass_Icon.png](../../../Assets/6_Art/Generated/Equipment/IronGuard_Cuirass/IronGuard_Cuirass_Icon.png) | 最大导入尺寸（2048 / 128） |
| Equipment | [Spacesuit_Head_Handcrafted_StyleSample_20261023.png](Equipment/Spacesuit_Head/Spacesuit_Head_Handcrafted_StyleSample_20261023.png) (`d55376a6aa6c4f938771a3c5b757828a`) | [Spacesuit_Head_Icon.png](../../../Assets/6_Art/Generated/Equipment/Spacesuit_Head/Spacesuit_Head_Icon.png) | 最大导入尺寸（2048 / 128） |
| Equipment | [Traveler_Hood_Handcrafted_StyleSample_20261023.png](Equipment/Traveler_Hood/Traveler_Hood_Handcrafted_StyleSample_20261023.png) (`37368cd1962f4772b0a2d117994b554e`) | [Traveler_Hood_Icon.png](../../../Assets/6_Art/Generated/Equipment/Traveler_Hood/Traveler_Hood_Icon.png) | 最大导入尺寸（2048 / 128） |
| Farming | [BerryCrop_Mature_LowPixelSample.png](Farming/PlantStyleSamplesV2/BerryCrop_Mature_LowPixelSample.png) (`54317c89f75146b4b5fe488cd1feaa80`) | [BerryCrop_Mature.png](../../../Assets/6_Art/Generated/Farming/PlantSet_LowPixel_20261023/BerryCrop/BerryCrop_Mature.png) | 最大尺寸、Pivot、PPU、Sprite 网格 |
| Farming | [RadishCrop_Mature_LowPixelSample.png](Farming/PlantStyleSamplesV2/RadishCrop_Mature_LowPixelSample.png) (`e1c38d7f704747a1920ac5fb4e5baa12`) | [RadishCrop_Mature.png](../../../Assets/6_Art/Generated/Farming/PlantSet_LowPixel_20261023/RadishCrop/RadishCrop_Mature.png) | 最大尺寸、Pivot、Sprite 网格 |
| Farming | [RiceCrop_Mature_LowPixelSample.png](Farming/PlantStyleSamplesV2/RiceCrop_Mature_LowPixelSample.png) (`a669448c3ff94ad59f0651cf0d3ed60c`) | [RiceCrop_Mature.png](../../../Assets/6_Art/Generated/Farming/PlantSet_LowPixel_20261023/RiceCrop/RiceCrop_Mature.png) | 最大尺寸、Pivot、Sprite 网格 |

归档 `.meta` 保留样张各自 GUID；这些 GUID 未被 Assets 内的运行时资源或 Addressables 配置引用，Addressables 使用表中正式资源的 GUID。需要恢复样张时，将对应 PNG 与 `.meta` 成对复制到临时位置。