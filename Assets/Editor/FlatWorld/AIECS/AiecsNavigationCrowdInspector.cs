using FlatWorld.AIECS.Gameplay;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.AIECS.Editor
{
    /// <summary>共享导航的显式人工入口与实际缓存计算统计，不自动进入 Play Mode 或创建压力群体。</summary>
    [CustomEditor(typeof(AiecsNavigationCrowd))]
    internal sealed class AiecsNavigationCrowdInspector : UnityEditor.Editor
    {
        /// <summary>显示真实共享计算次数，帮助区分目标图、出口图和单位数量。</summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var crowd = (AiecsNavigationCrowd)target;
            EditorGUILayout.HelpBox("使用实际游戏导航窗口。每个目标配置一个出生中心；两军共用此控制器的一个 ECS 空间索引。此入口只有导航实体与有限 Gizmos，尚未接入正式生物渲染/战斗。", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("在当前游戏世界创建群体")) crowd.CreateCrowd();
                if (GUILayout.Button("清理群体")) crowd.StopCrowd();
            }
            EditorGUILayout.LabelField("真实导航 Entity", crowd.CreatedEntities.ToString());
            EditorGUILayout.LabelField("出生点被阻挡/未加载", crowd.RejectedSpawnPositions.ToString());
            EditorGUILayout.LabelField("共享目标 / 缓存 Chunk / 出口", $"{crowd.SharedGoals} / {crowd.CachedChunks} / {crowd.CachedPortals}");
            EditorGUILayout.LabelField("累计 Chunk 内容刷新", crowd.ChunkBuilds.ToString());
            EditorGUILayout.LabelField("累计出口积分图计算", crowd.ExitBuilds.ToString());
            EditorGUILayout.LabelField("累计目标局部图计算", crowd.TargetBuilds.ToString());
            EditorGUILayout.LabelField("累计区块级路线计算", crowd.RouteBuilds.ToString());
            if (Application.isPlaying) Repaint();
        }
    }
}
