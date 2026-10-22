using Server.Envir;
using Xunit;

namespace ProtocolTests
{
    public sealed class CustomSystemReportFormatterTests
    {
        [Fact]
        public void DisplayTextKeepsStableSummaryShape()
        {
            CustomSystemTextReport report = CreateReport();

            string expected =
                "模式: ValidateOnly\r\n" +
                "资源: 加载 2, 导入 3, 导出 4\r\n" +
                "怪物文件: 加载 5, 导出 6\r\n" +
                "怪物属性: 加载 7, 导入 8, 导出 9\r\n" +
                "怪物掉落: 加载物品 10, 加载组 11, 导入物品 12, 导入组 13, 导出物品 14, 导出组 15\r\n" +
                "掉落特殊行: 金币 16, 任务专属跳过 17, 缺失物品 1\r\n" +
                "怪物视觉: 加载 18, 导入 19, 导出 20\r\n" +
                "怪物行为: 加载 21, 导入 22, 导出 23\r\n" +
                "技能: 加载 24, 导入 25, 导出 26\r\n" +
                "模板校验: 怪物 27, 技能 28\r\n" +
                "缺失引用: 资源 1, 怪物 1, 技能 1, 物品 1\r\n" +
                "跳过: 怪物 29, 技能 30\r\n" +
                "待迁移 TODO: 1\r\n" +
                "错误: 1\r\n" +
                "\r\n" +
                "缺失资源:\r\n" +
                "ResourceA\r\n" +
                "\r\n" +
                "缺失怪物:\r\n" +
                "MonsterA\r\n" +
                "\r\n" +
                "缺失技能:\r\n" +
                "MagicA\r\n" +
                "\r\n" +
                "缺失物品:\r\n" +
                "ItemA\r\n" +
                "\r\n" +
                "待迁移 TODO 示例:\r\n" +
                "怪物 `A` 需要迁移\r\n" +
                "\r\n" +
                "错误示例:\r\n" +
                "FailureA\r\n";

            Assert.Equal(expected, report.ToDisplayText());
        }

        [Fact]
        public void TodoReportTextKeepsStableShape()
        {
            CustomSystemTextReport report = CreateReport();

            string expected =
                "自定义怪物与技能全量导出 TODO 报告\r\n" +
                "\r\n" +
                "待处理项: 1\r\n" +
                "错误: 1\r\n" +
                "\r\n" +
                "错误明细:\r\n" +
                "- FailureA\r\n" +
                "\r\n" +
                "- 怪物 `A` 需要迁移\r\n" +
                "- 已处理说明\r\n";

            Assert.Equal(expected, CustomSystemReportFormatter.BuildTodoReportText(report));
        }

        private static CustomSystemTextReport CreateReport()
        {
            CustomSystemTextReport report = new()
            {
                Mode = CustomSystemImportMode.ValidateOnly,
                ResourcesLoaded = 2,
                ResourcesImported = 3,
                ResourcesExported = 4,
                MonsterFilesLoaded = 5,
                MonstersExported = 6,
                MonsterStatsLoaded = 7,
                MonsterStatsImported = 8,
                MonsterStatsExported = 9,
                MonsterDropEntriesLoaded = 10,
                MonsterDropGroupsLoaded = 11,
                MonsterDropEntriesImported = 12,
                MonsterDropGroupsImported = 13,
                MonsterDropEntriesExported = 14,
                MonsterDropGroupsExported = 15,
                MonsterDropGoldLines = 16,
                MonsterDropQuestOnlyLines = 17,
                MonsterVisualsLoaded = 18,
                MonsterVisualsImported = 19,
                MonsterVisualsExported = 20,
                MonsterBehaviorsLoaded = 21,
                MonsterBehaviorsImported = 22,
                MonsterBehaviorsExported = 23,
                SkillFilesLoaded = 24,
                SkillsImported = 25,
                SkillsExported = 26,
                TemplateMonsterFilesLoaded = 27,
                TemplateSkillFilesLoaded = 28,
                MonstersSkipped = 29,
                SkillsSkipped = 30,
            };

            report.MissingResources.Add("ResourceA");
            report.MissingMonsters.Add("MonsterA");
            report.MissingMagics.Add("MagicA");
            report.MissingItems.Add("ItemA");
            report.TodoDetails.Add("怪物 `A` 需要迁移");
            report.TodoDetails.Add("已处理说明");
            report.Failures.Add("FailureA");

            return report;
        }
    }
}
