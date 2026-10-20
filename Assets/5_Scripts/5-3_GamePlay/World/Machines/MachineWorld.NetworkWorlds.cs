using System;

public static partial class MachineWorld
{
    #region 单个地表副本的资源回收
    public static void ReleaseNetworkScope(string key)
    {
        if (!scopes.TryGetValue(key, out ScopeState value)) return;
        ScopeState previous = scope;
        string previousOverride = scopeOverride;
        try
        {
            scope = value; scopeOverride = key;
            ReleaseRuntime(); scopes.Remove(key);
        }
        finally { scope = ReferenceEquals(previous, value) ? new ScopeState() : previous; scopeOverride = previousOverride; }
        ReleaseCombatBridgeIfNoScopes();
    }
    #endregion
}
