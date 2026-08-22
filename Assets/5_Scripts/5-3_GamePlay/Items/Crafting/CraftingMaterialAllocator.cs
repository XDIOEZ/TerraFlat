using System;
using System.Collections.Generic;

/// <summary>
/// 配方材料的统一判定入口；集中处理空槽、精确物品、标签与数量语义。
/// </summary>
public static class CraftingIngredientMatcher
{
    public const float AmountEpsilon = 0.0001f;

    /// <summary>判断配方槽是否为空槽占位。</summary>
    public static bool IsEmpty(RuntimeRecipeIngredient ingredient)
    {
        return ingredient == null ||
               ingredient.amount <= 0 &&
               string.IsNullOrWhiteSpace(ingredient.ItemName) &&
               string.IsNullOrWhiteSpace(ingredient.Tag);
    }

    /// <summary>只判断物品身份，不检查数量。</summary>
    public static bool MatchesIdentity(RuntimeRecipeIngredient required, ItemData actual)
    {
        if (required == null || actual == null)
            return false;

        if (required.matchMode == MatchMode.ByTag)
        {
            string requiredTag = required.Tag?.Trim();
            if (string.IsNullOrEmpty(requiredTag) || actual.Tags == null)
                return false;

            for (int index = 0; index < actual.Tags.Count; index++)
            {
                if (string.Equals(actual.Tags[index]?.Trim(), requiredTag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        return string.Equals(
            actual.IDName?.Trim(),
            required.ItemName?.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>判断单个槽位是否完整满足有序配方槽。</summary>
    public static bool Matches(RuntimeRecipeIngredient required, ItemData actual)
    {
        if (IsEmpty(required))
            return actual == null;
        if (!MatchesIdentity(required, actual))
            return false;
        return required.amount <= 0 ||
               actual.Stack != null && actual.Stack.Amount + AmountEpsilon >= required.amount;
    }
}

/// <summary>
/// 无序配方材料分配器；通过容量流一次求出全局可行的扣料计划，避免 Exact/Tag 重叠时贪心误判。
/// 输入与配方网格最多为 3×3，流网络规模固定且很小。
/// </summary>
public static class CraftingMaterialAllocator
{
    /// <summary>为全部非空材料生成按输入槽聚合的扣料计划。</summary>
    public static bool TryAllocate(
        IReadOnlyList<ItemSlot> inputSlots,
        IReadOnlyList<RuntimeRecipeIngredient> ingredients,
        out List<CraftingConsumption> consumptions)
    {
        consumptions = new List<CraftingConsumption>();
        if (inputSlots == null || ingredients == null)
            return false;

        var consumingIngredients = new List<RuntimeRecipeIngredient>();
        float totalDemand = 0f;
        for (int ingredientIndex = 0; ingredientIndex < ingredients.Count; ingredientIndex++)
        {
            RuntimeRecipeIngredient ingredient = ingredients[ingredientIndex];
            if (CraftingIngredientMatcher.IsEmpty(ingredient))
                continue;

            if (!HasMatchingIdentity(inputSlots, ingredient))
                return false;

            // amount=0 是必须存在但不消耗的催化物/工具，只参与身份签名。
            if (ingredient.amount <= 0)
                continue;

            consumingIngredients.Add(ingredient);
            totalDemand += ingredient.amount;
        }

        if (consumingIngredients.Count == 0)
            return true;

        int sourceNode = 0;
        int ingredientNodeStart = 1;
        int slotNodeStart = ingredientNodeStart + consumingIngredients.Count;
        int sinkNode = slotNodeStart + inputSlots.Count;
        var network = new CapacityFlowNetwork(sinkNode + 1);
        var allocationEdges = new List<AllocationEdge>();

        for (int ingredientIndex = 0; ingredientIndex < consumingIngredients.Count; ingredientIndex++)
        {
            RuntimeRecipeIngredient ingredient = consumingIngredients[ingredientIndex];
            int ingredientNode = ingredientNodeStart + ingredientIndex;
            network.AddEdge(sourceNode, ingredientNode, ingredient.amount);

            for (int slotIndex = 0; slotIndex < inputSlots.Count; slotIndex++)
            {
                ItemData itemData = inputSlots[slotIndex]?.itemData;
                if (!CraftingIngredientMatcher.MatchesIdentity(ingredient, itemData) ||
                    itemData?.Stack == null ||
                    itemData.Stack.Amount <= CraftingIngredientMatcher.AmountEpsilon)
                {
                    continue;
                }

                CapacityFlowEdge edge = network.AddEdge(ingredientNode, slotNodeStart + slotIndex, totalDemand);
                allocationEdges.Add(new AllocationEdge(slotIndex, edge));
            }
        }

        for (int slotIndex = 0; slotIndex < inputSlots.Count; slotIndex++)
        {
            float available = inputSlots[slotIndex]?.itemData?.Stack?.Amount ?? 0f;
            network.AddEdge(slotNodeStart + slotIndex, sinkNode, Math.Max(0f, available));
        }

        float allocated = network.CalculateMaxFlow(sourceNode, sinkNode);
        if (allocated + CraftingIngredientMatcher.AmountEpsilon < totalDemand)
            return false;

        var consumedBySlot = new float[inputSlots.Count];
        for (int edgeIndex = 0; edgeIndex < allocationEdges.Count; edgeIndex++)
        {
            AllocationEdge allocation = allocationEdges[edgeIndex];
            float amount = allocation.Edge.OriginalCapacity - allocation.Edge.Capacity;
            if (amount > CraftingIngredientMatcher.AmountEpsilon)
                consumedBySlot[allocation.SlotIndex] += amount;
        }

        for (int slotIndex = 0; slotIndex < consumedBySlot.Length; slotIndex++)
        {
            if (consumedBySlot[slotIndex] > CraftingIngredientMatcher.AmountEpsilon)
                consumptions.Add(new CraftingConsumption(slotIndex, consumedBySlot[slotIndex]));
        }

        return true;
    }

    #region 身份预检

    /// <summary>确认催化物或消耗材料至少有一个身份候选。</summary>
    private static bool HasMatchingIdentity(
        IReadOnlyList<ItemSlot> inputSlots,
        RuntimeRecipeIngredient ingredient)
    {
        for (int slotIndex = 0; slotIndex < inputSlots.Count; slotIndex++)
        {
            if (CraftingIngredientMatcher.MatchesIdentity(ingredient, inputSlots[slotIndex]?.itemData))
                return true;
        }

        return false;
    }

    #endregion

    #region 小型容量流网络

    /// <summary>配方材料边到输入槽的映射，用于还原最终扣料计划。</summary>
    private readonly struct AllocationEdge
    {
        public AllocationEdge(int slotIndex, CapacityFlowEdge edge)
        {
            SlotIndex = slotIndex;
            Edge = edge;
        }

        public int SlotIndex { get; }
        public CapacityFlowEdge Edge { get; }
    }

    /// <summary>带反向残量边的容量流边。</summary>
    private sealed class CapacityFlowEdge
    {
        public CapacityFlowEdge(int target, int reverseIndex, float capacity)
        {
            Target = target;
            ReverseIndex = reverseIndex;
            Capacity = capacity;
            OriginalCapacity = capacity;
        }

        public int Target { get; }
        public int ReverseIndex { get; }
        public float Capacity { get; set; }
        public float OriginalCapacity { get; }
    }

    /// <summary>使用 Dinic 算法求解小规模浮点容量网络。</summary>
    private sealed class CapacityFlowNetwork
    {
        private readonly List<CapacityFlowEdge>[] edges;
        private readonly int[] levels;
        private readonly int[] nextEdges;

        public CapacityFlowNetwork(int nodeCount)
        {
            edges = new List<CapacityFlowEdge>[nodeCount];
            levels = new int[nodeCount];
            nextEdges = new int[nodeCount];
            for (int index = 0; index < nodeCount; index++)
                edges[index] = new List<CapacityFlowEdge>();
        }

        public CapacityFlowEdge AddEdge(int source, int target, float capacity)
        {
            var forward = new CapacityFlowEdge(target, edges[target].Count, Math.Max(0f, capacity));
            var reverse = new CapacityFlowEdge(source, edges[source].Count, 0f);
            edges[source].Add(forward);
            edges[target].Add(reverse);
            return forward;
        }

        public float CalculateMaxFlow(int source, int sink)
        {
            float result = 0f;
            while (BuildLevels(source, sink))
            {
                for (int index = 0; index < nextEdges.Length; index++)
                    nextEdges[index] = 0;

                while (true)
                {
                    float pushed = PushFlow(source, sink, float.MaxValue);
                    if (pushed <= CraftingIngredientMatcher.AmountEpsilon)
                        break;
                    result += pushed;
                }
            }

            return result;
        }

        private bool BuildLevels(int source, int sink)
        {
            for (int index = 0; index < levels.Length; index++)
                levels[index] = -1;

            var queue = new Queue<int>();
            levels[source] = 0;
            queue.Enqueue(source);
            while (queue.Count > 0)
            {
                int node = queue.Dequeue();
                for (int edgeIndex = 0; edgeIndex < edges[node].Count; edgeIndex++)
                {
                    CapacityFlowEdge edge = edges[node][edgeIndex];
                    if (edge.Capacity <= CraftingIngredientMatcher.AmountEpsilon || levels[edge.Target] >= 0)
                        continue;

                    levels[edge.Target] = levels[node] + 1;
                    queue.Enqueue(edge.Target);
                }
            }

            return levels[sink] >= 0;
        }

        private float PushFlow(int node, int sink, float available)
        {
            if (node == sink)
                return available;

            for (; nextEdges[node] < edges[node].Count; nextEdges[node]++)
            {
                CapacityFlowEdge edge = edges[node][nextEdges[node]];
                if (edge.Capacity <= CraftingIngredientMatcher.AmountEpsilon ||
                    levels[edge.Target] != levels[node] + 1)
                {
                    continue;
                }

                float pushed = PushFlow(edge.Target, sink, Math.Min(available, edge.Capacity));
                if (pushed <= CraftingIngredientMatcher.AmountEpsilon)
                    continue;

                edge.Capacity -= pushed;
                edges[edge.Target][edge.ReverseIndex].Capacity += pushed;
                return pushed;
            }

            return 0f;
        }
    }

    #endregion
}
