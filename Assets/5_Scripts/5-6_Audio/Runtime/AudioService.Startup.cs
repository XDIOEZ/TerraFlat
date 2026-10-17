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

        /// <summary>正式启动先等待整张音频引用图，再开放会播放声音的主菜单。</summary>
        public static IEnumerator PreloadAsync()
        {
            if (instance != null)
                yield break;

            if (preloadedRuntimeConfig == null)
            {
                if (runtimeConfigPreloadRequest == null)
                {
                    using var requestScope = StartupRequestMarker.Auto();
                    runtimeConfigPreloadRequest = Resources.LoadAsync<AudioRuntimeConfig>(RuntimeConfigResourcePath);
                }

                yield return runtimeConfigPreloadRequest;
                preloadedRuntimeConfig = runtimeConfigPreloadRequest.asset as AudioRuntimeConfig;
            }

            if (preloadedRuntimeConfig != null)
                preloadedCatalog = preloadedRuntimeConfig.Catalog;

            if (preloadedCatalog == null)
            {
                if (catalogPreloadRequest == null)
                {
                    using var requestScope = StartupRequestMarker.Auto();
                    catalogPreloadRequest = Resources.LoadAsync<AudioCatalog>(CatalogResourcePath);
                }

                yield return catalogPreloadRequest;
                preloadedCatalog = catalogPreloadRequest.asset as AudioCatalog;
            }

            // 创建声源池和设置提供者也在启动阶段完成，首个点击只负责播放。
            _ = Instance;
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
