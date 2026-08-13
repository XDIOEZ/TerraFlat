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

            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            if (panelRect != null && parent != null)
            {
                // 通用实例化会写入世界坐标，UI 面板挂到 PanelRoot 后必须恢复本地布局。
                panelRect.SetParent(parent, false);
                panelRect.anchorMin = Vector2.zero;
                panelRect.anchorMax = Vector2.one;
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.sizeDelta = Vector2.zero;
                panelRect.localPosition = Vector3.zero;
                panelRect.anchoredPosition = Vector2.zero;
                panelRect.localRotation = Quaternion.identity;
                panelRect.localScale = Vector3.one;
            }

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
