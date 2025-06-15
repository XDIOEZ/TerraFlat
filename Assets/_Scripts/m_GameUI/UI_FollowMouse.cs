using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UI_FollowMouse : MonoBehaviour
{
    [Tooltip("是否启用跟随鼠标功能")]
    public bool followMouse = true;

    [Tooltip("鼠标位置与 UI 元素的偏移量")]
    public Vector3 offset = Vector3.zero;

    public RectTransform rectTransform;

    void Start()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponentInChildren<RectTransform>();
        }
    }

    void Update()
    {
        if (followMouse && rectTransform != null)
        {
            FollowMousePosition();
        }
    }

    private void FollowMousePosition()
    {
        // 获取鼠标在屏幕中的位置
        Vector3 mousePosition = Input.mousePosition;

        // 将鼠标位置转换为世界坐标，并加上偏移
        rectTransform.position = mousePosition + offset;
    }

    public void EnableFollowMouse(bool enable)
    {
        followMouse = enable;
    }
}
