using Library;
using MirDB;
using PluginCore;
using Server.Envir;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Server.WinForms
{
    public sealed class MainForm : Form
    {
        private readonly ListBox _logList = new ListBox();
        private readonly FlowLayoutPanel _metricsPanel = new FlowLayoutPanel();
        private readonly Dictionary<string, Label> _metricLabels = new Dictionary<string, Label>();
        private readonly Panel _statusPanel = new Panel();
        private readonly Label _statusLabel = new Label();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private ToolStripMenuItem _pluginsMenu;
        private Session _pluginSession;

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
            serverMenu.DropDownItems.Add("启动", null, (_, _) => StartServer());
            serverMenu.DropDownItems.Add("停止", null, (_, _) => StopServer());
            serverMenu.DropDownItems.Add(new ToolStripSeparator());
            serverMenu.DropDownItems.Add("退出", null, (_, _) => Close());

            ToolStripMenuItem databaseMenu = new ToolStripMenuItem("数据库");
            databaseMenu.DropDownItems.Add("打开数据库编辑器", null, (_, _) => OpenDatabaseEditor());
            databaseMenu.DropDownItems.Add(new ToolStripSeparator());
            AddDatabaseShortcut(databaseMenu, "物品", "ItemInfo");
            AddDatabaseShortcut(databaseMenu, "怪物", "MonsterInfo");
            AddDatabaseShortcut(databaseMenu, "地图", "MapInfo");
            AddDatabaseShortcut(databaseMenu, "地图区域", "MapRegion");
            AddDatabaseShortcut(databaseMenu, "NPC", "NPCInfo");
            AddDatabaseShortcut(databaseMenu, "NPC页面", "NPCPage");
            AddDatabaseShortcut(databaseMenu, "任务", "QuestInfo");
            AddDatabaseShortcut(databaseMenu, "商城", "StoreInfo");
            databaseMenu.DropDownItems.Add(new ToolStripSeparator());
            AddDatabaseShortcut(databaseMenu, "账号", "AccountInfo");
            AddDatabaseShortcut(databaseMenu, "角色", "CharacterInfo");

            ToolStripMenuItem configMenu = new ToolStripMenuItem("配置");
            configMenu.DropDownItems.Add("打开配置编辑器", null, (_, _) => OpenConfigEditor());
            configMenu.DropDownItems.Add("保存配置", null, (_, _) => ConfigReader.Save(typeof(Config).Assembly));

            ToolStripMenuItem toolsMenu = new ToolStripMenuItem("工具");
            toolsMenu.DropDownItems.Add("地图查看器", null, (_, _) => new MapViewerForm().Show(this));
            toolsMenu.DropDownItems.Add("服务器诊断", null, (_, _) => OpenDiagnostics());

            _pluginsMenu = new ToolStripMenuItem("插件");

            menu.Items.Add(serverMenu);
            menu.Items.Add(databaseMenu);
            menu.Items.Add(configMenu);
            menu.Items.Add(toolsMenu);
            menu.Items.Add(_pluginsMenu);

            ToolStrip toolStrip = new ToolStrip();
            toolStrip.Margin = Padding.Empty;
            AddToolButton(toolStrip, "启动", (_, _) => StartServer());
            AddToolButton(toolStrip, "停止", (_, _) => StopServer());
            AddToolButton(toolStrip, "数据库", (_, _) => OpenDatabaseEditor());
            AddToolButton(toolStrip, "物品", (_, _) => OpenDatabaseEditor("ItemInfo"));
            AddToolButton(toolStrip, "怪物", (_, _) => OpenDatabaseEditor("MonsterInfo"));
            AddToolButton(toolStrip, "NPC", (_, _) => OpenDatabaseEditor("NPCInfo"));
            AddToolButton(toolStrip, "配置", (_, _) => OpenConfigEditor());
            AddToolButton(toolStrip, "地图", (_, _) => new MapViewerForm().Show(this));
            AddToolButton(toolStrip, "诊断", (_, _) => OpenDiagnostics());

            BuildMetricsPanel();

            _logList.Dock = DockStyle.Fill;
            _logList.Font = new Font(FontFamily.GenericMonospace, 9F);
            _logList.Margin = Padding.Empty;
            SetDoubleBuffered(_logList);

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
            layout.Controls.Add(_logList, 0, 3);
            layout.Controls.Add(_statusPanel, 0, 4);

            Controls.Add(layout);

            MainMenuStrip = menu;
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

        private static void AddToolButton(ToolStrip toolStrip, string text, EventHandler click)
        {
            ToolStripButton button = new ToolStripButton(text);
            button.Click += click;
            toolStrip.Items.Add(button);
        }

        private void AddDatabaseShortcut(ToolStripMenuItem menu, string text, string typeName)
        {
            menu.DropDownItems.Add(text, null, (_, _) => OpenDatabaseEditor(typeName));
        }

        private void LoadPlugins()
        {
            try
            {
                _pluginSession = MirDbSessionFactory.Open(SessionMode.System);

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

        private void UpdateInterface()
        {
            if (!SEnvir.DisplayLogs.IsEmpty)
            {
                _logList.BeginUpdate();
                try
                {
                    while (SEnvir.DisplayLogs.TryDequeue(out string log))
                    {
                        _logList.Items.Add(log);
                        if (_logList.Items.Count > 1000)
                            _logList.Items.RemoveAt(0);
                    }

                    _logList.TopIndex = Math.Max(0, _logList.Items.Count - 1);
                }
                finally
                {
                    _logList.EndUpdate();
                }
            }

            SetStatusText(
                _statusLabel,
                $"连接数: {SEnvir.Connections.Count}    对象: {SEnvir.ActiveObjects.Count} / {SEnvir.Objects.Count}    循环数: {SEnvir.LoopCount}    下载/上传: {FormatBytes(SEnvir.DownloadSpeed)}/s / {FormatBytes(SEnvir.UploadSpeed)}/s");

            SetMetric("状态", SEnvir.EnvirThread == null ? "已停止" : SEnvir.Started ? "运行中" : "停止中");
            SetMetric("连接", SEnvir.Connections.Count.ToString("#,##0"));
            SetMetric("对象", $"{SEnvir.ActiveObjects.Count:#,##0}/{SEnvir.Objects.Count:#,##0}");
            SetMetric("处理", SEnvir.ProcessObjectCount.ToString("#,##0"));
            SetMetric("循环", SEnvir.LoopCount.ToString("#,##0"));
            SetMetric("延迟", $"连接 {SEnvir.ConDelay:#,##0}ms / 保存 {SEnvir.SaveDelay:#,##0}ms");
            SetMetric("下载", $"{FormatBytes(SEnvir.TotalBytesReceived)} / {FormatBytes(SEnvir.DownloadSpeed)}/s");
            SetMetric("上传", $"{FormatBytes(SEnvir.TotalBytesSent)} / {FormatBytes(SEnvir.UploadSpeed)}/s");
            SetMetric("邮件", EmailService.EMailsSent.ToString("#,##0"));
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
    }
}
