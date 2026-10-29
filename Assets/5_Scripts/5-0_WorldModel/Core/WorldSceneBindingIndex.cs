using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>独立记录逻辑世界与场景的绑定，场景不是世界身份。</summary>
    public sealed class WorldSceneBindingIndex
    {
        #region 世界与场景绑定
        private readonly Dictionary<int, Dictionary<string, object>> worldsByScene = new();

        public bool BindWorldToScene(int sceneId, string worldKey, object owner)
        {
            if (string.IsNullOrWhiteSpace(worldKey)) throw new ArgumentException("世界身份为空。", nameof(worldKey));
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (!worldsByScene.TryGetValue(sceneId, out Dictionary<string, object> worlds))
                worldsByScene.Add(sceneId, worlds = new Dictionary<string, object>(StringComparer.Ordinal));
            if (worlds.TryGetValue(worldKey, out object previous) && ReferenceEquals(previous, owner)) return false;
            worlds[worldKey] = owner;
            return true;
        }

        public bool UnbindWorldFromScene(int sceneId, string worldKey, object owner)
        {
            if (owner == null || worldKey == null || !worldsByScene.TryGetValue(sceneId, out var worlds) ||
                !worlds.TryGetValue(worldKey, out object current) || !ReferenceEquals(current, owner)) return false;
            worlds.Remove(worldKey);
            if (worlds.Count == 0) worldsByScene.Remove(sceneId);
            return true;
        }

        public bool IsCurrentWorldOwner(int sceneId, string worldKey, object owner)
            => owner != null && worldKey != null && worldsByScene.TryGetValue(sceneId, out var worlds) &&
               worlds.TryGetValue(worldKey, out object current) && ReferenceEquals(current, owner);

        public bool HasWorldsForScene(int sceneId) => worldsByScene.ContainsKey(sceneId);

        // 同场景多个世界必须由调用方明确选择，缺失或过期的上下文不回退到其他世界。
        public bool TryResolveWorldKeyForScene(int sceneId, string explicitWorldKey, out string worldKey)
        {
            worldKey = null;
            if (!worldsByScene.TryGetValue(sceneId, out var worlds)) return false;
            if (explicitWorldKey != null)
            {
                if (!worlds.ContainsKey(explicitWorldKey)) return false;
                worldKey = explicitWorldKey;
                return true;
            }
            if (worlds.Count != 1) return false;
            foreach (string key in worlds.Keys) { worldKey = key; return true; }
            return false;
        }

        public bool TryResolveWorldKeyForOwner(int sceneId, string explicitWorldKey, object owner, out string worldKey)
        {
            worldKey = null;
            if (!IsCurrentWorldOwner(sceneId, explicitWorldKey, owner)) return false;
            worldKey = explicitWorldKey;
            return true;
        }

        public void Clear() => worldsByScene.Clear();
        #endregion
    }

    /// <summary>为逻辑世界分配会话内稳定、不复用的整数身份。</summary>
    public sealed class RuntimeWorldIdentityIndex
    {
        #region 逻辑世界整数身份
        private readonly Dictionary<string, int> identities = new(StringComparer.Ordinal);
        private int lastIssued;

        public int GetOrCreateRuntimeWorldIdentity(string worldKey)
        {
            if (string.IsNullOrWhiteSpace(worldKey)) throw new ArgumentException("世界身份为空。", nameof(worldKey));
            if (identities.TryGetValue(worldKey, out int identity)) return identity;
            if (lastIssued == int.MaxValue) throw new InvalidOperationException("运行时世界身份已耗尽。");
            identity = ++lastIssued;
            identities.Add(worldKey, identity);
            return identity;
        }

        public bool TryGetRuntimeWorldIdentity(string worldKey, out int identity)
        {
            identity = 0;
            return worldKey != null && identities.TryGetValue(worldKey, out identity);
        }

        // 清空世界映射但保留已分配序号，旧任务不能命中新会话的身份。
        public void Clear()
        {
            identities.Clear();
        }
        #endregion
    }
}
