// AI-Context: NetworkModeUIController 的 Prefab 加载分部；UI_NetworkMode 由 GameRes 预加载并实例化。

using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>
    /// 联机 UI Prefab 入口：运行时只负责从 GameRes 实例化，不创建视觉节点。
    /// </summary>
    public sealed partial class NetworkModeUIController
    {
        public const string NetworkPanelKey = "UI_NetworkMode";

        #region Prefab 实例化

        private static BasePanel CreateNetworkPanel(Transform parent)
        {
            GameObject panelObject = GameRes.Instance.InstantiatePrefab(NetworkPanelKey, parent: parent);
            if (panelObject == null)
                return null;

            panelObject.name = NetworkPanelKey;
            BasePanel basePanel = panelObject.GetComponent<BasePanel>();
            if (basePanel == null)
            {
                Debug.LogError($"[联机UI] {NetworkPanelKey} Prefab 缺少 BasePanel 组件。", panelObject);
                Object.Destroy(panelObject);
                return null;
            }

            basePanel.PanelName = NetworkPanelKey;
            basePanel.Init();
            UIManager.Instance.RegisterPanel(basePanel, NetworkPanelKey);
            panelObject.transform.SetAsLastSibling();
            return basePanel;
        }

        #endregion
    }
}
