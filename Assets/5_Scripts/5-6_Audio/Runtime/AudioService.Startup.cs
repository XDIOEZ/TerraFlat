using System.Collections;
using Unity.Profiling;
using UnityEngine;

namespace FlatWorld.Audio
{
    public sealed partial class AudioService
    {
        #region 启动资源预热

        private static readonly ProfilerMarker StartupRequestMarker = new ProfilerMarker("AudioService.Startup.Request");
        private static readonly ProfilerMarker ConfigurationMarker = new ProfilerMarker("AudioService.Startup.Configure");
        private static readonly ProfilerMarker CatalogIndexMarker = new ProfilerMarker("AudioService.Startup.Index");
        private static readonly ProfilerMarker VoicePoolMarker = new ProfilerMarker("AudioService.Startup.VoicePool");

        private static ResourceRequest runtimeConfigPreloadRequest;
        private static ResourceRequest catalogPreloadRequest;
        private static AudioRuntimeConfig preloadedRuntimeConfig;
        private static AudioCatalog preloadedCatalog;

        /// <summary>先发出音库请求，让纹理、预制体和配置构建复用音频异步等待时间。</summary>
        public static void BeginStartupResourcePreload()
        {
            if (instance != null || runtimeConfigPreloadRequest != null) return;
            using var requestScope = StartupRequestMarker.Auto();
            runtimeConfigPreloadRequest = Resources.LoadAsync<AudioRuntimeConfig>(RuntimeConfigResourcePath);
        }

        /// <summary>只查询已完成的请求；音库未就绪时继续加载其他资源，不阻塞主菜单准备。</summary>
        public static bool TryCompleteStartupResourcePreload()
        {
            if (instance != null) return true;
            BeginStartupResourcePreload();
            if (!runtimeConfigPreloadRequest.isDone) return false;

            preloadedRuntimeConfig = runtimeConfigPreloadRequest.asset as AudioRuntimeConfig;

            if (preloadedRuntimeConfig != null)
                preloadedCatalog = preloadedRuntimeConfig.Catalog;

            if (preloadedCatalog == null)
            {
                if (catalogPreloadRequest == null)
                {
                    using var requestScope = StartupRequestMarker.Auto();
                    catalogPreloadRequest = Resources.LoadAsync<AudioCatalog>(CatalogResourcePath);
                }

                if (!catalogPreloadRequest.isDone) return false;
                preloadedCatalog = catalogPreloadRequest.asset as AudioCatalog;
            }

            // 创建声源池和设置提供者也在启动阶段完成，首个点击只负责播放。
            _ = Instance;
            return true;
        }

        /// <summary>主菜单和完整资源就绪仍等待真实音库与声源池完成，重复调用复用同一请求。</summary>
        public static IEnumerator PreloadAsync()
        {
            while (!TryCompleteStartupResourcePreload()) yield return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStartupPreload()
        {
            runtimeConfigPreloadRequest = null;
            catalogPreloadRequest = null;
            preloadedRuntimeConfig = null;
            preloadedCatalog = null;
        }

        #endregion
    }
}
