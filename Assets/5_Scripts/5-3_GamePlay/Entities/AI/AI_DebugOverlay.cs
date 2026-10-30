using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using UnityEngine;

/// <summary>拥有详细诊断标签的能力独占该生物的头顶显示。</summary>
public interface IActorDebugOverlayOwner { }

public static class AI_DebugOverlay
{
	#region 集中绘制
	private static readonly Dictionary<MonoBehaviour, Action<Camera>> sources = new();
	private static readonly Dictionary<MonoBehaviour, Action<Camera>> hiveSources = new();
	private static AI_DebugOverlayRenderer renderer;
	public static bool Visible { get; private set; }
	internal static string TextLocale { get; private set; }

	/// <summary>游戏面板打开时暂避让头顶调试文字，关闭面板后自动恢复原开关。</summary>
	public static bool CanDrawWorldLabels
	{
		get
		{
			UIManager manager = UIManager.ExistingInstance;
			return manager == null ||
			    (!manager.HasOpenGamepadNavigationPanel() &&
			     !manager.HasOpenGameplayInputBlockingPanel() &&
			     !PlayerMobileControlsHUD.IsActiveDrawerOpen);
		}
	}

	public static void SetVisible(bool visible)
	{
		Visible = visible;
		RefreshRenderer();
	}

	public static bool Toggle()
	{
		SetVisible(!Visible);
		return Visible;
	}

	public static void Register(MonoBehaviour source, Action<Camera> draw)
	{
		sources[source] = draw;
		RefreshRenderer();
	}

	public static void Unregister(MonoBehaviour source)
	{
		sources.Remove(source);
		RefreshRenderer(false);
	}

	/// <summary>蜂巢调试使用同一个绘制入口，但保留独立的 GM 开关。</summary>
	public static void RegisterHive(MonoBehaviour source, Action<Camera> draw)
	{
		hiveSources[source] = draw;
		RefreshRenderer();
	}

	public static void UnregisterHive(MonoBehaviour source)
	{
		hiveSources.Remove(source);
		RefreshRenderer(false);
	}

	public static void RefreshVisibility() => RefreshRenderer();

	private static void RefreshRenderer(bool allowCreation = true)
	{
		// 只有任一开关对应的来源需要绘制时才启用唯一的 IMGUI 入口。
		bool needsRenderer = Visible && sources.Count > 0 ||
		    HiveColonyDebugOverlay.Visible && hiveSources.Count > 0;
		if (allowCreation && renderer == null && needsRenderer)
		{
			GameObject host = new GameObject("[AI Debug Overlay]");
			PersistentRuntimeObjectHierarchy.MoveToPersistentCategory(host, PersistentRuntimeObjectCategory.Debug);
			renderer = host.AddComponent<AI_DebugOverlayRenderer>();
		}
		if (renderer != null)
			renderer.enabled = needsRenderer;
	}

	public static void Draw()
	{
		bool drawActors = Visible && sources.Count > 0;
		bool drawHives = HiveColonyDebugOverlay.Visible && hiveSources.Count > 0;
		if ((!drawActors && !drawHives) || !Application.isPlaying || !CanDrawWorldLabels)
			return;
		Camera camera = Camera.main;
		if (camera == null)
			return;
		TextLocale = FlatWorldLocalizationService.CurrentLocaleCode;
		if (drawActors)
			foreach (var source in sources)
				if (source.Key != null && source.Key.isActiveAndEnabled)
					source.Value(camera);
		if (drawHives)
			foreach (var source in hiveSources)
				if (source.Key != null && source.Key.isActiveAndEnabled)
					source.Value(camera);
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetRuntime()
	{
		sources.Clear();
		hiveSources.Clear();
		renderer = null;
		Visible = false;
		TextLocale = null;
	}
	#endregion

	#region 调试文字复用
	// 只在显示值、语言或物品代际改变时重新格式化，仍逐帧读取真实状态。
	internal struct TextCache<TKey> where TKey : struct
	{
		private TKey key;
		private string locale;
		private uint generation;
		private string text;

		public bool TryGet(TKey current, string currentLocale, uint currentGeneration, out string cached)
		{
			cached = text;
			return text != null && locale == currentLocale && generation == currentGeneration &&
				EqualityComparer<TKey>.Default.Equals(key, current);
		}

		public string Store(TKey current, string currentLocale, uint currentGeneration, string value)
		{
			key = current;
			locale = currentLocale;
			generation = currentGeneration;
			return text = value;
		}
	}

	internal sealed class BuffTextCache
	{
		private readonly List<(string Name, string Id, bool Permanent, int Seconds)> states = new();
		private readonly List<string> texts = new();
		private string locale;
		private string text = string.Empty;

		public string GetText(Dictionary<string, BuffInstance> buffs, string currentLocale)
		{
			bool changed = locale != currentLocale;
			int count = 0;
			if (buffs != null)
				foreach (BuffInstance runtime in buffs.Values)
				{
					if (runtime?.Definition == null || runtime.IsExpired)
						continue;
					var current = (runtime.Definition.DisplayName, runtime.DefinitionId,
						runtime.Definition.IsPermanent,
						runtime.Definition.IsPermanent ? 0 :
							Mathf.CeilToInt(Mathf.Max(0f, runtime.RemainingDurationSeconds)));
					if (count == states.Count)
					{
						states.Add(current);
						changed = true;
					}
					else if (!states[count].Equals(current))
					{
						states[count] = current;
						changed = true;
					}
					count++;
				}
			if (count < states.Count)
			{
				states.RemoveRange(count, states.Count - count);
				changed = true;
			}
			if (!changed)
				return text;

			locale = currentLocale;
			texts.Clear();
			foreach (var state in states)
			{
				string buffName = string.IsNullOrWhiteSpace(state.Name)
					? state.Id : FlatWorldLocalizationService.GetUiText(state.Name);
				string duration = state.Permanent
					? FlatWorldLocalizationService.GetUiText("永久") : $"{state.Seconds}s";
				texts.Add(FlatWorldLocalizationService.GetUiFormat("{0} ({1})", buffName, duration));
			}
			return text = texts.Count == 0 ? string.Empty :
				FlatWorldLocalizationService.GetUiFormat(" | Buff: {0}", string.Join(", ", texts));
		}

		public void Clear()
		{
			states.Clear();
			texts.Clear();
			locale = null;
			text = string.Empty;
		}
	}
	#endregion
}
