using Library;
using Library.SystemModels;
using MirDB;
using Server.DBModels;
using Server.Envir;
using System.Reflection;

string databaseRoot = args.Length > 0
    ? args[0]
    : @"D:\MirProject\Mir3\Debug\Server\Database\";

string outputRoot = args.Length > 1
    ? args[1]
    : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\generated\Mir3MonsterDrops"));

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

Mir2DropExporter exporter = new(session);
Mir2DropExportReport report = exporter.Export(outputRoot);

Console.WriteLine($"数据库: {session.SystemPath}");
Console.WriteLine($"输出目录: {outputRoot}");
Console.WriteLine($"已导出怪物: {report.MonstersExported}");
Console.WriteLine($"普通掉落行: {report.DropLines}");
Console.WriteLine($"GROUP 行: {report.GroupLines}");
Console.WriteLine($"组内物品行: {report.GroupItemLines}");
