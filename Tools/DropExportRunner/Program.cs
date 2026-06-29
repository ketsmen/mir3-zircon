using Library;
using Library.SystemModels;
using MirDB;
using Server.DBModels;
using Server.Envir;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;

const string DefaultDatabaseRootFallback = @"D:\MirProject\Mir3\Debug\Server\Database\";

string repositoryRoot = GetRepositoryRoot();
CommandLineOptions options = ParseCommandLine(args);

if (!options.Valid)
{
    Console.WriteLine(options.Error);
    PrintUsage();
    Environment.ExitCode = 1;
    return;
}

string[] commandArgs = options.Arguments;
string defaultDatabaseRoot = GetDefaultDatabaseRoot();
string mode = commandArgs.Length > 0 ? commandArgs[0] : "drops";
bool customAllMode = mode.Equals("custom-all", StringComparison.OrdinalIgnoreCase);
bool customValidateMode = mode.Equals("custom-validate", StringComparison.OrdinalIgnoreCase);
bool customMode = customAllMode || customValidateMode;
bool npcValidateMode = mode.Equals("npc-validate", StringComparison.OrdinalIgnoreCase);
bool dropValidateMode = mode.Equals("drop-validate", StringComparison.OrdinalIgnoreCase);
bool localValidateMode = mode.Equals("local-validate", StringComparison.OrdinalIgnoreCase);
bool dropsMode = mode.Equals("drops", StringComparison.OrdinalIgnoreCase);
bool explicitMode = customMode || npcValidateMode || dropValidateMode || localValidateMode || dropsMode;

if (commandArgs.Length > 0 && !explicitMode && !LooksLikeLegacyDatabaseArgument(mode))
{
    PrintUsage();
    Environment.ExitCode = 1;
    return;
}

if (!localValidateMode && (options.Json || !string.IsNullOrWhiteSpace(options.SummaryFile)))
{
    Console.WriteLine("--json 和 --summary-file 仅支持 local-validate。");
    PrintUsage();
    Environment.ExitCode = 1;
    return;
}

int maximumArguments = localValidateMode ? 5 : explicitMode ? 3 : 2;
if (commandArgs.Length > maximumArguments)
{
    PrintUsage();
    Environment.ExitCode = 1;
    return;
}

string databaseRoot = explicitMode
    ? commandArgs.Length > 1 && !string.IsNullOrWhiteSpace(commandArgs[1]) ? commandArgs[1] : defaultDatabaseRoot
    : commandArgs.Length > 0 && !string.IsNullOrWhiteSpace(commandArgs[0]) ? commandArgs[0] : defaultDatabaseRoot;

string outputRoot = customMode
    ? commandArgs.Length > 2 ? commandArgs[2] : @"D:\MirProject\Mir3\Debug\Server\Exports\CustomAllDatabase"
    : npcValidateMode
        ? commandArgs.Length > 2 ? commandArgs[2] : @"D:\MirProject\Mir3\Debug\Server\Envir\NPCs"
        : dropValidateMode
            ? commandArgs.Length > 2 ? commandArgs[2] : Path.Combine(repositoryRoot, "Envir", "Drops")
            : explicitMode
                ? commandArgs.Length > 2 ? commandArgs[2] : Path.Combine(repositoryRoot, "generated", "Mir3MonsterDrops")
                : commandArgs.Length > 1 ? commandArgs[1] : Path.Combine(repositoryRoot, "generated", "Mir3MonsterDrops");

string localCustomRoot = localValidateMode && commandArgs.Length > 2 ? commandArgs[2] : Path.Combine(repositoryRoot, "Envir", "Custom");
string localNpcRoot = localValidateMode && commandArgs.Length > 3 ? commandArgs[3] : Path.Combine(repositoryRoot, "Envir", "NPCs");
string localDropRoot = localValidateMode && commandArgs.Length > 4 ? commandArgs[4] : Path.Combine(repositoryRoot, "Envir", "Drops");

