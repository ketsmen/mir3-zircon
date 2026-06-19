using Library;
using Library.SystemModels;
using MirDB;
using Server.DBModels;
using Server.Envir;
using System.Reflection;

string mode = args.Length > 0 ? args[0] : "drops";
bool customAllMode = mode.Equals("custom-all", StringComparison.OrdinalIgnoreCase);
bool customValidateMode = mode.Equals("custom-validate", StringComparison.OrdinalIgnoreCase);
bool customMode = customAllMode || customValidateMode;
bool npcValidateMode = mode.Equals("npc-validate", StringComparison.OrdinalIgnoreCase);
bool dropsMode = mode.Equals("drops", StringComparison.OrdinalIgnoreCase);
bool explicitMode = customMode || npcValidateMode || dropsMode;

if (args.Length > 0 && !explicitMode && !LooksLikeLegacyDatabaseArgument(mode))
{
    PrintUsage();
    Environment.ExitCode = 1;
    return;
}

string databaseRoot = customMode
    ? args.Length > 1 ? args[1] : @"D:\MirProject\Mir3\Debug\Server\Database\"
    : npcValidateMode
        ? args.Length > 1 ? args[1] : @"D:\MirProject\Mir3\Debug\Server\Database\"
    : explicitMode
        ? args.Length > 1 ? args[1] : @"D:\MirProject\Mir3\Debug\Server\Database\"
        : args.Length > 0 ? args[0] : @"D:\MirProject\Mir3\Debug\Server\Database\";

string outputRoot = customMode
    ? args.Length > 2 ? args[2] : @"D:\MirProject\Mir3\Debug\Server\Exports\CustomAllDatabase"
    : npcValidateMode
        ? args.Length > 2 ? args[2] : @"D:\MirProject\Mir3\Debug\Server\Envir\NPCs"
    : explicitMode
        ? args.Length > 2 ? args[2] : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\generated\Mir3MonsterDrops"))
        : args.Length > 1 ? args[1] : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\generated\Mir3MonsterDrops"));

databaseRoot = NormalizeDirectoryArgument(databaseRoot);
outputRoot = NormalizeDirectoryArgument(outputRoot);

if (!databaseRoot.EndsWith(Path.DirectorySeparatorChar) && !databaseRoot.EndsWith(Path.AltDirectorySeparatorChar))
    databaseRoot += Path.DirectorySeparatorChar;

Encryption.SetKey(null);

Session session = new(SessionMode.System, databaseRoot)
{
    BackUp = false,
    BackUpDelay = 0
};

session.Initialize(
    Assembly.GetAssembly(typeof(ItemInfo))!,
    Assembly.GetAssembly(typeof(AccountInfo))!);

if (customAllMode)
{
    CustomSystemDatabaseExporter customExporter = new(session);
    CustomSystemTextReport customReport = customExporter.ExportAllDatabaseMonstersAndSkills(outputRoot, overwrite: true);
    string repositoryRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\.."));
    LegacyMonsterVisualTextCompleter.Complete(outputRoot, repositoryRoot, customReport);

    Console.WriteLine($"数据库: {session.SystemPath}");
    Console.WriteLine($"输出目录: {outputRoot}");
    Console.WriteLine($"已导出怪物: {customReport.MonstersExported}");
    Console.WriteLine($"已导出技能: {customReport.SkillsExported}");
    Console.WriteLine($"已导出资源: {customReport.ResourcesExported}");
    Console.WriteLine($"真实自定义视觉: {customReport.MonsterVisualsExported}");
    Console.WriteLine($"真实自定义行为: {customReport.MonsterBehaviorsExported}");
    Console.WriteLine($"待迁移 TODO: {customReport.PendingTodoCount}");
    Console.WriteLine($"错误: {customReport.Failures.Count}");
    Console.WriteLine($"TODO 报告: {Path.Combine(outputRoot, "TODO报告.txt")}");

    if (customReport.Failures.Count > 0)
    {
        Environment.ExitCode = 1;
        Console.WriteLine();
        Console.WriteLine("错误示例:");
        foreach (string failure in customReport.Failures.Take(20))
            Console.WriteLine(failure);
    }

    return;
}

