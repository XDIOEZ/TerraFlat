using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>档案身份与运行时角色一对一关联，控制权和网络权威不写入角色数据。</summary>
public static class ActorProfileRegistry
{
    #region 档案身份与运行态索引
    public sealed class Binding
    {
        public Item Actor { get; internal set; }
        public string ProfileId { get; internal set; }
        public bool LocalProfile { get; internal set; }
        public bool WasCreated { get; internal set; }
        public bool ManualControl { get; internal set; }
        internal IDisposable ControlLease;
        internal bool ControlIsLocal;
        public bool Terminating { get; internal set; }
        public event Action Changed;
        internal void PublishChanged() => Changed?.Invoke();
    }

    private static ConditionalWeakTable<Item, Binding> bindings = new();
    public static Dictionary<string, Item> ProfileActors { get; } = new(StringComparer.Ordinal);
    public static event Action<Item> ControlChanged;
    public static Item LocalControlledActor { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeBindings()
    {
        bindings = new();
        ProfileActors.Clear();
        ControlChanged = null;
        LocalControlledActor = null;
    }

    public static Binding GetBinding(Item actor) => bindings.GetValue(actor, value => new Binding { Actor = value });
    public static bool IsProfileBound(Item actor)
        => actor != null && bindings.TryGetValue(actor, out var value) && !string.IsNullOrEmpty(value.ProfileId);
    public static bool IsProfileActorData(ItemData data)
        => ProfileActors.Values.Any(actor => actor != null && ReferenceEquals(actor.itemData, data)) ||
           SaveDataMgr.GetExistingInstance()?.SaveData?.ProfileActorData?.Values.Any(value => ReferenceEquals(value, data)) == true;
    public static string GetProfileId(Item actor)
        => actor != null && bindings.TryGetValue(actor, out var value) ? value.ProfileId : null;
    public static bool IsManuallyControlled(Item actor)
        => actor != null && bindings.TryGetValue(actor, out var value) && value.ManualControl && !value.Terminating;
    public static bool IsLocallyControlled(Item actor)
        => IsManuallyControlled(actor) && GetBinding(actor).ControlIsLocal;
    public static bool WasProfileCreated(Item actor) => actor != null && bindings.TryGetValue(actor, out var b) && b.WasCreated;
    public static bool IsNewLocalProfile(Item actor) => IsLocallyControlled(actor) && WasProfileCreated(actor);

    public static void BindProfile(Item actor, bool localProfile, bool profileDataWasCreated, string runtimeProfileName = null)
    {
        Binding binding = GetBinding(actor);
        string id = string.IsNullOrWhiteSpace(runtimeProfileName) ? binding.ProfileId : runtimeProfileName.Trim();
        if (string.IsNullOrWhiteSpace(id)) id = "actor:" + actor.GetInstanceID();
        if (binding.ProfileId != null && binding.ProfileId != id && ProfileActors.TryGetValue(binding.ProfileId, out var old) && old == actor)
            ProfileActors.Remove(binding.ProfileId);
        binding.ProfileId = id;
        binding.LocalProfile = localProfile;
        binding.WasCreated = profileDataWasCreated;
        binding.Terminating = false;
        ProfileActors[id] = actor;
        ItemMgr.GetExistingInstance()?.RemoveRuntimeAiEntity(actor);
        MonsterManager.GetForWorld(actor.gameObject.scene.name, false)?.Unregister(actor);
        var control = actor.GetComponentInChildren<Mod_ActorControl>(true);
        if (control != null)
        {
            binding.ControlLease?.Dispose();
            control.TryAcquireManualControl(binding, localProfile, out binding.ControlLease);
        }
        else SetManualControl(actor, true, localControl: localProfile);
        binding.PublishChanged();
    }

    internal static void SetManualControl(Item actor, bool enabled, bool terminating = false, bool localControl = false)
    {
        Binding binding = GetBinding(actor);
        if (enabled && localControl && LocalControlledActor != null && LocalControlledActor != actor)
            LocalControlledActor.GetComponentInChildren<Mod_ActorControl>(true)?.ReleaseManualControl();
        if (LocalControlledActor == actor && (!enabled || !localControl)) LocalControlledActor = null;
        if (enabled && localControl) LocalControlledActor = actor;
        binding.ControlIsLocal = localControl;
        binding.ManualControl = enabled;
        binding.Terminating = terminating;
        actor.GetComponentInChildren<Mod_ActorControl>(true)?.ApplyControlState(enabled, terminating);
        ActorControlModuleLifecycle.RefreshLocalModules(actor);
        binding.PublishChanged();
        ControlChanged?.Invoke(actor);
        actor.MarkModuleScheduleDirty();
    }

    public static void UnbindProfile(Item actor)
    {
        if (actor == null || !bindings.TryGetValue(actor, out var binding)) return;
        SetManualControl(actor, false, terminating: true);
        if (binding.ProfileId != null && ProfileActors.TryGetValue(binding.ProfileId, out var current) && current == actor)
            ProfileActors.Remove(binding.ProfileId);
        bindings.Remove(actor);
    }

    public static Item GetProfileActorOrNull(Item actor) => IsProfileBound(actor) ? actor : null;
    public static bool TryGetProfileActor(Component component, out Item actor)
    { actor = GetProfileActorOrNull(component != null ? component.GetComponent<Item>() : null); return actor != null; }
    public static bool TryGetProfileActor(GameObject gameObject, out Item actor)
    { actor = GetProfileActorOrNull(gameObject != null ? gameObject.GetComponent<Item>() : null); return actor != null; }
    public static Item FindProfileActor() => ProfileActors.Values.FirstOrDefault(actor => actor != null);
    public static Item[] GetProfileActors() => ProfileActors.Values.Where(actor => actor != null).ToArray();

    // 编辑态夹具同样显式绑定身份，不再通过测试专用实体子类模拟玩家。
    public static Item CreateProfileActorForFixture(GameObject root)
    {
        var actor = root.AddComponent<GameItem>();
        BindProfile(actor, false, false, "fixture:" + root.GetInstanceID());
        return actor;
    }
    #endregion
}
