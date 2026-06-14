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
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AutoGenerateColumns = false;
            _grid.MultiSelect = true;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.ReadOnly = readOnly;
            _grid.DataError += Grid_DataError;
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CellParsing += Grid_CellParsing;
            _grid.SelectionChanged += (_, _) => CurrentObjectChanged?.Invoke(this, EventArgs.Empty);
            _grid.Enter += (_, _) => BecameActive?.Invoke(this, EventArgs.Empty);

            Controls.Add(_grid);
            BuildColumns();
            RefreshBinding();
        }

        public object CurrentObject => _grid.CurrentRow?.DataBoundItem;

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
    }
}
