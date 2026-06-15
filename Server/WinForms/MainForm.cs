using Library;
using MirDB;
using PluginCore;
using Server.Envir;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace Server.WinForms
{
    public sealed class MainForm : Form
    {
        private readonly ContextMenuStrip _logMenu = new ContextMenuStrip();
        private readonly SplitContainer _workspaceSplit = new SplitContainer();
        private readonly TreeView _navigationTree = new TreeView();
        private readonly Panel _logPanel = new Panel();
        private readonly TabControl _logTabs = new TabControl();
        private readonly TabPage _systemLogPage = new TabPage("系统日志");
        private readonly TabPage _chatLogPage = new TabPage("聊天日志");
        private readonly ListBox _systemLogList = new ListBox();
        private readonly ListBox _chatLogList = new ListBox();
        private readonly ToolStripTextBox _navigationFilterText = new ToolStripTextBox();
        private readonly ToolStripTextBox _logFilterText = new ToolStripTextBox();
        private readonly ToolStripLabel _logStatusLabel = new ToolStripLabel();
        private readonly List<string> _systemLogs = new List<string>();
        private readonly List<string> _chatLogs = new List<string>();
        private readonly FlowLayoutPanel _metricsPanel = new FlowLayoutPanel();
        private readonly Dictionary<string, Label> _metricLabels = new Dictionary<string, Label>();
        private readonly Panel _statusPanel = new Panel();
        private readonly Label _statusLabel = new Label();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private ToolStripMenuItem _pluginsMenu;
        private ToolStripMenuItem _startMenuItem;
        private ToolStripMenuItem _stopMenuItem;
        private ToolStripButton _startToolButton;
        private ToolStripButton _stopToolButton;
        private Session _pluginSession;
        private DatabaseEditorForm _databaseEditorForm;
        private DatabaseEditorForm _onlineDatabaseEditorForm;
        private ConfigEditorForm _configEditorForm;
        private DiagnosticsForm _diagnosticsForm;
        private MapViewerForm _mapViewerForm;
        private const int MaxLogLines = 2000;
        private static readonly string UiCachePath = Path.Combine(Application.StartupPath, "Server.WinForms.cache.json");

        public MainForm()
        {
            Text = "Zircon 服务端管理器";
            Width = 1100;
            Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            BuildLayout();

            _timer.Interval = 500;
            _timer.Tick += (_, _) => UpdateInterface();
            _timer.Start();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LoadUiCache();
            LoadPlugins();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (SEnvir.EnvirThread != null)
            {
                DialogResult result = MessageBox.Show(
                    this,
                    "服务端仍在运行，关闭管理器会停止服务端。\r\n\r\n确定停止并退出吗？",
                    "退出服务端管理器",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (!ConfirmDatabaseEditorsBeforeExit())
            {
                e.Cancel = true;
                return;
            }

            SaveUiCache();
            base.OnFormClosing(e);

            if (SEnvir.EnvirThread != null)
            {
                SEnvir.Started = false;
                while (SEnvir.EnvirThread != null)
                {
                    Application.DoEvents();
                    Thread.Sleep(1);
                }
            }

            ConfigReader.Save(typeof(Config).Assembly);
        }

        private bool ConfirmDatabaseEditorsBeforeExit()
        {
            if (_databaseEditorForm != null && !_databaseEditorForm.IsDisposed && _databaseEditorForm.HasUnsavedChanges)
            {
                MessageBox.Show(
                    this,
                    "离线数据库编辑器仍有未保存修改。请先保存或关闭数据库编辑器，再退出服务端管理器。",
                    "未保存数据库",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                ActivateForm(_databaseEditorForm);
                return false;
            }

            if (_onlineDatabaseEditorForm != null && !_onlineDatabaseEditorForm.IsDisposed && _onlineDatabaseEditorForm.HasUnsavedChanges)
            {
                MessageBox.Show(
                    this,
                    "在线数据库编辑器仍有未保存状态。请先保存或关闭数据库编辑器，再退出服务端管理器。",
                    "未保存数据库",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                ActivateForm(_onlineDatabaseEditorForm);
                return false;
            }

            return true;
        }

        private void BuildLayout()
        {
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));

            MenuStrip menu = new MenuStrip();
            menu.Margin = Padding.Empty;

            ToolStripMenuItem serverMenu = new ToolStripMenuItem("服务端");
            _startMenuItem = (ToolStripMenuItem)serverMenu.DropDownItems.Add("启动", null, (_, _) => StartServer());
            _stopMenuItem = (ToolStripMenuItem)serverMenu.DropDownItems.Add("停止", null, (_, _) => StopServer());
            serverMenu.DropDownItems.Add(new ToolStripSeparator());
            serverMenu.DropDownItems.Add("退出", null, (_, _) => Close());

            ToolStripMenuItem databaseMenu = new ToolStripMenuItem("数据库");
            databaseMenu.DropDownItems.Add("打开数据库编辑器", null, (_, _) => OpenDatabaseEditor());
            databaseMenu.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem playerDataMenu = AddDatabaseGroup(databaseMenu, "玩家");
            AddDatabaseShortcut(playerDataMenu, "基础属性", "BaseStat");
            AddDatabaseShortcut(playerDataMenu, "技能", "MagicInfo");
            AddDatabaseShortcut(playerDataMenu, "名望", "FameInfo");
            AddDatabaseShortcut(playerDataMenu, "修炼", "DisciplineInfo");
            AddDatabaseShortcut(playerDataMenu, "宠物", "CompanionInfo");
            AddDatabaseShortcut(playerDataMenu, "宠物等级", "CompanionLevelInfo");
            AddDatabaseShortcut(playerDataMenu, "宠物技能", "CompanionSkillInfo");
            AddDatabaseShortcut(playerDataMenu, "帮助", "HelpInfo");

            ToolStripMenuItem mapDataMenu = AddDatabaseGroup(databaseMenu, "地图");
            AddDatabaseShortcut(mapDataMenu, "地图", "MapInfo");
            AddDatabaseShortcut(mapDataMenu, "副本", "InstanceInfo");
            AddDatabaseShortcut(mapDataMenu, "地图区域", "MapRegion");
            AddDatabaseShortcut(mapDataMenu, "移动点", "MovementInfo");
            AddDatabaseShortcut(mapDataMenu, "安全区", "SafeZoneInfo");
            AddDatabaseShortcut(mapDataMenu, "守卫", "GuardInfo");
            AddDatabaseShortcut(mapDataMenu, "矿点", "MineInfo");
            AddDatabaseShortcut(mapDataMenu, "钓鱼", "FishingInfo");
            AddDatabaseShortcut(mapDataMenu, "城堡", "CastleInfo");
            mapDataMenu.DropDownItems.Add("事件总览", null, (_, _) => OpenEventEditor());
            AddDatabaseShortcut(mapDataMenu, "世界事件", "WorldEventInfo");
            AddDatabaseShortcut(mapDataMenu, "玩家事件", "PlayerEventInfo");
            AddDatabaseShortcut(mapDataMenu, "怪物事件", "MonsterEventInfo");

            ToolStripMenuItem npcDataMenu = AddDatabaseGroup(databaseMenu, "NPC与任务");
            AddDatabaseShortcut(npcDataMenu, "NPC", "NPCInfo");
            AddDatabaseShortcut(npcDataMenu, "NPC页面", "NPCPage");
            AddDatabaseShortcut(npcDataMenu, "NPC数据", "GameNPCList");
            AddDatabaseShortcut(npcDataMenu, "任务", "QuestInfo");
            AddDatabaseShortcut(npcDataMenu, "里程碑", "MilestoneInfo");
            AddDatabaseShortcut(npcDataMenu, "商店", "StoreInfo");

            ToolStripMenuItem itemDataMenu = AddDatabaseGroup(databaseMenu, "物品");
            AddDatabaseShortcut(itemDataMenu, "物品", "ItemInfo");
            AddDatabaseShortcut(itemDataMenu, "物品属性", "ItemInfoStat");
            AddDatabaseShortcut(itemDataMenu, "套装", "SetInfo");
            AddDatabaseShortcut(itemDataMenu, "武器制作属性", "WeaponCraftStatInfo");
            AddDatabaseShortcut(itemDataMenu, "礼包", "BundleInfo");
            AddDatabaseShortcut(itemDataMenu, "宝箱", "LootBoxInfo");

            ToolStripMenuItem monsterDataMenu = AddDatabaseGroup(databaseMenu, "怪物");
            AddDatabaseShortcut(monsterDataMenu, "怪物", "MonsterInfo");
            AddDatabaseShortcut(monsterDataMenu, "怪物属性", "MonsterInfoStat");
            AddDatabaseShortcut(monsterDataMenu, "掉落", "DropInfo");
            AddDatabaseShortcut(monsterDataMenu, "掉落组", "DropGroupInfo");
            AddDatabaseShortcut(monsterDataMenu, "刷新", "RespawnInfo");

            ToolStripMenuItem commerceDataMenu = AddDatabaseGroup(databaseMenu, "商城与货币");
            AddDatabaseShortcut(commerceDataMenu, "货币", "CurrencyInfo");
            AddDatabaseShortcut(commerceDataMenu, "充值记录", "GameGoldPayment");
            AddDatabaseShortcut(commerceDataMenu, "商城销售", "GameStoreSale");

            ToolStripMenuItem managementDataMenu = AddDatabaseGroup(databaseMenu, "管理");
            AddDatabaseShortcut(managementDataMenu, "账号", "AccountInfo");
            AddDatabaseShortcut(managementDataMenu, "角色", "CharacterInfo");
            AddDatabaseShortcut(managementDataMenu, "用户掉落", "UserDrop");
            AddDatabaseShortcut(managementDataMenu, "邮件", "MailInfo");
            AddDatabaseShortcut(managementDataMenu, "攻城统计", "UserConquestStats");

            ToolStripMenuItem configMenu = new ToolStripMenuItem("配置");
            configMenu.DropDownItems.Add("打开配置编辑器", null, (_, _) => OpenConfigEditor());
            configMenu.DropDownItems.Add("数据库加密设置", null, (_, _) => OpenDatabaseEncryptionSettings());
            configMenu.DropDownItems.Add("数据库同步设置", null, (_, _) => OpenDatabaseSynchronizationSettings());
            configMenu.DropDownItems.Add(new ToolStripSeparator());
            configMenu.DropDownItems.Add("保存配置", null, (_, _) => ConfigReader.Save(typeof(Config).Assembly));

            ToolStripMenuItem toolsMenu = new ToolStripMenuItem("工具");
            toolsMenu.DropDownItems.Add("地图查看器", null, (_, _) => OpenMapViewer());
            toolsMenu.DropDownItems.Add("服务器诊断", null, (_, _) => OpenDiagnostics());
            toolsMenu.DropDownItems.Add("孤立数据诊断", null, (_, _) => OpenOrphanDiagnostics());
            toolsMenu.DropDownItems.Add("网络诊断", null, (_, _) => OpenNetworkDiagnostics());
            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            toolsMenu.DropDownItems.Add("导出当前日志", null, (_, _) => ExportDisplayedLogs());
            toolsMenu.DropDownItems.Add("清空当前日志", null, (_, _) => ClearDisplayedLogs());
            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            toolsMenu.DropDownItems.Add("重置界面布局", null, (_, _) => ResetUiLayout());

            _pluginsMenu = new ToolStripMenuItem("插件");

            menu.Items.Add(serverMenu);
            menu.Items.Add(databaseMenu);
            menu.Items.Add(configMenu);
            menu.Items.Add(toolsMenu);
            menu.Items.Add(_pluginsMenu);

            ToolStrip toolStrip = new ToolStrip();
            toolStrip.Margin = Padding.Empty;
            _startToolButton = AddToolButton(toolStrip, "启动", (_, _) => StartServer());
            _stopToolButton = AddToolButton(toolStrip, "停止", (_, _) => StopServer());
            AddToolButton(toolStrip, "数据库", (_, _) => OpenDatabaseEditor());
            AddToolButton(toolStrip, "物品", (_, _) => OpenDatabaseEditor("ItemInfo"));
            AddToolButton(toolStrip, "怪物", (_, _) => OpenDatabaseEditor("MonsterInfo"));
            AddToolButton(toolStrip, "NPC", (_, _) => OpenDatabaseEditor("NPCInfo"));
            AddToolButton(toolStrip, "事件", (_, _) => OpenEventEditor());
            AddToolButton(toolStrip, "配置", (_, _) => OpenConfigEditor());
            AddToolButton(toolStrip, "加密", (_, _) => OpenDatabaseEncryptionSettings());
            AddToolButton(toolStrip, "同步", (_, _) => OpenDatabaseSynchronizationSettings());
            AddToolButton(toolStrip, "地图", (_, _) => OpenMapViewer());
            AddToolButton(toolStrip, "诊断", (_, _) => OpenDiagnostics());

            BuildMetricsPanel();

            BuildLogMenu();
            BuildLogPanel();
            BuildWorkspacePanel();

            _statusPanel.Dock = DockStyle.Fill;
            _statusPanel.BackColor = SystemColors.Window;
            _statusPanel.Margin = Padding.Empty;
            _statusPanel.Padding = new Padding(8, 0, 8, 0);

            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.AutoEllipsis = true;
            _statusLabel.Font = new Font("Microsoft YaHei UI", 9F);
            _statusLabel.ForeColor = SystemColors.ControlText;
            _statusLabel.BackColor = SystemColors.Window;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Margin = Padding.Empty;
            _statusLabel.Padding = Padding.Empty;
            _statusPanel.Controls.Add(_statusLabel);

            layout.Controls.Add(menu, 0, 0);
            layout.Controls.Add(toolStrip, 0, 1);
            layout.Controls.Add(_metricsPanel, 0, 2);
            layout.Controls.Add(_workspaceSplit, 0, 3);
            layout.Controls.Add(_statusPanel, 0, 4);

            Controls.Add(layout);

            MainMenuStrip = menu;
            UpdateServerCommandState();
        }

        private void BuildLogMenu()
        {
            _logMenu.Items.Add("复制日志", null, (_, _) => CopySelectedLogs());
            _logMenu.Items.Add("导出当前日志", null, (_, _) => ExportDisplayedLogs());
            _logMenu.Items.Add(new ToolStripSeparator());
            _logMenu.Items.Add("清空当前日志", null, (_, _) => ClearDisplayedLogs());
            _logMenu.Items.Add("清空全部日志", null, (_, _) => ClearAllDisplayedLogs());
        }

        private void BuildLogPanel()
        {
            _logPanel.Dock = DockStyle.Fill;
            _logPanel.Margin = Padding.Empty;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            ToolStrip logToolStrip = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Margin = Padding.Empty
            };
            logToolStrip.Items.Add(new ToolStripLabel("日志过滤"));
            _logFilterText.AutoSize = false;
            _logFilterText.Width = 260;
            _logFilterText.ToolTipText = "按关键字过滤系统日志和聊天日志。";
            _logFilterText.TextChanged += (_, _) => ApplyLogFilters();
            logToolStrip.Items.Add(_logFilterText);
            logToolStrip.Items.Add(new ToolStripSeparator());
            logToolStrip.Items.Add("复制", null, (_, _) => CopySelectedLogs());
            logToolStrip.Items.Add("导出", null, (_, _) => ExportDisplayedLogs());
            logToolStrip.Items.Add("清空", null, (_, _) => ClearDisplayedLogs());
            logToolStrip.Items.Add(new ToolStripSeparator());
            _logStatusLabel.Text = "系统日志: 0 行";
            logToolStrip.Items.Add(_logStatusLabel);

            InitializeLogList(_systemLogList);
            InitializeLogList(_chatLogList);

            _systemLogPage.Controls.Add(_systemLogList);
            _chatLogPage.Controls.Add(_chatLogList);

            _logTabs.Dock = DockStyle.Fill;
            _logTabs.Margin = Padding.Empty;
            _logTabs.SelectedIndexChanged += (_, _) => UpdateLogStatus();
            _logTabs.TabPages.Add(_systemLogPage);
            _logTabs.TabPages.Add(_chatLogPage);

            layout.Controls.Add(logToolStrip, 0, 0);
            layout.Controls.Add(_logTabs, 0, 1);
            _logPanel.Controls.Add(layout);
        }

        private void InitializeLogList(ListBox list)
        {
            list.Dock = DockStyle.Fill;
            list.Font = new Font(FontFamily.GenericMonospace, 9F);
            list.Margin = Padding.Empty;
            list.SelectionMode = SelectionMode.MultiExtended;
            list.ContextMenuStrip = _logMenu;
            SetDoubleBuffered(list);
        }

        private void BuildWorkspacePanel()
        {
            _workspaceSplit.Dock = DockStyle.Fill;
            _workspaceSplit.Margin = Padding.Empty;
            _workspaceSplit.FixedPanel = FixedPanel.Panel1;
            _workspaceSplit.SplitterDistance = 230;

            TableLayoutPanel navigationLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            navigationLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            navigationLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            ToolStrip navigationToolStrip = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Margin = Padding.Empty
            };
            navigationToolStrip.Items.Add(new ToolStripLabel("导航过滤"));
            _navigationFilterText.AutoSize = false;
            _navigationFilterText.Width = 150;
            _navigationFilterText.ToolTipText = "按入口名称或数据库类型名过滤左侧导航。";
            _navigationFilterText.TextChanged += (_, _) => BuildNavigationTree();
            navigationToolStrip.Items.Add(_navigationFilterText);
            navigationToolStrip.Items.Add("清空", null, (_, _) => _navigationFilterText.Text = string.Empty);

            _navigationTree.Dock = DockStyle.Fill;
            _navigationTree.HideSelection = false;
            _navigationTree.AfterSelect += NavigationTree_AfterSelect;

            BuildNavigationTree();

            navigationLayout.Controls.Add(navigationToolStrip, 0, 0);
            navigationLayout.Controls.Add(_navigationTree, 0, 1);
            _workspaceSplit.Panel1.Controls.Add(navigationLayout);
            _workspaceSplit.Panel2.Controls.Add(_logPanel);
        }

        private void BuildNavigationTree()
        {
            string filter = _navigationFilterText.Text?.Trim() ?? string.Empty;

            _navigationTree.BeginUpdate();
            try
            {
                _navigationTree.Nodes.Clear();

                TreeNode operations = AddNavigationGroup("运维");
                AddNavigationAction(operations, "系统日志", () => _logTabs.SelectedTab = _systemLogPage);
                AddNavigationAction(operations, "聊天日志", () => _logTabs.SelectedTab = _chatLogPage);
                AddNavigationAction(operations, "配置编辑器", () => OpenConfigEditor());
                AddNavigationAction(operations, "数据库加密设置", OpenDatabaseEncryptionSettings);
                AddNavigationAction(operations, "数据库同步设置", OpenDatabaseSynchronizationSettings);
                AddNavigationAction(operations, "服务器诊断", OpenDiagnostics);
                AddNavigationAction(operations, "孤立数据诊断", OpenOrphanDiagnostics);
                AddNavigationAction(operations, "网络诊断", OpenNetworkDiagnostics);
                AddNavigationAction(operations, "地图查看器", () => OpenMapViewer());

                TreeNode player = AddNavigationGroup("玩家");
                AddDatabaseNavigation(player, "基础属性", "BaseStat");
                AddDatabaseNavigation(player, "技能", "MagicInfo");
                AddDatabaseNavigation(player, "名望", "FameInfo");
                AddDatabaseNavigation(player, "修炼", "DisciplineInfo");
                AddDatabaseNavigation(player, "宠物", "CompanionInfo");
                AddDatabaseNavigation(player, "宠物等级", "CompanionLevelInfo");
                AddDatabaseNavigation(player, "宠物技能", "CompanionSkillInfo");
                AddDatabaseNavigation(player, "货币", "CurrencyInfo");
                AddDatabaseNavigation(player, "帮助", "HelpInfo");

                TreeNode map = AddNavigationGroup("地图");
                AddDatabaseNavigation(map, "地图", "MapInfo");
                AddDatabaseNavigation(map, "副本", "InstanceInfo");
                AddDatabaseNavigation(map, "地图区域", "MapRegion");
                AddDatabaseNavigation(map, "移动点", "MovementInfo");
                AddDatabaseNavigation(map, "安全区", "SafeZoneInfo");
                AddDatabaseNavigation(map, "守卫", "GuardInfo");
                AddDatabaseNavigation(map, "矿点", "MineInfo");
                AddDatabaseNavigation(map, "钓鱼", "FishingInfo");
                AddDatabaseNavigation(map, "城堡", "CastleInfo");
                AddNavigationAction(map, "事件总览", OpenEventEditor);
                AddDatabaseNavigation(map, "世界事件", "WorldEventInfo");
                AddDatabaseNavigation(map, "玩家事件", "PlayerEventInfo");
                AddDatabaseNavigation(map, "怪物事件", "MonsterEventInfo");

                TreeNode npc = AddNavigationGroup("NPC与任务");
                AddDatabaseNavigation(npc, "NPC", "NPCInfo");
                AddDatabaseNavigation(npc, "NPC页面", "NPCPage");
                AddDatabaseNavigation(npc, "NPC数据", "GameNPCList");
                AddDatabaseNavigation(npc, "任务", "QuestInfo");
                AddDatabaseNavigation(npc, "里程碑", "MilestoneInfo");
                AddDatabaseNavigation(npc, "商店", "StoreInfo");

                TreeNode item = AddNavigationGroup("物品");
                AddDatabaseNavigation(item, "物品", "ItemInfo");
                AddDatabaseNavigation(item, "物品属性", "ItemInfoStat");
                AddDatabaseNavigation(item, "套装", "SetInfo");
                AddDatabaseNavigation(item, "武器制作属性", "WeaponCraftStatInfo");
                AddDatabaseNavigation(item, "礼包", "BundleInfo");
                AddDatabaseNavigation(item, "宝箱", "LootBoxInfo");

                TreeNode monster = AddNavigationGroup("怪物");
                AddDatabaseNavigation(monster, "怪物", "MonsterInfo");
                AddDatabaseNavigation(monster, "怪物属性", "MonsterInfoStat");
                AddDatabaseNavigation(monster, "掉落", "DropInfo");
                AddDatabaseNavigation(monster, "掉落组", "DropGroupInfo");
                AddDatabaseNavigation(monster, "刷新", "RespawnInfo");

                TreeNode management = AddNavigationGroup("管理");
                AddDatabaseNavigation(management, "账号", "AccountInfo");
                AddDatabaseNavigation(management, "角色", "CharacterInfo");
                AddDatabaseNavigation(management, "用户掉落", "UserDrop");
                AddDatabaseNavigation(management, "充值记录", "GameGoldPayment");
                AddDatabaseNavigation(management, "商城销售", "GameStoreSale");
                AddDatabaseNavigation(management, "邮件", "MailInfo");
                AddDatabaseNavigation(management, "攻城统计", "UserConquestStats");

                RemoveEmptyNavigationGroups(filter);

                _navigationTree.ExpandAll();
            }
            finally
            {
                _navigationTree.EndUpdate();
            }
        }

        private TreeNode AddNavigationGroup(string text)
        {
            TreeNode node = new TreeNode(text);
            _navigationTree.Nodes.Add(node);
            return node;
        }

        private void AddNavigationAction(TreeNode parent, string text, Action action, params string[] keywords)
        {
            if (!ShouldShowNavigation(parent.Text, text, keywords)) return;

            parent.Nodes.Add(new TreeNode(text) { Tag = action });
        }

        private void AddDatabaseNavigation(TreeNode parent, string text, string typeName)
        {
            AddNavigationAction(parent, text, () => OpenDatabaseEditor(typeName), typeName);
        }

        private bool ShouldShowNavigation(string groupText, string text, IEnumerable<string> keywords)
        {
            string filter = _navigationFilterText.Text?.Trim();
            if (string.IsNullOrWhiteSpace(filter)) return true;

            return NavigationTextMatches(groupText, filter) ||
                   NavigationTextMatches(text, filter) ||
                   keywords.Any(x => NavigationTextMatches(x, filter));
        }

        private static bool NavigationTextMatches(string text, string filter)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RemoveEmptyNavigationGroups(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return;

            for (int i = _navigationTree.Nodes.Count - 1; i >= 0; i--)
            {
                TreeNode node = _navigationTree.Nodes[i];
                if (node.Nodes.Count == 0 && !NavigationTextMatches(node.Text, filter))
                    _navigationTree.Nodes.RemoveAt(i);
            }
        }

        private void NavigationTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is Action action)
                action();
        }

        private void BuildMetricsPanel()
        {
            _metricsPanel.Dock = DockStyle.Fill;
            _metricsPanel.BackColor = SystemColors.ControlLight;
            _metricsPanel.Padding = new Padding(8, 6, 8, 6);
            _metricsPanel.WrapContents = false;
            _metricsPanel.AutoScroll = true;

            AddMetric("状态");
            AddMetric("连接");
            AddMetric("对象");
            AddMetric("处理");
            AddMetric("循环");
            AddMetric("运行");
            AddMetric("延迟");
            AddMetric("下载");
            AddMetric("上传");
            AddMetric("邮件");
        }

        private void AddMetric(string title)
        {
            Panel panel = new Panel
            {
                Width = 128,
                Height = 58,
                Margin = new Padding(0, 0, 8, 0),
                BackColor = SystemColors.Window
            };

            Label titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                ForeColor = SystemColors.GrayText
            };

            Label valueLabel = new Label
            {
                Text = "-",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 4),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                AutoEllipsis = true
            };

            panel.Controls.Add(valueLabel);
            panel.Controls.Add(titleLabel);
            _metricLabels[title] = valueLabel;
            _metricsPanel.Controls.Add(panel);
        }

        private static ToolStripButton AddToolButton(ToolStrip toolStrip, string text, EventHandler click)
        {
            ToolStripButton button = new ToolStripButton(text);
            button.Click += click;
            toolStrip.Items.Add(button);
            return button;
        }

        private void AddDatabaseShortcut(ToolStripMenuItem menu, string text, string typeName)
        {
            menu.DropDownItems.Add(text, null, (_, _) => OpenDatabaseEditor(typeName));
        }

        private static ToolStripMenuItem AddDatabaseGroup(ToolStripMenuItem menu, string text)
        {
            ToolStripMenuItem group = new ToolStripMenuItem(text);
            menu.DropDownItems.Add(group);
            return group;
        }

        private void LoadPlugins()
        {
            try
            {
                _pluginSession = MirDbSessionFactory.Open(SessionMode.System);
                if (CurrencyBootstrapService.EnsureDefaultCurrencies(_pluginSession, true))
                    SEnvir.Log("已补齐默认货币数据。");
                int updatedCompanions = CompanionBootstrapService.EnsureDefaultCurrency(_pluginSession, true);
                if (updatedCompanions > 0)
                    SEnvir.Log($"已为 {updatedCompanions:#,##0} 条宠物数据补齐默认货币。");
                int migratedMapStats = MapInfoBootstrapService.MigrateLegacyBuffStats(_pluginSession, true);
                if (migratedMapStats > 0)
                    SEnvir.Log($"已迁移 {migratedMapStats:#,##0} 条旧地图倍率字段到 BuffStats。");

                PluginLoader.Instance.Log += (_, e) => SEnvir.Log(e.Message);
                PluginLoader.Instance.View += (_, e) => OpenPluginView(e.View);
                PluginLoader.Instance.MapViewer += (_, e) =>
                {
                    if (string.IsNullOrEmpty(e.MapPath)) return;
                    MapViewerForm viewer = OpenMapViewer();
                    viewer.LoadMap(e.MapPath);
                };

                PluginLoader.LoadPlugins(new PluginMenuHost(_pluginsMenu), _pluginSession);
            }
            catch (Exception ex)
            {
                SEnvir.Log($"插件加载失败: {ex}");
            }
        }

        private void OpenPluginView(Type viewType)
        {
            if (viewType == null)
            {
                MessageBox.Show(this, "插件请求打开的视图类型为空。", "插件", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!typeof(Form).IsAssignableFrom(viewType))
            {
                MessageBox.Show(this, $"插件视图 {viewType.FullName} 不是 WinForms Form，无法在当前管理器中直接打开。", "插件", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Form view = (Form)Activator.CreateInstance(viewType);
                view.StartPosition = FormStartPosition.CenterParent;
                view.Show(this);
            }
            catch (Exception ex)
            {
                SEnvir.Log($"打开插件视图失败: {ex}");
                MessageBox.Show(this, ex.ToString(), "打开插件视图失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartServer()
        {
            if (!ConfirmOfflineEditorBeforeStart()) return;

            try
            {
                SEnvir.StartServer();
                UpdateInterface();
            }
            catch (Exception ex)
            {
                SEnvir.Log(ex.ToString(), true);
            }
        }

        private bool ConfirmOfflineEditorBeforeStart()
        {
            if (_databaseEditorForm == null || _databaseEditorForm.IsDisposed) return true;

            string message = _databaseEditorForm.HasUnsavedChanges
                ? "离线数据库编辑器仍有未保存修改。直接启动服务端会加载磁盘上的旧数据库，当前编辑器修改不会生效。\r\n\r\n是否仍然启动服务端？"
                : "离线数据库编辑器仍然打开，可能占用数据库文件并影响服务端加载。\r\n\r\n是否仍然启动服务端？";

            if (MessageBox.Show(this, message, "启动服务端", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                return true;

            ActivateForm(_databaseEditorForm);
            return false;
        }

        private void StopServer()
        {
            if (SEnvir.Started &&
                MessageBox.Show(
                    this,
                    "确定要停止当前服务端吗？",
                    "停止服务端",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            if (!CloseOnlineEditorBeforeStop()) return;

            SEnvir.Started = false;
            UpdateInterface();
        }

        private bool CloseOnlineEditorBeforeStop()
        {
            if (_onlineDatabaseEditorForm == null || _onlineDatabaseEditorForm.IsDisposed) return true;

            if (_onlineDatabaseEditorForm.HasUnsavedChanges)
            {
                MessageBox.Show(
                    this,
                    "在线数据库编辑器仍有未保存状态。请先在数据库编辑器中保存或关闭窗口，再停止服务端。",
                    "停止服务端",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                ActivateForm(_onlineDatabaseEditorForm);
                return false;
            }

            _onlineDatabaseEditorForm.Close();
            return _onlineDatabaseEditorForm == null || _onlineDatabaseEditorForm.IsDisposed;
        }

        private void OpenDatabaseEditor(string initialTypeName = null)
        {
            OpenDatabaseEditorCore(initialTypeName);
        }

        private DatabaseEditorForm OpenDatabaseEditorCore(string initialTypeName = null)
        {
            if (SEnvir.EnvirThread != null)
            {
                if (SEnvir.Session == null)
                {
                    MessageBox.Show(this, "服务端数据库正在加载中，请稍后再打开数据库编辑器。", "数据库", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return null;
                }

                if (_onlineDatabaseEditorForm == null || _onlineDatabaseEditorForm.IsDisposed)
                {
                    _onlineDatabaseEditorForm = new DatabaseEditorForm(SEnvir.Session, true, initialTypeName);
                    _onlineDatabaseEditorForm.FormClosed += (_, _) => _onlineDatabaseEditorForm = null;
                    _onlineDatabaseEditorForm.Show(this);
                }
                else
                {
                    _onlineDatabaseEditorForm.TryNavigateToType(initialTypeName);
                    ActivateForm(_onlineDatabaseEditorForm);
                }

                return _onlineDatabaseEditorForm;
            }

            if (_databaseEditorForm == null || _databaseEditorForm.IsDisposed)
            {
                _databaseEditorForm = new DatabaseEditorForm(initialTypeName);
                _databaseEditorForm.FormClosed += (_, _) => _databaseEditorForm = null;
                _databaseEditorForm.Show(this);
            }
            else
            {
                _databaseEditorForm.TryNavigateToType(initialTypeName);
                ActivateForm(_databaseEditorForm);
            }

            return _databaseEditorForm;
        }

        private void OpenEventEditor()
        {
            DatabaseEditorForm editor = OpenDatabaseEditorCore("WorldEventInfo");
            editor?.TryNavigateToFilteredType("Event", "WorldEventInfo");
        }

        private ConfigEditorForm OpenConfigEditor()
        {
            if (_configEditorForm == null || _configEditorForm.IsDisposed)
            {
                _configEditorForm = new ConfigEditorForm();
                _configEditorForm.FormClosed += (_, _) => _configEditorForm = null;
                _configEditorForm.Show(this);
                return _configEditorForm;
            }

            ActivateForm(_configEditorForm);
            return _configEditorForm;
        }

        private void OpenDatabaseEncryptionSettings()
        {
            OpenConfigEditor().FocusDatabaseEncryptionSettings();
        }

        private void OpenDatabaseSynchronizationSettings()
        {
            OpenConfigEditor().FocusDatabaseSynchronizationSettings();
        }

        private void OpenDiagnostics()
        {
            OpenDiagnosticsForm()?.ShowReportPage();
        }

        private void OpenOrphanDiagnostics()
        {
            OpenDiagnosticsForm()?.ShowOrphanPage();
        }

        private void OpenNetworkDiagnostics()
        {
            OpenDiagnosticsForm()?.ShowNetworkPage();
        }

        private DiagnosticsForm OpenDiagnosticsForm()
        {
            if (_diagnosticsForm == null || _diagnosticsForm.IsDisposed)
            {
                _diagnosticsForm = new DiagnosticsForm();
                _diagnosticsForm.FormClosed += (_, _) => _diagnosticsForm = null;
                _diagnosticsForm.Show(this);
                return _diagnosticsForm;
            }

            ActivateForm(_diagnosticsForm);
            return _diagnosticsForm;
        }

        private MapViewerForm OpenMapViewer()
        {
            if (_mapViewerForm == null || _mapViewerForm.IsDisposed)
            {
                _mapViewerForm = new MapViewerForm();
                _mapViewerForm.FormClosed += (_, _) => _mapViewerForm = null;
                _mapViewerForm.Show(this);
            }
            else
            {
                ActivateForm(_mapViewerForm);
            }

            return _mapViewerForm;
        }

        private static void ActivateForm(Form form)
        {
            if (form.WindowState == FormWindowState.Minimized)
                form.WindowState = FormWindowState.Normal;

            form.BringToFront();
            form.Activate();
        }

        private void CopySelectedLogs()
        {
            ListBox logList = GetActiveLogList();
            IEnumerable<object> source = logList.SelectedItems.Count > 0
                ? logList.SelectedItems.Cast<object>()
                : logList.Items.Cast<object>();

            IEnumerable<string> lines = source.Select(x => x?.ToString()).Where(x => !string.IsNullOrEmpty(x));
            string text = string.Join(Environment.NewLine, lines);
            if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
                return;
            }

            MessageBox.Show(this, "当前没有可复制的日志。", "复制日志", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExportDisplayedLogs()
        {
            ListBox logList = GetActiveLogList();
            if (logList.Items.Count == 0)
            {
                MessageBox.Show(this, "当前没有可导出的日志。", "导出日志", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string logName = GetActiveLogName();
            using SaveFileDialog dialog = new SaveFileDialog
            {
                Title = $"导出{logName}",
                Filter = "文本文件|*.txt|所有文件|*.*",
                FileName = $"{logName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                File.WriteAllLines(dialog.FileName, logList.Items.Cast<object>().Select(x => x?.ToString() ?? string.Empty));
                MessageBox.Show(this, $"日志已导出到：\r\n{dialog.FileName}", "导出日志", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "导出日志失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ClearDisplayedLogs()
        {
            List<string> source = GetActiveLogSource();
            ListBox logList = GetActiveLogList();
            if (logList.Items.Count == 0 && source.Count == 0) return;

            if (MessageBox.Show(this, $"确定清空当前界面上的{GetActiveLogName()}吗？这不会删除已写入磁盘的日志文件。", "清空日志", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            source.Clear();
            logList.Items.Clear();
            UpdateLogStatus();
        }

        private void ClearAllDisplayedLogs()
        {
            if (_systemLogs.Count == 0 && _chatLogs.Count == 0) return;

            if (MessageBox.Show(this, "确定清空当前界面上的全部系统日志和聊天日志吗？这不会删除已写入磁盘的日志文件。", "清空日志", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _systemLogs.Clear();
            _chatLogs.Clear();
            _systemLogList.Items.Clear();
            _chatLogList.Items.Clear();
            UpdateLogStatus();
        }

        private void UpdateInterface()
        {
            DrainLogQueue(SEnvir.DisplayLogs, _systemLogs, _systemLogList);
            DrainLogQueue(SEnvir.DisplayChatLogs, _chatLogs, _chatLogList);
            UpdateServerCommandState();

            SetStatusText(
                _statusLabel,
                $"连接数: {SEnvir.Connections.Count}    对象: {SEnvir.ActiveObjects.Count} / {SEnvir.Objects.Count}    循环数: {SEnvir.LoopCount}    下载/上传: {FormatBytes(SEnvir.DownloadSpeed)}/s / {FormatBytes(SEnvir.UploadSpeed)}/s");

            SetMetric("状态", SEnvir.EnvirThread == null ? "已停止" : SEnvir.Started ? "运行中" : "停止中");
            SetMetric("连接", SEnvir.Connections.Count.ToString("#,##0"));
            SetMetric("对象", $"{SEnvir.ActiveObjects.Count:#,##0}/{SEnvir.Objects.Count:#,##0}");
            SetMetric("处理", SEnvir.ProcessObjectCount.ToString("#,##0"));
            SetMetric("循环", SEnvir.LoopCount.ToString("#,##0"));
            SetMetric("运行", GetUptimeText());
            SetMetric("延迟", $"连接 {SEnvir.ConDelay:#,##0}ms / 保存 {SEnvir.SaveDelay:#,##0}ms");
            SetMetric("下载", $"{FormatBytes(SEnvir.TotalBytesReceived)} / {FormatBytes(SEnvir.DownloadSpeed)}/s");
            SetMetric("上传", $"{FormatBytes(SEnvir.TotalBytesSent)} / {FormatBytes(SEnvir.UploadSpeed)}/s");
            SetMetric("邮件", EmailService.EMailsSent.ToString("#,##0"));
        }

        private void UpdateServerCommandState()
        {
            bool canStart = SEnvir.EnvirThread == null;
            bool canStop = SEnvir.Started;

            SetEnabled(_startMenuItem, canStart);
            SetEnabled(_startToolButton, canStart);
            SetEnabled(_stopMenuItem, canStop);
            SetEnabled(_stopToolButton, canStop);
        }

        private void DrainLogQueue(ConcurrentQueue<string> queue, List<string> source, ListBox list)
        {
            if (queue.IsEmpty) return;

            list.BeginUpdate();
            try
            {
                while (queue.TryDequeue(out string log))
                    AppendLog(source, list, log);
            }
            finally
            {
                list.EndUpdate();
            }

            UpdateLogStatus();
        }

        private void AppendLog(List<string> source, ListBox list, string log)
        {
            source.Add(log);
            bool trimmed = false;
            if (source.Count > MaxLogLines)
            {
                source.RemoveRange(0, source.Count - MaxLogLines);
                trimmed = true;
            }

            if (trimmed)
            {
                ApplyLogFilter(source, list);
                return;
            }

            if (!MatchesLogFilter(log)) return;

            list.Items.Add(log);
            if (list.Items.Count > MaxLogLines)
                list.Items.RemoveAt(0);

            list.TopIndex = Math.Max(0, list.Items.Count - 1);
        }

        private void ApplyLogFilters()
        {
            ApplyLogFilter(_systemLogs, _systemLogList);
            ApplyLogFilter(_chatLogs, _chatLogList);
            UpdateLogStatus();
        }

        private void ApplyLogFilter(List<string> source, ListBox list)
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (string log in source.Where(MatchesLogFilter))
                    list.Items.Add(log);

                if (list.Items.Count > 0)
                    list.TopIndex = Math.Max(0, list.Items.Count - 1);
            }
            finally
            {
                list.EndUpdate();
            }
        }

        private bool MatchesLogFilter(string log)
        {
            string filter = _logFilterText.Text?.Trim();
            return string.IsNullOrWhiteSpace(filter) || log.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateLogStatus()
        {
            _systemLogPage.Text = $"系统日志 ({_systemLogList.Items.Count}/{_systemLogs.Count})";
            _chatLogPage.Text = $"聊天日志 ({_chatLogList.Items.Count}/{_chatLogs.Count})";

            ListBox activeList = GetActiveLogList();
            int total = GetActiveLogSource().Count;
            string filter = string.IsNullOrWhiteSpace(_logFilterText.Text) ? string.Empty : $"，过滤: {_logFilterText.Text.Trim()}";
            _logStatusLabel.Text = $"{GetActiveLogName()}: {activeList.Items.Count}/{total} 行{filter}";
        }

        private ListBox GetActiveLogList()
        {
            return ReferenceEquals(_logTabs.SelectedTab, _chatLogPage) ? _chatLogList : _systemLogList;
        }

        private List<string> GetActiveLogSource()
        {
            return ReferenceEquals(_logTabs.SelectedTab, _chatLogPage) ? _chatLogs : _systemLogs;
        }

        private string GetActiveLogName()
        {
            return ReferenceEquals(_logTabs.SelectedTab, _chatLogPage) ? "聊天日志" : "系统日志";
        }

        private static void SetStatusText(Label label, string text)
        {
            if (label.Text == text) return;

            label.Text = text;
        }

        private void SetMetric(string name, string value)
        {
            if (!_metricLabels.TryGetValue(name, out Label label)) return;
            if (label.Text == value) return;

            label.Text = value;
        }

        private static void SetEnabled(ToolStripItem item, bool enabled)
        {
            if (item == null || item.Enabled == enabled) return;

            item.Enabled = enabled;
        }

        private static void SetDoubleBuffered(Control control)
        {
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(control, true, null);
        }

        private static string FormatBytes(long value)
        {
            const decimal kb = 1024;
            const decimal mb = kb * 1024;
            const decimal gb = mb * 1024;

            if (value > gb) return $"{value / gb:#,##0.0}GB";
            if (value > mb) return $"{value / mb:#,##0.0}MB";
            if (value > kb) return $"{value / kb:#,##0}KB";
            return $"{value:#,##0}B";
        }

        private static string GetUptimeText()
        {
            if (SEnvir.EnvirThread == null || SEnvir.StartTime == DateTime.MinValue)
                return "-";

            TimeSpan uptime = Time.Now - SEnvir.StartTime;
            if (uptime.TotalDays >= 1)
                return $"{(int)uptime.TotalDays}天 {uptime:hh\\:mm\\:ss}";

            return uptime.ToString("hh\\:mm\\:ss");
        }

        private void LoadUiCache()
        {
            try
            {
                if (!File.Exists(UiCachePath)) return;

                MainFormUiCache cache = JsonSerializer.Deserialize<MainFormUiCache>(File.ReadAllText(UiCachePath));
                if (cache == null) return;

                if (cache.Width >= 900 && cache.Height >= 600)
                    Size = new Size(cache.Width, cache.Height);

                if (cache.Maximized)
                    WindowState = FormWindowState.Maximized;

                if (cache.SplitterDistance > 120)
                    _workspaceSplit.SplitterDistance = Math.Min(cache.SplitterDistance, Math.Max(120, _workspaceSplit.Width - 160));

                ApplyExpandedNavigation(cache.ExpandedNavigationPaths);
            }
            catch (Exception ex)
            {
                SEnvir.Log($"加载 WinForms 界面布局失败: {ex}");
            }
        }

        private void SaveUiCache()
        {
            try
            {
                MainFormUiCache cache = new MainFormUiCache
                {
                    Width = RestoreBounds.Width > 0 ? RestoreBounds.Width : Width,
                    Height = RestoreBounds.Height > 0 ? RestoreBounds.Height : Height,
                    Maximized = WindowState == FormWindowState.Maximized,
                    SplitterDistance = _workspaceSplit.SplitterDistance,
                    ExpandedNavigationPaths = GetExpandedNavigationPaths().ToList()
                };

                File.WriteAllText(UiCachePath, JsonSerializer.Serialize(cache, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
            catch (Exception ex)
            {
                SEnvir.Log($"保存 WinForms 界面布局失败: {ex}");
            }
        }

        private void ResetUiLayout()
        {
            try
            {
                if (File.Exists(UiCachePath))
                    File.Delete(UiCachePath);

                WindowState = FormWindowState.Normal;
                Size = new Size(1100, 720);
                _workspaceSplit.SplitterDistance = 230;
                _navigationFilterText.Text = string.Empty;
                _navigationTree.ExpandAll();
                MessageBox.Show(this, "界面布局已重置。", "界面布局", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "重置界面布局失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private IEnumerable<string> GetExpandedNavigationPaths()
        {
            foreach (TreeNode node in GetExpandedNavigationNodes(_navigationTree.Nodes))
                yield return node.FullPath;
        }

        private static IEnumerable<TreeNode> GetExpandedNavigationNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.IsExpanded)
                    yield return node;

                foreach (TreeNode child in GetExpandedNavigationNodes(node.Nodes))
                    yield return child;
            }
        }

        private void ApplyExpandedNavigation(IReadOnlyCollection<string> expandedPaths)
        {
            if (expandedPaths == null || expandedPaths.Count == 0) return;

            _navigationTree.CollapseAll();
            foreach (TreeNode node in GetAllNavigationNodes(_navigationTree.Nodes))
            {
                if (expandedPaths.Contains(node.FullPath))
                    node.Expand();
            }
        }

        private static IEnumerable<TreeNode> GetAllNavigationNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                yield return node;

                foreach (TreeNode child in GetAllNavigationNodes(node.Nodes))
                    yield return child;
            }
        }

        private sealed class MainFormUiCache
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public bool Maximized { get; set; }
            public int SplitterDistance { get; set; }
            public List<string> ExpandedNavigationPaths { get; set; } = new List<string>();
        }
    }
}