databaseRoot = NormalizeDirectoryArgument(databaseRoot);
outputRoot = NormalizeDirectoryArgument(outputRoot);
localCustomRoot = NormalizeDirectoryArgument(localCustomRoot);
localNpcRoot = NormalizeDirectoryArgument(localNpcRoot);
localDropRoot = NormalizeDirectoryArgument(localDropRoot);

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

if (localValidateMode)
{
    RunLocalValidation(session, localCustomRoot, localNpcRoot, localDropRoot, options.Json, options.SummaryFile);
    return;
}

if (customAllMode)
{
    CustomSystemDatabaseExporter customExporter = new(session);
    CustomSystemTextReport customReport = customExporter.ExportAllDatabaseMonstersAndSkills(outputRoot, overwrite: true);
    string customRepositoryRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\.."));
    LegacyMonsterVisualTextCompleter.Complete(outputRoot, customRepositoryRoot, customReport);

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

if (dropValidateMode)
{
    var result = ValidateMir2Drops(session, outputRoot);

    Console.WriteLine($"数据库: {session.SystemPath}");
    Console.WriteLine($"验证目录: {outputRoot}");
    PrintValidationTupleResult("Mir2 掉落文本", result);

    if (!result.Success)
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

static string GetDefaultDatabaseRoot()
{
    return FirstNonEmptyEnvironmentVariable("MIR3_DATABASE_ROOT", "ZIRCON_DATABASE_ROOT") ?? DefaultDatabaseRootFallback;
}

static string? FirstNonEmptyEnvironmentVariable(params string[] names)
{
    foreach (string name in names)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
            return value;
    }

    return null;
}

static string GetRepositoryRoot()
{
    string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\.."));

    return Directory.Exists(Path.Combine(root, "Tools")) && Directory.Exists(Path.Combine(root, "Envir"))
        ? root
        : Directory.GetCurrentDirectory();
}

static void RunLocalValidation(Session session, string customRoot, string npcRoot, string dropRoot, bool jsonOutput, string summaryFile)
{
    LocalValidationReport report = BuildLocalValidationReport(session, customRoot, npcRoot, dropRoot);

    if (jsonOutput || !string.IsNullOrWhiteSpace(summaryFile))
        WriteValidationJson(report, summaryFile, jsonOutput);

    if (!jsonOutput)
        PrintLocalValidationReport(report, summaryFile);

    if (!report.Success)
        Environment.ExitCode = 1;
}

static LocalValidationReport BuildLocalValidationReport(Session session, string customRoot, string npcRoot, string dropRoot)
{
    ValidationCheckReport[] checks =
    [
        ToCheckReport("custom", "自定义系统", ValidateCustomSystem(session, customRoot)),
        ToCheckReport("npc", "文本 NPC", ValidateTextNpcs(session, npcRoot)),
        ToCheckReport("mir2Drops", "Mir2 掉落文本", ValidateMir2Drops(session, dropRoot)),
    ];

    return new LocalValidationReport
    {
        GeneratedAt = DateTimeOffset.Now,
        Database = session.SystemPath,
        CustomRoot = customRoot,
        NpcRoot = npcRoot,
        DropRoot = dropRoot,
        Total = checks.Length,
        Passed = checks.Count(x => x.Success),
        Success = checks.All(x => x.Success),
        Checks = checks.ToList()
    };
}

static ValidationCheckReport ToCheckReport(string key, string name, (bool Success, string Summary, List<string> Errors, List<string> Warnings) result)
{
    return new ValidationCheckReport
    {
        Key = key,
        Name = name,
        Success = result.Success,
        Summary = result.Summary,
        Errors = result.Errors,
        Warnings = result.Warnings
    };
}

static void PrintLocalValidationReport(LocalValidationReport report, string summaryFile)
{
    Console.WriteLine("本地文本校验");
    Console.WriteLine($"数据库: {report.Database}");
    Console.WriteLine($"自定义系统: {report.CustomRoot}");
    Console.WriteLine($"文本 NPC: {report.NpcRoot}");
    Console.WriteLine($"Mir2 掉落: {report.DropRoot}");

    if (!string.IsNullOrWhiteSpace(summaryFile))
        Console.WriteLine($"摘要文件: {Path.GetFullPath(summaryFile)}");

    foreach (ValidationCheckReport check in report.Checks)
    {
        Console.WriteLine();
        PrintValidationCheckResult(check);
    }

    Console.WriteLine();
    Console.WriteLine($"汇总: {report.Passed}/{report.Total} 通过");
}

static void WriteValidationJson(LocalValidationReport report, string summaryFile, bool writeStdout)
{
    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });

    if (!string.IsNullOrWhiteSpace(summaryFile))
    {
        string path = Path.GetFullPath(summaryFile);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, json);
    }

    if (writeStdout)
        Console.WriteLine(json);
}

