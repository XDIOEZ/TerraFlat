using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace FlatWorld.AIECS.Gameplay
{
    public sealed partial class AiecsPlayground
    {
        #region 开发场景生物战利品

        private readonly Dictionary<string, int> lootActorTemplates = new(StringComparer.Ordinal);

        /// <summary>
        /// 在开始场景时展开战利品引用的 Actor 闭包，保留前两军索引；新增生物与两军共享同一模拟、占格和表现目录。
        /// 开发入口会暂停正式生态宿主，因此死亡产出不能再转发给那个被暂停的宿主，也不能退回 Item。
        /// </summary>
        private void BuildScenarioCatalog(out string[] ids, out string[] factions, out bool[] fleePolicies)
        {
            var actorIds = new List<string> { TeamAActor, TeamBActor };
            var actorFactions = new List<string> { "aiecs.demo.a", "aiecs.demo.b" };
            var policies = new List<bool> { false, false };
            lootActorTemplates.Clear();
            lootActorTemplates[TeamAActor] = 0;
            if (!lootActorTemplates.ContainsKey(TeamBActor)) lootActorTemplates.Add(TeamBActor, 1);
            GameRes resources = GameRes.ExistingInstance;
            for (int i = 0; i < actorIds.Count; i++)
            {
                if (!resources.TryGetItemDefinition(actorIds[i], out RuntimeItemDefinition definition))
                    throw new InvalidOperationException("开发生物缺少定义：" + actorIds[i]);
                if (string.IsNullOrEmpty(definition.LootTableId)) continue;
                if (!resources.TryGetLootTable(definition.LootTableId, out RuntimeLootTable table))
                    throw new InvalidOperationException("开发生物缺少战利品表：" + definition.LootTableId);
                foreach (var entry in table.Entries)
                {
                    if (!resources.TryGetItemDefinition(entry.ItemId, out RuntimeItemDefinition produced))
                        throw new InvalidOperationException("战利品缺少定义：" + entry.ItemId);
                    if (!produced.IsActor || lootActorTemplates.ContainsKey(entry.ItemId)) continue;
                    bool passive = !AiecsDefinitionCompiler.TryValidate(entry.ItemId, false, out _);
                    if (passive && !AiecsDefinitionCompiler.TryValidate(entry.ItemId, true, out string reason))
                        throw new InvalidOperationException("生物战利品无法编译：" + entry.ItemId + "，" + reason);
                    lootActorTemplates.Add(entry.ItemId, actorIds.Count);
                    actorIds.Add(entry.ItemId);
                    actorFactions.Add(produced.CreateItemData().FactionId ?? string.Empty);
                    policies.Add(passive);
                }
            }
            ids = actorIds.ToArray(); factions = actorFactions.ToArray(); fleePolicies = policies.ToArray();
        }

        /// <summary>一次交付一个真实 Actor；位置由正式 Spawn 校验，拥挤时保留请求等待空格。</summary>
        private bool TrySpawnScenarioLootActor(string actorId, float2 position)
        {
            if (bridge == null || !lootActorTemplates.TryGetValue(actorId, out int template)) return false;
            for (int ring = 0; ring <= 3; ring++)
                for (int y = -ring; y <= ring; y++)
                    for (int x = -ring; x <= ring; x++)
                    {
                        if (math.max(math.abs(x), math.abs(y)) != ring) continue;
                        if (!bridge.Spawn(template, position + new float2(x, y))) continue;
                        spawned++;
                        return true;
                    }
            return false;
        }

        /// <summary>GM 显式验证罕见的生物产出分支；从当前战利品闭包选择，不改概率或伪造死亡。</summary>
        public void QueueActorLootTest()
        {
            if (bridge == null || player == null) return;
            foreach (var pair in lootActorTemplates)
            {
                if (pair.Value < 2) continue;
                bridge.QueueLoot(pair.Key, (Vector2)player.transform.position + Vector2.up * 2f, 1);
                SetStatus("已提交 1 个生物产出验收请求：" + pair.Key + "；等待真实占格与生成。未修改死亡数量或掉落概率。");
                return;
            }
            SetStatus("当前两军战利品表没有额外的生物产出定义。");
        }

        #endregion
    }
}
