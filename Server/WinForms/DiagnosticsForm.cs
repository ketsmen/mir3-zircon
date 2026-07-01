using Library;
using Library.Network;
using MirDB;
using Server.Diagnostics;
using Server.Envir;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
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
        private readonly TabPage _connectionsPage = new TabPage("连接能力");
        private readonly TextBox _reportText = new TextBox();
        private readonly DataGridView _orphanGrid = new DataGridView();
        private readonly DataGridView _networkGrid = new DataGridView();
        private readonly DataGridView _connectionsGrid = new DataGridView();
        private readonly StatusStrip _statusStrip = new StatusStrip();
        private readonly ToolStripStatusLabel _statusLabel = new ToolStripStatusLabel();
        private readonly System.Windows.Forms.Timer _refreshTimer = new System.Windows.Forms.Timer();
        private readonly ToolStripButton _networkMonitorButton = new ToolStripButton("网络诊断");
        private readonly ToolStripButton _connectionCapabilityFilterButton = new ToolStripButton("只看旧端/缺能力");
        private readonly BindingList<OrphanDiagnostic.OrphanTypeResult> _orphanResults = new BindingList<OrphanDiagnostic.OrphanTypeResult>();
        private readonly BindingList<DiagnosticValue> _networkDiagnostics = new BindingList<DiagnosticValue>();
        private readonly BindingList<ConnectionCapabilityRow> _connectionCapabilities = new BindingList<ConnectionCapabilityRow>();
        private int _connectionTotalCount;
        private int _connectionMissingCount;
        private int _connectionLegacyCount;
        private bool _operationRunning;
        private bool CanUpdateUi => !IsDisposed && !Disposing;

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
            _refreshTimer.Tick += (_, _) => RefreshLiveDiagnostics();
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
            _connectionCapabilityFilterButton.CheckOnClick = true;
            _connectionCapabilityFilterButton.CheckedChanged += (_, _) => RefreshConnectionCapabilities();
            toolStrip.Items.Add(_connectionCapabilityFilterButton);
            AddButton(toolStrip, "刷新连接能力", (_, _) => RefreshConnectionCapabilities());
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

            _connectionsGrid.Dock = DockStyle.Fill;
            _connectionsGrid.AutoGenerateColumns = false;
            _connectionsGrid.ReadOnly = true;
            _connectionsGrid.AllowUserToAddRows = false;
            _connectionsGrid.AllowUserToDeleteRows = false;
            _connectionsGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _connectionsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            _connectionsGrid.RowPrePaint += ConnectionsGrid_RowPrePaint;
            ConfigureConnectionColumns();
            _connectionsGrid.DataSource = _connectionCapabilities;

            _connectionsPage.Controls.Add(_connectionsGrid);
            _tabs.TabPages.Add(_connectionsPage);

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

        public void ShowConnectionsPage()
        {
            _tabs.SelectedTab = _connectionsPage;
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
                RefreshLiveDiagnostics();
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
            ReloadNetworkDiagnostics();
            ReloadConnectionCapabilities();
        }

        private void RefreshNetworkDiagnostics()
        {
            RefreshLiveDiagnostics();
        }

        private void RefreshConnectionCapabilities()
        {
            ReloadConnectionCapabilities();
            _connectionsGrid.Refresh();
            UpdateStatus();
        }

        private void RefreshLiveDiagnostics()
        {
            ReloadNetworkDiagnostics();
            ReloadConnectionCapabilities();
            _networkGrid.Refresh();
            _connectionsGrid.Refresh();
            UpdateStatus();
        }

        private void ReloadNetworkDiagnostics()
        {
            _networkDiagnostics.Clear();

            foreach (DiagnosticValue value in BaseConnection.Diagnostics.Values.Select(CloneDiagnosticValue).OrderBy(x => x.Name))
                _networkDiagnostics.Add(value);
        }

        private void ReloadConnectionCapabilities()
        {
            _connectionCapabilities.Clear();

            List<ConnectionCapabilityRow> rows = SnapshotConnections()
                .OrderBy(x => x.SessionID)
                .Select(connection => new ConnectionCapabilityRow(connection))
                .ToList();

            _connectionTotalCount = rows.Count;
            _connectionMissingCount = rows.Count(x => x.MissingHeroCapabilities);
            _connectionLegacyCount = rows.Count(x => x.LegacyOrUnknown);

            foreach (ConnectionCapabilityRow row in rows)
            {
                if (_connectionCapabilityFilterButton.Checked && !row.MissingHeroCapabilities) continue;

                _connectionCapabilities.Add(row);
            }
        }

        private void ConnectionsGrid_RowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _connectionsGrid.Rows.Count) return;

            DataGridViewRow gridRow = _connectionsGrid.Rows[e.RowIndex];
            if (gridRow.DataBoundItem is not ConnectionCapabilityRow row) return;

            if (row.LegacyOrUnknown)
                gridRow.DefaultCellStyle.BackColor = Color.MistyRose;
            else if (row.MissingHeroCapabilities)
                gridRow.DefaultCellStyle.BackColor = Color.LemonChiffon;
            else
                gridRow.DefaultCellStyle.BackColor = Color.Empty;
        }

        private static List<SConnection> SnapshotConnections()
        {
            List<SConnection> connections = new List<SConnection>();

            for (int i = 0; i < SEnvir.Connections.Count; i++)
            {
                SConnection connection;

                try
                {
                    connection = SEnvir.Connections[i];
                }
                catch (ArgumentOutOfRangeException)
                {
                    break;
                }

                if (connection != null)
                    connections.Add(connection);
            }

            return connections;
        }

        private static DiagnosticValue CloneDiagnosticValue(DiagnosticValue value)
        {
            lock (value)
            {
                return new DiagnosticValue
                {
                    Name = value.Name,
                    Count = value.Count,
                    TotalSize = value.TotalSize,
                    LargestSize = value.LargestSize,
                    TotalTime = value.TotalTime,
                    LargestTime = value.LargestTime
                };
            }
        }

        private async Task RunDatabaseValidation()
        {
            if (!BeginDiagnosticOperation()) return;

            try
            {
                _reportText.Text = "正在验证数据库...";
                UpdateStatus("正在验证数据库...");

                List<string> messages = await Task.Run(() =>
                {
                    Session session = _session ?? SEnvir.Session ?? MirDbSessionFactory.Open(SessionMode.Both);
                    MirDbMetadata metadata = new MirDbMetadata(session);
                    return new ValidationService(metadata).Validate();
                });

                if (!CanUpdateUi) return;

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
                if (!CanUpdateUi)
                {
                    SEnvir.Log("数据库验证失败: " + ex);
                    return;
                }

                _reportText.Text = ex.ToString();
                UpdateStatus("数据库验证失败。");
                MessageBox.Show(this, ex.ToString(), "数据库验证失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                EndDiagnosticOperation();
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

            if (_operationRunning)
            {
                UpdateStatus("已有诊断任务正在运行，请等待完成。");
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

            if (!BeginDiagnosticOperation()) return;

            try
            {
                _reportText.Text = cleanRun ? "正在清理孤立数据..." : "正在扫描孤立数据...";
                UpdateStatus(cleanRun ? "正在清理孤立数据..." : "正在扫描孤立数据...");

                OrphanDiagnostic.ScanResult result = await Task.Run(() => cleanRun
                    ? OrphanDiagnostic.MarkTemporaryOnCleanableOrphans(session)
                    : OrphanDiagnostic.Scan(session));

                if (!CanUpdateUi) return;

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
                if (!CanUpdateUi)
                {
                    SEnvir.Log((cleanRun ? "清理孤立数据失败: " : "扫描孤立数据失败: ") + ex);
                    return;
                }

                _reportText.Text = ex.ToString();
                SEnvir.Log(ex.ToString());
                UpdateStatus(cleanRun ? "清理孤立数据失败。" : "扫描孤立数据失败。");
                MessageBox.Show(this, ex.ToString(), cleanRun ? "清理孤立数据失败" : "扫描孤立数据失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                EndDiagnosticOperation();
            }
        }

        private bool BeginDiagnosticOperation()
        {
            if (_operationRunning)
            {
                UpdateStatus("已有诊断任务正在运行，请等待完成。");
                return false;
            }

            _operationRunning = true;
            UseWaitCursor = true;
            return true;
        }

        private void EndDiagnosticOperation()
        {
            _operationRunning = false;
            if (CanUpdateUi)
                UseWaitCursor = false;
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
            foreach (DiagnosticValue value in BaseConnection.Diagnostics.Values)
            {
                lock (value)
                {
                    value.Count = 0;
                    value.TotalSize = 0;
                    value.LargestSize = 0;
                    value.TotalTime = TimeSpan.Zero;
                    value.LargestTime = TimeSpan.Zero;
                }
            }

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

            try
            {
                WinFormsFileWriter.WriteAllTextAtomically(dialog.FileName, text);
                UpdateStatus($"诊断报告已导出: {dialog.FileName}");
                MessageBox.Show(this, $"诊断报告已导出到：\r\n{dialog.FileName}", "导出报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                UpdateStatus("诊断报告导出失败。");
                MessageBox.Show(this, ex.ToString(), "导出报告失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CopyReport()
        {
            string text = _reportText.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "当前没有可复制的诊断报告。", "复制报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            TrySetClipboardText(text, "复制报告", "诊断报告已复制。");
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

            TrySetClipboardText(text, "复制当前页", $"已复制当前页 {_tabs.SelectedTab.Text}。");
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

            try
            {
                WinFormsFileWriter.WriteAllTextAtomically(dialog.FileName, BuildGridTsv(grid));
                UpdateStatus($"当前页已导出: {dialog.FileName}");
                MessageBox.Show(this, $"当前页已导出到：\r\n{dialog.FileName}", "导出当前页", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                UpdateStatus("当前页导出失败。");
                MessageBox.Show(this, ex.ToString(), "导出当前页失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
            else if (_tabs.SelectedTab == _connectionsPage)
            {
                string filter = _connectionCapabilityFilterButton.Checked ? $"，当前显示 {_connectionCapabilities.Count:#,##0}" : string.Empty;
                pageStatus = $"连接能力: {_connectionTotalCount:#,##0} 个连接，旧端/未知: {_connectionLegacyCount:#,##0}，缺能力: {_connectionMissingCount:#,##0}{filter}";
            }
            else
            {
                pageStatus = $"网络统计: {_networkDiagnostics.Count:#,##0} 项，监控: {(BaseConnection.Monitor ? "开启" : "关闭")}";
            }

            _statusLabel.Text = string.IsNullOrWhiteSpace(message) ? pageStatus : $"{message}  {pageStatus}";
        }

        private void TrySetClipboardText(string text, string title, string successMessage)
        {
            try
            {
                Clipboard.SetText(text);
                UpdateStatus(successMessage);
            }
            catch (Exception ex)
            {
                UpdateStatus($"{title}失败。");
                MessageBox.Show(this, ex.ToString(), $"{title}失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        private void ConfigureConnectionColumns()
        {
            _connectionsGrid.Columns.Add(CreateTextColumn(nameof(ConnectionCapabilityRow.SessionID), "会话"));
            _connectionsGrid.Columns.Add(CreateTextColumn(nameof(ConnectionCapabilityRow.IPAddress), "IP"));
            _connectionsGrid.Columns.Add(CreateTextColumn(nameof(ConnectionCapabilityRow.Stage), "阶段"));
            _connectionsGrid.Columns.Add(CreateTextColumn(nameof(ConnectionCapabilityRow.Account), "账号"));
            _connectionsGrid.Columns.Add(CreateTextColumn(nameof(ConnectionCapabilityRow.Character), "角色"));
            _connectionsGrid.Columns.Add(CreateTextColumn(nameof(ConnectionCapabilityRow.ClientCapabilities), "客户端能力"));
            _connectionsGrid.Columns.Add(CreateCheckColumn(nameof(ConnectionCapabilityRow.MissingHeroCapabilities), "缺能力"));
            _connectionsGrid.Columns.Add(CreateCheckColumn(nameof(ConnectionCapabilityRow.HeroItemSyncIndex), "英雄同步索引"));
            _connectionsGrid.Columns.Add(CreateCheckColumn(nameof(ConnectionCapabilityRow.HeroMultiInspectCache), "多查看缓存"));
            _connectionsGrid.Columns.Add(CreateCheckColumn(nameof(ConnectionCapabilityRow.HeroItemRequestIndex), "请求目标索引"));
            _connectionsGrid.Columns.Add(CreateCheckColumn(nameof(ConnectionCapabilityRow.HeroGradePromotePreview), "品质培养预览"));
            _connectionsGrid.Columns.Add(CreateTextColumn(nameof(ConnectionCapabilityRow.MissingHeroCapabilityNames), "缺失能力", DataGridViewAutoSizeColumnMode.DisplayedCells));
            _connectionsGrid.Columns.Add(CreateTextColumn(nameof(ConnectionCapabilityRow.Summary), "摘要", DataGridViewAutoSizeColumnMode.Fill));
        }

        private static DataGridViewTextBoxColumn CreateTextColumn(string propertyName, string headerText, DataGridViewAutoSizeColumnMode autoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells)
        {
            return new DataGridViewTextBoxColumn
            {
                DataPropertyName = propertyName,
                HeaderText = headerText,
                AutoSizeMode = autoSizeMode,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
        }

        private static DataGridViewCheckBoxColumn CreateCheckColumn(string propertyName, string headerText)
        {
            return new DataGridViewCheckBoxColumn
            {
                DataPropertyName = propertyName,
                HeaderText = headerText,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCells,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
        }

        private sealed class ConnectionCapabilityRow
        {
            private const ServerCapabilities HeroCapabilities =
                ServerCapabilities.HeroItemSyncIndex |
                ServerCapabilities.HeroMultiInspectCache |
                ServerCapabilities.HeroItemRequestIndex |
                ServerCapabilities.HeroGradePromotePreview;

            public int SessionID { get; }
            public string IPAddress { get; }
            public string Stage { get; }
            public string Account { get; }
            public string Character { get; }
            public string ClientCapabilities { get; }
            public bool LegacyOrUnknown { get; }
            public bool MissingHeroCapabilities { get; }
            public bool HeroItemSyncIndex { get; }
            public bool HeroMultiInspectCache { get; }
            public bool HeroItemRequestIndex { get; }
            public bool HeroGradePromotePreview { get; }
            public string MissingHeroCapabilityNames { get; }
            public string Summary { get; }

            public ConnectionCapabilityRow(SConnection connection)
            {
                SessionID = connection.SessionID;
                IPAddress = connection.IPAddress;
                Stage = connection.Stage.ToString();
                Account = connection.Account?.EMailAddress ?? string.Empty;
                Character = connection.Player?.Name ?? string.Empty;
                ClientCapabilities = connection.ClientCapabilities.ToString();
                LegacyOrUnknown = connection.ClientCapabilities == ServerCapabilities.None;
                MissingHeroCapabilities = (connection.ClientCapabilities & HeroCapabilities) != HeroCapabilities;
                HeroItemSyncIndex = connection.SupportsClientCapability(ServerCapabilities.HeroItemSyncIndex);
                HeroMultiInspectCache = connection.SupportsClientCapability(ServerCapabilities.HeroMultiInspectCache);
                HeroItemRequestIndex = connection.SupportsClientCapability(ServerCapabilities.HeroItemRequestIndex);
                HeroGradePromotePreview = connection.SupportsClientCapability(ServerCapabilities.HeroGradePromotePreview);
                MissingHeroCapabilityNames = connection.GetMissingHeroCapabilitiesSummary();
                Summary = connection.GetCapabilitiesSummary();
            }
        }
    }
}
