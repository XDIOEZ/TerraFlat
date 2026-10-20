#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Networking.Gameplay
{
    public sealed class GameplayTestAutomaticSessionLease : MonoBehaviour
    {
        #region 隔离测试会话租约
        private IDisposable suppression;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= Attach;
            SceneManager.sceneLoaded += Attach;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AttachCurrent() => Attach(default, default);
        private static void Attach(Scene scene, LoadSceneMode mode)
        {
            GameplayTestScene host = FindObjectOfType<GameplayTestScene>(true);
            if (host != null && host.GetComponent<GameplayTestAutomaticSessionLease>() == null)
                host.gameObject.AddComponent<GameplayTestAutomaticSessionLease>();
        }
        // 只阻止隔离宿主入世时自动开局，不停止或接管任何已有网络会话。
        private void Awake() => suppression = NetworkGameBootstrap.SuppressAutomaticSession();
        private void OnDestroy() { suppression?.Dispose(); suppression = null; }
        #endregion
    }
}
#endif
