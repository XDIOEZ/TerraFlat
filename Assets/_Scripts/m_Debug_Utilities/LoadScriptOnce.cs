using UnityEditor;
using UnityEngine;
using System.Diagnostics;
using System.Reflection; // 用于处理方法调用的反射

#if UNITY_EDITOR

[CustomEditor(typeof(MonoBehaviour), true)]
public class LoadScriptOnce : Editor
{
    [MenuItem("CONTEXT/MonoBehaviour/执行 Start 方法")]
    private static void ExecuteStart(MenuCommand command)
    {
        ExecuteMethod(command.context as MonoBehaviour, "Start");
    }

    [MenuItem("CONTEXT/MonoBehaviour/执行 PlayerInputActions 方法")]
    private static void ExecuteTest(MenuCommand command)
    {
        ExecuteMethod(command.context as MonoBehaviour, "PlayerInputActions");
    }

    private static void ExecuteMethod(MonoBehaviour monoBehaviour, string methodName)
    {
        if (monoBehaviour == null) return;

        var method = monoBehaviour.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (method != null)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
  method.Invoke(monoBehaviour, null);
        /*    try
            {
              
            }
            catch (TargetInvocationException e)
            {
                UnityEngine.Debug.LogError($"执行 {methodName} 时发生错误: {e.InnerException}");
            }
            finally
            {
                stopwatch.Stop();
                UnityEngine.Debug.Log($"{methodName} 方法执行耗时: {stopwatch.ElapsedMilliseconds} 毫秒");
            }*/
        }
        else
        {
            UnityEngine.Debug.LogWarning($"未找到 {methodName} 方法，或 {methodName} 方法不可访问");
        }
    }
}
#endif