static (bool Success, string Summary, List<string> Errors, List<string> Warnings) ValidateCustomSystem(Session session, string customRoot)
{
    try
    {
        CustomSystemDatabaseImporter importer = new(session);
        CustomSystemTextReport report = importer.Import(customRoot, CustomSystemImportMode.ValidateOnly);
        List<string> errors = report.Failures.Take(40).ToList();

        string summary =
            $"资源文件={report.ResourcesLoaded}, 怪物文件={report.MonsterFilesLoaded}, 技能文件={report.SkillFilesLoaded}, " +
            $"模板怪物={report.TemplateMonsterFilesLoaded}, 模板技能={report.TemplateSkillFilesLoaded}, " +
            $"怪物属性={report.MonsterStatsLoaded}, 掉落物品={report.MonsterDropEntriesLoaded}, 掉落组={report.MonsterDropGroupsLoaded}, " +
            $"视觉={report.MonsterVisualsLoaded}, 行为={report.MonsterBehaviorsLoaded}, 错误={report.Failures.Count}";

        return (report.Failures.Count == 0, summary, errors, []);
    }
    catch (Exception ex)
    {
        return (false, "校验未完成。", [$"{ex.GetType().Name}: {ex.Message}"], []);
    }
}

static (bool Success, string Summary, List<string> Errors, List<string> Warnings) ValidateTextNpcs(Session session, string npcRoot)
{
    try
    {
        TextNpcDatabaseImporter importer = new(session);
        TextNpcDatabaseImportReport report = importer.Import(npcRoot, TextNpcDatabaseImportMode.ValidateOnly);

        int loadErrors = report.LoadReport?.Failures.Count ?? 0;
        int mappingErrors = report.Failures.Count;
        List<string> errors = [];

        if (report.LoadReport != null)
            errors.AddRange(report.LoadReport.Failures.Take(40));

        errors.AddRange(report.Failures.Take(40));

        return (loadErrors == 0 && mappingErrors == 0, report.ToDisplayText(), errors, []);
    }
    catch (Exception ex)
    {
        return (false, "校验未完成。", [$"{ex.GetType().Name}: {ex.Message}"], []);
    }
}

static (bool Success, string Summary, List<string> Errors, List<string> Warnings) ValidateMir2Drops(Session session, string dropRoot)
{
    try
    {
        Mir2DropImporter importer = new(session);
        Mir2DropOverrideLoadResult result = importer.BuildCompiledOverrides(dropRoot);
        Mir2DropImportReport report = result.Report;
        int missingMonsters = report.MissingMonsters.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int missingItems = report.MissingItems.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        List<string> errors = [];
        List<string> warnings = [];

        errors.AddRange(report.Failures.Take(40));
        errors.AddRange(report.MissingMonsters
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .Select(x => $"缺失怪物: {x}"));
        errors.AddRange(report.MissingItems
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .Select(x => $"缺失物品: {x}"));

        warnings.AddRange(report.QuestOnlyLines
            .Take(20)
            .Select(x => $"任务专属行: {x}"));

        string summary = report.ToDisplayText() + $"\r\n可用覆盖怪物: {result.Overrides.Count}";
        bool success = report.Failures.Count == 0 && missingMonsters == 0 && missingItems == 0;

        return (success, summary, errors, warnings);
    }
    catch (Exception ex)
    {
        return (false, "校验未完成。", [$"{ex.GetType().Name}: {ex.Message}"], []);
    }
}

