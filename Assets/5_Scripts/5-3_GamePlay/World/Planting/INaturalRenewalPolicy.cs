/// <summary>自然资源被移除后的年度恢复资格；由资源能力决定，生成表现不识别具体植物类型。</summary>
public interface INaturalRenewalPolicy
{
    bool TryGetRenewalYear(out int year);
}
