using Library;
using MirDB;
using PluginCore;
using Server.Envir;
using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Server.WinForms
{
    public sealed class MainForm : Form
    {
        private readonly ListBox _logList = new ListBox();
        private readonly StatusStrip _statusStrip = new StatusStrip();
        private readonly ToolStripStatusLabel _connectionLabel = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel _objectLabel = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel _loopLabel = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel _speedLabel = new ToolStripStatusLabel();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private ToolStripMenuItem _pluginsMenu;
        private Session _pluginSession;

        public MainForm()
        {
            Text = "Zircon 服务端管理器";
            Width = 1100;
            Height = 720;
            StartPosition = FormStartPosition.CenterScreen;

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

            _pluginSession?.Save(true);
            ConfigReader.Save(typeof(Config).Assembly);
        }

        private void BuildLayout()
        {
            MenuStrip menu = new MenuStrip();

            ToolStripMenuItem serverMenu = new ToolStripMenuItem("服务端");
            serverMenu.DropDownItems.Add("启动", null, (_, _) => StartServer());
            serverMenu.DropDownItems.Add("停止", null, (_, _) => StopServer());
            serverMenu.DropDownItems.Add(new ToolStripSeparator());
            serverMenu.DropDownItems.Add("退出", null, (_, _) => Close());

            ToolStripMenuItem databaseMenu = new ToolStripMenuItem("数据库");
            databaseMenu.DropDownItems.Add("打开数据库编辑器", null, (_, _) => OpenDatabaseEditor());
            databaseMenu.DropDownItems.Add("保存配置", null, (_, _) => ConfigReader.Save(Assembly.GetAssembly(typeof(Config))));

            ToolStripMenuItem toolsMenu = new ToolStripMenuItem("工具");
            toolsMenu.DropDownItems.Add("地图查看器", null, (_, _) => new MapViewerForm().Show(this));

            _pluginsMenu = new ToolStripMenuItem("插件");

            menu.Items.Add(serverMenu);
            menu.Items.Add(databaseMenu);
            menu.Items.Add(toolsMenu);
            menu.Items.Add(_pluginsMenu);

            ToolStrip toolStrip = new ToolStrip();
            AddToolButton(toolStrip, "启动", (_, _) => StartServer());
            AddToolButton(toolStrip, "停止", (_, _) => StopServer());
            AddToolButton(toolStrip, "数据库", (_, _) => OpenDatabaseEditor());
            AddToolButton(toolStrip, "地图", (_, _) => new MapViewerForm().Show(this));

            _logList.Dock = DockStyle.Fill;
            _logList.Font = new Font(FontFamily.GenericMonospace, 9F);

            _statusStrip.Items.Add(_connectionLabel);
            _statusStrip.Items.Add(_objectLabel);
            _statusStrip.Items.Add(_loopLabel);
            _statusStrip.Items.Add(_speedLabel);

            Controls.Add(_logList);
            Controls.Add(_statusStrip);
            Controls.Add(toolStrip);
            Controls.Add(menu);

            MainMenuStrip = menu;
            menu.Dock = DockStyle.Top;
            toolStrip.Dock = DockStyle.Top;
            _statusStrip.Dock = DockStyle.Bottom;
        }

        private static void AddToolButton(ToolStrip toolStrip, string text, EventHandler click)
        {
            ToolStripButton button = new ToolStripButton(text);
            button.Click += click;
            toolStrip.Items.Add(button);
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

        private void OpenDatabaseEditor()
        {
            if (SEnvir.EnvirThread != null)
            {
                MessageBox.Show(this, "服务端运行中不能离线编辑 System.db / Users.db。请先停止服务端。", "数据库", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DatabaseEditorForm form = new DatabaseEditorForm();
            form.Show(this);
        }

        private void UpdateInterface()
        {
            while (SEnvir.DisplayLogs.TryDequeue(out string log))
            {
                _logList.Items.Add(log);
                if (_logList.Items.Count > 1000)
                    _logList.Items.RemoveAt(0);
                _logList.TopIndex = _logList.Items.Count - 1;
            }

            _connectionLabel.Text = $"连接数: {SEnvir.Connections.Count}";
            _objectLabel.Text = $"对象: {SEnvir.ActiveObjects.Count} / {SEnvir.Objects.Count}";
            _loopLabel.Text = $"循环数: {SEnvir.LoopCount}";
            _speedLabel.Text = $"下载/上传: {FormatBytes(SEnvir.DownloadSpeed)}/s / {FormatBytes(SEnvir.UploadSpeed)}/s";
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
