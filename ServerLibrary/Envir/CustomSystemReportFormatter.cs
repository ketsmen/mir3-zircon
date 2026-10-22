using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Server.Envir
{
    public static class CustomSystemReportFormatter
    {
        public static string BuildDisplayText(CustomSystemTextReport report)
        {
            StringBuilder sb = new();
            sb.AppendLine($"模式: {report.Mode}");
            sb.AppendLine($"资源: 加载 {report.ResourcesLoaded}, 导入 {report.ResourcesImported}, 导出 {report.ResourcesExported}");
            sb.AppendLine($"怪物文件: 加载 {report.MonsterFilesLoaded}, 导出 {report.MonstersExported}");
            sb.AppendLine($"怪物属性: 加载 {report.MonsterStatsLoaded}, 导入 {report.MonsterStatsImported}, 导出 {report.MonsterStatsExported}");
            sb.AppendLine($"怪物掉落: 加载物品 {report.MonsterDropEntriesLoaded}, 加载组 {report.MonsterDropGroupsLoaded}, 导入物品 {report.MonsterDropEntriesImported}, 导入组 {report.MonsterDropGroupsImported}, 导出物品 {report.MonsterDropEntriesExported}, 导出组 {report.MonsterDropGroupsExported}");
            sb.AppendLine($"掉落特殊行: 金币 {report.MonsterDropGoldLines}, 任务专属跳过 {report.MonsterDropQuestOnlyLines}, 缺失物品 {report.MissingItems.Distinct(StringComparer.OrdinalIgnoreCase).Count()}");
            sb.AppendLine($"怪物视觉: 加载 {report.MonsterVisualsLoaded}, 导入 {report.MonsterVisualsImported}, 导出 {report.MonsterVisualsExported}");
            sb.AppendLine($"怪物行为: 加载 {report.MonsterBehaviorsLoaded}, 导入 {report.MonsterBehaviorsImported}, 导出 {report.MonsterBehaviorsExported}");
            sb.AppendLine($"技能: 加载 {report.SkillFilesLoaded}, 导入 {report.SkillsImported}, 导出 {report.SkillsExported}");
            if (report.TemplateMonsterFilesLoaded > 0 || report.TemplateSkillFilesLoaded > 0)
                sb.AppendLine($"模板校验: 怪物 {report.TemplateMonsterFilesLoaded}, 技能 {report.TemplateSkillFilesLoaded}");
            sb.AppendLine($"缺失引用: 资源 {report.MissingResources.Distinct(StringComparer.OrdinalIgnoreCase).Count()}, 怪物 {report.MissingMonsters.Distinct(StringComparer.OrdinalIgnoreCase).Count()}, 技能 {report.MissingMagics.Distinct(StringComparer.OrdinalIgnoreCase).Count()}, 物品 {report.MissingItems.Distinct(StringComparer.OrdinalIgnoreCase).Count()}");
            sb.AppendLine($"跳过: 怪物 {report.MonstersSkipped}, 技能 {report.SkillsSkipped}");
            sb.AppendLine($"待迁移 TODO: {report.PendingTodoCount}");
            sb.AppendLine($"错误: {report.Failures.Count}");

            AppendSamples(sb, "缺失资源", report.MissingResources);
            AppendSamples(sb, "缺失怪物", report.MissingMonsters);
            AppendSamples(sb, "缺失技能", report.MissingMagics);
            AppendSamples(sb, "缺失物品", report.MissingItems);
            AppendSamples(sb, "待迁移 TODO 示例", report.PendingTodoDetails);

            if (report.Failures.Count > 0)
                AppendSamples(sb, "错误示例", report.Failures);

            return sb.ToString();
        }

        public static string BuildTodoReportText(CustomSystemTextReport report)
        {
            StringBuilder sb = new();
            sb.AppendLine("自定义怪物与技能全量导出 TODO 报告");
            sb.AppendLine();
            sb.AppendLine($"待处理项: {report.PendingTodoCount}");
            sb.AppendLine($"错误: {report.Failures.Count}");
            sb.AppendLine();

            if (report.Failures.Count > 0)
            {
                sb.AppendLine("错误明细:");
                foreach (string failure in report.Failures)
                    sb.AppendLine("- " + failure);
                sb.AppendLine();
            }

            foreach (string todo in report.TodoDetails)
                sb.AppendLine("- " + todo);

            return sb.ToString();
        }

        private static void AppendSamples(StringBuilder sb, string title, IEnumerable<string> values)
        {
            List<string> samples = values?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .ToList() ?? [];

            if (samples.Count == 0) return;

            sb.AppendLine();
            sb.AppendLine($"{title}:");
            sb.AppendLine(string.Join(Environment.NewLine, samples));
        }
    }
}
