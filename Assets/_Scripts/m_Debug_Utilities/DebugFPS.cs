using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DebugFPS : MonoBehaviour
{
    private float deltaTimeSum = 0f;
    private int frameCount = 0;
    private float fps = 0f;
    private bool showFPS = false;

    void Update()
    {
        deltaTimeSum += Time.deltaTime;
        frameCount++;

        if (deltaTimeSum >= 1f)
        {
            fps = frameCount / deltaTimeSum;
            deltaTimeSum = 0f;
            frameCount = 0;
        }

        if (Input.GetKeyDown(KeyCode.F) && Input.GetKeyDown(KeyCode.P))
        {
            showFPS = !showFPS;
        }
    }

    void OnGUI()
    {
        if (showFPS)
        {
            GUI.Label(new Rect(10, 10, 100, 20), "FPS: " + fps.ToString("F2"));
        }
    }
}