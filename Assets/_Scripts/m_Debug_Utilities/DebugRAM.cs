using System.Diagnostics;
using UnityEngine;

public class DebugRAM : MonoBehaviour
{
    private bool isMemoryDisplayEnabled = false;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R) && Input.GetKeyDown(KeyCode.A))
        {
            isMemoryDisplayEnabled = !isMemoryDisplayEnabled;
        }
    }

    void OnGUI()
    {
        if (isMemoryDisplayEnabled)
        {
            ShowMemoryUsage();
        }
    }

    void ShowMemoryUsage()
    {
        Process process = Process.GetCurrentProcess();
        long memoryUsed = process.PrivateMemorySize64;
        float memoryUsedMB = memoryUsed / (1024.0f * 1024.0f);
        GUI.Label(new Rect(10, 10, 200, 20), "Memory Used: " + memoryUsedMB.ToString("F2") + " MB");

    }
    // todo 创建一个颜色实例方法





}