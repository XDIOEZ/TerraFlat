using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

/// <summary>托管 MOD 程序集缓存，只加载已启用包在清单中明确声明的 DLL。</summary>
public static class ModManagedAssemblyStore
{
    #region 包校验与代码读取
    public sealed class PackageCode
    {
        internal string EntryName;
        internal readonly List<AssemblyImage> Images = new();
    }

    internal sealed class AssemblyImage
    {
        public string Name;
        public string FullName;
        public string Hash;
        public byte[] Bytes;
        public Assembly Loaded;
        public bool Loading;
    }

    private static readonly Dictionary<string, AssemblyImage> processImages = new(StringComparer.OrdinalIgnoreCase);
    private static bool resolverInstalled;

    public static void ValidateDefinition(string root, ModManagedDefinition definition)
    {
        if (definition == null) return;
        if (string.IsNullOrWhiteSpace(definition.EntryType) || definition.EntryType.Length > 512 ||
            string.IsNullOrWhiteSpace(definition.EntryAssembly) || (definition.Dependencies?.Count ?? 0) > 64)
            throw new InvalidDataException("托管 MOD 清单缺少有效入口或依赖数量过多。");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string relative in Paths(definition))
        {
            string path = Resolve(root, relative);
            if (!paths.Add(path)) throw new InvalidDataException("托管 MOD 程序集路径重复：" + relative);
            if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("托管 MOD 单个程序集超过 64MB：" + relative);
            AssemblyName name = AssemblyName.GetAssemblyName(path); // 只解析元数据，此处不执行代码。
            if (string.Equals(name.Name, "0Harmony", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("游戏已内置 Harmony；请从 MOD 包及 managed.dependencies 移除 0Harmony.dll，并用游戏提供的版本重新编译。");
        }
    }

    public static IEnumerable<string> Paths(ModManagedDefinition definition)
    {
        if (definition == null) yield break;
        yield return definition.EntryAssembly;
        foreach (string path in definition.Dependencies ?? new List<string>()) yield return path;
    }

    private static string Resolve(string root, string relative)
    {
        if (!string.Equals(Path.GetExtension(relative), ".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("托管 MOD 仅允许显式声明 DLL：" + relative);
        string path = ModPathUtility.ResolvePackagePath(root, relative, true);
        string current = path;
        string boundary = Path.GetFullPath(root);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("托管 MOD 不允许符号链接或目录联接：" + relative);
            if (string.Equals(current, boundary, StringComparison.OrdinalIgnoreCase)) break;
            current = Path.GetDirectoryName(current);
        }
        return path;
    }

    /// <summary>读取清单声明的代码；文件哈希只用于识别进程内同名 DLL 是否发生变化。</summary>
    public static PackageCode ReadPackage(string root, ModManagedDefinition definition)
    {
        ValidateDefinition(root, definition);
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        var package = new PackageCode();
        string entryPath = Resolve(root, definition.EntryAssembly);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string relative in Paths(definition).OrderBy(value => value, StringComparer.Ordinal))
        {
            string path = Resolve(root, relative);
            byte[] bytes = File.ReadAllBytes(path);
            AssemblyName name = AssemblyName.GetAssemblyName(path);
            if (!names.Add(name.Name)) throw new InvalidDataException("一个 MOD 包含同名程序集：" + name.Name);
            using var sha = SHA256.Create();
            var image = new AssemblyImage { Name = name.Name, FullName = name.FullName, Bytes = bytes, Hash = Hex(sha.ComputeHash(bytes)) };
            package.Images.Add(image);
            if (string.Equals(path, entryPath, StringComparison.OrdinalIgnoreCase)) package.EntryName = image.Name;
        }
        return package;
    }

    private static string Hex(byte[] hash) => BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    #endregion

    #region 进程程序集缓存
    /// <summary>同进程内不允许换字节的 DLL 热替换；普通内容重载可重用完全相同的程序集。</summary>
    public static Assembly LoadTrusted(PackageCode package)
    {
#if ENABLE_IL2CPP
        throw new PlatformNotSupportedException("当前 IL2CPP 平台不支持托管 DLL / Harmony MOD；JSON、资源及 Lua MOD 不受此限制。");
#else
        if (package == null) throw new ArgumentNullException(nameof(package));
        EnsureBuiltInHarmonyLoaded();
        foreach (AssemblyImage image in package.Images)
        {
            if (processImages.TryGetValue(image.Name, out var existing))
            {
                if (existing.Hash != image.Hash) throw new InvalidOperationException("程序集已加载但代码不同，须重启游戏：" + image.Name);
                continue;
            }
            Assembly conflicting = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(value => value.GetName().Name == image.Name);
            if (conflicting != null)
                throw new InvalidOperationException("MOD 不能替换已加载的游戏、系统或其它加载器程序集：" + image.Name);
        }
        foreach (AssemblyImage image in package.Images)
            if (!processImages.ContainsKey(image.Name)) processImages.Add(image.Name, image);
        if (!resolverInstalled) { AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly; resolverInstalled = true; }
        foreach (AssemblyImage image in package.Images) LoadImage(processImages[image.Name]);
        return LoadImage(processImages[package.EntryName]);
#endif
    }

    /// <summary>按程序集名加载游戏自带 Harmony，避免 GamePlay 对补丁库建立编译期引用并被 Burst 扫描。</summary>
    private static void EnsureBuiltInHarmonyLoaded()
    {
        const string harmonyAssemblyName = "0Harmony";
        if (AppDomain.CurrentDomain.GetAssemblies().Any(value =>
                string.Equals(value.GetName().Name, harmonyAssemblyName, StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            Assembly.Load(harmonyAssemblyName);
        }
        catch (Exception exception)
        {
            throw new FileLoadException(
                "游戏自带 Harmony 运行库未能加载；请确认发行包包含 0Harmony.dll。", exception);
        }
    }

    private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name);
        if (!processImages.TryGetValue(name.Name, out var image)) return null;
        if (!string.Equals(image.FullName, name.FullName, StringComparison.OrdinalIgnoreCase))
            throw new FileLoadException("MOD 依赖程序集版本与声明文件不一致：" + name.FullName);
        return LoadImage(image);
    }

    private static Assembly LoadImage(AssemblyImage image)
    {
        if (image.Loaded != null) return image.Loaded;
        if (image.Loading) throw new FileLoadException("托管 MOD 程序集存在无法解析的循环加载：" + image.Name);
        image.Loading = true;
        try
        {
            image.Loaded = Assembly.Load(image.Bytes);
            if (image.Loaded.FullName != image.FullName) throw new FileLoadException("DLL 元数据在读取期间发生变化，请重新确认代码：" + image.Name);
            image.Bytes = null;
            return image.Loaded;
        }
        finally { image.Loading = false; }
    }
    #endregion
}
