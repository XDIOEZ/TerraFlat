using System.Globalization;

// 物质量以十进制原值保存，避免依赖运行时 decimal 的内存布局。
public static class PersistentDecimalText
{
    public static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    public static decimal Parse(string value) => value == null ? 0m : decimal.Parse(value, CultureInfo.InvariantCulture);
}
