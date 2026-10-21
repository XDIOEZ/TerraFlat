using FlatWorld.Networking;

public interface IPlayerStateAuthorityModule { }
public interface IClientPlayerPresentationTick { }

public static class PlayerSimulationAuthority
{
    #region 玩家状态的唯一结算端
    // 能力声明自己的结算归属，新的生存模块实现接口即可复用同一网络状态契约。
    public static bool CanTick(Module module)
        => module is not IPlayerStateAuthorityModule || module is IClientPlayerPresentationTick || GameNetwork.HasStateAuthority;
    #endregion
}
