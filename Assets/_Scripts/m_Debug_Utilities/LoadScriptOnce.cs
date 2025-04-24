using UnityEditor;
using UnityEngine;
using System.Diagnostics;
using System.Reflection; // ���ڴ��������õķ���

#if UNITY_EDITOR

[CustomEditor(typeof(MonoBehaviour), true)]
public class LoadScriptOnce : Editor
{
    [MenuItem("CONTEXT/MonoBehaviour/ִ�� Start ����")]
    private static void ExecuteStart(MenuCommand command)
    {
        ExecuteMethod(command.context as MonoBehaviour, "Start");
    }

    [MenuItem("CONTEXT/MonoBehaviour/ִ�� PlayerInputActions ����")]
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
                UnityEngine.Debug.LogError($"ִ�� {methodName} ʱ��������: {e.InnerException}");
            }
            finally
            {
                stopwatch.Stop();
                UnityEngine.Debug.Log($"{methodName} ����ִ�к�ʱ: {stopwatch.ElapsedMilliseconds} ����");
            }*/
        }
        else
        {
            UnityEngine.Debug.LogWarning($"δ�ҵ� {methodName} �������� {methodName} �������ɷ���");
        }
    }
}
#endif