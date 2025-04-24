
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DebugMenu : MonoBehaviour
{
    private bool debugMenuVisible = false; // ���Ƶ��Բ˵�����ʾ������
    private string fpsOutput = ""; // ���ڴ洢FPS���
    private string memoryOutput = ""; // ���ڴ洢�ڴ�ռ�����
    private bool showFps = false; // �����Ƿ���ʾFPS��Ϣ
    private float fpsUpdateTimer = 0f; // FPS���¼�ʱ��
    private float fps = 0f; // ��ǰFPSֵ


    void Update()
    {
        // ����Ƿ�����F5��
        if (Input.GetKeyDown(KeyCode.F5))
        {
            debugMenuVisible = !debugMenuVisible; // �л����Բ˵��Ŀɼ���
        }

        // ÿ�����һ��FPS
        fpsUpdateTimer += Time.deltaTime;
        if (fpsUpdateTimer >= 0.5f) // ÿ�����
        {
            fps = 1.0f / Time.deltaTime; // ���㵱ǰFPS
            fpsUpdateTimer = 0f; // ���ü�ʱ��

            if (showFps) // ֻ������ʾFPSʱ������ʾ����
            {
                fpsOutput = "��ǰFPS: " + fps.ToString("F2"); // ��ʽ����ʾFPS
            }
        }
    }

    void OnGUI()
    {
        if (debugMenuVisible)
        {
            // ���Ƶ��Բ˵���GUI
            GUILayout.BeginArea(new Rect(10, 10, 500, 600)); // ��չ�˵�����������������Box

            // ���Ʋ˵���
            if (GUILayout.Button("�л�FPS���"))
            {
                showFps = !showFps; // �л��Ƿ���ʾFPS��Ϣ
                if (!showFps)
                {
                    fpsOutput = ""; // ����FPSʱ�����ʾ����
                }
            }

            if (GUILayout.Button("����ڴ�ռ��"))
            {
                DebugMemory();
            }

            // ���Ƶ�һ����壨��ʾFPS��
            GUILayout.Space(10); // �����Ͱ�ť֮������һ��ռ�
            GUILayout.Box(fpsOutput, GUILayout.Width(480), GUILayout.Height(200)); // ����һ�����������ʾFPS��Ϣ

            // ���Ƶڶ�����壨��ʾ�ڴ�ռ�ã�
            GUILayout.Space(10); // �����Ͱ�ť֮������һ��ռ�
            GUILayout.Box(memoryOutput, GUILayout.Width(480), GUILayout.Height(200)); // ����һ�����������ʾ�ڴ�ռ��

            GUILayout.EndArea();
        }
    }

    void DebugMemory()
    {
        // ��ȡ��ǰ�ڴ�ʹ����������µ�����Ϣ
        float memoryUsage = (System.GC.GetTotalMemory(false) / 1048576f); // ת��ΪMB
        memoryOutput = "��ǰ�ڴ�ռ��: " + memoryUsage.ToString("F2") + " MB"; // ��ʽ����ʾ�ڴ�ռ��
    }
}
