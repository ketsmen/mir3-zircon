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
        private const int MaxLogLines = 2000;

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
            LoadPlugins();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
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
            AddDatabaseShortcut(playerDataMenu, "帮助", "HelpInfo");

            ToolStripMenuItem mapDataMenu = AddDatabaseGroup(databaseMenu, "地图");
            AddDatabaseShortcut(mapDataMenu, "地图", "MapInfo");
            AddDatabaseShortcut(mapDataMenu, "副本", "InstanceInfo");
            AddDatabaseShortcut(mapDataMenu, "地图区域", "MapRegion");
            AddDatabaseShortcut(mapDataMenu, "移动点", "MovementInfo");
            AddDatabaseShortcut(mapDataMenu, "安全区", "SafeZoneInfo");
            AddDatabaseShortcut(mapDataMenu, "钓鱼", "FishingInfo");
            AddDatabaseShortcut(mapDataMenu, "城堡", "CastleInfo");
            AddDatabaseShortcut(mapDataMenu, "事件", "WorldEventInfo");

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
            configMenu.DropDownItems.Add("保存配置", null, (_, _) => ConfigReader.Save(typeof(Config).Assembly));

            ToolStripMenuItem toolsMenu = new ToolStripMenuItem("工具");
            toolsMenu.DropDownItems.Add("地图查看器", null, (_, _) => new MapViewerForm().Show(this));
            toolsMenu.DropDownItems.Add("服务器诊断", null, (_, _) => OpenDiagnostics());
            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            toolsMenu.DropDownItems.Add("导出当前日志", null, (_, _) => ExportDisplayedLogs());
            toolsMenu.DropDownItems.Add("清空当前日志", null, (_, _) => ClearDisplayedLogs());

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
            AddToolButton(toolStrip, "配置", (_, _) => OpenConfigEditor());
            AddToolButton(toolStrip, "地图", (_, _) => new MapViewerForm().Show(this));
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
            _logMenu.Items.Add("复制选中日志", null, (_, _) => CopySelectedLogs());
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

            _navigationTree.Dock = DockStyle.Fill;
            _navigationTree.HideSelection = false;
            _navigationTree.AfterSelect += NavigationTree_AfterSelect;

            BuildNavigationTree();

            _workspaceSplit.Panel1.Controls.Add(_navigationTree);
            _workspaceSplit.Panel2.Controls.Add(_logPanel);
        }

        private void BuildNavigationTree()
        {
            _navigationTree.BeginUpdate();
            try
            {
                _navigationTree.Nodes.Clear();

                TreeNode operations = AddNavigationGroup("运维");
                AddNavigationAction(operations, "系统日志", () => _logTabs.SelectedTab = _systemLogPage);
                AddNavigationAction(operations, "聊天日志", () => _logTabs.SelectedTab = _chatLogPage);
                AddNavigationAction(operations, "配置编辑器", OpenConfigEditor);
                AddNavigationAction(operations, "服务器诊断", OpenDiagnostics);
                AddNavigationAction(operations, "地图查看器", () => new MapViewerForm().Show(this));

                TreeNode player = AddNavigationGroup("玩家");
                AddDatabaseNavigation(player, "基础属性", "BaseStat");
                AddDatabaseNavigation(player, "技能", "MagicInfo");
                AddDatabaseNavigation(player, "名望", "FameInfo");
                AddDatabaseNavigation(player, "修炼", "DisciplineInfo");
                AddDatabaseNavigation(player, "宠物", "CompanionInfo");
                AddDatabaseNavigation(player, "货币", "CurrencyInfo");
                AddDatabaseNavigation(player, "帮助", "HelpInfo");

                TreeNode map = AddNavigationGroup("地图");
                AddDatabaseNavigation(map, "地图", "MapInfo");
                AddDatabaseNavigation(map, "副本", "InstanceInfo");
                AddDatabaseNavigation(map, "地图区域", "MapRegion");
                AddDatabaseNavigation(map, "移动点", "MovementInfo");
                AddDatabaseNavigation(map, "安全区", "SafeZoneInfo");
                AddDatabaseNavigation(map, "钓鱼", "FishingInfo");
                AddDatabaseNavigation(map, "城堡", "CastleInfo");
                AddDatabaseNavigation(map, "事件", "WorldEventInfo");

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
                AddDatabaseNavigation(monster, "刷新", "RespawnInfo");

                TreeNode management = AddNavigationGroup("管理");
                AddDatabaseNavigation(management, "账号", "AccountInfo");
                AddDatabaseNavigation(management, "角色", "CharacterInfo");
                AddDatabaseNavigation(management, "用户掉落", "UserDrop");
                AddDatabaseNavigation(management, "充值记录", "GameGoldPayment");
                AddDatabaseNavigation(management, "商城销售", "GameStoreSale");
                AddDatabaseNavigation(management, "邮件", "MailInfo");
                AddDatabaseNavigation(management, "攻城统计", "UserConquestStats");

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

        private static void AddNavigationAction(TreeNode parent, string text, Action action)
        {
            parent.Nodes.Add(new TreeNode(text) { Tag = action });
        }

        private void AddDatabaseNavigation(TreeNode parent, string text, string typeName)
        {
            AddNavigationAction(parent, text, () => OpenDatabaseEditor(typeName));
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

                PluginLoader.Instance.Log += (_, e) => SEnvir.Log(e.Message);
                PluginLoader.Instance.View += (_, e) => MessageBox.Show(this, $"插件请求打开视图: {e.View?.FullName}", "插件", MessageBoxButtons.OK, MessageBoxIcon.Information);
                PluginLoader.Instance.MapViewer += (_, e) =>
                {
                    if (string.IsNullOrEmpty(e.MapPath)) return;
                    MapViewerForm viewer = new MapViewerForm();
                    viewer.LoadMap(e.MapPath);
                    viewer.Show(this);
                };

                PluginLoader.LoadPlugins(new PluginMenuHost(_pluginsMenu), _pluginSession);
            }
            catch (Exception ex)
            {
                SEnvir.Log($"插件加载失败: {ex}");
            }
        }

        private void StartServer()
        {
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

        private void StopServer()
        {
            SEnvir.Started = false;
            UpdateInterface();
        }

        private void OpenDatabaseEditor(string initialTypeName = null)
        {
            if (SEnvir.EnvirThread != null)
            {
                if (SEnvir.Session == null)
                {
                    MessageBox.Show(this, "服务端数据库正在加载中，请稍后再打开数据库编辑器。", "数据库", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                DatabaseEditorForm onlineForm = new DatabaseEditorForm(SEnvir.Session, true, initialTypeName);
                onlineForm.Show(this);
                return;
            }

            DatabaseEditorForm form = new DatabaseEditorForm(initialTypeName);
            form.Show(this);
        }

        private void OpenConfigEditor()
        {
            ConfigEditorForm form = new ConfigEditorForm();
            form.Show(this);
        }

        private void OpenDiagnostics()
        {
            DiagnosticsForm form = new DiagnosticsForm(SEnvir.Session);
            form.Show(this);
        }

        private void CopySelectedLogs()
        {
            ListBox logList = GetActiveLogList();
            IEnumerable<string> lines = logList.SelectedItems.Cast<object>().Select(x => x?.ToString()).Where(x => !string.IsNullOrEmpty(x));
            string text = string.Join(Environment.NewLine, lines);
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
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
    }
}