static void PrintValidationTupleResult(string name, (bool Success, string Summary, List<string> Errors, List<string> Warnings) result)
{
    PrintValidationCheckResult(new ValidationCheckReport
    {
        Name = name,
        Success = result.Success,
        Summary = result.Summary,
        Errors = result.Errors,
        Warnings = result.Warnings
    });
}

static void PrintValidationCheckResult(ValidationCheckReport result)
{
    Console.WriteLine($"{(result.Success ? "[PASS]" : "[FAIL]")} {result.Name}");

    foreach (string line in SplitLines(result.Summary))
        Console.WriteLine($"  {line}");

    if (result.Errors?.Count > 0)
    {
        Console.WriteLine("  错误示例:");
        foreach (string error in result.Errors.Take(80))
            Console.WriteLine($"    {error}");
    }

    if (result.Warnings?.Count > 0)
    {
        Console.WriteLine("  警告:");
        foreach (string warning in result.Warnings.Take(40))
            Console.WriteLine($"    {warning}");
    }
}

static IEnumerable<string> SplitLines(string value)
{
    if (string.IsNullOrWhiteSpace(value))
        yield break;

    foreach (string line in value.Replace("\r\n", "\n").Split('\n'))
    {
        if (string.IsNullOrWhiteSpace(line)) continue;

        yield return line;
    }
}

static void PrintUsage()
{
    Console.WriteLine("用法:");
    Console.WriteLine("  DropExportRunner [databaseRoot] [outputRoot]");
    Console.WriteLine("  DropExportRunner drops [databaseRoot] [outputRoot]");
    Console.WriteLine("  DropExportRunner custom-all [databaseRoot] [outputRoot]");
    Console.WriteLine("  DropExportRunner custom-validate [databaseRoot] [customRoot]");
    Console.WriteLine("  DropExportRunner npc-validate [databaseRoot] [npcTextRoot]");
    Console.WriteLine("  DropExportRunner drop-validate [databaseRoot] [dropTextRoot]");
    Console.WriteLine("  DropExportRunner local-validate [databaseRoot] [customRoot] [npcTextRoot] [dropTextRoot] [--json] [--summary-file path]");
}

static CommandLineOptions ParseCommandLine(string[] args)
{
    CommandLineOptions options = new();
    List<string> arguments = [];

    for (int i = 0; i < args.Length; i++)
    {
        string arg = args[i];

        if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
        {
            options.Json = true;
            continue;
        }

        if (arg.Equals("--summary-file", StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 >= args.Length)
                return CommandLineOptions.Invalid("--summary-file 需要路径参数。");

            options.SummaryFile = args[++i];
            continue;
        }

        if (arg.StartsWith("--summary-file=", StringComparison.OrdinalIgnoreCase))
        {
            options.SummaryFile = arg["--summary-file=".Length..];
            if (string.IsNullOrWhiteSpace(options.SummaryFile))
                return CommandLineOptions.Invalid("--summary-file 需要路径参数。");

            continue;
        }

        arguments.Add(arg);
    }

    options.Arguments = arguments.ToArray();
    return options;
}

public sealed class CommandLineOptions
{
    public string[] Arguments { get; set; } = [];
    public bool Json { get; set; }
    public string SummaryFile { get; set; } = string.Empty;
    public bool Valid { get; set; } = true;
    public string Error { get; set; } = string.Empty;

    public static CommandLineOptions Invalid(string error)
    {
        return new CommandLineOptions
        {
            Valid = false,
            Error = error
        };
    }
}

public sealed class LocalValidationReport
{
    public DateTimeOffset GeneratedAt { get; set; }
    public string Database { get; set; } = string.Empty;
    public string CustomRoot { get; set; } = string.Empty;
    public string NpcRoot { get; set; } = string.Empty;
    public string DropRoot { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int Passed { get; set; }
    public int Total { get; set; }
    public List<ValidationCheckReport> Checks { get; set; } = [];
}

public sealed class ValidationCheckReport
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
