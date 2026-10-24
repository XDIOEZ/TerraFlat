using System;
using System.Collections.Generic;
using MemoryPack;
using MemoryPack.Formatters;

#region 配置字典的字符串比较规则

// 字典的大小写规则属于配置契约，二进制恢复不能退回默认比较器。
public sealed class BinaryTextLibrariesAttribute : MemoryPackCustomFormatterAttribute<DictionaryFormatter<string, List<string>>, Dictionary<string, List<string>>>
{
    public override DictionaryFormatter<string, List<string>> GetFormatter() => new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BinaryTextGeneratorsAttribute : MemoryPackCustomFormatterAttribute<DictionaryFormatter<string, TextLibraryGeneratorDto>, Dictionary<string, TextLibraryGeneratorDto>>
{
    public override DictionaryFormatter<string, TextLibraryGeneratorDto> GetFormatter() => new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BinaryAtmosphereCompositionAttribute : MemoryPackCustomFormatterAttribute<DictionaryFormatter<string, decimal>, Dictionary<string, decimal>>
{
    public override DictionaryFormatter<string, decimal> GetFormatter() => new(StringComparer.OrdinalIgnoreCase);
}

#endregion
