using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlatWorld.AIECS
{
    /// <summary>
    /// 隔离原型中旧显示项的排序租约；只临时修改外层 Order，停用时原样恢复。
    /// 颜色仍由原生路径绘制一次，内部 SortingGroup、材质、MPB、阴影及法线保持原职责。
    /// 正式动态世界接入和多相机共享租约属于后续门槛，不能把本作用域用于半个正式世界。
    /// </summary>
    internal sealed class AiecsLegacySortScope : IDisposable
    {
        // 原型根节点内所有旧显示项。
        internal readonly List<Entry> Entries = new();

        /// <summary>一次收集完整旧显示范围，并在修改任何顺序前验证可支持的分组语义。</summary>
        internal AiecsLegacySortScope(Transform root)
        {
            var groups = new HashSet<SortingGroup>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is SpriteRenderer))
                    throw new InvalidOperationException("P1 旧显示范围仅支持 SpriteRenderer；Tilemap/粒子需先实现适配。");
                SortingGroup selected = null;
                foreach (SortingGroup group in renderer.GetComponentsInParent<SortingGroup>(true))
                {
                    if (!group.enabled) continue;
                    if (selected != null)
                        throw new InvalidOperationException("P1 尚未接入嵌套 SortingGroup，不能忽略其排序语义。");
                    selected = group;
                }
                if (selected != null && !selected.transform.IsChildOf(root) && selected.transform != root)
                    throw new InvalidOperationException("排序租约不能越出原型旧显示范围。");
                if (selected != null && !groups.Add(selected)) continue;
                Entries.Add(new Entry(renderer, selected));
            }
        }

        /// <summary>释放全部外层排序覆盖。</summary>
        public void Dispose()
        {
            foreach (Entry entry in Entries) entry.Restore();
            Entries.Clear();
        }

        /// <summary>一个原生 Sprite 或最外层 SortingGroup 的不可变排序基线。</summary>
        internal sealed class Entry
        {
            // 原生对象与创建时顺序。
            private readonly Renderer renderer;
            private readonly SortingGroup group;
            private readonly Renderer[] members;
            internal readonly int Layer;
            internal readonly int OriginalOrder;
            internal readonly int Queue;

            /// <summary>缓存组成员，避免每帧扫描层级；混合队列先明确拒绝。</summary>
            internal Entry(Renderer renderer, SortingGroup group)
            {
                this.renderer = renderer;
                this.group = group;
                members = group != null ? group.GetComponentsInChildren<Renderer>(true) : new[] { renderer };
                Layer = group != null ? group.sortingLayerID : renderer.sortingLayerID;
                OriginalOrder = group != null ? group.sortingOrder : renderer.sortingOrder;
                Queue = renderer.sharedMaterial != null ? renderer.sharedMaterial.renderQueue : 3000;
                foreach (Renderer member in members)
                {
                    if (member.sharedMaterial != null && member.sharedMaterial.renderQueue != Queue)
                        throw new InvalidOperationException("P1 尚未支持组内多 RenderQueue，不能把该组按单一队列插入。");
                }
            }

            /// <summary>读取此显示项当前是否启用及其 Y 轴排序锚点。</summary>
            internal bool TryGetY(out float y)
            {
                y = 0f;
                if (renderer == null || (group != null && !group.isActiveAndEnabled)) return false;
                bool visible = false;
                foreach (Renderer member in members)
                    visible |= member != null && member.enabled && member.gameObject.activeInHierarchy && !member.forceRenderingOff;
                if (!visible) return false;
                if (group != null) y = group.transform.position.y;
                else if (renderer is SpriteRenderer sprite && sprite.spriteSortPoint == SpriteSortPoint.Pivot)
                    y = sprite.transform.position.y;
                else y = renderer.bounds.center.y;
                return true;
            }

            /// <summary>覆盖本帧外部显示顺序，不改组内成员顺序。</summary>
            internal void Apply(int order)
            {
                if (group != null) group.sortingOrder = order;
                else if (renderer != null) renderer.sortingOrder = order;
            }

            /// <summary>恢复进入原型前的外部排序值。</summary>
            internal void Restore()
            {
                Apply(OriginalOrder);
            }
        }
    }
}
