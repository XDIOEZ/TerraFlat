using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class ModulePayloadAudit
{
    // 从 Unity 编译响应文件解析真实泛型调用，避免只按字符串猜测载荷类型。
    public static void Main(string[] args)
    {
        string root = args[0];
        var groups = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (string response in args.Skip(1))
        {
            string text = File.ReadAllText(response);
            var references = Regex.Matches(text, @"(?m)^-r:""([^""]+)""")
                .Select(match => Path.GetFullPath(match.Groups[1].Value, root)).Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => MetadataReference.CreateFromFile(path)).ToArray();
            var trees = Regex.Matches(text, @"(?m)^""(Assets/[^""]+\.cs)""")
                .Select(match => match.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, path)),
                    new CSharpParseOptions(LanguageVersion.Preview), path)).ToArray();
            var compilation = CSharpCompilation.Create("Audit", trees, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
            foreach (var tree in trees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var symbol = model.GetSymbolInfo(call).Symbol as IMethodSymbol;
                    string owner = symbol?.ContainingType?.Name;
                    bool direct = owner == "Ex_ModData_MemoryPackable" &&
                        new[] { "GetData", "ReadData", "WriteData", "OutData", "ReadRuntimeState", "WriteRuntimeState" }.Contains(symbol.Name);
                    bool machine = (owner == "MachinePersistence" || owner == "MachineModuleState") &&
                        (symbol.Name == "Read" || symbol.Name == "Write");
                    bool shipMass = symbol?.Name == "ReadBinary" && tree.FilePath.EndsWith("ShipMassReader.cs", StringComparison.Ordinal);
                    if ((!direct && !machine && !shipMass) || symbol.TypeArguments.Length != 1)
                        continue;
                    ITypeSymbol type = symbol.TypeArguments[0];
                    string name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (!groups.TryGetValue(name, out var entry))
                    {
                        entry = new Entry { Type = name, Unmanaged = type.IsUnmanagedType, IsValueType = type.IsValueType };
                        if (type is INamedTypeSymbol named)
                        {
                            entry.Declarations.AddRange(named.DeclaringSyntaxReferences.Select(reference =>
                                reference.SyntaxTree.FilePath + ":" + (reference.GetSyntax().GetLocation().GetLineSpan().StartLinePosition.Line + 1)));
                            foreach (ISymbol member in named.GetMembers())
                            {
                                ITypeSymbol fieldType;
                                if (member is IFieldSymbol field && !field.IsStatic && !field.IsImplicitlyDeclared)
                                    fieldType = field.Type;
                                else if (member is IPropertySymbol property && !property.IsStatic &&
                                         property.GetMethod != null && property.SetMethod != null)
                                    fieldType = property.Type;
                                else continue;
                                var attributes = member.GetAttributes().Select(attribute => attribute.AttributeClass?.Name).ToArray();
                                if (attributes.Contains("MemoryPackIgnoreAttribute") ||
                                    member.DeclaredAccessibility != Accessibility.Public && !attributes.Contains("MemoryPackIncludeAttribute"))
                                    continue;
                                entry.Fields.Add(member.Name + ": " + fieldType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                            }
                        }
                        groups[name] = entry;
                    }
                    entry.Calls.Add(tree.FilePath + ":" + (call.GetLocation().GetLineSpan().StartLinePosition.Line + 1) + " [" + owner + "." + symbol.Name + "]");
                }
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(groups.Values.OrderBy(x => x.Type),
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class Entry
    {
        public string Type { get; set; }
        public bool Unmanaged { get; set; }
        public bool IsValueType { get; set; }
        public List<string> Calls { get; } = new List<string>();
        public List<string> Declarations { get; } = new List<string>();
        public List<string> Fields { get; } = new List<string>();
    }
}
