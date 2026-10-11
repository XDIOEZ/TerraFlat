using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>权威雷击只处理真实落点和正式生命入口，闪光与电弧由表现层订阅同一事件。</summary>
public static class LightningStrikeSystem
{
    #region 会话与命中去重
    private static readonly Dictionary<Int2, int> LastSequences = new();
    private static GameSaveData activeSave;
    private static object activeTerrain;
    private static int activeSceneHandle;
    private static string activeWorld;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        LastSequences.Clear();
        activeSave = null; activeTerrain = null; activeWorld = null; activeSceneHandle = 0;
    }

    private static bool Reserve(in RegionalLightningEvent strike)
    {
        GameSaveData save = SaveDataMgr.Instance?.SaveData;
        object terrain = ChunkMgr.ExistingInstance?.WorldRuntime;
        int sceneHandle = SceneManager.GetActiveScene().handle;
        if (!ReferenceEquals(activeSave, save) || !ReferenceEquals(activeTerrain, terrain) ||
            activeSceneHandle != sceneHandle || !string.Equals(activeWorld, strike.WorldKey, StringComparison.Ordinal))
        {
            Reset();
            activeSave = save; activeTerrain = terrain; activeSceneHandle = sceneHandle; activeWorld = strike.WorldKey;
        }
        if (LastSequences.TryGetValue(strike.Region, out int previous) && strike.Sequence == previous) return false;
        LastSequences[strike.Region] = strike.Sequence;
        return true;
    }

    // 环境身份包含事件种类、真实世界、区块和序号，避免与其它区块或爆炸串号。
    private static ulong EventId(in RegionalLightningEvent strike)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char value in "flatworld.lightning:" + strike.WorldKey) Mix(value);
        Mix(unchecked((uint)strike.Region.X)); Mix(unchecked((uint)strike.Region.Y)); Mix(unchecked((uint)strike.Sequence));
        return hash == 0 ? 1UL : hash;
        void Mix(uint value) { hash = unchecked((hash ^ value) * 1099511628211UL); }
    }
    #endregion

    #region 权威落点伤害
    public static void Apply(RegionalLightningEvent strike, float radius, float peakDamage)
    {
        if (!GameNetwork.HasStateAuthority || string.IsNullOrWhiteSpace(strike.WorldKey) || strike.Sequence <= 0 ||
            !string.Equals(SceneManager.GetActiveScene().name, strike.WorldKey, StringComparison.Ordinal) ||
            !WorldAddress.FromWorldKey(strike.WorldKey).IsSurface ||
            SaveDataMgr.Instance?.SaveData?.PlanetData_Dict?.ContainsKey(strike.WorldKey) != true ||
            WeatherMgr.IsWeatherSuppressedInDimension(DimensionManager.ExistingInstance?.ActiveDefinition) ||
            !float.IsFinite(radius) || radius <= 0f || !float.IsFinite(peakDamage) || peakDamage <= 0f ||
            !float.IsFinite(strike.Position.x) || !float.IsFinite(strike.Position.y) || !Reserve(strike)) return;
        ulong eventId = EventId(strike);
        Vector2 center = WorldTopologyRuntime.NormalizePosition(strike.Position);
        ApplyItemDamage(strike.WorldKey, eventId, center, radius, peakDamage);

        CombatIdentity source = new() { Backend = CombatBackend.Environment, Value = eventId };
        CombatDamageContext context = new()
        {
            Attack = new CombatAttackKey { Source = source, Sequence = unchecked((uint)strike.Sequence), Pulse = 1 },
            Credit = source, Damage = new float4(0f, 0f, 0f, peakDamage), Origin = center, HitPoint = center,
            ResourceToolEfficiency = 1f, BuildingMultiplier = 1f,
            Clock = new CombatClock { Tick = (ulong)Time.frameCount, Time = Time.timeAsDouble, DeltaTime = Time.deltaTime }
        };
        GameplayCombatBridge.ApplyEnvironmentalDamage(strike.WorldKey, context, radius);
        FlatWorld.NaturalEntities.NaturalEntityEcsService.ApplyEnvironmentalDamage(strike.WorldKey, eventId, center, radius, peakDamage);
        FlatWorld.Spaceflight.SpaceSurfaceQuery.ApplyEnvironmentalDamageInRadius(strike.WorldKey, center, radius, peakDamage, eventId);
    }

    private static void ApplyItemDamage(string world, ulong eventId, Vector2 center, float radius, float peakDamage)
    {
        ItemMgr items = ItemMgr.Instance;
        if (items == null) return;
        var targets = new HashSet<Item>(items.WorldRunTimeItems.Values);
        foreach (Player player in items.Player_DIC.Values) targets.Add(player);
        foreach (Item target in targets)
        {
            if (target == null || target.itemData == null || target.Owner != null || target.InHand || target.PersistenceOwner != null ||
                target.DestructionHandled || !target.gameObject.activeInHierarchy || target.gameObject.scene.name != world) continue;
            if (MachineWorld.OwnsWorldItem(target.itemData) ||
                Mod_Building.TryReadBuildingData(target.itemData, out _, out var building) && building.Role == BuildingRole.PlacedBuilding) continue;
            Vector2 position = WorldLocalPresentation.ToLogical((Vector2)target.transform.position);
            float distance = WorldTopologyRuntime.Distance(center, position);
            if (distance >= radius) continue;
            target.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.HurtEnvironmentalDamage(eventId, center,
                peakDamage * (1f - distance / radius));
        }
    }

    #endregion
}
