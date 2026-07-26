// AI-Context: Item/Module 音频适配层；把业务事件名映射到全局 AudioCue ID，并只保存需要跨存档恢复的循环状态。

using System;
using System.Collections.Generic;
using FlatWorld.Audio;
using MemoryPack;
using UnityEngine;

[Serializable]
public sealed class ItemAudioBinding
{
    [Tooltip("模块内事件名，例如 act / hit / break")]
    public string EventId;

    [Tooltip("AudioCatalog 中的稳定 ID，例如 item.axe.hit")]
    public string CueId;

    [Tooltip("3D 声音是否持续跟随当前 Item")]
    public bool FollowItem = true;

    [Tooltip("是否覆盖 AudioCue 自身的循环设置")]
    public bool OverrideLoop;

    public bool Loop;

    [Tooltip("循环声是否在 Item 存档中记录并在加载后恢复")]
    public bool PersistWhileSaved;

    [Min(0f)] public float FadeIn;
    [Min(0f)] public float FadeOut = 0.08f;
    [Min(0.01f)] public float VolumeScale = 1f;
    [Min(0.01f)] public float PitchScale = 1f;
}

[MemoryPackable]
[Serializable]
public partial class ItemAudioEmitterSaveData
{
    public List<string> ActivePersistentEvents = new List<string>();
}

[DisallowMultipleComponent]
public sealed class Mod_AudioEmitter : Module, IItemPoolLifecycle
{
    public const string ModuleId = "Mod_AudioEmitter";

    [SerializeField] private Ex_ModData_MemoryPackable modSaveData = new Ex_ModData_MemoryPackable();
    [SerializeField] private List<ItemAudioBinding> bindings = new List<ItemAudioBinding>();
    [SerializeField] private bool playOnItemAct = true;
    [SerializeField] private string itemActEvent = "act";

    private readonly Dictionary<string, ItemAudioBinding> bindingByEvent =
        new Dictionary<string, ItemAudioBinding>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<AudioHandle>> handlesByEvent =
        new Dictionary<string, List<AudioHandle>>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> warnedMissingEvents =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private Item boundItem;

    public override ModuleData _Data
    {
        get => modSaveData;
        set => modSaveData = value as Ex_ModData_MemoryPackable ?? new Ex_ModData_MemoryPackable();
    }

    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    public override void Awake()
    {
        if (modSaveData == null)
            modSaveData = new Ex_ModData_MemoryPackable();
        if (string.IsNullOrWhiteSpace(modSaveData.ID))
            modSaveData.ID = ModuleId;

        base.Awake();
    }

    public override void Load()
    {
        RebuildBindings();
        BindItemEvents();

        ItemAudioEmitterSaveData saveData = new ItemAudioEmitterSaveData();
        modSaveData.ReadData(ref saveData);
        if (saveData?.ActivePersistentEvents == null)
            return;

        for (int i = 0; i < saveData.ActivePersistentEvents.Count; i++)
            PlayEvent(saveData.ActivePersistentEvents[i]);
    }

    public override void Save()
    {
        ItemAudioEmitterSaveData saveData = new ItemAudioEmitterSaveData();
        foreach (KeyValuePair<string, ItemAudioBinding> pair in bindingByEvent)
        {
            if (!pair.Value.PersistWhileSaved ||
                !handlesByEvent.TryGetValue(pair.Key, out List<AudioHandle> handles))
            {
                continue;
            }

            PruneStopped(handles);
            if (handles.Count > 0)
                saveData.ActivePersistentEvents.Add(pair.Key);
        }

        modSaveData.WriteData(saveData);
    }

    public override void Act()
    {
        PlayEvent(itemActEvent);
        base.Act();
    }

