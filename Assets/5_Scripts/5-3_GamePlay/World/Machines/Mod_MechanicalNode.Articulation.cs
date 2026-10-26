using Newtonsoft.Json.Linq;

public sealed partial class Mod_MechanicalNode
{
    #region 关节表现声明
    public JObject Articulation; // 模块严格校验接收零件声明，具体关节装配只由表现层消费。
    #endregion
}
