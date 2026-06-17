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

string databaseRoot = customMode
    ? args.Length > 1 ? args[1] : @"D:\MirProject\Mir3\Debug\Server\Database\"
    : args.Length > 0 ? args[0] : @"D:\MirProject\Mir3\Debug\Server\Database\";

string outputRoot = customMode
    ? args.Length > 2 ? args[2] : @"D:\MirProject\Mir3\Debug\Server\Exports\CustomAllDatabase"
    : args.Length > 1 ? args[1] : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\generated\Mir3MonsterDrops"));

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
    Console.WriteLine($"怪物属性: {customReport.MonsterStatsLoaded}");
    Console.WriteLine($"怪物掉落物品: {customReport.MonsterDropEntriesLoaded}");
    Console.WriteLine($"怪物掉落组: {customReport.MonsterDropGroupsLoaded}");
    Console.WriteLine($"怪物行为: {customReport.MonsterBehaviorsLoaded}");
    Console.WriteLine($"技能: {customReport.SkillFilesLoaded}");
    Console.WriteLine($"错误: {customReport.Failures.Count}");

    if (customReport.Failures.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("错误示例:");
        foreach (string failure in customReport.Failures.Take(40))
            Console.WriteLine(failure);
    }

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
