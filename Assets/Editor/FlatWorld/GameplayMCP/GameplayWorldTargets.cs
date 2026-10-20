using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.NaturalEntities;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.GameplayMCP
{
    /// <summary>把当前已加载后端投影为同一目标句柄，动作执行前重新校验世界和真实实体身份。</summary>
    internal static class GameplayWorldTargets
    {
        #region 目标投影与会话边界
        internal sealed class Target
        {
            internal string Backend, ItemId;
            internal int Guid;
            internal Vector3 Position, Aim; // 统一为规范逻辑世界坐标，表现镜像不进入句柄契约。
            internal float Amount = 1f;
            internal bool Pickup;
            internal Vector2? Health;
            internal IInteractable Interaction;
            internal Func<bool> Refresh;
        }
        private sealed class Binding
        {
            internal Player Player;
            internal uint PlayerGeneration;
            internal int Scene;
            internal GameplayWorldRuntime World;
            internal Target Target;
        }
        private const int MaximumBindings = 256;
        private static readonly Dictionary<string, Binding> bindings = new(StringComparer.Ordinal);
        private static readonly Queue<string> issued = new();
        private static readonly string session = System.Guid.NewGuid().ToString("N");
        private static ulong sequence;

        internal static string Bind(Target target, Player player)
        {
            while (issued.Count >= MaximumBindings) bindings.Remove(issued.Dequeue());
            string token = session + ":" + (++sequence).ToString("x");
            GameplayWorldRegistry.TryGet(player.gameObject.scene.name, out GameplayWorldRuntime world);
            bindings[token] = new Binding { Player = player, PlayerGeneration = player.RuntimeGeneration,
                Scene = SceneManager.GetActiveScene().handle, World = world, Target = target };
            issued.Enqueue(token);
            return token;
        }

        internal static bool TryResolve(string token, Player player, out Target target, out string error)
        {
            target = null;
            if (player == null || player.DestructionHandled)
            { error = "当前玩家已失效，请重新查询 source=world。"; return false; }
            GameplayWorldRegistry.TryGet(player.gameObject.scene.name, out GameplayWorldRuntime world);
            if (string.IsNullOrWhiteSpace(token) || !bindings.TryGetValue(token, out Binding binding) ||
                binding.Player != player || binding.PlayerGeneration != player.RuntimeGeneration ||
                binding.Scene != SceneManager.GetActiveScene().handle || !ReferenceEquals(binding.World, world))
            { error = "目标句柄已过期或不属于当前玩家/世界，请重新查询 source=world。"; return false; }
            using var worldScope = world != null ? GameplayWorldRegistry.UseWorld(world.WorldKey) : null;
            if (binding.Target.Refresh?.Invoke() != true)
            { bindings.Remove(token); error = "目标已删除、卸载、换代或离开观察范围，请重新查询 source=world。"; return false; }
            target = binding.Target;
            error = null;
            return true;
        }
        #endregion

        #region 当前世界只读查询
        internal static object Query(Player player, JObject parameters, HashSet<string> identities)
        {
            float radius;
            int limit, offset;
            bool? pickup;
            bool details;
            try
            {
                radius = parameters.Value<float?>("radius") ?? 16f;
                limit = Mathf.Clamp(parameters.Value<int?>("limit") ?? 3, 1, 32);
                offset = Mathf.Max(0, parameters.Value<int?>("offset") ?? 0);
                pickup = parameters.Value<bool?>("pickup");
                details = parameters.Value<bool?>("includeDetails") ?? false;
            }
            catch (Exception exception) when (exception is FormatException || exception is InvalidCastException || exception is OverflowException || exception is ArgumentException)
            { return new ErrorResponse("invalid_world_query: radius/limit/offset/pickup/includeDetails 参数类型无效。"); }
            if (!float.IsFinite(radius) || radius <= 0f)
                return new ErrorResponse("invalid_world_query: radius 必须是大于零的有限数。");
            radius = Mathf.Clamp(radius, .1f, 64f);
            string tag = parameters.Value<string>("tag")?.Trim();
            bool filterIdentity = !string.IsNullOrWhiteSpace(parameters.Value<string>("itemId")) ||
                !string.IsNullOrWhiteSpace(parameters.Value<string>("query"));
            var all = new List<Target>();
            var dedupe = new HashSet<string>(StringComparer.Ordinal);
            using var worldScope = GameplayWorldRegistry.TryGet(player.gameObject.scene.name, out _)
                ? GameplayWorldRegistry.UseWorld(player.gameObject.scene.name) : null;
            void Add(Target target)
            {
                if (filterIdentity && !identities.Contains(target.ItemId ?? string.Empty) ||
                    pickup.HasValue && target.Pickup != pickup.Value ||
                    WorldTopologyRuntime.Distance(player.transform.position, target.Position) > radius) return;
                if (!string.IsNullOrEmpty(tag) && (GameRes.ExistingInstance == null ||
                    !GameRes.ExistingInstance.TryGetItemDefinition(target.ItemId, out RuntimeItemDefinition definition) ||
                    !definition.HasTag(tag))) return;
                if (dedupe.Add(target.Guid + ":" + target.ItemId)) all.Add(target);
            }

            var machines = new List<MachineEntity>();
            MachineWorld.CollectDiagnosticNodes(machines);
            foreach (MachineEntity machine in machines)
                if (machine?.Snapshot != null && machine.ScopeKey == player.gameObject.scene.name)
                { Target target = Machine(machine); if (target.Refresh()) Add(target); }
            var natural = new List<NaturalEntityTargetObservation>();
            NaturalEntityEcsService.QueryTargetObservations(player, radius, natural);
            foreach (NaturalEntityTargetObservation observation in natural)
            { Target target = Natural(observation); if (target.Refresh()) Add(target); }
            ItemMgr itemMgr = ItemMgr.GetExistingInstance();
            if (itemMgr != null)
                foreach (Item item in GameplayQueryTool.GetRuntimeCandidates(itemMgr, player, radius))
                {
                    // 登记和回收边界允许短暂没有数据，不能据此建立可操作句柄。
                    if (item == null || item.itemData == null) continue;
                    Target target = Runtime(item, player); if (target.Refresh()) Add(target);
                }
            var drops = new List<DroppedItemObservation>();
            DroppedItemService.QueryNearbyLightweightDrops(player.transform.position, radius, drops);
            foreach (DroppedItemObservation observation in drops)
                if (DroppedItemService.TryGetObservationHandle(observation.Id, out DroppedItemHandle handle))
                    Add(Drop(observation, handle));

            Target[] ordered = all.OrderBy(value => WorldTopologyRuntime.Distance(player.transform.position, value.Position))
                .ThenBy(value => value.Backend, StringComparer.Ordinal).ThenBy(value => value.Guid).ToArray();
            var result = new JArray();
            foreach (Target target in ordered.Skip(offset).Take(limit))
            {
                var entry = new JObject { ["target"] = Bind(target, player), ["backend"] = target.Backend,
                    ["guid"] = target.Guid, ["id"] = target.ItemId,
                    ["position"] = new JArray(Round(target.Position.x), Round(target.Position.y)),
                    ["distance"] = Round(WorldTopologyRuntime.Distance(player.transform.position, target.Position)),
                    ["amount"] = Round(target.Amount), ["pickup"] = target.Pickup,
                    ["interactable"] = target.Interaction?.CanInteract(player) == true };
                if (details)
                {
                    entry["name"] = GameplayMcpRuntime.ResolveItemDisplayName(target.ItemId, target.ItemId);
                    entry["hp"] = target.Health.HasValue ? new JArray(Round(target.Health.Value.x), Round(target.Health.Value.y)) : null;
                }
                result.Add(entry);
            }
            bool truncated = offset + result.Count < ordered.Length;
            return new SuccessResponse("FlatWorld loaded world targets.", new { source = "world", radius,
                total_count = ordered.Length, offset, returned_count = result.Count, limit, truncated,
                next_offset = truncated ? offset + result.Count : (int?)null, matches = result });
        }
        #endregion

        #region 后端实时解析
        private static Target Runtime(Item item, Player player)
        {
            uint generation = item.RuntimeGeneration;
            ItemData data = item.itemData;
            var target = new Target { Backend = "item", Guid = data.Guid, ItemId = data.IDName };
            target.Refresh = () =>
            {
                if (item == null || item.RuntimeGeneration != generation || !ReferenceEquals(item.itemData, data) ||
                    item.DestructionHandled || item.InHand || !item.gameObject.activeInHierarchy ||
                    item.gameObject.scene != player.gameObject.scene) return false;
                target.Position = WorldLocalPresentation.ToLogical(item.transform.position);
                target.Aim = WorldLocalPresentation.ToLogical(GameplayMcpRuntime.ResolveActorAimPoint(item));
                target.Amount = data.Stack?.Amount ?? 1f;
                target.Pickup = data.Stack?.CanBePickedUp == true;
                target.Interaction = item.GetComponentsInChildren<MonoBehaviour>(true).OfType<IInteractable>()
                    .FirstOrDefault(value => value.CanInteract(player));
                Mod_DamageReceiver health = item.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
                target.Health = health == null ? null : new Vector2(health.Hp, health.MaxHp);
                return true;
            };
            return target;
        }

        private static Target Natural(NaturalEntityTargetObservation initial)
        {
            var target = new Target { Backend = "natural", Guid = initial.Target.TargetGuid, ItemId = initial.ItemId };
            target.Refresh = () =>
            {
                if (!NaturalEntityEcsService.TryGetTargetObservation(initial.Handle, out NaturalEntityTargetObservation current) ||
                    !ReferenceEquals(initial.Target, current.Target)) return false;
                target.Position = WorldLocalPresentation.ToLogical(current.Target.WorldPosition);
                target.Aim = current.AimBounds.size.sqrMagnitude > 0f
                    ? WorldTopologyRuntime.NormalizePosition(current.AimBounds.center) : target.Position;
                target.Interaction = current.Target;
                target.Health = current.HasHealth ? current.Health : null;
                return true;
            };
            return target;
        }

        private static Target Machine(MachineEntity node)
        {
            string scopeKey = node.ScopeKey;
            var target = new Target { Backend = "machine", Guid = node.Id, ItemId = node.Snapshot.IDName };
            target.Refresh = () =>
            {
                if (node.ScopeKey != scopeKey || !MachineWorld.Contains(node) || ChunkMgr.ExistingInstance == null ||
                    !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(node.Position, out _)) return false;
                target.Position = target.Aim = WorldLocalPresentation.ToLogical(node.Position);
                target.Interaction = MachineWorld.GetOrCreateInteractionTarget(node);
                return target.Interaction != null;
            };
            return target;
        }

        private static Target Drop(DroppedItemObservation initial, DroppedItemHandle handle)
        {
            var target = new Target { Backend = "drop", Guid = initial.Id, ItemId = initial.ItemId,
                Position = WorldLocalPresentation.ToLogical(initial.Position), Aim = WorldLocalPresentation.ToLogical(initial.Position), Amount = initial.Amount, Pickup = initial.Pickable };
            target.Refresh = () =>
            {
                if (!handle.IsValid) return false;
                var observations = new List<DroppedItemObservation>();
                DroppedItemService.QueryNearbyLightweightDrops(target.Position, 64f, observations);
                foreach (DroppedItemObservation current in observations)
                    if (current.Id == handle.Id && current.ItemId == target.ItemId)
                    {
                        target.Position = target.Aim = WorldLocalPresentation.ToLogical(current.Position);
                        target.Amount = current.Amount; target.Pickup = current.Pickable;
                        return true;
                    }
                return false;
            };
            return target;
        }

        private static float Round(float value) => (float)Math.Round(value, 3, MidpointRounding.AwayFromZero);
        #endregion
    }
}
