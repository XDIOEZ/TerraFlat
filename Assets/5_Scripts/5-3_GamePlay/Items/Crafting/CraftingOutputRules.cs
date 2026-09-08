using System.Collections.Generic;

/// <summary>在库存事务预检前生成产物的派生状态；规则只修改新产物，不消费输入。</summary>
public interface ICraftingOutputRule
{
    bool Prepare(Inventory input, CraftingRecipeMatch match, IReadOnlyList<ItemData> outputs, out string error);
}

/// <summary>制作产物状态扩展点；食品等业务自行注册，通用制作服务不依赖具体玩法。</summary>
public static class CraftingOutputRules
{
    private static readonly SortedDictionary<string, ICraftingOutputRule> Rules = new(); // 稳定执行顺序。
    /// <summary>同一身份重复初始化时替换规则。</summary>
    public static void Register(string id, ICraftingOutputRule rule) => Rules[id] = rule;
    /// <summary>预览与提交使用同一状态推导，失败时输入保持原样。</summary>
    public static bool Prepare(Inventory input, CraftingRecipeMatch match, IReadOnlyList<ItemData> outputs, out string error)
    {
        error = null;
        foreach (ICraftingOutputRule rule in Rules.Values)
            if (!rule.Prepare(input, match, outputs, out error)) return false;
        return true;
    }
}
