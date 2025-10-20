
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DebugMenu : MonoBehaviour
{
    private bool debugMenuVisible = false; // 控制调试菜单的显示与隐藏
    private string fpsOutput = ""; // 用于存储FPS输出
    private string memoryOutput = ""; // 用于存储内存占用输出
    private bool showFps = false; // 控制是否显示FPS信息
    private float fpsUpdateTimer = 0f; // FPS更新计时器
    private float fps = 0f; // 当前FPS值


    void Update()
    {
        // 检查是否按下了F5键
        if (Input.GetKeyDown(KeyCode.F5))
        {
            debugMenuVisible = !debugMenuVisible; // 切换调试菜单的可见性
        }

        // 每秒更新一次FPS
        fpsUpdateTimer += Time.deltaTime;
        if (fpsUpdateTimer >= 0.5f) // 每秒更新
        {
            fps = 1.0f / Time.deltaTime; // 计算当前FPS
            fpsUpdateTimer = 0f; // 重置计时器

            if (showFps) // 只有在显示FPS时更新显示内容
            {
                fpsOutput = "当前FPS: " + fps.ToString("F2"); // 格式化显示FPS
            }
        }
    }

    void OnGUI()
    {
        if (debugMenuVisible)
        {
            // 绘制调试菜单的GUI
            GUILayout.BeginArea(new Rect(10, 10, 500, 600)); // 扩展菜单区域宽度以容纳两个Box

            // 绘制菜单项
            if (GUILayout.Button("切换FPS输出"))
            {
                showFps = !showFps; // 切换是否显示FPS信息
                if (!showFps)
                {
                    fpsOutput = ""; // 隐藏FPS时清空显示内容
                }
            }

            if (GUILayout.Button("输出内存占用"))
            {
                DebugMemory();
            }

            // 绘制第一个面板（显示FPS）
            GUILayout.Space(10); // 给面板和按钮之间增加一点空间
            GUILayout.Box(fpsOutput, GUILayout.Width(480), GUILayout.Height(200)); // 创建一个面板用于显示FPS信息

            // 绘制第二个面板（显示内存占用）
            GUILayout.Space(10); // 给面板和按钮之间增加一点空间
            GUILayout.Box(memoryOutput, GUILayout.Width(480), GUILayout.Height(200)); // 创建一个面板用于显示内存占用

            GUILayout.EndArea();
        }
    }

    void DebugMemory()
    {
        // 获取当前内存使用情况并更新调试信息
        float memoryUsage = (System.GC.GetTotalMemory(false) / 1048576f); // 转换为MB
        memoryOutput = "当前内存占用: " + memoryUsage.ToString("F2") + " MB"; // 格式化显示内存占用
    }
}
