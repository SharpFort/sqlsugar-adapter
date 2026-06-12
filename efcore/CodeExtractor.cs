using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeExtractor;

/// <summary>
/// C# 代码结构提取器 - 使用 Roslyn 编译器 API 精确解析
/// 提取: namespace, using, class, interface, struct, enum, record,
///       method, property, field, event, XML 文档注释
/// </summary>
public class Program
{
    static readonly JsonSerializerOptions _jsonOpts = new() 
    { 
        WriteIndented = true, 
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
    };

    public static int Main(string[] args)
    {
        var rootPath = args.Length > 0 ? args[0] : ".";
        var outputDir = Path.Combine(rootPath, "_code_analysis");
        Directory.CreateDirectory(outputDir);

        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  C# Roslyn 代码结构提取器 v2.0");
        Console.WriteLine($"  项目: {Path.GetFullPath(rootPath)}");
        Console.WriteLine("═══════════════════════════════════════════");

        // 1. 查找所有 .cs 文件
        var csFiles = Directory.GetFiles(rootPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\") && !f.Contains("/obj/") && !f.Contains("/bin/"))
            .OrderBy(f => f)
            .ToList();

        Console.WriteLine($"\n  找到 {csFiles.Count} 个 .cs 文件\n");

        // 2. 创建语法树集合
        var fileAnalyses = new List<FileAnalysis>();
        var treeMap = new Dictionary<string, SyntaxTree>();

        foreach (var filePath in csFiles)
        {
            var relPath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
            Console.WriteLine($"  [{fileAnalyses.Count + 1}/{csFiles.Count}] 解析: {relPath}");
            
            try
            {
                var code = File.ReadAllText(filePath);
                var tree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest));
                treeMap[relPath] = tree;
                
                var analysis = AnalyzeFile(tree, code, relPath, filePath);
                fileAnalyses.Add(analysis);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"    ⚠️ 解析失败: {ex.Message}");
            }
        }

        // 3. JSON 输出
        var projectName = Path.GetFileName(Path.GetFullPath(rootPath));
        var jsonReport = new
        {
            project = projectName,
            generatedAt = DateTimeOffset.Now,
            totalFiles = csFiles.Count,
            totalClasses = fileAnalyses.Sum(f => f.Classes.Count),
            totalInterfaces = fileAnalyses.Sum(f => f.Interfaces.Count),
            totalMethods = fileAnalyses.Sum(f => f.Methods.Count),
            totalProperties = fileAnalyses.Sum(f => f.Properties.Count),
            totalEnums = fileAnalyses.Sum(f => f.Enums.Count),
            totalRecords = fileAnalyses.Sum(f => f.Records.Count),
            files = fileAnalyses
        };

        var jsonPath = Path.Combine(outputDir, "code_structure.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(jsonReport, _jsonOpts), Encoding.UTF8);
        Console.WriteLine($"\n  ✅ JSON: {jsonPath} ({new FileInfo(jsonPath).Length / 1024}KB)");

        // 4. Markdown 报告
        var mdPath = Path.Combine(outputDir, "code_structure_report.md");
        GenerateMarkdown(fileAnalyses, mdPath, rootPath, projectName);
        Console.WriteLine($"  ✅ MD:   {mdPath} ({new FileInfo(mdPath).Length / 1024}KB)");

        // 5. 按文件夹摘要
        var summaryPath = Path.Combine(outputDir, "folder_summary.md");
        GenerateFolderSummary(fileAnalyses, summaryPath, rootPath);
        Console.WriteLine($"  ✅ 摘要: {summaryPath} ({new FileInfo(summaryPath).Length / 1024}KB)");

        Console.WriteLine("\n═══════════════════════════════════════════");
        Console.WriteLine("  提取完成！");
        Console.WriteLine("═══════════════════════════════════════════");
        return 0;
    }

    //=====================================================================
    // 单文件分析
    //=====================================================================
    static FileAnalysis AnalyzeFile(SyntaxTree tree, string sourceCode, string relPath, string fullPath)
    {
        var root = tree.GetCompilationUnitRoot();
        var analysis = new FileAnalysis
        {
            File = relPath,
            Namespace = root.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "",
            Usings = root.Usings.Select(u => u.Name?.ToString() ?? u.ToString()).ToList(),
            LineCount = sourceCode.Split('\n').Length
        };

        // 获取所有类型声明 (class/interface/struct/record)
        var typeDecls = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

        foreach (var type in typeDecls)
        {
            var doc = GetXmlDocSummary(type, sourceCode);
            var modifiers = string.Join(" ", type.Modifiers.Select(m => m.Text));
            var baseList = type.BaseList?.Types.Select(t => t.ToString()).ToList();

            if (type is ClassDeclarationSyntax cls)
            {
                analysis.Classes.Add(new ClassInfo
                {
                    Name = cls.Identifier.Text,
                    Modifiers = modifiers,
                    BaseTypes = baseList ?? new List<string>(),
                    TypeParameters = cls.TypeParameterList?.Parameters.Select(p => p.Identifier.Text).ToList(),
                    Summary = doc,
                    IsPartial = cls.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)),
                    IsAbstract = cls.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)),
                    IsStatic = cls.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)),
                    IsSealed = cls.Modifiers.Any(m => m.IsKind(SyntaxKind.SealedKeyword)),
                    LineStart = cls.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });
            }
            else if (type is InterfaceDeclarationSyntax iface)
            {
                analysis.Interfaces.Add(new InterfaceInfo
                {
                    Name = iface.Identifier.Text,
                    Modifiers = modifiers,
                    BaseTypes = baseList ?? new List<string>(),
                    TypeParameters = iface.TypeParameterList?.Parameters.Select(p => p.Identifier.Text).ToList(),
                    Summary = doc,
                    LineStart = iface.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });
            }
        }

        // 获取枚举 (EnumDeclarationSyntax 继承自 MemberDeclarationSyntax, 非 TypeDeclarationSyntax)
        var enums = root.DescendantNodes().OfType<EnumDeclarationSyntax>();
        foreach (var e in enums)
        {
            var doc = GetXmlDocSummary(e, sourceCode);
            analysis.Enums.Add(new EnumInfo
            {
                Name = e.Identifier.Text,
                Modifiers = string.Join(" ", e.Modifiers.Select(m => m.Text)),
                Summary = doc,
                Members = e.Members.Select(m => m.Identifier.Text).ToList(),
                LineStart = e.GetLocation().GetLineSpan().StartLinePosition.Line + 1
            });
        }

        // 获取记录 (RecordDeclarationSyntax)
        // RecordDeclarationSyntax 也继承自 TypeDeclarationSyntax，但某些 Roslyn 版本可能不包含
        try
        {
            var records = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .Where(t => t.Kind().ToString() == "RecordDeclaration")
                .ToList();
            foreach (var r in records)
            {
                analysis.Records.Add(new RecordInfo
                {
                    Name = r.Identifier.Text,
                    Modifiers = string.Join(" ", r.Modifiers.Select(m => m.Text)),
                    Summary = GetXmlDocSummary(r, sourceCode),
                    LineStart = r.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });
            }
        }
        catch { /* Record support not available in this Roslyn version */ }

        // 提取方法 (遍历每个类型)
        foreach (var type in typeDecls)
        {
            var methods = type.Members.OfType<MethodDeclarationSyntax>();
            foreach (var m in methods)
            {
                analysis.Methods.Add(new MethodInfo
                {
                    Name = m.Identifier.Text,
                    ReturnType = m.ReturnType.ToString(),
                    Parameters = m.ParameterList.Parameters.Select(p => new ParamInfo
                    {
                        Name = p.Identifier.Text,
                        Type = p.Type?.ToString() ?? "var",
                        HasDefault = p.Default != null,
                        DefaultValue = p.Default?.Value?.ToString()
                    }).ToList(),
                    Modifiers = string.Join(" ", m.Modifiers.Select(mm => mm.Text)),
                    Summary = GetXmlDocSummary(m, sourceCode),
                    ReturnsDoc = GetXmlDocReturns(m, sourceCode),
                    IsAsync = m.Modifiers.Any(mm => mm.IsKind(SyntaxKind.AsyncKeyword)),
                    IsVirtual = m.Modifiers.Any(mm => mm.IsKind(SyntaxKind.VirtualKeyword)),
                    IsOverride = m.Modifiers.Any(mm => mm.IsKind(SyntaxKind.OverrideKeyword)),
                    IsStatic = m.Modifiers.Any(mm => mm.IsKind(SyntaxKind.StaticKeyword)),
                    IsAbstract = m.Modifiers.Any(mm => mm.IsKind(SyntaxKind.AbstractKeyword)),
                    AccessModifier = GetAccessModifier(m.Modifiers),
                    LineStart = m.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });
            }

            // 提取属性
            var props = type.Members.OfType<PropertyDeclarationSyntax>();
            foreach (var p in props)
            {
                analysis.Properties.Add(new PropertyInfo
                {
                    Name = p.Identifier.Text,
                    Type = p.Type.ToString(),
                    Modifiers = string.Join(" ", p.Modifiers.Select(mm => mm.Text)),
                    Summary = GetXmlDocSummary(p, sourceCode),
                    HasGetter = p.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) ?? true,
                    HasSetter = p.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)) ?? false,
                    HasInit = p.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.InitAccessorDeclaration)) ?? false,
                    IsExpressionBodied = p.ExpressionBody != null,
                    AccessModifier = GetAccessModifier(p.Modifiers),
                    LineStart = p.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });
            }

            // 提取字段
            var fields = type.Members.OfType<FieldDeclarationSyntax>();
            foreach (var f in fields)
            {
                foreach (var v in f.Declaration.Variables)
                {
                    analysis.Fields.Add(new FieldInfo
                    {
                        Name = v.Identifier.Text,
                        Type = f.Declaration.Type.ToString(),
                        Modifiers = string.Join(" ", f.Modifiers.Select(mm => mm.Text)),
                        Summary = GetXmlDocSummary(f, sourceCode),
                        AccessModifier = GetAccessModifier(f.Modifiers),
                        LineStart = f.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                    });
                }
            }

            // 提取事件
            var events = type.Members.OfType<EventDeclarationSyntax>();
            foreach (var e in events)
            {
                analysis.Events.Add(new EventInfo
                {
                    Name = e.Identifier.Text,
                    Type = e.Type.ToString(),
                    Modifiers = string.Join(" ", e.Modifiers.Select(mm => mm.Text)),
                    Summary = GetXmlDocSummary(e, sourceCode),
                    LineStart = e.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });
            }
        }

        // 提取顶级委托 (delegate)
        var delegates = root.DescendantNodes().OfType<DelegateDeclarationSyntax>();
        foreach (var d in delegates)
        {
            analysis.Delegates.Add(new DelegateInfo
            {
                Name = d.Identifier.Text,
                ReturnType = d.ReturnType.ToString(),
                Parameters = d.ParameterList.Parameters.Select(p => p.Identifier.Text + ": " + p.Type).ToList(),
                Summary = GetXmlDocSummary(d, sourceCode),
                LineStart = d.GetLocation().GetLineSpan().StartLinePosition.Line + 1
            });
        }

        return analysis;
    }

    //=====================================================================
    // XML 文档注释提取
    //=====================================================================
    static string? GetXmlDocSummary(SyntaxNode node, string sourceCode)
    {
        var trivia = node.GetLeadingTrivia()
            .SelectMany(t => t.GetStructure() is DocumentationCommentTriviaSyntax doc ? doc.Content : Enumerable.Empty<SyntaxNode>());

        foreach (var item in trivia)
        {
            if (item is XmlElementSyntax el && el.StartTag.Name.ToString() == "summary")
            {
                return string.Join(" ", el.Content.Select(c => c.ToString().Trim().TrimStart('/').Trim()))
                    .Replace("  ", " ")
                    .Trim();
            }
        }
        return null;
    }

    static string? GetXmlDocReturns(SyntaxNode node, string sourceCode)
    {
        var trivia = node.GetLeadingTrivia()
            .SelectMany(t => t.GetStructure() is DocumentationCommentTriviaSyntax doc ? doc.Content : Enumerable.Empty<SyntaxNode>());

        foreach (var item in trivia)
        {
            if (item is XmlElementSyntax el && el.StartTag.Name.ToString() == "returns")
            {
                return string.Join(" ", el.Content.Select(c => c.ToString().Trim().TrimStart('/').Trim()))
                    .Replace("  ", " ").Trim();
            }
        }
        return null;
    }

    static string GetAccessModifier(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))) return "public";
        if (modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword))) return "private";
        if (modifiers.Any(m => m.IsKind(SyntaxKind.ProtectedKeyword)))
        {
            if (modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword))) return "protected internal";
            return "protected";
        }
        if (modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword))) return "internal";
        return "private"; // 默认
    }

    //=====================================================================
    // Markdown 报告生成
    //=====================================================================
    static void GenerateMarkdown(List<FileAnalysis> files, string path, string rootPath, string projectName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# C# 代码结构分析报告 (Roslyn v2.0)");
        sb.AppendLine();
        
        var totalClasses = files.Sum(f => f.Classes.Count);
        var totalInterfaces = files.Sum(f => f.Interfaces.Count);
        var totalMethods = files.Sum(f => f.Methods.Count);
        var totalProps = files.Sum(f => f.Properties.Count);
        var totalEnums = files.Sum(f => f.Enums.Count);
        var totalRecords = files.Sum(f => f.Records.Count);
        
        sb.AppendLine($"> **项目:** {projectName}  ");
        sb.AppendLine($"> **生成时间:** {DateTimeOffset.Now}  ");
        sb.AppendLine($"> **文件数:** {files.Count}  ");
        sb.AppendLine($"> **类:** {totalClasses} | **接口:** {totalInterfaces} | **方法:** {totalMethods} | **属性:** {totalProps} | **枚举:** {totalEnums} | **记录:** {totalRecords}  ");
        sb.AppendLine($"> **解析引擎:** Microsoft.CodeAnalysis.CSharp (Roslyn)  ");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // 总览表
        sb.AppendLine("## 📂 文件总览");
        sb.AppendLine();
        sb.AppendLine("| # | 文件 | 命名空间 | 类 | 接口 | 枚举 | 记录 | 方法 | 属性 | 字段 | 行数 |");
        sb.AppendLine("|---|------|----------|-----|------|------|------|------|------|------|------|");

        int idx = 0;
        foreach (var file in files)
        {
            idx++;
            var ns = (file.Namespace ?? "—").Replace("|", "\\|");
            var classesStr = string.Join(", ", file.Classes.Select(c => c.Name));
            var ifacesStr = string.Join(", ", file.Interfaces.Select(i => i.Name));
            var enumsStr = string.Join(", ", file.Enums.Select(e => e.Name));
            var recordsStr = string.Join(", ", file.Records.Select(r => r.Name));
            var classesOut = classesStr.Length > 0 ? classesStr : "—";
            var ifacesOut = ifacesStr.Length > 0 ? ifacesStr : "—";
            var enumsOut = enumsStr.Length > 0 ? enumsStr : "—";
            var recordsOut = recordsStr.Length > 0 ? recordsStr : "—";

            sb.AppendLine($"| {idx} | `{file.File}` | {ns} | {classesOut} | {ifacesOut} | {enumsOut} | {recordsOut} | {file.Methods.Count} | {file.Properties.Count} | {file.Fields.Count} | {file.LineCount} |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // 详细分析
        sb.AppendLine("## 📝 类型结构详细分析");
        sb.AppendLine();

        foreach (var file in files)
        {
            sb.AppendLine($"### `{file.File}`");
            sb.AppendLine();
            sb.AppendLine($"**命名空间:** `{file.Namespace ?? "—"}` | **行数:** {file.LineCount}");
            sb.AppendLine();

            // Using 列表
            if (file.Usings.Count > 0)
            {
                sb.AppendLine("**Using:** " + string.Join(", ", file.Usings.Select(u => $"`{u}`")));
                sb.AppendLine();
            }

            // 类
            foreach (var cls in file.Classes)
            {
                var dtors = cls.TypeParameters != null && cls.TypeParameters.Count > 0
                    ? $"<{string.Join(", ", cls.TypeParameters)}>" : "";
                var baseT = cls.BaseTypes.Count > 0
                    ? $" : {string.Join(", ", cls.BaseTypes)}" : "";
                var tags = $"{(cls.IsPartial ? " `partial`" : "")}{(cls.IsAbstract ? " `abstract`" : "")}{(cls.IsStatic ? " `static`" : "")}";

                sb.AppendLine($"#### 类: `{cls.Modifiers}{tags}` **{cls.Name}{dtors}**{baseT}");
                if (!string.IsNullOrEmpty(cls.Summary)) sb.AppendLine($"> {cls.Summary}");
                sb.AppendLine();
            }

            // 接口
            foreach (var iface in file.Interfaces)
            {
                var dtors = iface.TypeParameters != null && iface.TypeParameters.Count > 0
                    ? $"<{string.Join(", ", iface.TypeParameters)}>" : "";
                sb.AppendLine($"#### 接口: `{iface.Modifiers}` **{iface.Name}{dtors}**");
                if (!string.IsNullOrEmpty(iface.Summary)) sb.AppendLine($"> {iface.Summary}");
                sb.AppendLine();
            }

            // 枚举
            foreach (var enm in file.Enums)
            {
                sb.AppendLine($"#### 枚举: `{enm.Modifiers}` **{enm.Name}**");
                sb.AppendLine($"成员: {string.Join(", ", enm.Members.Select(m => $"`{m}`"))}");
                sb.AppendLine();
            }

            // 记录
            foreach (var rec in file.Records)
            {
                sb.AppendLine($"#### 记录: `{rec.Modifiers}` **{rec.Name}**");
                sb.AppendLine();
            }

            // 方法列表
            if (file.Methods.Count > 0)
            {
                sb.AppendLine("| # | 访问级 | 修饰符 | 返回类型 | 方法签名 | 说明 |");
                sb.AppendLine("|---|--------|--------|----------|----------|------|");
                int mi = 0;
                foreach (var m in file.Methods)
                {
                    mi++;
                    var paramList = string.Join(", ", m.Parameters.Select(p => $"{p.Type} {p.Name}"));
                    var summary = m.Summary ?? "—";
                    if (summary.Length > 100) summary = summary[..100] + "...";
                    sb.AppendLine($"| {mi} | `{m.AccessModifier}` | `{m.Modifiers}` | `{m.ReturnType}` | `{m.Name}({paramList})` | {summary} |");
                }
                sb.AppendLine();
            }

            // 属性列表
            if (file.Properties.Count > 0)
            {
                sb.AppendLine("| # | 访问级 | 类型 | 属性名 | get/set | 说明 |");
                sb.AppendLine("|---|--------|------|--------|---------|------|");
                int pi = 0;
                foreach (var p in file.Properties)
                {
                    pi++;
                    var gs = $"{(p.HasGetter ? "get" : "")}/{(p.HasSetter ? "set" : "")}{(p.HasInit ? "/init" : "")}";
                    var summary = p.Summary ?? "—";
                    if (summary.Length > 80) summary = summary[..80] + "...";
                    sb.AppendLine($"| {pi} | `{p.AccessModifier}` | `{p.Type}` | `{p.Name}` | {gs} | {summary} |");
                }
                sb.AppendLine();
            }

            // 字段列表
            if (file.Fields.Count > 0)
            {
                sb.AppendLine("| # | 访问级 | 类型 | 字段名 | 说明 |");
                sb.AppendLine("|---|--------|------|--------|------|");
                int fi = 0;
                foreach (var fld in file.Fields)
                {
                    fi++;
                    var summary = fld.Summary ?? "—";
                    if (summary.Length > 80) summary = summary[..80] + "...";
                    sb.AppendLine($"| {fi} | `{fld.AccessModifier}` | `{fld.Type}` | `{fld.Name}` | {summary} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    //=====================================================================
    // 文件夹级别摘要
    //=====================================================================
    static void GenerateFolderSummary(List<FileAnalysis> analyses, string path, string rootPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 文件夹级别代码摘要");
        sb.AppendLine();

        // 按目录分组
        var groups = analyses
            .GroupBy(a => Path.GetDirectoryName(a.File)?.Replace('\\', '/') ?? ".")
            .OrderBy(g => g.Key);

        sb.AppendLine("| 目录 | 文件数 | 类 | 接口 | 方法 | 属性 | 字段 | 枚举 | 委托 | 总行数 |");
        sb.AppendLine("|------|--------|-----|------|------|------|------|------|------|--------|");

        int totalFiles = 0, totalClasses = 0, totalInterfaces = 0, totalMethods = 0;
        int totalProperties = 0, totalFields = 0, totalEnums = 0, totalDelegates = 0, totalLines = 0;

        foreach (var g in groups)
        {
            var files = g.Count();
            var cls = g.Sum(x => x.Classes.Count);
            var ifc = g.Sum(x => x.Interfaces.Count);
            var met = g.Sum(x => x.Methods.Count);
            var prp = g.Sum(x => x.Properties.Count);
            var fld = g.Sum(x => x.Fields.Count);
            var enm = g.Sum(x => x.Enums.Count);
            var del = g.Sum(x => x.Delegates.Count);
            var lns = g.Sum(x => x.LineCount);

            sb.AppendLine($"| `{g.Key}/` | {files} | {cls} | {ifc} | {met} | {prp} | {fld} | {enm} | {del} | {lns} |");

            totalFiles += files; totalClasses += cls; totalInterfaces += ifc; totalMethods += met;
            totalProperties += prp; totalFields += fld; totalEnums += enm; totalDelegates += del; totalLines += lns;
        }

        sb.AppendLine($"| **总计** | **{totalFiles}** | **{totalClasses}** | **{totalInterfaces}** | **{totalMethods}** | **{totalProperties}** | **{totalFields}** | **{totalEnums}** | **{totalDelegates}** | **{totalLines}** |");
        sb.AppendLine();

        // 项目引用/依赖
        sb.AppendLine("## 🗂️ 项目文件索引");
        sb.AppendLine();
        foreach (var f in analyses)
        {
            var icon = f.Classes.Count > 0 ? "🔷" : f.Interfaces.Count > 0 ? "🔶" : "📄";
            sb.AppendLine($"- {icon} `{f.File}` — {f.Classes.Count}C {f.Interfaces.Count}I {f.Methods.Count}M {f.Properties.Count}P [{f.LineCount}行]");
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }
}

//=====================================================================
// 数据模型
//=====================================================================

public class FileAnalysis
{
    public string File { get; set; } = "";
    public string? Namespace { get; set; }
    public List<string> Usings { get; set; } = new();
    public int LineCount { get; set; }
    public List<ClassInfo> Classes { get; set; } = new();
    public List<InterfaceInfo> Interfaces { get; set; } = new();
    public List<EnumInfo> Enums { get; set; } = new();
    public List<RecordInfo> Records { get; set; } = new();
    public List<MethodInfo> Methods { get; set; } = new();
    public List<PropertyInfo> Properties { get; set; } = new();
    public List<FieldInfo> Fields { get; set; } = new();
    public List<EventInfo> Events { get; set; } = new();
    public List<DelegateInfo> Delegates { get; set; } = new();
}

public class ClassInfo
{
    public string Name { get; set; } = "";
    public string? Modifiers { get; set; }
    public List<string> BaseTypes { get; set; } = new();
    public List<string>? TypeParameters { get; set; }
    public string? Summary { get; set; }
    public bool IsPartial { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsStatic { get; set; }
    public bool IsSealed { get; set; }
    public int LineStart { get; set; }
}

public class InterfaceInfo
{
    public string Name { get; set; } = "";
    public string? Modifiers { get; set; }
    public List<string> BaseTypes { get; set; } = new();
    public List<string>? TypeParameters { get; set; }
    public string? Summary { get; set; }
    public int LineStart { get; set; }
}

public class EnumInfo
{
    public string Name { get; set; } = "";
    public string? Modifiers { get; set; }
    public string? Summary { get; set; }
    public List<string> Members { get; set; } = new();
    public int LineStart { get; set; }
}

public class RecordInfo
{
    public string Name { get; set; } = "";
    public string? Modifiers { get; set; }
    public string? Summary { get; set; }
    public int LineStart { get; set; }
}

public class MethodInfo
{
    public string Name { get; set; } = "";
    public string? ReturnType { get; set; }
    public List<ParamInfo> Parameters { get; set; } = new();
    public string? Modifiers { get; set; }
    public string? Summary { get; set; }
    public string? ReturnsDoc { get; set; }
    public bool IsAsync { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsOverride { get; set; }
    public bool IsStatic { get; set; }
    public bool IsAbstract { get; set; }
    public string AccessModifier { get; set; } = "private";
    public int LineStart { get; set; }
}

public class ParamInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool HasDefault { get; set; }
    public string? DefaultValue { get; set; }
}

public class PropertyInfo
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public string? Modifiers { get; set; }
    public string? Summary { get; set; }
    public bool HasGetter { get; set; }
    public bool HasSetter { get; set; }
    public bool HasInit { get; set; }
    public bool IsExpressionBodied { get; set; }
    public string AccessModifier { get; set; } = "private";
    public int LineStart { get; set; }
}

public class FieldInfo
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public string? Modifiers { get; set; }
    public string? Summary { get; set; }
    public string AccessModifier { get; set; } = "private";
    public int LineStart { get; set; }
}

public class EventInfo
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public string? Modifiers { get; set; }
    public string? Summary { get; set; }
    public int LineStart { get; set; }
}

public class DelegateInfo
{
    public string Name { get; set; } = "";
    public string? ReturnType { get; set; }
    public List<string> Parameters { get; set; } = new();
    public string? Summary { get; set; }
    public int LineStart { get; set; }
}
