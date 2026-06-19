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
        public event EventHandler FilterCleared;

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
            _grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
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

        public string SortText => _sortProperty == null ? string.Empty : $"{_sortProperty.Name} {(_sortDirection == ListSortDirection.Ascending ? "升序" : "降序")}";

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

        public void RefreshBinding(bool commitCurrentEdit = false)
        {
            if (commitCurrentEdit && !EndPendingEdit()) return;

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
            if (!EndPendingEdit()) return;

            _filterText = text ?? string.Empty;
            RefreshBinding();
        }

        public void ExportVisibleRows(IWin32Window owner)
        {
            if (!EndPendingEdit()) return;

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

            try
            {
                WinFormsFileWriter.WriteAllTextAtomically(dialog.FileName, BuildRowsTsv(_grid.Rows.Cast<DataGridViewRow>()));
                MessageBox.Show(owner, $"已导出 {_grid.Rows.Count:#,##0} 行到：\r\n{dialog.FileName}", "导出当前视图", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, ex.ToString(), "导出当前视图失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void CopyVisibleRows(IWin32Window owner)
        {
            if (!EndPendingEdit()) return;

            if (_grid.Rows.Count == 0)
            {
                MessageBox.Show(owner, "当前视图没有可复制的行。", "复制当前视图", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            TrySetClipboardText(
                BuildRowsTsv(_grid.Rows.Cast<DataGridViewRow>()),
                owner,
                "复制当前视图",
                $"已复制当前视图 {_grid.Rows.Count:#,##0} 行。");
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

        public bool EndPendingEdit()
        {
            try
            {
                if (_grid.IsCurrentCellDirty)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);

                return _grid.EndEdit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "提交编辑失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
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
            if (!EndPendingEdit()) return;
            if (!ConfirmUserDataMutation("新增一行")) return;

            AddNewAfterConfirmed();
        }

        private void AddNewAfterConfirmed()
        {
            object item = CreateNewItem();
            if (item == null) return;

            ClearFilter();
            RefreshBinding();
            SelectObject(item);
            OnContentChanged();
        }

        public void InsertAfterCurrent()
        {
            if (_readOnly) return;
            if (!EndPendingEdit()) return;
            if (!ConfirmUserDataMutation("在当前行后插入新行")) return;

            object current = CurrentObject;
            if (current is not DBObject dbObject)
            {
                AddNewAfterConfirmed();
                return;
            }

            object item;
            if (_rootCollection != null)
            {
                item = _metadata.InsertRootObjectAfter(_itemType, dbObject.Index);
            }
            else
            {
                AddNewAfterConfirmed();
                return;
            }

            ClearFilter();
            RefreshBinding();
            SelectObject(item);
            OnContentChanged();
        }

        public void DuplicateCurrent()
        {
            if (_readOnly) return;
            if (!EndPendingEdit()) return;
            if (!ConfirmUserDataMutation("复制当前对象为新行")) return;

            object current = CurrentObject;
            if (current == null) return;

            object item = CreateNewItem();
            if (item == null) return;

            try
            {
                foreach (PropertyInfo property in _metadata.GetValueProperties(_itemType))
                {
                    if (property.Name == nameof(DBObject.Index) || !CanWrite(property)) continue;

                    object value = property.GetValue(current);
                    property.SetValue(item, value);
                }
            }
            catch (Exception ex)
            {
                RemoveAddedRows(new[] { item });
                RefreshBinding();
                MessageBox.Show(this, ex.ToString(), "复制当前对象失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ClearFilter();
            RefreshBinding();
            SelectObject(item);
            OnContentChanged();
        }

        public void DeleteSelected()
        {
            if (_readOnly) return;
            if (!EndPendingEdit()) return;

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
            _contextMenu.Items.Add("清除排序", null, (_, _) => ClearSort());
            _contextMenu.Items.Add("隐藏当前列", null, (_, _) => HideCurrentColumn());
            _contextMenu.Items.Add("显示所有列", null, (_, _) => ShowAllColumns());
            _contextMenu.Items.Add("重置列布局", null, (_, _) => ResetColumnLayout());
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("刷新", null, (_, _) => RefreshBinding(true));
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
            if (!EndPendingEdit()) return;

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
            BecameActive?.Invoke(this, EventArgs.Empty);
        }

        private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (_binding || !_grid.IsCurrentCellDirty) return;
            if (_grid.CurrentCell is not DataGridViewCheckBoxCell &&
                _grid.CurrentCell is not DataGridViewComboBoxCell)
                return;

            try
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "提交编辑失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        private void ClearSort()
        {
            if (_sortProperty == null) return;
            if (!EndPendingEdit()) return;

            _sortProperty = null;
            _sortDirection = ListSortDirection.Ascending;
            RefreshBinding();
            UpdateSortGlyphs();
            BecameActive?.Invoke(this, EventArgs.Empty);
        }

        private void CopyCurrentCell()
        {
            if (!EndPendingEdit()) return;
            if (_grid.CurrentCell == null) return;

            string text = Convert.ToString(_grid.CurrentCell.FormattedValue, CultureInfo.CurrentCulture) ?? string.Empty;
            if (!string.IsNullOrEmpty(text))
                TrySetClipboardText(text, this, "复制单元格");
        }

        private void CopySelectedRows()
        {
            if (!EndPendingEdit()) return;

            List<DataGridViewRow> rows = _grid.SelectedCells
                .Cast<DataGridViewCell>()
                .Select(x => _grid.Rows[x.RowIndex])
                .Distinct()
                .OrderBy(x => x.Index)
                .ToList();

            if (rows.Count == 0 && _grid.CurrentRow != null)
                rows.Add(_grid.CurrentRow);

            if (rows.Count == 0) return;

            TrySetClipboardText(BuildRowsTsv(rows), this, "复制选中行");
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

        private bool TrySetClipboardText(string text, IWin32Window owner, string title, string successMessage = null)
        {
            try
            {
                Clipboard.SetText(text);
                if (!string.IsNullOrWhiteSpace(successMessage))
                    MessageBox.Show(owner, successMessage, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, ex.ToString(), $"{title}失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
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
            if (!EndPendingEdit()) return;

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

                WinFormsFileWriter.WriteAllLinesAtomically(path, lines);
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
                if (_grid.IsCurrentCellInEditMode && _grid.EditingControl is TextBoxBase)
                    return;

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
            if (_readOnly) return;
            if (!TryGetClipboardText(out string text)) return;
            if (string.IsNullOrWhiteSpace(text)) return;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return;

            bool hasColumnHeader = HasVisibleColumnHeader(lines[0]);
            int firstDataLine = hasColumnHeader ? 1 : 0;
            if (firstDataLine >= lines.Length) return;
            if (!EndPendingEdit()) return;

            int startColumn = _grid.CurrentCell?.ColumnIndex ?? GetFirstVisibleColumnIndex();
            if (startColumn < 0) return;
            List<(DataGridViewColumn Column, int ValueIndex)> pasteTargets = GetPasteTargets(lines[0], hasColumnHeader, startColumn);
            if (pasteTargets.Count == 0) return;

            object startObject = _grid.CurrentRow?.DataBoundItem;
            int startRow = _grid.CurrentCell?.RowIndex ?? 0;
            int dataRows = lines.Length - firstDataLine;
            List<(int RowOffset, PropertyInfo Property, object Value)> changes = new List<(int RowOffset, PropertyInfo Property, object Value)>();

            for (int lineIndex = firstDataLine; lineIndex < lines.Length; lineIndex++)
            {
                string[] values = lines[lineIndex].Split('\t');
                int rowOffset = lineIndex - firstDataLine;

                foreach ((DataGridViewColumn column, int valueIndex) in pasteTargets)
                {
                    if (valueIndex < 0 || valueIndex >= values.Length) continue;

                    PropertyInfo property = _itemType.GetProperty(column.DataPropertyName);
                    if (property == null || !CanWrite(property)) continue;

                    if (!TryConvertCellText(values[valueIndex], property.PropertyType, out object value))
                    {
                        MessageBox.Show(this, $"无法把 `{values[valueIndex]}` 写入 {property.Name}。", "粘贴", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    changes.Add((rowOffset, property, value));
                }
            }

            if (changes.Count == 0) return;

            if (!string.IsNullOrWhiteSpace(_filterText))
            {
                ClearFilter();
                RefreshBinding();

                if (startObject != null)
                {
                    startRow = FindRowIndex(startObject);
                    if (startRow < 0)
                    {
                        MessageBox.Show(this, "清除过滤后无法定位粘贴起始行。", "粘贴", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else
                {
                    startRow = 0;
                }
            }

            List<object> targetObjects = GetPasteTargetObjects(startRow, dataRows);
            int missingRows = startRow + dataRows - _grid.Rows.Count;
            List<object> addedItems = new List<object>();

            if (missingRows > 0)
            {
                if (!AppendRowsForPaste(missingRows, out addedItems))
                    return;

                for (int i = 0; i < targetObjects.Count; i++)
                {
                    if (targetObjects[i] != null) continue;

                    int addedIndex = i - (dataRows - missingRows);
                    if (addedIndex >= 0 && addedIndex < addedItems.Count)
                        targetObjects[i] = addedItems[addedIndex];
                }
            }

            int changed = 0;
            List<(object Target, PropertyInfo Property, object OldValue)> appliedChanges = new List<(object Target, PropertyInfo Property, object OldValue)>();

            try
            {
                foreach (var change in changes)
                {
                    if (change.RowOffset < 0 || change.RowOffset >= targetObjects.Count) continue;

                    object target = targetObjects[change.RowOffset];
                    if (target == null) continue;

                    object oldValue = change.Property.GetValue(target);
                    change.Property.SetValue(target, change.Value);
                    appliedChanges.Add((target, change.Property, oldValue));
                    changed++;
                }
            }
            catch (Exception ex)
            {
                for (int i = appliedChanges.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var applied = appliedChanges[i];
                        applied.Property.SetValue(applied.Target, applied.OldValue);
                    }
                    catch
                    {
                        // 尽量回滚粘贴失败前的写入；单个属性回滚失败时继续处理其他属性。
                    }
                }

                RemoveAddedRows(addedItems);
                RefreshBinding();
                MessageBox.Show(this, ex.ToString(), "粘贴失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (changed > 0 || addedItems.Count > 0)
            {
                List<object> pastedTargets = targetObjects.Where(x => x != null).ToList();
                List<PropertyInfo> pastedProperties = changes.Select(x => x.Property).ToList();

                _grid.EndEdit();
                RefreshBinding();
                SelectPasteRange(pastedTargets, pastedProperties);
                OnContentChanged();
            }
        }

        private void SelectPasteRange(IReadOnlyList<object> targets, IReadOnlyList<PropertyInfo> properties)
        {
            if (targets.Count == 0 || properties.Count == 0) return;

            HashSet<object> targetSet = new HashSet<object>(targets, ReferenceEqualityComparer.Instance);
            HashSet<string> propertyNames = new HashSet<string>(properties.Select(x => x.Name), StringComparer.Ordinal);
            List<DataGridViewColumn> columns = _grid.Columns
                .Cast<DataGridViewColumn>()
                .Where(x => x.Visible && propertyNames.Contains(x.DataPropertyName))
                .ToList();
            if (targetSet.Count == 0 || columns.Count == 0) return;

            _grid.ClearSelection();
            DataGridViewCell firstCell = null;

            foreach (DataGridViewRow row in _grid.Rows)
            {
                object item = row.DataBoundItem;
                if (item == null || !targetSet.Contains(item)) continue;

                foreach (DataGridViewColumn column in columns)
                {
                    DataGridViewCell cell = row.Cells[column.Index];
                    cell.Selected = true;
                    firstCell ??= cell;
                }
            }

            if (firstCell != null)
                _grid.CurrentCell = firstCell;
        }

        private bool TryGetClipboardText(out string text)
        {
            try
            {
                text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                return true;
            }
            catch (Exception ex)
            {
                text = null;
                MessageBox.Show(this, ex.ToString(), "读取剪贴板失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private List<object> GetPasteTargetObjects(int startRow, int dataRows)
        {
            List<object> result = new List<object>();

            for (int i = 0; i < dataRows; i++)
            {
                int rowIndex = startRow + i;
                result.Add(rowIndex >= 0 && rowIndex < _grid.Rows.Count ? _grid.Rows[rowIndex].DataBoundItem : null);
            }

            return result;
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

        private List<(DataGridViewColumn Column, int ValueIndex)> GetPasteTargets(string headerLine, bool hasColumnHeader, int startColumn)
        {
            if (!hasColumnHeader)
                return GetPasteColumns(startColumn)
                    .Select((column, index) => (Column: column, ValueIndex: index))
                    .Where(x => !x.Column.ReadOnly)
                    .ToList();

            string[] headers = headerLine.Split('\t');
            List<DataGridViewColumn> visibleColumns = GetVisibleColumns();
            List<(DataGridViewColumn Column, int ValueIndex)> result = new List<(DataGridViewColumn Column, int ValueIndex)>();

            for (int i = 0; i < headers.Length; i++)
            {
                string header = headers[i].Trim();
                DataGridViewColumn column = visibleColumns.FirstOrDefault(x => string.Equals(x.HeaderText?.Trim(), header, StringComparison.OrdinalIgnoreCase));
                if (column == null || column.ReadOnly) continue;

                result.Add((column, i));
            }

            return result;
        }

        private int GetFirstVisibleColumnIndex()
        {
            return _grid.Columns.Cast<DataGridViewColumn>()
                .Where(x => x.Visible)
                .OrderBy(x => x.DisplayIndex)
                .Select(x => x.Index)
                .FirstOrDefault(-1);
        }

        private bool AppendRowsForPaste(int count, out List<object> addedItems)
        {
            addedItems = new List<object>();
            if (count <= 0) return true;
            if (!ConfirmUserDataMutation($"粘贴并新增 {count} 行")) return false;

            ClearFilter();

            for (int i = 0; i < count; i++)
            {
                object item = CreateNewItem();
                if (item == null)
                {
                    RemoveAddedRows(addedItems);
                    RefreshBinding();
                    return false;
                }

                addedItems.Add(item);
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

        private void RemoveAddedRows(IEnumerable<object> items)
        {
            foreach (object item in items)
            {
                if (item is DBObject dbObject)
                {
                    dbObject.Delete();
                    continue;
                }

                if (_source.Contains(item))
                    _source.Remove(item);
            }
        }

        private bool HasVisibleColumnHeader(string line)
        {
            string[] values = line.Split('\t');
            if (values.Length == 0) return false;

            HashSet<string> visibleHeaders = new HashSet<string>(
                GetVisibleColumns()
                    .Select(x => x.HeaderText?.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            if (visibleHeaders.Count == 0) return false;

            HashSet<string> matchedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in values)
            {
                string header = value.Trim();
                if (string.IsNullOrWhiteSpace(header) || !visibleHeaders.Contains(header) || !matchedHeaders.Add(header))
                    return false;
            }

            return true;
        }

        private void OnContentChanged()
        {
            if (_binding) return;

            ContentChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ClearFilter()
        {
            if (string.IsNullOrWhiteSpace(_filterText)) return;

            _filterText = string.Empty;
            FilterCleared?.Invoke(this, EventArgs.Empty);
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
                    if (text.Length <= 1)
                    {
                        value = string.IsNullOrEmpty(text) ? '\0' : text[0];
                        return true;
                    }

                    value = null;
                    return false;
                }

                if (type == typeof(bool))
                {
                    return TryConvertBooleanText(text, out value);
                }

                if (type.IsEnum)
                {
                    return TryConvertEnumText(type, text, out value);
                }

                if (type.IsSubclassOf(typeof(DBObject)))
                    return TryConvertObjectReference(text, type, out value);

                if (type == typeof(Point))
                {
                    if (!TryParseIntPair(text, out int x, out int y))
                    {
                        value = null;
                        return false;
                    }

                    value = new Point(x, y);
                    return true;
                }

                if (type == typeof(Size))
                {
                    if (!TryParseIntPair(text, out int width, out int height))
                    {
                        value = null;
                        return false;
                    }

                    value = new Size(width, height);
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

        private static bool TryParseIntPair(string text, out int first, out int second)
        {
            first = 0;
            second = 0;

            string[] parts = (text ?? string.Empty).Split(',');
            return parts.Length == 2 &&
                   int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out first) &&
                   int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out second);
        }

        private static bool TryConvertBooleanText(string text, out object value)
        {
            string normalized = text?.Trim() ?? string.Empty;

            if (normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("checked", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("是", StringComparison.OrdinalIgnoreCase) ||
                normalized == "1")
            {
                value = true;
                return true;
            }

            if (normalized.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("unchecked", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("n", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("否", StringComparison.OrdinalIgnoreCase) ||
                normalized == "0" ||
                normalized.Length == 0)
            {
                value = false;
                return true;
            }

            value = null;
            return false;
        }

        private static bool TryConvertEnumText(Type enumType, string text, out object value)
        {
            value = null;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            try
            {
                object parsed = Enum.Parse(enumType, text, true);
                if (!IsDefinedEnumValue(enumType, parsed))
                    return false;

                value = parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDefinedEnumValue(Type enumType, object value)
        {
            if (Enum.IsDefined(enumType, value))
                return true;

            if (enumType.GetCustomAttribute<FlagsAttribute>() == null)
                return false;

            ulong raw = ToUInt64(value);
            if (raw == 0)
                return true;

            ulong allowedBits = 0;
            foreach (object definedValue in Enum.GetValues(enumType))
                allowedBits |= ToUInt64(definedValue);

            return (raw & ~allowedBits) == 0;
        }

        private static ulong ToUInt64(object value)
        {
            TypeCode typeCode = Convert.GetTypeCode(value);
            return typeCode is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
                ? unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture))
                : Convert.ToUInt64(value, CultureInfo.InvariantCulture);
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
            if (type != typeof(Point) &&
                type != typeof(Size) &&
                type != typeof(Color) &&
                type != typeof(TimeSpan) &&
                type != typeof(DateTime))
                return;

            string text = Convert.ToString(e.Value, CultureInfo.InvariantCulture);
            if (TryConvertCellText(text, property.PropertyType, out object value))
            {
                e.Value = value;
                e.ParsingApplied = true;
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
