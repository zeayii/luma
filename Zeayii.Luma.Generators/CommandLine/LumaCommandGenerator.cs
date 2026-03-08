using System.Text;
using Microsoft.CodeAnalysis;

namespace Zeayii.Luma.Generators.CommandLine;

/// <summary>
/// <b>爬虫命令生成器</b>
/// <para>
/// 扫描实现了站点模块接口的类型，并自动生成子命令挂载代码。
/// </para>
/// </summary>
[Generator]
public sealed class LumaCommandGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.CompilationProvider, static (sourceProductionContext, compilation) => Emit(sourceProductionContext, compilation));
    }

    /// <summary>
    /// 生成命令源码。
    /// </summary>
    /// <param name="sourceProductionContext">源生成上下文。</param>
    /// <param name="compilation">编译上下文。</param>
    private static void Emit(SourceProductionContext sourceProductionContext, Compilation compilation)
    {
        if (!IsEnabled(compilation))
        {
            return;
        }

        var modules = CollectModules(compilation);
        sourceProductionContext.AddSource("Zeayii.Luma.CommandLine.Generated.LumaCommands.g.cs", GenerateSource(modules));
    }

    /// <summary>
    /// 判断生成器是否具备运行条件。
    /// </summary>
    /// <param name="compilation">编译上下文。</param>
    /// <returns>满足条件返回 <c>true</c>。</returns>
    private static bool IsEnabled(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName("Zeayii.Luma.Abstractions.CommandLine.ILumaCommandModule") is not null && compilation.GetTypeByMetadataName("System.CommandLine.RootCommand") is not null;
    }

    /// <summary>
    /// 收集所有站点模块类型。
    /// </summary>
    /// <param name="compilation">编译上下文。</param>
    /// <returns>模块元数据列表。</returns>
    private static List<ModuleMetadata> CollectModules(Compilation compilation)
    {
        var moduleInterfaceSymbol = compilation.GetTypeByMetadataName("Zeayii.Luma.Abstractions.CommandLine.ILumaCommandModule");
        if (moduleInterfaceSymbol is null)
        {
            return [];
        }

        var result = new List<ModuleMetadata>();
        var dedupe = new HashSet<string>(StringComparer.Ordinal);
        var assemblies = new List<IAssemblySymbol>(1 + compilation.SourceModule.ReferencedAssemblySymbols.Length)
        {
            compilation.Assembly
        };
        assemblies.AddRange(compilation.SourceModule.ReferencedAssemblySymbols);

        foreach (var assembly in assemblies)
        {
            CollectFromNamespace(assembly.GlobalNamespace, moduleInterfaceSymbol, result, dedupe);
        }

        result.Sort(static (left, right) => string.Compare(left.TypeDisplay, right.TypeDisplay, StringComparison.Ordinal));
        return result;
    }

    /// <summary>
    /// 递归扫描命名空间。
    /// </summary>
    /// <param name="namespaceSymbol">命名空间符号。</param>
    /// <param name="moduleInterfaceSymbol">站点模块接口符号。</param>
    /// <param name="result">结果集合。</param>
    /// <param name="dedupe">去重集合。</param>
    private static void CollectFromNamespace(INamespaceSymbol namespaceSymbol, INamedTypeSymbol moduleInterfaceSymbol, List<ModuleMetadata> result, HashSet<string> dedupe)
    {
        foreach (var typeMember in namespaceSymbol.GetTypeMembers())
        {
            CollectFromType(typeMember, moduleInterfaceSymbol, result, dedupe);
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            CollectFromNamespace(childNamespace, moduleInterfaceSymbol, result, dedupe);
        }
    }

    /// <summary>
    /// 扫描类型及其嵌套类型。
    /// </summary>
    /// <param name="typeSymbol">类型符号。</param>
    /// <param name="moduleInterfaceSymbol">站点模块接口符号。</param>
    /// <param name="result">结果集合。</param>
    /// <param name="dedupe">去重集合。</param>
    private static void CollectFromType(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol moduleInterfaceSymbol,
        List<ModuleMetadata> result,
        HashSet<string> dedupe)
    {
        if (typeSymbol.TypeKind == TypeKind.Class && typeSymbol.IsAbstract is false && typeSymbol.DeclaredAccessibility == Accessibility.Public && Implements(typeSymbol, moduleInterfaceSymbol))
        {
            var fullyQualifiedTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (dedupe.Add(fullyQualifiedTypeName))
            {
                result.Add(new ModuleMetadata(fullyQualifiedTypeName, BuildFactoryName(typeSymbol)));
            }
        }

        foreach (var nestedType in typeSymbol.GetTypeMembers())
        {
            CollectFromType(nestedType, moduleInterfaceSymbol, result, dedupe);
        }
    }

    /// <summary>
    /// 判断类型是否实现指定接口。
    /// </summary>
    /// <param name="typeSymbol">目标类型。</param>
    /// <param name="interfaceSymbol">接口符号。</param>
    /// <returns>实现则返回 <c>true</c>。</returns>
    private static bool Implements(INamedTypeSymbol typeSymbol, INamedTypeSymbol interfaceSymbol)
    {
        return typeSymbol.AllInterfaces.Any(item => SymbolEqualityComparer.Default.Equals(item, interfaceSymbol));
    }

    /// <summary>
    /// 构建命令工厂方法名称。
    /// </summary>
    /// <param name="typeSymbol">站点模块类型。</param>
    /// <returns>工厂方法后缀。</returns>
    private static string BuildFactoryName(INamedTypeSymbol typeSymbol)
    {
        var value = typeSymbol.Name;
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Where(char.IsLetterOrDigit))
        {
            builder.Append(character);
        }

        return builder.Length == 0 ? "Unknown" : builder.ToString();
    }

    /// <summary>
    /// 生成最终源码。
    /// </summary>
    /// <param name="modules">站点模块元数据集合。</param>
    /// <returns>源码文本。</returns>
    private static string GenerateSource(List<ModuleMetadata> modules)
    {
        var builder = new StringBuilder(16 * 1024);
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine("using System.CommandLine;");
        builder.AppendLine("using Zeayii.Luma.Abstractions.CommandLine;");
        builder.AppendLine("using Zeayii.Luma.CommandLine.Execution;");
        builder.AppendLine("using Zeayii.Luma.CommandLine.Options;");
        builder.AppendLine();
        builder.AppendLine("namespace Zeayii.Luma.CommandLine.Commands;");
        builder.AppendLine();
        builder.AppendLine("/// <summary>");
        builder.AppendLine("/// <b>编译期生成的站点命令扩展</b>");
        builder.AppendLine("/// </summary>");
        builder.AppendLine("internal static partial class RootCommandExtensions");
        builder.AppendLine("{");
        builder.AppendLine("    /// <summary>");
        builder.AppendLine("    /// 向根命令挂载所有自动发现的站点子命令。");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    /// <param name=\"rootCommand\">根命令对象。</param>");
        builder.AppendLine("    public static void AddGeneratedLumaCommands(this global::System.CommandLine.RootCommand rootCommand)");
        builder.AppendLine("    {");
        builder.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(rootCommand);");

        foreach (var module in modules)
        {
            builder.Append("        rootCommand.Add(Create").Append(module.FactoryName).AppendLine("Command());");
        }

        builder.AppendLine("    }");

        foreach (var module in modules)
        {
            builder.AppendLine();
            builder.Append("    /// <summary>").AppendLine();
            builder.Append("    /// 创建 ").Append(module.TypeDisplay).AppendLine(" 对应的子命令。");
            builder.AppendLine("    /// </summary>");
            builder.Append("    private static global::System.CommandLine.Command Create").Append(module.FactoryName).AppendLine("Command()");
            builder.AppendLine("    {");
            builder.AppendLine("        var commandName = " + module.TypeDisplay + ".CommandName;");
            builder.AppendLine("        var command = new global::System.CommandLine.Command(commandName)");
            builder.AppendLine("        {");
            builder.AppendLine("            Description = " + module.TypeDisplay + ".Description");
            builder.AppendLine("        };");
            builder.AppendLine("        var commonOptions = CommonCommandOptionsBuilder.AddTo(command);");
            builder.AppendLine("        command.SetAction(async (parseResult, cancellationToken) =>");
            builder.AppendLine("        {");
            builder.AppendLine("            var applicationOptions = CommonCommandOptionsBuilder.BuildApplicationOptions(parseResult, commonOptions, commandName);");
            builder.Append("            return await LumaCommandExecutor.ExecuteAsync<").Append(module.TypeDisplay).AppendLine(">(applicationOptions, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        });");
            builder.AppendLine("        return command;");
            builder.AppendLine("    }");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    /// <summary>
    /// 站点模块元数据。
    /// </summary>
    private sealed class ModuleMetadata
    {
        /// <summary>
        /// 初始化站点模块元数据。
        /// </summary>
        /// <param name="typeDisplay">类型全名。</param>
        /// <param name="factoryName">工厂名称。</param>
        public ModuleMetadata(string typeDisplay, string factoryName)
        {
            TypeDisplay = typeDisplay;
            FactoryName = factoryName;
        }

        /// <summary>
        /// 类型全名。
        /// </summary>
        public string TypeDisplay { get; }

        /// <summary>
        /// 工厂名称。
        /// </summary>
        public string FactoryName { get; }
    }
}

