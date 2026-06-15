using Library.Network;
using MirDB;
using Server.Diagnostics;
using Server.Envir;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Server.WinForms
{
    public sealed class DiagnosticsForm : Form
    {
        private readonly Session _session;
        private readonly TabControl _tabs = new TabControl();
        private readonly TextBox _reportText = new TextBox();
        private readonly DataGridView _orphanGrid = new DataGridView();
        private readonly DataGridView _networkGrid = new DataGridView();
        private readonly System.Windows.Forms.Timer _refreshTimer = new System.Windows.Forms.Timer();
        private readonly ToolStripButton _networkMonitorButton = new ToolStripButton("网络诊断");
        private readonly BindingList<OrphanDiagnostic.OrphanTypeResult> _orphanResults = new BindingList<OrphanDiagnostic.OrphanTypeResult>();
        private readonly BindingList<DiagnosticValue> _networkDiagnostics = new BindingList<DiagnosticValue>();

        public DiagnosticsForm(Session session = null)
        {
            _session = session;

            Text = "服务器诊断";
            Width = 1100;
            Height = 720;
            StartPosition = FormStartPosition.CenterParent;

            BuildLayout();
            LoadNetworkDiagnostics();

            _refreshTimer.Interval = 1000;
            _refreshTimer.Tick += (_, _) => RefreshNetworkDiagnostics();
            _refreshTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _refreshTimer.Dispose();

            base.Dispose(disposing);
        }

        private void BuildLayout()
        {
            ToolStrip toolStrip = new ToolStrip();
            AddButton(toolStrip, "数据库验证", async (_, _) => await RunDatabaseValidation());
            AddButton(toolStrip, "扫描孤立数据", async (_, _) => await RunOrphanScan(cleanRun: false));
            AddButton(toolStrip, "清理孤立数据", async (_, _) => await RunOrphanScan(cleanRun: true));
            toolStrip.Items.Add(new ToolStripSeparator());

            _networkMonitorButton.CheckOnClick = true;
            _networkMonitorButton.Checked = BaseConnection.Monitor;
            _networkMonitorButton.CheckedChanged += (_, _) => BaseConnection.Monitor = _networkMonitorButton.Checked;
            toolStrip.Items.Add(_networkMonitorButton);
            AddButton(toolStrip, "刷新网络统计", (_, _) => RefreshNetworkDiagnostics());
            AddButton(toolStrip, "重置网络统计", (_, _) => ResetNetworkDiagnostics());

            _tabs.Dock = DockStyle.Fill;

            _reportText.Dock = DockStyle.Fill;
            _reportText.Multiline = true;
            _reportText.ScrollBars = ScrollBars.Both;
            _reportText.WordWrap = false;
            _reportText.ReadOnly = true;

            TabPage reportPage = new TabPage("报告");
            reportPage.Controls.Add(_reportText);
            _tabs.TabPages.Add(reportPage);

            _orphanGrid.Dock = DockStyle.Fill;
            _orphanGrid.AutoGenerateColumns = true;
            _orphanGrid.ReadOnly = true;
            _orphanGrid.AllowUserToAddRows = false;
            _orphanGrid.AllowUserToDeleteRows = false;
            _orphanGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _orphanGrid.DataSource = _orphanResults;

            TabPage orphanPage = new TabPage("孤立数据");
            orphanPage.Controls.Add(_orphanGrid);
            _tabs.TabPages.Add(orphanPage);

            _networkGrid.Dock = DockStyle.Fill;
            _networkGrid.AutoGenerateColumns = true;
            _networkGrid.ReadOnly = true;
            _networkGrid.AllowUserToAddRows = false;
            _networkGrid.AllowUserToDeleteRows = false;
            _networkGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _networkGrid.DataSource = _networkDiagnostics;

            TabPage networkPage = new TabPage("网络诊断");
            networkPage.Controls.Add(_networkGrid);
            _tabs.TabPages.Add(networkPage);

            Controls.Add(_tabs);
            Controls.Add(toolStrip);
            toolStrip.Dock = DockStyle.Top;
        }

        private static ToolStripButton AddButton(ToolStrip toolStrip, string text, EventHandler click)
        {
            ToolStripButton button = new ToolStripButton(text);
            button.Click += click;
            toolStrip.Items.Add(button);
            return button;
        }

        private void LoadNetworkDiagnostics()
        {
            _networkDiagnostics.Clear();

            foreach (DiagnosticValue value in BaseConnection.Diagnostics.Values.OrderBy(x => x.Name))
                _networkDiagnostics.Add(value);
        }

        private void RefreshNetworkDiagnostics()
        {
            foreach (DiagnosticValue value in BaseConnection.Diagnostics.Values.OrderBy(x => x.Name))
            {
                if (_networkDiagnostics.Contains(value)) continue;

                _networkDiagnostics.Add(value);
            }

            _networkGrid.Refresh();
        }

        private async Task RunDatabaseValidation()
        {
            try
            {
                UseWaitCursor = true;
                _reportText.Text = "正在验证数据库...";

                List<string> messages = await Task.Run(() =>
                {
                    Session session = _session ?? SEnvir.Session ?? MirDbSessionFactory.Open(SessionMode.Both);
                    MirDbMetadata metadata = new MirDbMetadata(session);
                    return new ValidationService(metadata).Validate();
                });

                if (messages.Count == 0)
                {
                    _reportText.Text = "数据库验证完成，未发现重复身份字段或断开的对象引用。";
                    return;
                }

                _reportText.Text =
                    $"数据库验证完成，发现 {messages.Count:#,##0} 条问题。\r\n\r\n" +
                    string.Join("\r\n", messages.Take(1000));
            }
            catch (Exception ex)
            {
                _reportText.Text = ex.ToString();
                MessageBox.Show(this, ex.ToString(), "数据库验证失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private async Task RunOrphanScan(bool cleanRun)
        {
            if (SEnvir.Session == null)
            {
                MessageBox.Show(this, "孤立数据扫描依赖已加载的在线服务器数据库。请先启动服务端，或在服务端加载完成后再运行。", "孤立数据", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (cleanRun &&
                MessageBox.Show(
                    this,
                    "清理会把可确认的孤立子数据标记为临时数据，下一次保存数据库时会跳过这些数据。确定继续吗？",
                    "清理孤立数据",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try
            {
                UseWaitCursor = true;
                _reportText.Text = cleanRun ? "正在清理孤立数据..." : "正在扫描孤立数据...";

                OrphanDiagnostic.ScanResult result = await Task.Run(() => cleanRun
                    ? OrphanDiagnostic.MarkTemporaryOnCleanableOrphans()
                    : OrphanDiagnostic.Scan());

                LoadOrphanResults(result);
                _reportText.Text = OrphanDiagnostic.FormatLog(result, cleanRun);
                _tabs.SelectedIndex = 1;
            }
            catch (Exception ex)
            {
                _reportText.Text = ex.ToString();
                SEnvir.Log(ex.ToString());
                MessageBox.Show(this, ex.ToString(), cleanRun ? "清理孤立数据失败" : "扫描孤立数据失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void LoadOrphanResults(OrphanDiagnostic.ScanResult result)
        {
            _orphanResults.Clear();

            foreach (OrphanDiagnostic.OrphanTypeResult row in result.Results)
                _orphanResults.Add(row);

            if (_orphanGrid.Columns.Count > 0)
                _orphanGrid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
        }

        private void ResetNetworkDiagnostics()
        {
            foreach (DiagnosticValue value in _networkDiagnostics)
            {
                value.Count = 0;
                value.TotalSize = 0;
                value.LargestSize = 0;
                value.TotalTime = TimeSpan.Zero;
                value.LargestTime = TimeSpan.Zero;
            }

            _networkGrid.Refresh();
            RefreshNetworkDiagnostics();
            _reportText.Text = "网络诊断统计已重置。";
        }
    }
}