    /// <summary>
    /// 给其他 Module、动画事件和 UltEvent 使用的统一入口。
    /// </summary>
    public AudioHandle PlayEvent(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId) ||
            !bindingByEvent.TryGetValue(eventId, out ItemAudioBinding binding) ||
            string.IsNullOrWhiteSpace(binding.CueId))
        {
            if (!string.IsNullOrWhiteSpace(eventId) && warnedMissingEvents.Add(eventId))
                Debug.LogWarning($"[Mod_AudioEmitter] {name} 未配置音频事件：{eventId}", this);
            return AudioHandle.Invalid;
        }

        Transform followTarget = item != null ? item.transform : transform;
        AudioPlayOptions options = binding.FollowItem
            ? AudioPlayOptions.Attached(followTarget, binding.VolumeScale, binding.PitchScale)
            : AudioPlayOptions.At(followTarget.position, binding.VolumeScale, binding.PitchScale);
        options.FadeIn = binding.FadeIn;
        options.OverrideLoop = binding.OverrideLoop;
        options.Loop = binding.Loop;

        AudioHandle handle = AudioService.Instance.Play(binding.CueId, options);
        if (!handle.IsValid)
            return handle;

        if (!handlesByEvent.TryGetValue(binding.EventId, out List<AudioHandle> handles))
        {
            handles = new List<AudioHandle>(2);
            handlesByEvent.Add(binding.EventId, handles);
        }

        PruneStopped(handles);
        handles.Add(handle);
        return handle;
    }

    public void PlayItemAct()
    {
        PlayEvent(itemActEvent);
    }

    public void StopEvent(string eventId)
    {
        if (!handlesByEvent.TryGetValue(eventId, out List<AudioHandle> handles))
            return;

        float fadeOut = bindingByEvent.TryGetValue(eventId, out ItemAudioBinding binding)
            ? binding.FadeOut
            : 0f;

        for (int i = 0; i < handles.Count; i++)
            handles[i].Stop(fadeOut);
        handles.Clear();
    }

    public void StopAllOwned()
    {
        foreach (KeyValuePair<string, List<AudioHandle>> pair in handlesByEvent)
        {
            float fadeOut = bindingByEvent.TryGetValue(pair.Key, out ItemAudioBinding binding)
                ? binding.FadeOut
                : 0f;

            List<AudioHandle> handles = pair.Value;
            for (int i = 0; i < handles.Count; i++)
                handles[i].Stop(fadeOut);
            handles.Clear();
        }
    }

    public void OnItemTakenFromPool()
    {
        StopAllOwned();
        UnbindItemEvents();
    }

    public void OnItemReturnedToPool()
    {
        StopAllOwned();
        UnbindItemEvents();
    }

    private void OnDestroy()
    {
        StopAllOwned();
        UnbindItemEvents();
    }

    private void HandleItemAct()
    {
        if (playOnItemAct)
            PlayEvent(itemActEvent);
    }

    private void HandleItemDestroy(Item destroyedItem)
    {
        StopAllOwned();
        UnbindItemEvents();
    }

    private void BindItemEvents()
    {
        UnbindItemEvents();
        if (item == null)
            return;

        boundItem = item;
        boundItem.OnAct.DynamicCalls += HandleItemAct;
        boundItem.OnItemDestroy.DynamicCalls += HandleItemDestroy;
    }

    private void UnbindItemEvents()
    {
        if (boundItem == null)
            return;

        boundItem.OnAct.DynamicCalls -= HandleItemAct;
        boundItem.OnItemDestroy.DynamicCalls -= HandleItemDestroy;
        boundItem = null;
    }

    private void RebuildBindings()
    {
        bindingByEvent.Clear();
        warnedMissingEvents.Clear();

        for (int i = 0; i < bindings.Count; i++)
        {
            ItemAudioBinding binding = bindings[i];
            if (binding == null)
                continue;

            binding.EventId = binding.EventId == null ? string.Empty : binding.EventId.Trim();
            binding.CueId = binding.CueId == null ? string.Empty : binding.CueId.Trim();
            if (string.IsNullOrWhiteSpace(binding.EventId))
                continue;

            if (bindingByEvent.ContainsKey(binding.EventId))
            {
                Debug.LogWarning($"[Mod_AudioEmitter] 重复事件配置：{binding.EventId}，采用第一项", this);
                continue;
            }

            bindingByEvent.Add(binding.EventId, binding);
        }
    }

    private static void PruneStopped(List<AudioHandle> handles)
    {
        for (int i = handles.Count - 1; i >= 0; i--)
        {
            if (!handles[i].IsPlaying)
                handles.RemoveAt(i);
        }
    }

    private void OnValidate()
    {
        if (modSaveData == null)
            modSaveData = new Ex_ModData_MemoryPackable();
        modSaveData.ID = ModuleId;
    }
}
