using Library;
using Library.SystemModels;
using MirDB;
using Server.Envir;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Server.WinForms
{
    public sealed class MirDbGridControl : UserControl
    {
        private readonly Session _session;
        private readonly MirDbMetadata _metadata;
        private readonly Type _itemType;
        private readonly IList _source;
        private readonly object _rootCollection;
        private readonly bool _readOnly;
        private readonly object _ownerObject;
        private readonly DataGridView _grid = new DataGridView();
        private readonly ContextMenuStrip _contextMenu = new ContextMenuStrip();
        private string _filterText = string.Empty;
        private PropertyInfo _sortProperty;
        private ListSortDirection _sortDirection = ListSortDirection.Ascending;
        private bool _loadingColumnLayout;
        private bool _binding;

        public event EventHandler CurrentObjectChanged;
        public event EventHandler BecameActive;
        public event EventHandler<DBObjectReferenceEventArgs> ReferenceOpenRequested;
        public event EventHandler ContentChanged;

        public MirDbGridControl(Session session, MirDbMetadata metadata, Type itemType, IList source, object rootCollection, bool readOnly, object ownerObject = null)
        {
            _session = session;
            _metadata = metadata;
            _itemType = itemType;
            _source = source;
            _rootCollection = rootCollection;
            _readOnly = readOnly;
            _ownerObject = ownerObject;

            Dock = DockStyle.Fill;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _grid.Dock = DockStyle.Fill;
            SetDoubleBuffered(_grid);
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AutoGenerateColumns = false;
            _grid.MultiSelect = true;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
            _grid.ReadOnly = readOnly;
            _grid.DataError += Grid_DataError;
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CellParsing += Grid_CellParsing;
            _grid.SelectionChanged += (_, _) => CurrentObjectChanged?.Invoke(this, EventArgs.Empty);
            _grid.Enter += (_, _) => BecameActive?.Invoke(this, EventArgs.Empty);
            _grid.CellMouseDown += Grid_CellMouseDown;
            _grid.CellDoubleClick += Grid_CellDoubleClick;
            _grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
            _grid.KeyDown += Grid_KeyDown;
            _grid.CellValueChanged += (_, _) => OnContentChanged();

            BuildContextMenu();

            Controls.Add(_grid);
            BuildColumns();
            LoadColumnLayout();
            _grid.ColumnWidthChanged += (_, _) => SaveColumnLayout();
            _grid.ColumnDisplayIndexChanged += (_, _) => SaveColumnLayout();
            _grid.ColumnStateChanged += Grid_ColumnStateChanged;
            RefreshBinding();
        }

        public object CurrentObject => _grid.CurrentRow?.DataBoundItem;

        public string ItemTypeName => _itemType.Name;

        public Type ItemType => _itemType;

        public int VisibleCount => _grid.Rows.Count;

        public int TotalCount => _source.Count;

        public int SelectedObjectCount => SelectedObjects.Count();

        public string CurrentIndexText => CurrentObject is DBObject dbObject ? dbObject.Index.ToString("#,##0") : string.Empty;

        public IEnumerable<object> AllObjects => _source.Cast<object>();

        public IEnumerable<object> SelectedObjects
        {
            get
            {
                HashSet<object> result = new HashSet<object>();

                foreach (DataGridViewCell cell in _grid.SelectedCells)
                {
                    object item = _grid.Rows[cell.RowIndex].DataBoundItem;
                    if (item != null) result.Add(item);
                }

                if (result.Count == 0 && _grid.CurrentRow?.DataBoundItem != null)
                    result.Add(_grid.CurrentRow.DataBoundItem);

                return result;
            }
        }

        public void RefreshBinding()
        {
            object current = CurrentObject;
            _binding = true;
            try
            {
                IList list = CreateVisibleList();
                _grid.DataSource = list;
            }
            finally
            {
                _binding = false;
            }

            SelectObject(current);
        }

        public void ApplySearch(string text)
        {
            _filterText = text ?? string.Empty;
            RefreshBinding();
        }

        public void ExportVisibleRows(IWin32Window owner)
        {
            if (_grid.Rows.Count == 0)
            {
                MessageBox.Show(owner, "当前视图没有可导出的行。", "导出当前视图", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "导出当前视图",
                Filter = "TSV 文件 (*.tsv)|*.tsv|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = $"{_itemType.Name}_View_{DateTime.Now:yyyyMMdd_HHmmss}.tsv",
                RestoreDirectory = true
            };

            if (dialog.ShowDialog(owner) != DialogResult.OK) return;

            File.WriteAllText(dialog.FileName, BuildRowsTsv(_grid.Rows.Cast<DataGridViewRow>()));
            MessageBox.Show(owner, $"已导出 {_grid.Rows.Count:#,##0} 行到：\r\n{dialog.FileName}", "导出当前视图", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void CopyVisibleRows(IWin32Window owner)
        {
            if (_grid.Rows.Count == 0)
            {
                MessageBox.Show(owner, "当前视图没有可复制的行。", "复制当前视图", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Clipboard.SetText(BuildRowsTsv(_grid.Rows.Cast<DataGridViewRow>()));
            MessageBox.Show(owner, $"已复制当前视图 {_grid.Rows.Count:#,##0} 行。", "复制当前视图", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void FocusGrid()
        {
            if (_grid.Rows.Count > 0 && _grid.CurrentCell == null)
            {
                DataGridViewRow firstRow = _grid.Rows[0];
                _grid.CurrentCell = firstRow.Cells.Cast<DataGridViewCell>().FirstOrDefault(x => x.Visible);
            }

            _grid.Focus();
        }

        public bool SelectByIndex(int index)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.DataBoundItem is not DBObject dbObject || dbObject.Index != index) continue;

                _grid.ClearSelection();
                _grid.CurrentCell = row.Cells.Cast<DataGridViewCell>().FirstOrDefault(x => x.Visible);
                row.Selected = true;
                if (row.Index >= 0 && row.Index < _grid.Rows.Count)
                    _grid.FirstDisplayedScrollingRowIndex = row.Index;

                CurrentObjectChanged?.Invoke(this, EventArgs.Empty);
                BecameActive?.Invoke(this, EventArgs.Empty);
                return true;
            }

            return false;
        }

        public void AddNew()
        {
            if (_readOnly) return;
            if (!ConfirmUserDataMutation("新增一行")) return;

            object item = CreateNewItem();
            if (item == null) return;

            _filterText = string.Empty;
            RefreshBinding();
            SelectObject(item);
            OnContentChanged();
        }

        public void InsertAfterCurrent()
        {
            if (_readOnly) return;
            if (!ConfirmUserDataMutation("在当前行后插入新行")) return;

            object current = CurrentObject;
            if (current is not DBObject dbObject)
            {
                AddNew();
                return;
            }

            object item;
            if (_rootCollection != null)
            {
                item = _metadata.InsertRootObjectAfter(_itemType, dbObject.Index);
            }
            else
            {
                AddNew();
                return;
            }

            _filterText = string.Empty;
            RefreshBinding();
            SelectObject(item);
            OnContentChanged();
        }

        public void DuplicateCurrent()
        {
            if (_readOnly) return;
            if (!ConfirmUserDataMutation("复制当前对象为新行")) return;

            object current = CurrentObject;
            if (current == null) return;

            object item = CreateNewItem();
            if (item == null) return;

            foreach (PropertyInfo property in _metadata.GetValueProperties(_itemType))
            {
                if (property.Name == nameof(DBObject.Index) || !CanWrite(property)) continue;

                object value = property.GetValue(current);
                property.SetValue(item, value);
            }

            _filterText = string.Empty;
            RefreshBinding();
            SelectObject(item);
            OnContentChanged();
        }

        public void DeleteSelected()
        {
            if (_readOnly) return;

            List<DBObject> objects = SelectedObjects.OfType<DBObject>().ToList();
            if (objects.Count == 0) return;

            if (!ConfirmUserDataMutation($"删除选中的 {objects.Count} 行"))
                return;

            if (MessageBox.Show(this, $"确定删除选中的 {objects.Count} 行吗？", "删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            foreach (DBObject item in objects)
                item.Delete();

            RefreshBinding();
            OnContentChanged();
        }

        private bool ConfirmUserDataMutation(string action)
        {
            if (!_metadata.IsUserObject(_itemType)) return true;

            return MessageBox.Show(
                this,
                $"{_itemType.Name} 是用户数据库对象。\r\n\r\n{action}可能影响玩家账号、角色、物品、邮件或日志数据。确定继续吗？",
                "用户数据保护",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private object CreateNewItem()
        {
            if (_rootCollection != null)
                return _metadata.CreateRootObject(_itemType);

            if (_source is IBindingList bindingList)
                return bindingList.AddNew();

            MessageBox.Show(this, "当前集合不支持新增。", "新增", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        private void SelectObject(object item)
        {
            if (item == null) return;

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!ReferenceEquals(row.DataBoundItem, item)) continue;

                _grid.CurrentCell = row.Cells.Cast<DataGridViewCell>().FirstOrDefault(x => x.Visible);
                row.Selected = true;
                break;
            }
        }

        private IList CreateVisibleList()
        {
            IEnumerable<object> items = _source.Cast<object>();

            if (!string.IsNullOrWhiteSpace(_filterText))
            {
                string filter = _filterText.Trim();
                items = items.Where(x => RowContains(x, filter));
            }

            List<object> sortedItems = items.ToList();
            if (_sortProperty != null)
                sortedItems.Sort(CompareSortItems);

            Type listType = typeof(BindingList<>).MakeGenericType(_itemType);
            IList list = (IList)Activator.CreateInstance(listType);
            foreach (object item in sortedItems)
                list.Add(item);

            return list;
        }

        private int CompareSortItems(object left, object right)
        {
            object leftValue = _sortProperty.GetValue(left);
            object rightValue = _sortProperty.GetValue(right);
            int result = CompareSortValues(leftValue, rightValue);
            return _sortDirection == ListSortDirection.Descending ? -result : result;
        }

        private int CompareSortValues(object left, object right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;

            Type leftType = left.GetType();
            if (leftType == right.GetType() && left is IComparable comparable)
                return comparable.CompareTo(right);

            string leftText = _metadata.GetDisplayText(left);
            string rightText = _metadata.GetDisplayText(right);
            return string.Compare(leftText, rightText, StringComparison.CurrentCultureIgnoreCase);
        }

        private bool RowContains(object item, string filter)
        {
            foreach (PropertyInfo property in _metadata.GetValueProperties(_itemType))
            {
                object value = property.GetValue(item);
                string text = _metadata.GetDisplayText(value);
                if (text?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private void BuildContextMenu()
        {
            _contextMenu.Items.Add("复制当前单元格", null, (_, _) => CopyCurrentCell());
            _contextMenu.Items.Add("复制选中行", null, (_, _) => CopySelectedRows());
            ToolStripItem pasteItem = _contextMenu.Items.Add("粘贴表格", null, (_, _) => PasteClipboardTable());
            pasteItem.Enabled = !_readOnly;
            _contextMenu.Items.Add("复制当前视图", null, (_, _) => CopyVisibleRows(this));
            _contextMenu.Items.Add("导出当前视图", null, (_, _) => ExportVisibleRows(this));
            _contextMenu.Items.Add(new ToolStripSeparator());
            ToolStripItem duplicateItem = _contextMenu.Items.Add("复制当前对象为新行", null, (_, _) => DuplicateCurrent());
            duplicateItem.Enabled = !_readOnly;
            ToolStripItem deleteItem = _contextMenu.Items.Add("删除选中行", null, (_, _) => DeleteSelected());
            deleteItem.Enabled = !_readOnly;
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("选择当前行", null, (_, _) => SelectCurrentRow());
            _contextMenu.Items.Add("打开引用对象", null, (_, _) => OpenCurrentReference());
            _contextMenu.Items.Add("最佳列宽", null, (_, _) => _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells));
            _contextMenu.Items.Add("隐藏当前列", null, (_, _) => HideCurrentColumn());
            _contextMenu.Items.Add("显示所有列", null, (_, _) => ShowAllColumns());
            _contextMenu.Items.Add("重置列布局", null, (_, _) => ResetColumnLayout());
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("刷新", null, (_, _) => RefreshBinding());
            _grid.ContextMenuStrip = _contextMenu;
        }

        private void Grid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0) return;

            _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            if (!_grid.Rows[e.RowIndex].Selected && !_grid[e.ColumnIndex, e.RowIndex].Selected)
                _grid[e.ColumnIndex, e.RowIndex].Selected = true;
        }

        private void Grid_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex < 0) return;

            DataGridViewColumn column = _grid.Columns[e.ColumnIndex];
            PropertyInfo property = _itemType.GetProperty(column.DataPropertyName);
            if (property == null) return;

            if (_sortProperty == property)
            {
                _sortDirection = _sortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }
            else
            {
                _sortProperty = property;
                _sortDirection = ListSortDirection.Ascending;
            }

            RefreshBinding();
            UpdateSortGlyphs();
        }

        private void UpdateSortGlyphs()
        {
            foreach (DataGridViewColumn column in _grid.Columns)
            {
                PropertyInfo property = _itemType.GetProperty(column.DataPropertyName);
                if (property == null || property != _sortProperty)
                {
                    column.HeaderCell.SortGlyphDirection = SortOrder.None;
                    continue;
                }

                column.HeaderCell.SortGlyphDirection = _sortDirection == ListSortDirection.Ascending
                    ? SortOrder.Ascending
                    : SortOrder.Descending;
            }
        }

        private void CopyCurrentCell()
        {
            if (_grid.CurrentCell == null) return;

            string text = Convert.ToString(_grid.CurrentCell.FormattedValue, CultureInfo.CurrentCulture) ?? string.Empty;
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        }

        private void CopySelectedRows()
        {
            List<DataGridViewRow> rows = _grid.SelectedCells
                .Cast<DataGridViewCell>()
                .Select(x => _grid.Rows[x.RowIndex])
                .Distinct()
                .OrderBy(x => x.Index)
                .ToList();

            if (rows.Count == 0 && _grid.CurrentRow != null)
                rows.Add(_grid.CurrentRow);

            if (rows.Count == 0) return;

            Clipboard.SetText(BuildRowsTsv(rows));
        }

        private string BuildRowsTsv(IEnumerable<DataGridViewRow> rows)
        {
            List<DataGridViewColumn> columns = GetVisibleColumns();

            List<string> lines = new List<string>
            {
                string.Join("\t", columns.Select(x => EscapeTsv(x.HeaderText)))
            };

            foreach (DataGridViewRow row in rows)
            {
                lines.Add(string.Join("\t", columns.Select(column => EscapeTsv(Convert.ToString(row.Cells[column.Index].FormattedValue, CultureInfo.CurrentCulture)))));
            }

            return string.Join(Environment.NewLine, lines);
        }

        private List<DataGridViewColumn> GetVisibleColumns()
        {
            return _grid.Columns.Cast<DataGridViewColumn>()
                .Where(x => x.Visible)
                .OrderBy(x => x.DisplayIndex)
                .ToList();
        }

        private static string EscapeTsv(string value)
        {
            return (value ?? string.Empty)
                .Replace("\t", " ")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private void SelectCurrentRow()
        {
            if (_grid.CurrentRow == null) return;

            _grid.ClearSelection();
            foreach (DataGridViewCell cell in _grid.CurrentRow.Cells)
                cell.Selected = true;
        }

        private void OpenCurrentReference()
        {
            if (_grid.CurrentCell == null) return;

            OpenReference(_grid.CurrentCell.RowIndex, _grid.CurrentCell.ColumnIndex, true);
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            OpenReference(e.RowIndex, e.ColumnIndex, false);
        }

        private void OpenReference(int rowIndex, int columnIndex, bool showMessage)
        {
            if (!TryGetReference(rowIndex, columnIndex, out DBObject dbObject))
            {
                if (showMessage)
                    MessageBox.Show(this, "当前单元格不是数据库对象引用。", "打开引用对象", MessageBoxButtons.OK, MessageBoxIcon.Information);

                return;
            }

            ReferenceOpenRequested?.Invoke(this, new DBObjectReferenceEventArgs(dbObject));
        }

        private bool TryGetReference(int rowIndex, int columnIndex, out DBObject dbObject)
        {
            dbObject = null;
            if (rowIndex < 0 || columnIndex < 0 || rowIndex >= _grid.Rows.Count || columnIndex >= _grid.Columns.Count)
                return false;

            object rowObject = _grid.Rows[rowIndex].DataBoundItem;
            if (rowObject == null) return false;

            DataGridViewColumn column = _grid.Columns[columnIndex];
            PropertyInfo property = _itemType.GetProperty(column.DataPropertyName);
            if (property == null) return false;

            dbObject = property.GetValue(rowObject) as DBObject;
            return dbObject != null;
        }

        private void HideCurrentColumn()
        {
            if (_grid.CurrentCell == null) return;

            int visibleColumns = _grid.Columns.Cast<DataGridViewColumn>().Count(x => x.Visible);
            if (visibleColumns <= 1) return;

            _grid.Columns[_grid.CurrentCell.ColumnIndex].Visible = false;
        }

        private void ShowAllColumns()
        {
            foreach (DataGridViewColumn column in _grid.Columns)
                column.Visible = true;

            SaveColumnLayout();
        }

        private void Grid_ColumnStateChanged(object sender, DataGridViewColumnStateChangedEventArgs e)
        {
            if ((e.StateChanged & DataGridViewElementStates.Visible) == 0) return;

            SaveColumnLayout();
        }

        private void LoadColumnLayout()
        {
            _loadingColumnLayout = true;

            try
            {
                string path = GetColumnLayoutPath();
                if (!File.Exists(path)) return;

                Dictionary<string, (int DisplayIndex, int Width, bool Visible)> layout =
                    new Dictionary<string, (int DisplayIndex, int Width, bool Visible)>(StringComparer.OrdinalIgnoreCase);

                foreach (string line in File.ReadAllLines(path).Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = line.Split('\t');
                    if (parts.Length < 4) continue;
                    if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int displayIndex)) continue;
                    if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)) continue;
                    if (!bool.TryParse(parts[3], out bool visible)) continue;

                    layout[parts[0]] = (displayIndex, Math.Max(32, width), visible);
                }

                foreach (DataGridViewColumn column in _grid.Columns)
                {
                    if (!layout.TryGetValue(column.Name, out var state)) continue;

                    column.Width = state.Width;
                }

                int nextDisplayIndex = 0;
                HashSet<string> applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, (int DisplayIndex, int Width, bool Visible)> entry in layout.OrderBy(x => x.Value.DisplayIndex))
                {
                    if (!_grid.Columns.Contains(entry.Key)) continue;

                    _grid.Columns[entry.Key].DisplayIndex = nextDisplayIndex++;
                    applied.Add(entry.Key);
                }

                foreach (DataGridViewColumn column in _grid.Columns.Cast<DataGridViewColumn>().OrderBy(x => x.Index))
                {
                    if (applied.Contains(column.Name)) continue;

                    column.DisplayIndex = nextDisplayIndex++;
                }

                foreach (DataGridViewColumn column in _grid.Columns)
                {
                    if (!layout.TryGetValue(column.Name, out var state)) continue;

                    column.Visible = state.Visible;
                }

                if (_grid.Columns.Cast<DataGridViewColumn>().All(x => !x.Visible) && _grid.Columns.Count > 0)
                    _grid.Columns[0].Visible = true;
            }
            catch
            {
                // 布局文件只影响编辑器显示，损坏时忽略并回到默认列布局。
            }
            finally
            {
                _loadingColumnLayout = false;
            }
        }

        private void SaveColumnLayout()
        {
            if (_loadingColumnLayout || _grid.Columns.Count == 0) return;

            try
            {
                string path = GetColumnLayoutPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                List<string> lines = new List<string>
                {
                    "Column\tDisplayIndex\tWidth\tVisible"
                };

                foreach (DataGridViewColumn column in _grid.Columns.Cast<DataGridViewColumn>().OrderBy(x => x.DisplayIndex))
                {
                    lines.Add(string.Join("\t",
                        EscapeTsv(column.Name),
                        column.DisplayIndex.ToString(CultureInfo.InvariantCulture),
                        column.Width.ToString(CultureInfo.InvariantCulture),
                        column.Visible.ToString(CultureInfo.InvariantCulture)));
                }

                File.WriteAllLines(path, lines);
            }
            catch
            {
                // 列布局保存失败不应影响数据库编辑。
            }
        }

        private void ResetColumnLayout()
        {
            _loadingColumnLayout = true;

            try
            {
                foreach (DataGridViewColumn column in _grid.Columns.Cast<DataGridViewColumn>().OrderBy(x => x.Index))
                {
                    column.Visible = true;
                    column.DisplayIndex = column.Index;
                }

                _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);

                string path = GetColumnLayoutPath();
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                MessageBox.Show(this, "重置列布局失败，请确认编辑器目录可写。", "列布局", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _loadingColumnLayout = false;
            }
        }

        private string GetColumnLayoutPath()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinFormsLayouts");
            string fileName = _itemType.FullName ?? _itemType.Name;

            foreach (char invalid in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(invalid, '_');

            return Path.Combine(root, fileName + ".columns.tsv");
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (_readOnly) return;

            if (e.Control && e.KeyCode == Keys.V)
            {
                PasteClipboardTable();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.Delete && !_grid.IsCurrentCellInEditMode)
            {
                DeleteSelected();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void PasteClipboardTable()
        {
            if (!Clipboard.ContainsText()) return;

            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return;

            int firstDataLine = HasVisibleColumnHeader(lines[0]) ? 1 : 0;
            if (firstDataLine >= lines.Length) return;

            int startColumn = _grid.CurrentCell?.ColumnIndex ?? GetFirstVisibleColumnIndex();
            if (startColumn < 0) return;
            List<DataGridViewColumn> pasteColumns = GetPasteColumns(startColumn);
            if (pasteColumns.Count == 0) return;

            object startObject = _grid.CurrentRow?.DataBoundItem;
            int startRow = _grid.CurrentCell?.RowIndex ?? 0;
            int dataRows = lines.Length - firstDataLine;
            int missingRows = startRow + dataRows - _grid.Rows.Count;
            bool addedRows = false;

            if (missingRows > 0)
            {
                if (!AppendRowsForPaste(missingRows))
                    return;

                addedRows = true;
                if (startObject != null)
                    startRow = FindRowIndex(startObject);

                if (startRow < 0)
                    startRow = Math.Max(0, _grid.Rows.Count - dataRows);
            }

            int changed = 0;

            for (int lineIndex = firstDataLine; lineIndex < lines.Length && startRow + lineIndex - firstDataLine < _grid.Rows.Count; lineIndex++)
            {
                string[] values = lines[lineIndex].Split('\t');
                int targetRow = startRow + lineIndex - firstDataLine;

                for (int columnOffset = 0; columnOffset < values.Length && columnOffset < pasteColumns.Count; columnOffset++)
                {
                    DataGridViewColumn column = pasteColumns[columnOffset];
                    if (column.ReadOnly) continue;

                    PropertyInfo property = _itemType.GetProperty(column.DataPropertyName);
                    if (property == null || !CanWrite(property)) continue;

                    if (!TryConvertCellText(values[columnOffset], property.PropertyType, out object value))
                    {
                        if (addedRows)
                            OnContentChanged();

                        MessageBox.Show(this, $"无法把 `{values[columnOffset]}` 写入 {property.Name}。", "粘贴", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    _grid.Rows[targetRow].Cells[column.Index].Value = value;
                    changed++;
                }
            }

            if (changed > 0 || addedRows)
            {
                _grid.EndEdit();
                _grid.Refresh();
                OnContentChanged();
            }
        }

        private List<DataGridViewColumn> GetPasteColumns(int startColumn)
        {
            if (startColumn < 0 || startColumn >= _grid.Columns.Count) return new List<DataGridViewColumn>();

            DataGridViewColumn current = _grid.Columns[startColumn];
            int startDisplayIndex = current.Visible ? current.DisplayIndex : 0;

            return _grid.Columns.Cast<DataGridViewColumn>()
                .Where(x => x.Visible && x.DisplayIndex >= startDisplayIndex)
                .OrderBy(x => x.DisplayIndex)
                .ToList();
        }

        private int GetFirstVisibleColumnIndex()
        {
            return _grid.Columns.Cast<DataGridViewColumn>()
                .Where(x => x.Visible)
                .OrderBy(x => x.DisplayIndex)
                .Select(x => x.Index)
                .FirstOrDefault(-1);
        }

        private bool AppendRowsForPaste(int count)
        {
            if (count <= 0) return true;
            if (!ConfirmUserDataMutation($"粘贴并新增 {count} 行")) return false;

            if (!string.IsNullOrWhiteSpace(_filterText))
            {
                _filterText = string.Empty;
                RefreshBinding();
            }

            for (int i = 0; i < count; i++)
            {
                if (CreateNewItem() == null)
                    return false;
            }

            RefreshBinding();
            return true;
        }

        private int FindRowIndex(object item)
        {
            if (item == null) return -1;

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (ReferenceEquals(row.DataBoundItem, item))
                    return row.Index;
            }

            return -1;
        }

        private bool HasVisibleColumnHeader(string line)
        {
            string[] values = line.Split('\t');
            List<DataGridViewColumn> columns = GetVisibleColumns();
            if (values.Length == 0 || values.Length > columns.Count) return false;

            for (int i = 0; i < values.Length; i++)
            {
                string header = columns[i].HeaderText ?? string.Empty;
                if (!string.Equals(values[i].Trim(), header, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private void OnContentChanged()
        {
            if (_binding) return;

            ContentChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool TryConvertCellText(string text, Type targetType, out object value)
        {
            Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
            text = text?.Trim() ?? string.Empty;

            try
            {
                if (targetType != type && string.IsNullOrWhiteSpace(text))
                {
                    value = null;
                    return true;
                }

                if (type == typeof(string))
                {
                    value = text;
                    return true;
                }

                if (type == typeof(char))
                {
                    value = string.IsNullOrEmpty(text) ? '\0' : text[0];
                    return true;
                }

                if (type == typeof(bool))
                {
                    value = text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                            text.Equals("checked", StringComparison.OrdinalIgnoreCase) ||
                            text.Equals("是", StringComparison.OrdinalIgnoreCase) ||
                            text == "1";
                    return true;
                }

                if (type.IsEnum)
                {
                    value = Enum.Parse(type, text, true);
                    return true;
                }

                if (type.IsSubclassOf(typeof(DBObject)))
                    return TryConvertObjectReference(text, type, out value);

                if (type == typeof(Point))
                {
                    string[] parts = text.Split(',');
                    value = new Point(int.Parse(parts[0].Trim(), CultureInfo.InvariantCulture), int.Parse(parts[1].Trim(), CultureInfo.InvariantCulture));
                    return true;
                }

                if (type == typeof(Size))
                {
                    string[] parts = text.Split(',');
                    value = new Size(int.Parse(parts[0].Trim(), CultureInfo.InvariantCulture), int.Parse(parts[1].Trim(), CultureInfo.InvariantCulture));
                    return true;
                }

                if (type == typeof(Color))
                {
                    value = ColorTranslator.FromHtml(text);
                    return true;
                }

                if (type == typeof(TimeSpan))
                {
                    value = TimeSpan.Parse(text, CultureInfo.InvariantCulture);
                    return true;
                }

                if (type == typeof(DateTime))
                {
                    value = DateTime.Parse(text, CultureInfo.InvariantCulture);
                    return true;
                }

                value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        private bool TryConvertObjectReference(string text, Type objectType, out object value)
        {
            value = null;

            if (string.IsNullOrWhiteSpace(text) || text.Equals("[空]", StringComparison.OrdinalIgnoreCase))
                return true;

            if (TryExtractObjectIndex(text, out int index))
            {
                value = _metadata.GetBinding(objectType).Cast<object>().OfType<DBObject>().FirstOrDefault(x => x.Index == index);
                return value != null;
            }

            foreach (object item in _metadata.GetBinding(objectType))
            {
                string display = _metadata.GetDisplayText(item);
                if (display.Equals(text, StringComparison.OrdinalIgnoreCase) ||
                    Convert.ToString(item, CultureInfo.CurrentCulture)?.Equals(text, StringComparison.OrdinalIgnoreCase) == true)
                {
                    value = item;
                    return true;
                }
            }

            return false;
        }

        private static bool TryExtractObjectIndex(string text, out int index)
        {
            index = 0;
            int hash = text.LastIndexOf('#');
            if (hash >= 0)
            {
                int end = text.IndexOf(')', hash);
                string part = end > hash ? text.Substring(hash + 1, end - hash - 1) : text.Substring(hash + 1);
                return int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
        }

        private void BuildColumns()
        {
            foreach (PropertyInfo property in _metadata.GetValueProperties(_itemType))
            {
                DataGridViewColumn column = CreateColumn(property);
                column.DataPropertyName = property.Name;
                column.HeaderText = property.Name;
                column.Name = property.Name;
                column.ReadOnly = _readOnly || !CanWrite(property);
                column.SortMode = DataGridViewColumnSortMode.Programmatic;
                _grid.Columns.Add(column);
            }
        }

        private DataGridViewColumn CreateColumn(PropertyInfo property)
        {
            Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (propertyType == typeof(bool))
                return new DataGridViewCheckBoxColumn();

            if (propertyType.IsEnum)
            {
                DataGridViewComboBoxColumn column = new DataGridViewComboBoxColumn();
                column.DataSource = GetEnumChoices(property, propertyType);
                column.ValueType = propertyType;
                column.AutoComplete = true;
                return column;
            }

            if (propertyType.IsSubclassOf(typeof(DBObject)))
            {
                DataGridViewComboBoxColumn column = new DataGridViewComboBoxColumn();
                column.DataSource = GetObjectChoices(property, propertyType);
                column.DisplayMember = nameof(ObjectChoice.Display);
                column.ValueMember = nameof(ObjectChoice.Value);
                column.ValueType = propertyType;
                column.FlatStyle = FlatStyle.Flat;
                column.AutoComplete = true;
                return column;
            }

            DataGridViewTextBoxColumn textColumn = new DataGridViewTextBoxColumn();
            textColumn.ValueType = propertyType;

            if (!MirDbMetadata.IsSimpleEditable(propertyType))
                textColumn.ReadOnly = true;

            return textColumn;
        }

        private object GetEnumChoices(PropertyInfo property, Type propertyType)
        {
            if (propertyType == typeof(EventActionType) &&
                property.Name == nameof(BaseEventAction.Type))
            {
                List<EventActionType> values = _itemType switch
                {
                    Type type when type == typeof(WorldEventAction) => SEnvir.EventHandler.GetWorldEventActions(),
                    Type type when type == typeof(PlayerEventAction) => SEnvir.EventHandler.GetPlayerEventActions(),
                    Type type when type == typeof(MonsterEventAction) => SEnvir.EventHandler.GetMonsterEventActions(),
                    _ => null
                };

                if (values != null)
                {
                    HashSet<EventActionType> added = new HashSet<EventActionType>(values);
                    foreach (EventActionType current in _source.Cast<object>().Select(x => property.GetValue(x)).OfType<EventActionType>())
                    {
                        if (added.Add(current))
                            values.Add(current);
                    }

                    return values.OrderBy(x => (int)x).ToList();
                }
            }

            return Enum.GetValues(propertyType);
        }

        private IReadOnlyList<ObjectChoice> GetObjectChoices(PropertyInfo property, Type propertyType)
        {
            if (propertyType == typeof(ItemInfo) &&
                _itemType == typeof(CurrencyInfo) &&
                property.Name == nameof(CurrencyInfo.DropItem))
            {
                return GetFilteredObjectChoices<ItemInfo>(property, x => x.ItemType == Library.ItemType.Currency);
            }

            if (propertyType == typeof(MonsterInfo) &&
                _itemType == typeof(CastleInfo) &&
                property.Name == nameof(CastleInfo.Monster))
            {
                return GetFilteredObjectChoices<MonsterInfo>(property, x => x.Flag == MonsterFlag.CastleObjective || x.Flag == MonsterFlag.CastleDefense);
            }

            if (propertyType == typeof(MapRegion) &&
                TryGetRegionFilter(property, out Func<MapRegion, bool> regionFilter))
            {
                return GetFilteredObjectChoices(property, regionFilter);
            }

            if (_ownerObject is MapInfo map &&
                _itemType == typeof(MineInfo) &&
                property.Name == nameof(MineInfo.Region) &&
                propertyType == typeof(MapRegion))
            {
                return GetMiningRegionChoices(map);
            }

            return _metadata.GetObjectChoices(propertyType);
        }

        private bool TryGetRegionFilter(PropertyInfo property, out Func<MapRegion, bool> filter)
        {
            filter = null;

            if (_itemType == typeof(NPCInfo) && property.Name == nameof(NPCInfo.Region))
            {
                filter = x => x.RegionType == RegionType.None || x.RegionType == RegionType.Npc;
                return true;
            }

            if (_itemType == typeof(RespawnInfo) && property.Name == nameof(RespawnInfo.Region))
            {
                filter = x => x.RegionType == RegionType.None || x.RegionType == RegionType.Spawn || x.RegionType == RegionType.SpawnConnection;
                return true;
            }

            if (_itemType == typeof(MovementInfo) &&
                (property.Name == nameof(MovementInfo.SourceRegion) || property.Name == nameof(MovementInfo.DestinationRegion)))
            {
                filter = x => x.RegionType == RegionType.None || x.RegionType == RegionType.Connection || x.RegionType == RegionType.SpawnConnection;
                return true;
            }

            if (_itemType == typeof(MilestoneInfoTask) && property.Name == nameof(MilestoneInfoTask.Region))
            {
                filter = x => x.RegionType == RegionType.None || x.RegionType == RegionType.Area;
                return true;
            }

            if (property.Name == "RegionParameter1" && _itemType.Name.Contains("Event", StringComparison.Ordinal))
            {
                filter = x => x.RegionType == RegionType.None || x.RegionType == RegionType.Area;
                return true;
            }

            return false;
        }

        private IReadOnlyList<ObjectChoice> GetFilteredObjectChoices<T>(PropertyInfo property, Func<T, bool> filter) where T : DBObject
        {
            List<ObjectChoice> choices = new List<ObjectChoice>
            {
                new ObjectChoice(null, "[空]")
            };

            HashSet<T> added = new HashSet<T>();

            foreach (T item in _metadata.GetBinding(typeof(T)).OfType<T>().Where(filter).OrderBy(x => x.Index))
            {
                choices.Add(new ObjectChoice(item, _metadata.GetDisplayText(item)));
                added.Add(item);
            }

            foreach (T item in _source.Cast<object>().Select(x => property.GetValue(x)).OfType<T>().Where(x => added.Add(x)).OrderBy(x => x.Index))
                choices.Add(new ObjectChoice(item, _metadata.GetDisplayText(item) + " [不匹配过滤]"));

            return choices;
        }

        private IReadOnlyList<ObjectChoice> GetMiningRegionChoices(MapInfo map)
        {
            List<ObjectChoice> choices = new List<ObjectChoice>
            {
                new ObjectChoice(null, "[空]")
            };

            HashSet<MapRegion> added = new HashSet<MapRegion>();
            foreach (MapRegion region in _metadata.GetBinding(typeof(MapRegion)).OfType<MapRegion>().Where(x => x.Map == map).OrderBy(x => x.Index))
            {
                choices.Add(new ObjectChoice(region, _metadata.GetDisplayText(region)));
                added.Add(region);
            }

            foreach (MapRegion region in _source.Cast<object>().OfType<MineInfo>().Select(x => x.Region).Where(x => x != null && added.Add(x)).OrderBy(x => x.Index))
                choices.Add(new ObjectChoice(region, _metadata.GetDisplayText(region) + " [跨地图]"));

            return choices;
        }

        private bool CanWrite(PropertyInfo property)
        {
            MethodInfo setter = property.SetMethod;
            if (setter == null || !setter.IsPublic) return false;

            Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            return MirDbMetadata.IsSimpleEditable(type) || type == typeof(bool) || type.IsEnum || type.IsSubclassOf(typeof(DBObject));
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            PropertyInfo property = _itemType.GetProperty(_grid.Columns[e.ColumnIndex].DataPropertyName);
            if (property == null) return;

            Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (type.IsSubclassOf(typeof(DBObject)) || type.IsEnum || type == typeof(bool) || MirDbMetadata.IsSimpleEditable(type))
                return;

            e.Value = _metadata.GetDisplayText(e.Value);
            e.FormattingApplied = true;
        }

        private void Grid_CellParsing(object sender, DataGridViewCellParsingEventArgs e)
        {
            if (e.ColumnIndex < 0) return;

            PropertyInfo property = _itemType.GetProperty(_grid.Columns[e.ColumnIndex].DataPropertyName);
            if (property == null) return;

            Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (e.Value == null || !MirDbMetadata.IsSimpleEditable(type)) return;

            string text = Convert.ToString(e.Value, CultureInfo.InvariantCulture);

            try
            {
                if (type == typeof(Point))
                {
                    string[] parts = text.Split(',');
                    e.Value = new Point(int.Parse(parts[0].Trim()), int.Parse(parts[1].Trim()));
                    e.ParsingApplied = true;
                }
                else if (type == typeof(Size))
                {
                    string[] parts = text.Split(',');
                    e.Value = new Size(int.Parse(parts[0].Trim()), int.Parse(parts[1].Trim()));
                    e.ParsingApplied = true;
                }
                else if (type == typeof(Color))
                {
                    e.Value = ColorTranslator.FromHtml(text);
                    e.ParsingApplied = true;
                }
                else if (type == typeof(TimeSpan))
                {
                    e.Value = TimeSpan.Parse(text, CultureInfo.InvariantCulture);
                    e.ParsingApplied = true;
                }
            }
            catch
            {
                e.ParsingApplied = false;
            }
        }

        private void Grid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        private static void SetDoubleBuffered(Control control)
        {
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(control, true, null);
        }
    }

    public sealed class DBObjectReferenceEventArgs : EventArgs
    {
        public DBObjectReferenceEventArgs(DBObject reference)
        {
            Reference = reference;
        }

        public DBObject Reference { get; }
    }
}
