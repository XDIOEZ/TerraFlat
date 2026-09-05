using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Gameplay.Quests;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>本体资源的组合根；新增目录只需注册独立阶段及依赖，不再扩展中心加载协程。</summary>
public partial class GameRes
{
    #region 加载计划

    /// <summary>权重表示阶段工作占比，只有最后的引用校验结束后才发布目录。</summary>
    private ResourceLoadPipeline CreateResourceLoadPlan()
    {
        var plan = new ResourceLoadPipeline((title, progress) =>
        {
            loadingText = title;
            loadingProgress = Mathf.Max(loadingProgress, Mathf.Clamp(progress, 0, 0.99f));
        });
        List<ItemDefinitionDto> itemSources = null;
        plan.Add("addressables", "初始化资源目录", 2, InitializeAddressableCatalog);
        plan.Add("item-manifest", "解析物品清单", 3,
            () => LoadCatalog<List<ItemDefinitionDto>>((done, fail) =>
                ItemDefinitionCatalogLoader.LoadBuiltInDefinitionsAsync(done, fail, plan.Report), value => itemSources = value), "addressables");
        plan.Add("players", "加载玩家创建配置", 1,
            () => LoadCatalog<PlayerCreationTemplateCatalogConfig>(PlayerCreationTemplateJsonLoader.LoadBuiltInAsync,
                PlayerCreationTemplateCatalogService.ReplaceBuiltIn));
        plan.Add("time", "加载时间系统配置", 1,
            () => LoadCatalog<TimeSystemConfigCatalog>(TimeSystemConfigLoader.LoadBuiltInAsync, TimeSystemConfigService.ReplaceCatalog));
        plan.Add("loot", "加载战利品表", 1,
            () => LoadCatalog<IReadOnlyList<RuntimeLootTable>>(LootTableCatalogLoader.LoadBuiltInAsync,
                tables => { foreach (RuntimeLootTable table in tables) RegisterLootTable(table); }));
        plan.Add("prefabs", "加载运行时预制体", 20, () => LoadPrefabCatalog(itemSources), "item-manifest");
        plan.Add("tiles", "加载地形图块", 3, () => LoadAssetGroup<TileBase>("TileBase", tileBaseDict, tile => tile.name, true), "addressables");
        plan.Add("tile-blocks", "加载逻辑地块", 3, () => LoadAssetGroup<Tile_Block>("TileBlock", TileBlockDict, tile => tile.name, true), "tiles");
        plan.Add("inventory", "加载初始库存", 2, () => LoadAssetGroup<Inventoryinit>("InventoryInit", InventoryInitDict, asset => asset.name, true), "addressables");
        plan.Add("skills", "加载技能资源", 2, () => LoadAssetGroup<BaseSkill>("Skill", SkillDict, asset => asset.name, true), "addressables");
        plan.Add("items", "构建物品定义", 25,
            () => LoadCatalog<int>((done, fail) => ItemDefinitionCatalogLoader.LoadBuiltInAsync(this, done, fail, plan.Report, itemSources), _ => { }),
            "prefabs", "loot", "tile-blocks");
        plan.Add("actors", "构建生物定义", 15,
            () => LoadCatalog<int>((done, fail) => ActorDefinitionCatalogLoader.LoadBuiltInAsync(this, done, fail, plan.Report), _ => { }), "items");
        plan.Add("animal-skills", "加载动物技能", 1,
            () => LoadCatalog<AnimalSkillCatalog>(AnimalSkillCatalogLoader.LoadBuiltInAsync, AnimalSkillCatalogService.Replace), "actors");
        plan.Add("spawners", "加载生物生成配置", 1,
            () => LoadCatalog<SpawnerConfigCatalog>(SpawnerConfigCatalogLoader.LoadBuiltInAsync, SpawnerConfigCatalogService.ReplaceCatalog), "actors");
        plan.Add("recipes", "加载制作配方", 3,
            () => LoadCatalog<int>((done, fail) => RecipeCatalogLoader.LoadBuiltInAsync(this, done, fail), _ => { }), "items");
        plan.Add("buffs", "加载状态效果", 2,
            () => LoadCatalog<int>((done, fail) => BuffCatalogLoader.LoadBuiltInAsync(this, done, fail), _ => { }), "items");
        plan.Add("quests", "加载任务目录", 2,
            () => LoadCatalog<int>(QuestCatalogLoader.LoadBuiltInAsync, _ => { }), "items");
        plan.Add("texts", "加载文字库", 1,
            () => LoadCatalog<TextLibraryService>(TextLibraryCatalogLoader.LoadBuiltInAsync, value => textLibraryService = value));
        plan.Add("validate-built-in", "校验本体资源引用", 3,
            () => RunAction(() => ResourceCatalogValidation.Validate(this)),
            "players", "time", "items", "actors", "animal-skills", "spawners", "recipes", "buffs", "quests", "texts", "inventory", "skills");
        plan.Add("mods", "加载扩展内容", 5, () => LoadModCatalog(plan), "validate-built-in");
        plan.Add("validate-final", "校验最终资源目录", 3,
            () => RunAction(() =>
            {
                ResourceCatalogValidation.Validate(this);
                GameManager.Instance?.ApplyDefaultTimeSystemProfile();
            }), "mods");
        return plan;
    }

    #endregion

    #region 目录加载契约

    /// <summary>将目录加载器的回调转换为统一异常语义；漏报完成与返回空目录都属于错误。</summary>
    private static IEnumerator LoadCatalog<T>(Func<Action<T>, Action<Exception>, IEnumerator> load, Action<T> register)
    {
        T result = default;
        bool completed = false;
        Exception error = null;
        yield return load(value => { result = value; completed = true; }, exception => error = exception);
        if (error != null) throw new InvalidDataException("目录加载失败：" + error.Message, error);
        if (!completed || result == null) throw new InvalidDataException($"目录加载器 {typeof(T).Name} 未返回有效结果。");
        register(result);
    }

    /// <summary>同步注册与校验仍纳入阶段异常和进度管理。</summary>
    private static IEnumerator RunAction(Action action) { action(); yield break; }

    /// <summary>扩展在本体通过校验后接入，扩展完成后还要校验最终目录。</summary>
    private IEnumerator LoadModCatalog(ResourceLoadPipeline plan)
    {
        ModRuntimeManager mods = ModRuntimeManager.Ensure(gameObject);
        yield return mods.LoadEnabledMods(this, (_, progress) => plan.Report(progress));
        if (!mods.IsReady) throw new InvalidDataException("MOD 加载失败：" + mods.FailureReason);
    }

    #endregion
}
