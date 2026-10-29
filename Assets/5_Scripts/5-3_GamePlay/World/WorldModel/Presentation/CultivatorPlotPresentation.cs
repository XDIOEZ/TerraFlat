using System;
using UnityEngine;

/// <summary>培育仓共用的只读图层布局，地面网格和船上 Sprite 使用同一套槽位坐标。</summary>
internal sealed class CultivatorPlotPresentation
{
    #region 当前植株与机身坐标
    private MachineEntity entity;
    private MachineContent content;
    private CultivatorState state;
    private CultivatorVisualConfiguration configuration;
    internal int PlotCount => state?.Plots?.Count ?? 0;

    internal void Refresh(MachineEntity node)
    {
        if (node == null) { Clear(); return; }
        try
        {
            MachineDefinition definition = node.Definition ??
                throw new InvalidOperationException("培育仓表现缺少机器定义。");
            MachineContent current = definition.Content;
            CultivatorVisualConfiguration layout = configuration;
            if (!ReferenceEquals(content, current))
            {
                var module = current.Find<Mod_Cultivator>() ??
                    throw new InvalidOperationException("培育仓缺少植株显示模块配置：" + definition.Id);
                layout = module.Value(nameof(Mod_Cultivator.Visual), ((Mod_Cultivator)module.Authoring).Visual) ??
                    throw new InvalidOperationException("培育仓缺少植株布局：" + definition.Id);
            }
            CultivatorState currentState = (node.Logic as CultivatorLogic)?.State ??
                MachineModuleState.Read<CultivatorState>(node.Snapshot, Mod_Cultivator.ModuleId);
            // 配置和快照全部成功后再提交，失败不能把新实体与上次植株混在一起。
            entity = node;
            content = current;
            configuration = layout;
            state = currentState;
        }
        catch { Clear(); throw; }
    }

    private void Clear() { entity = null; state = null; content = null; configuration = null; }

    internal bool TryGetPlot(int index, Sprite body, out Sprite sprite, out Vector3 offset, out Vector3 scale)
    {
        sprite = null; offset = Vector3.zero; scale = Vector3.one;
        if (body == null || configuration?.PlotAnchors == null ||
            (uint)index >= (uint)configuration.PlotAnchors.Length) return false;
        float relativeScale;
        bool present = entity?.Logic is CultivatorLogic logic
            ? logic.TryGetPlotVisual(index, out sprite, out _, out relativeScale)
            : CultivatorLogic.TryGetPlotVisual(state, index, out sprite, out relativeScale);
        if (!present) return false;
        Bounds bodyBounds = body.bounds, cropBounds = sprite.bounds;
        Vector2 limit = Vector2.Scale(configuration.PlotSize, bodyBounds.size);
        float fit = Mathf.Min(limit.x / Mathf.Max(.0001f, cropBounds.size.x),
            limit.y / Mathf.Max(.0001f, cropBounds.size.y)) * relativeScale;
        Vector2 anchor = configuration.PlotAnchors[index];
        offset = bodyBounds.min + Vector3.Scale(bodyBounds.size, new Vector3(anchor.x, anchor.y, 0f)) -
            new Vector3(cropBounds.center.x, cropBounds.min.y, 0f) * fit;
        offset.z = 0f;
        scale = new Vector3(fit, fit, 1f);
        return true;
    }
    #endregion
}