if (customValidateMode)
{
    CustomSystemDatabaseImporter importer = new(session);
    CustomSystemTextReport customReport = importer.Import(outputRoot, CustomSystemImportMode.ValidateOnly);

    Console.WriteLine($"数据库: {session.SystemPath}");
    Console.WriteLine($"验证目录: {outputRoot}");
    Console.WriteLine($"资源文件: {customReport.ResourcesLoaded}");
    Console.WriteLine($"怪物文件: {customReport.MonsterFilesLoaded}");
    Console.WriteLine($"技能文件: {customReport.SkillFilesLoaded}");
    if (customReport.TemplateMonsterFilesLoaded > 0 || customReport.TemplateSkillFilesLoaded > 0)
        Console.WriteLine($"模板校验: 怪物 {customReport.TemplateMonsterFilesLoaded}, 技能 {customReport.TemplateSkillFilesLoaded}");
    Console.WriteLine($"怪物属性: {customReport.MonsterStatsLoaded}");
    Console.WriteLine($"怪物掉落物品: {customReport.MonsterDropEntriesLoaded}");
    Console.WriteLine($"怪物掉落组: {customReport.MonsterDropGroupsLoaded}");
    Console.WriteLine($"怪物视觉: {customReport.MonsterVisualsLoaded}");
    Console.WriteLine($"怪物行为: {customReport.MonsterBehaviorsLoaded}");
    Console.WriteLine($"错误: {customReport.Failures.Count}");

    if (customReport.Failures.Count > 0)
    {
        Environment.ExitCode = 1;
        Console.WriteLine();
        Console.WriteLine("错误示例:");
        foreach (string failure in customReport.Failures.Take(40))
            Console.WriteLine(failure);
    }

    return;
}

if (npcValidateMode)
{
    TextNpcDatabaseImporter importer = new(session);
    TextNpcDatabaseImportReport npcReport = importer.Import(outputRoot, TextNpcDatabaseImportMode.ValidateOnly);
    int loadErrors = npcReport.LoadReport?.Failures.Count ?? 0;
    int mappingErrors = npcReport.Failures.Count;

    Console.WriteLine($"数据库: {session.SystemPath}");
    Console.WriteLine($"验证目录: {outputRoot}");
    Console.WriteLine(npcReport.ToDisplayText());

    if (loadErrors > 0 && npcReport.LoadReport != null)
    {
        Console.WriteLine();
        Console.WriteLine("解析错误示例:");
        foreach (string failure in npcReport.LoadReport.Failures.Take(40))
            Console.WriteLine(failure);
    }

    if (mappingErrors > 0)
    {
        Console.WriteLine();
        Console.WriteLine("数据库映射错误示例:");
        foreach (string failure in npcReport.Failures.Take(40))
            Console.WriteLine(failure);
    }

    if (loadErrors > 0 || mappingErrors > 0)
        Environment.ExitCode = 1;

    return;
}

Mir2DropExporter exporter = new(session);
Mir2DropExportReport report = exporter.Export(outputRoot);

Console.WriteLine($"数据库: {session.SystemPath}");
Console.WriteLine($"输出目录: {outputRoot}");
Console.WriteLine($"已导出怪物: {report.MonstersExported}");
Console.WriteLine($"普通掉落行: {report.DropLines}");
Console.WriteLine($"GROUP 行: {report.GroupLines}");
Console.WriteLine($"组内物品行: {report.GroupItemLines}");

static bool LooksLikeLegacyDatabaseArgument(string value)
{
    if (string.IsNullOrWhiteSpace(value)) return false;
    if (Directory.Exists(value)) return true;
    if (Path.IsPathRooted(value)) return true;
    if (value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar)) return true;
    if (value.Equals(".", StringComparison.Ordinal) || value.Equals("..", StringComparison.Ordinal)) return true;

    return false;
}

static string NormalizeDirectoryArgument(string value)
{
    return string.IsNullOrWhiteSpace(value) ? value : Path.GetFullPath(value);
}

static void PrintUsage()
{
    Console.WriteLine("用法:");
    Console.WriteLine("  DropExportRunner [databaseRoot] [outputRoot]");
    Console.WriteLine("  DropExportRunner drops [databaseRoot] [outputRoot]");
    Console.WriteLine("  DropExportRunner custom-all [databaseRoot] [outputRoot]");
    Console.WriteLine("  DropExportRunner custom-validate [databaseRoot] [customRoot]");
    Console.WriteLine("  DropExportRunner npc-validate [databaseRoot] [npcTextRoot]");
}
