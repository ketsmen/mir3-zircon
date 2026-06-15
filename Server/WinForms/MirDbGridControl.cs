using MirDB;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
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
        private readonly DataGridView _grid = new DataGridView();
        private readonly ContextMenuStrip _contextMenu = new ContextMenuStrip();
        private string _filterText = string.Empty;

        public event EventHandler CurrentObjectChanged;
        public event EventHandler BecameActive;

        public MirDbGridControl(Session session, MirDbMetadata metadata, Type itemType, IList source, object rootCollection, bool readOnly)
        {
            _session = session;
            _metadata = metadata;
            _itemType = itemType;
            _source = source;
            _rootCollection = rootCollection;
            _readOnly = readOnly;

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
            _grid.KeyDown += Grid_KeyDown;

            BuildContextMenu();

            Controls.Add(_grid);
            BuildColumns();
            RefreshBinding();
        }

        public object CurrentObject => _grid.CurrentRow?.DataBoundItem;

        public string ItemTypeName => _itemType.Name;

        public int VisibleCount => _grid.Rows.Count;

        public int TotalCount => _source.Count;

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
            IList list = CreateVisibleList();
            _grid.DataSource = list;
        }

        public void ApplySearch(string text)
        {
            _filterText = text ?? string.Empty;
            RefreshBinding();
        }

        public void AddNew()
        {
            if (_readOnly) return;

            object item;

            if (_rootCollection != null)
            {
                item = _metadata.CreateRootObject(_itemType);
            }
            else if (_source is IBindingList bindingList)
            {
                item = bindingList.AddNew();
            }
            else
            {
                MessageBox.Show(this, "当前集合不支持新增。", "新增", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _filterText = string.Empty;
            RefreshBinding();
            SelectObject(item);
        }

        public void InsertAfterCurrent()
        {
            if (_readOnly) return;

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
        }

        public void DeleteSelected()
        {
            if (_readOnly) return;

            List<DBObject> objects = SelectedObjects.OfType<DBObject>().ToList();
            if (objects.Count == 0) return;

            if (MessageBox.Show(this, $"确定删除选中的 {objects.Count} 行吗？", "删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            foreach (DBObject item in objects)
                item.Delete();

            RefreshBinding();
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

            Type listType = typeof(BindingList<>).MakeGenericType(_itemType);
            IList list = (IList)Activator.CreateInstance(listType);
            foreach (object item in items)
                list.Add(item);

            return list;
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
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add("选择当前行", null, (_, _) => SelectCurrentRow());
            _contextMenu.Items.Add("最佳列宽", null, (_, _) => _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells));
            _contextMenu.Items.Add("隐藏当前列", null, (_, _) => HideCurrentColumn());
            _contextMenu.Items.Add("显示所有列", null, (_, _) => ShowAllColumns());
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

            List<DataGridViewColumn> columns = _grid.Columns.Cast<DataGridViewColumn>()
                .Where(x => x.Visible)
                .OrderBy(x => x.DisplayIndex)
                .ToList();

            List<string> lines = new List<string>
            {
                string.Join("\t", columns.Select(x => x.HeaderText))
            };

            foreach (DataGridViewRow row in rows)
            {
                lines.Add(string.Join("\t", columns.Select(column => Convert.ToString(row.Cells[column.Index].FormattedValue, CultureInfo.CurrentCulture)?.Replace("\t", " ") ?? string.Empty)));
            }

            Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }

        private void SelectCurrentRow()
        {
            if (_grid.CurrentRow == null) return;

            _grid.ClearSelection();
            foreach (DataGridViewCell cell in _grid.CurrentRow.Cells)
                cell.Selected = true;
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
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (_readOnly || !e.Control || e.KeyCode != Keys.V) return;

            PasteClipboardTable();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void PasteClipboardTable()
        {
            if (_grid.CurrentCell == null || !Clipboard.ContainsText()) return;

            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return;

            int startRow = _grid.CurrentCell.RowIndex;
            int startColumn = _grid.CurrentCell.ColumnIndex;
            int changed = 0;

            for (int rowOffset = 0; rowOffset < lines.Length && startRow + rowOffset < _grid.Rows.Count; rowOffset++)
            {
                string[] values = lines[rowOffset].Split('\t');

                for (int columnOffset = 0; columnOffset < values.Length && startColumn + columnOffset < _grid.Columns.Count; columnOffset++)
                {
                    DataGridViewColumn column = _grid.Columns[startColumn + columnOffset];
                    if (!column.Visible || column.ReadOnly) continue;

                    PropertyInfo property = _itemType.GetProperty(column.DataPropertyName);
                    if (property == null || !CanWrite(property)) continue;

                    if (!TryConvertCellText(values[columnOffset], property.PropertyType, out object value))
                    {
                        MessageBox.Show(this, $"无法把 `{values[columnOffset]}` 写入 {property.Name}。", "粘贴", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    _grid.Rows[startRow + rowOffset].Cells[column.Index].Value = value;
                    changed++;
                }
            }

            if (changed > 0)
            {
                _grid.EndEdit();
                _grid.Refresh();
            }
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
                column.DataSource = Enum.GetValues(propertyType);
                column.ValueType = propertyType;
                return column;
            }

            if (propertyType.IsSubclassOf(typeof(DBObject)))
            {
                DataGridViewComboBoxColumn column = new DataGridViewComboBoxColumn();
                column.DataSource = _metadata.GetObjectChoices(propertyType);
                column.DisplayMember = nameof(ObjectChoice.Display);
                column.ValueMember = nameof(ObjectChoice.Value);
                column.ValueType = propertyType;
                column.FlatStyle = FlatStyle.Flat;
                return column;
            }

            DataGridViewTextBoxColumn textColumn = new DataGridViewTextBoxColumn();
            textColumn.ValueType = propertyType;

            if (!MirDbMetadata.IsSimpleEditable(propertyType))
                textColumn.ReadOnly = true;

            return textColumn;
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
}
