using Library.Network;
using MirDB;
using Server.Diagnostics;
using Server.Envir;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Server.WinForms
{
    public sealed class DiagnosticsForm : Form
    {
        private readonly Session _session;
        private readonly TabControl _tabs = new TabControl();
        private readonly TabPage _reportPage = new TabPage("报告");
        private readonly TabPage _orphanPage = new TabPage("孤立数据");
        private readonly TabPage _networkPage = new TabPage("网络诊断");
        private readonly TextBox _reportText = new TextBox();
        private readonly DataGridView _orphanGrid = new DataGridView();
        private readonly DataGridView _networkGrid = new DataGridView();
        private readonly StatusStrip _statusStrip = new StatusStrip();
        private readonly ToolStripStatusLabel _statusLabel = new ToolStripStatusLabel();
        private readonly System.Windows.Forms.Timer _refreshTimer = new System.Windows.Forms.Timer();
        private readonly ToolStripButton _networkMonitorButton = new ToolStripButton("网络诊断");
        private readonly BindingList<OrphanDiagnostic.OrphanTypeResult> _orphanResults = new BindingList<OrphanDiagnostic.OrphanTypeResult>();
        private readonly BindingList<DiagnosticValue> _networkDiagnostics = new BindingList<DiagnosticValue>();

        public event EventHandler DatabaseChanged;

        public DiagnosticsForm(Session session = null)
        {
            _session = session;

            Text = "服务器诊断";
            Width = 1100;
            Height = 720;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            KeyDown += DiagnosticsForm_KeyDown;

            BuildLayout();
            LoadNetworkDiagnostics();
            UpdateStatus("诊断窗口已打开。");

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
            toolStrip.Items.Add(new ToolStripSeparator());
            AddButton(toolStrip, "复制报告", (_, _) => CopyReport());
            AddButton(toolStrip, "复制当前页", (_, _) => CopyCurrentPage());
            AddButton(toolStrip, "导出报告", (_, _) => ExportReport());
            AddButton(toolStrip, "导出当前页", (_, _) => ExportCurrentPage());

            _tabs.Dock = DockStyle.Fill;
            _tabs.SelectedIndexChanged += (_, _) => UpdateStatus();

            _reportText.Dock = DockStyle.Fill;
            _reportText.Multiline = true;
            _reportText.ScrollBars = ScrollBars.Both;
            _reportText.WordWrap = false;
            _reportText.ReadOnly = true;

            _reportPage.Controls.Add(_reportText);
            _tabs.TabPages.Add(_reportPage);

            _orphanGrid.Dock = DockStyle.Fill;
            _orphanGrid.AutoGenerateColumns = true;
            _orphanGrid.ReadOnly = true;
            _orphanGrid.AllowUserToAddRows = false;
            _orphanGrid.AllowUserToDeleteRows = false;
            _orphanGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _orphanGrid.DataSource = _orphanResults;

            _orphanPage.Controls.Add(_orphanGrid);
            _tabs.TabPages.Add(_orphanPage);

            _networkGrid.Dock = DockStyle.Fill;
            _networkGrid.AutoGenerateColumns = true;
            _networkGrid.ReadOnly = true;
            _networkGrid.AllowUserToAddRows = false;
            _networkGrid.AllowUserToDeleteRows = false;
            _networkGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _networkGrid.DataSource = _networkDiagnostics;

            _networkPage.Controls.Add(_networkGrid);
            _tabs.TabPages.Add(_networkPage);

            _statusLabel.Spring = true;
            _statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            _statusStrip.Items.Add(_statusLabel);

            Controls.Add(_tabs);
            Controls.Add(_statusStrip);
            Controls.Add(toolStrip);
            toolStrip.Dock = DockStyle.Top;
            _statusStrip.Dock = DockStyle.Bottom;
        }

        public void ShowReportPage()
        {
            _tabs.SelectedTab = _reportPage;
        }

        public void ShowReport(string text)
        {
            _reportText.Text = text ?? string.Empty;
            ShowReportPage();
        }

        public void ShowOrphanPage()
        {
            _tabs.SelectedTab = _orphanPage;
        }

        public void ShowNetworkPage()
        {
            _tabs.SelectedTab = _networkPage;
        }

        private static ToolStripButton AddButton(ToolStrip toolStrip, string text, EventHandler click)
        {
            ToolStripButton button = new ToolStripButton(text);
            button.Click += click;
            toolStrip.Items.Add(button);
            return button;
        }

        private void DiagnosticsForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C && !IsReportTextCopy(e))
            {
                CopyCurrentPage();
                SuppressKey(e);
                return;
            }

            if (e.Control && e.KeyCode == Keys.S)
            {
                ExportCurrentPage();
                SuppressKey(e);
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                RefreshNetworkDiagnostics();
                SuppressKey(e);
            }
        }

        private bool IsReportTextCopy(KeyEventArgs e)
        {
            return e.Control && e.KeyCode == Keys.C && _reportText.Focused && !string.IsNullOrEmpty(_reportText.SelectedText);
        }

        private static void SuppressKey(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
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
            UpdateStatus();
        }

        private async Task RunDatabaseValidation()
        {
            try
            {
                UseWaitCursor = true;
                _reportText.Text = "正在验证数据库...";
                UpdateStatus("正在验证数据库...");

                List<string> messages = await Task.Run(() =>
                {
                    Session session = _session ?? SEnvir.Session ?? MirDbSessionFactory.Open(SessionMode.Both);
                    MirDbMetadata metadata = new MirDbMetadata(session);
                    return new ValidationService(metadata).Validate();
                });

                if (messages.Count == 0)
                {
                    _reportText.Text = "数据库验证完成，未发现重复身份字段、断开的对象引用或业务规则问题。";
                    UpdateStatus("数据库验证完成，未发现问题。");
                    return;
                }

                _reportText.Text =
                    $"数据库验证完成，发现 {messages.Count:#,##0} 条问题。\r\n\r\n" +
                    string.Join("\r\n", messages.Take(1000));
                UpdateStatus($"数据库验证完成，发现 {messages.Count:#,##0} 条问题。");
            }
            catch (Exception ex)
            {
                _reportText.Text = ex.ToString();
                UpdateStatus("数据库验证失败。");
                MessageBox.Show(this, ex.ToString(), "数据库验证失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private async Task RunOrphanScan(bool cleanRun)
        {
            Session session = _session ?? SEnvir.Session;
            if (session == null)
            {
                MessageBox.Show(this, "孤立数据扫描依赖已加载的数据库会话。请先启动服务端，或从数据库编辑器打开诊断窗口。", "孤立数据", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                UpdateStatus(cleanRun ? "正在清理孤立数据..." : "正在扫描孤立数据...");

                OrphanDiagnostic.ScanResult result = await Task.Run(() => cleanRun
                    ? OrphanDiagnostic.MarkTemporaryOnCleanableOrphans(session)
                    : OrphanDiagnostic.Scan(session));

                LoadOrphanResults(result);
                _reportText.Text = OrphanDiagnostic.FormatLog(result, cleanRun);
                if (cleanRun && result.TotalMarkedTemporary > 0)
                    DatabaseChanged?.Invoke(this, EventArgs.Empty);

                _tabs.SelectedIndex = 1;
                UpdateStatus(cleanRun
                    ? $"孤立数据清理完成，标记 {result.TotalMarkedTemporary:#,##0} 条。"
                    : $"孤立数据扫描完成，发现 {CountReportedOrphans(result):#,##0} 条。");
            }
            catch (Exception ex)
            {
                _reportText.Text = ex.ToString();
                SEnvir.Log(ex.ToString());
                UpdateStatus(cleanRun ? "清理孤立数据失败。" : "扫描孤立数据失败。");
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
            UpdateStatus("网络诊断统计已重置。");
        }

        private void ExportReport()
        {
            string text = _reportText.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "当前没有可导出的诊断报告。", "导出报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "导出诊断报告",
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = $"Diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                RestoreDirectory = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            File.WriteAllText(dialog.FileName, text);
            UpdateStatus($"诊断报告已导出: {dialog.FileName}");
            MessageBox.Show(this, $"诊断报告已导出到：\r\n{dialog.FileName}", "导出报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void CopyReport()
        {
            string text = _reportText.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "当前没有可复制的诊断报告。", "复制报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Clipboard.SetText(text);
            UpdateStatus("诊断报告已复制。");
        }

        private void CopyCurrentPage()
        {
            DataGridView grid = _tabs.SelectedTab?.Controls.OfType<DataGridView>().FirstOrDefault();
            if (grid == null)
            {
                CopyReport();
                return;
            }

            string text = BuildGridTsv(grid);
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "当前页没有可复制的行。", "复制当前页", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Clipboard.SetText(text);
            UpdateStatus($"已复制当前页 {_tabs.SelectedTab.Text}。");
        }

        private void ExportCurrentPage()
        {
            DataGridView grid = _tabs.SelectedTab?.Controls.OfType<DataGridView>().FirstOrDefault();
            if (grid == null)
            {
                ExportReport();
                return;
            }

            ExportGrid(grid, _tabs.SelectedTab.Text);
        }

        private void ExportGrid(DataGridView grid, string title)
        {
            List<DataGridViewColumn> columns = grid.Columns.Cast<DataGridViewColumn>()
                .Where(x => x.Visible)
                .OrderBy(x => x.DisplayIndex)
                .ToList();

            if (columns.Count == 0 || grid.Rows.Count == 0)
            {
                MessageBox.Show(this, "当前页没有可导出的行。", "导出当前页", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using SaveFileDialog dialog = new SaveFileDialog
            {
                Title = $"导出{title}",
                Filter = "TSV 文件 (*.tsv)|*.tsv|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = $"{title}_{DateTime.Now:yyyyMMdd_HHmmss}.tsv",
                RestoreDirectory = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            File.WriteAllText(dialog.FileName, BuildGridTsv(grid));
            UpdateStatus($"当前页已导出: {dialog.FileName}");
            MessageBox.Show(this, $"当前页已导出到：\r\n{dialog.FileName}", "导出当前页", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UpdateStatus(string message = null)
        {
            string pageStatus;

            if (_tabs.SelectedTab == _reportPage)
            {
                int length = _reportText.TextLength;
                int lines = string.IsNullOrEmpty(_reportText.Text) ? 0 : _reportText.Lines.Length;
                pageStatus = $"报告: {lines:#,##0} 行，{length:#,##0} 字符";
            }
            else if (_tabs.SelectedTab == _orphanPage)
            {
                int totalOrphans = _orphanResults.Sum(x => x.CleanableOrphans + x.ExistingTemporaryOrphans);
                pageStatus = $"孤立数据: {_orphanResults.Count:#,##0} 类，{totalOrphans:#,##0} 条";
            }
            else
            {
                pageStatus = $"网络统计: {_networkDiagnostics.Count:#,##0} 项，监控: {(BaseConnection.Monitor ? "开启" : "关闭")}";
            }

            _statusLabel.Text = string.IsNullOrWhiteSpace(message) ? pageStatus : $"{message}  {pageStatus}";
        }

        private static int CountReportedOrphans(OrphanDiagnostic.ScanResult result)
        {
            return result.Results.Sum(x => x.CleanableOrphans + x.ExistingTemporaryOrphans);
        }

        private string BuildGridTsv(DataGridView grid)
        {
            List<DataGridViewColumn> columns = grid.Columns.Cast<DataGridViewColumn>()
                .Where(x => x.Visible)
                .OrderBy(x => x.DisplayIndex)
                .ToList();

            if (columns.Count == 0 || grid.Rows.Count == 0)
                return string.Empty;

            List<string> lines = new List<string>
            {
                string.Join("\t", columns.Select(x => EscapeTsv(x.HeaderText)))
            };

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;

                lines.Add(string.Join("\t", columns.Select(column =>
                    EscapeTsv(Convert.ToString(row.Cells[column.Index].FormattedValue, CultureInfo.CurrentCulture)))));
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string EscapeTsv(string value)
        {
            return (value ?? string.Empty)
                .Replace("\t", " ")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }
    }
}
