#if UNITY_EDITOR

using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace FlatWorld.Editor.Diagnostics
{
    /// <summary>
    /// Addressables 快速进入 Play Mode 状态守卫。
    /// 禁用 Domain Reload 时，编辑器静态实例会跨 Play 会话保留；进入游戏前主动重建，
    /// 避免已经清空或失效的 Resource Locator 被下一次资源启动流程继续复用。
    /// </summary>
    internal static class AddressablesBootstrapProbe
    {
        #region 常量

        private const string LogPrefix = "[AddressablesBootstrap]";

        #endregion

        #region Play Mode 入口

        /// <summary>仅在禁用 Domain Reload 的编辑器配置下刷新 Addressables 静态实例。</summary>
        [InitializeOnEnterPlayMode]
        private static void RecreateAddressablesBeforePlayMode()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled ||
                (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0)
            {
                return;
            }

            if (!TryRecreateAddressablesInstance(out string failureReason))
            {
                Debug.LogWarning(
                    $"{LogPrefix} 进入 Play Mode 前未能重建 Addressables 实例：{failureReason}。" +
                    "GameRes 会在 Prefab 标签为空时中止加载并输出根因。");
            }
        }

        #endregion

        #region 实例重建

        /// <summary>
        /// 复用 Addressables 包自身的编辑器重建开关，不直接构造或篡改 ResourceManager。
        /// </summary>
        internal static bool TryRecreateAddressablesInstance(out string failureReason)
        {
            failureReason = string.Empty;

            try
            {
                const BindingFlags Flags = BindingFlags.Static |
                                           BindingFlags.Public |
                                           BindingFlags.NonPublic;
                Type addressablesType = typeof(Addressables);
                FieldInfo reinitializeField = addressablesType.GetField(
                    "reinitializeAddressables",
                    Flags);
                FieldInfo instanceField = addressablesType.GetField(
                    "m_AddressablesInstance",
                    Flags);
                if (reinitializeField == null || reinitializeField.FieldType != typeof(bool))
                {
                    failureReason = "Addressables.reinitializeAddressables 字段不存在或类型已变化";
                    return false;
                }

                if (instanceField == null)
                {
                    failureReason = "Addressables.m_AddressablesInstance 字段不存在";
                    return false;
                }

                object previousInstance = instanceField.GetValue(null);
                reinitializeField.SetValue(null, true);

                // 访问公开入口，让 Addressables 按包内既有逻辑在主线程完成实例替换。
                _ = Addressables.ResourceManager;
                object currentInstance = instanceField.GetValue(null);
                if (ReferenceEquals(previousInstance, currentInstance))
                {
                    failureReason = "Addressables 实例未发生替换";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                failureReason = exception.Message;
                return false;
            }
        }

        #endregion
    }
}

#endif
