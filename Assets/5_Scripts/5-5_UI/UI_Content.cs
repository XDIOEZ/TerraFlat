using UnityEngine;

/// <summary>
/// 标记背包、快捷栏等界面的动态槽位容器，供库存逻辑按类型定位 Content。
/// 本组件不执行帧循环；布局与内容刷新由所属 Inventory 显式驱动。
/// </summary>
[DisallowMultipleComponent]
public class UI_Content : MonoBehaviour
{
}
