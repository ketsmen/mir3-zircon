using MirDB;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Server.WinForms
{
    public sealed class DatabaseEditorForm : Form
    {
        private readonly DatabaseLockService _lockService = new DatabaseLockService();
        private readonly Session _session;
        private readonly MirDbMetadata _metadata;
        private readonly JsonImportExportService _jsonService;
        private bool _readOnly;

        private readonly TreeView _tree = new TreeView();
        private readonly SplitContainer _split = new SplitContainer();
        private readonly SplitContainer _rightSplit = new SplitContainer();
        private readonly Panel _gridHost = new Panel();
        private readonly TabControl _childTabs = new TabControl();
        private readonly ToolStripTextBox _searchText = new ToolStripTextBox();
        private readonly ToolStripLabel _statusLabel = new ToolStripLabel();

        private MirDbGridControl _rootGrid;
        private MirDbGridControl _activeGrid;
        private Type _currentType;

        public DatabaseEditorForm()
        {
            Text = "MirDB 数据库编辑器";
            Width = 1280;
            Height = 760;

            _session = MirDbSessionFactory.Open(SessionMode.Both);
            _readOnly = !_lockService.AcquireWriteLocks(out string lockReason);
            _metadata = new MirDbMetadata(_session);
            _jsonService = new JsonImportExportService(_session);

            BuildLayout();
            BuildTree();

            if (_readOnly)
                MessageBox.Show(this, lockReason, "只读模式", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _lockService.Dispose();

            base.Dispose(disposing);
        }

        private void BuildLayout()
        {
            ToolStrip toolStrip = new ToolStrip();
            AddButton(toolStrip, "保存", (_, _) => SaveDatabase());
            AddButton(toolStrip, "新增", (_, _) => _activeGrid?.AddNew());
            AddButton(toolStrip, "插入到当前行后", (_, _) => _activeGrid?.InsertAfterCurrent());
            AddButton(toolStrip, "删除", (_, _) => _activeGrid?.DeleteSelected());
            AddButton(toolStrip, "导入", (_, _) => ImportCurrent());
            AddButton(toolStrip, "导出", (_, _) => ExportCurrent());
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(new ToolStripLabel("搜索"));
            _searchText.AutoSize = false;
            _searchText.Width = 220;
            _searchText.TextChanged += (_, _) => _activeGrid?.ApplySearch(_searchText.Text);
            toolStrip.Items.Add(_searchText);
            toolStrip.Items.Add(new ToolStripSeparator());
            _statusLabel.Text = _readOnly ? "只读" : "可编辑";
            toolStrip.Items.Add(_statusLabel);

            _split.Dock = DockStyle.Fill;
            _split.FixedPanel = FixedPanel.Panel1;
            _split.SplitterDistance = 260;

            _tree.Dock = DockStyle.Fill;
            _tree.AfterSelect += Tree_AfterSelect;
            _split.Panel1.Controls.Add(_tree);

            _rightSplit.Dock = DockStyle.Fill;
            _rightSplit.Orientation = Orientation.Horizontal;
            _rightSplit.SplitterDistance = 450;

            _gridHost.Dock = DockStyle.Fill;
            _childTabs.Dock = DockStyle.Fill;
            _rightSplit.Panel1.Controls.Add(_gridHost);
            _rightSplit.Panel2.Controls.Add(_childTabs);
            _split.Panel2.Controls.Add(_rightSplit);

            Controls.Add(_split);
            Controls.Add(toolStrip);
            toolStrip.Dock = DockStyle.Top;
        }

        private static void AddButton(ToolStrip toolStrip, string text, EventHandler click)
        {
            ToolStripButton button = new ToolStripButton(text);
            button.Click += click;
            toolStrip.Items.Add(button);
        }

        private void BuildTree()
        {
            TreeNode systemNode = new TreeNode("系统数据");
            TreeNode userNode = new TreeNode("用户数据");

            foreach (Type type in _metadata.ObjectTypes)
            {
                TreeNode node = new TreeNode($"{type.Name} ({_metadata.GetCount(type)})")
                {
                    Tag = type
                };

                if (_metadata.IsUserObject(type))
                    userNode.Nodes.Add(node);
                else
                    systemNode.Nodes.Add(node);
            }

            _tree.Nodes.Add(systemNode);
            _tree.Nodes.Add(userNode);
            systemNode.Expand();
        }

        private void Tree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is not Type type) return;
            ShowCollection(type);
        }

        private void ShowCollection(Type type)
        {
            _currentType = type;
            _gridHost.Controls.Clear();
            _childTabs.TabPages.Clear();

            IList source = _metadata.GetBinding(type);
            _rootGrid = new MirDbGridControl(_session, _metadata, type, source, _metadata.GetCollection(type), _readOnly);
            _rootGrid.CurrentObjectChanged += (_, _) => ShowChildLists(_rootGrid.CurrentObject);
            _rootGrid.BecameActive += (_, _) => _activeGrid = _rootGrid;

            _gridHost.Controls.Add(_rootGrid);
            _activeGrid = _rootGrid;
            _searchText.Text = string.Empty;
            _statusLabel.Text = $"{type.Name}: {source.Count} 行" + (_readOnly ? "，只读" : string.Empty);

            ShowChildLists(_rootGrid.CurrentObject);
        }

        private void ShowChildLists(object owner)
        {
            _childTabs.TabPages.Clear();
            if (owner == null || _currentType == null) return;

            foreach (PropertyInfo property in _metadata.GetChildListProperties(_currentType))
            {
                IList list = property.GetValue(owner) as IList;
                if (list == null) continue;

                Type childType = property.PropertyType.GetGenericArguments()[0];
                TabPage page = new TabPage($"{property.Name} ({list.Count})");
                MirDbGridControl grid = new MirDbGridControl(_session, _metadata, childType, list, null, _readOnly);
                grid.BecameActive += (_, _) => _activeGrid = grid;
                page.Controls.Add(grid);
                _childTabs.TabPages.Add(page);
            }
        }

        private void SaveDatabase()
        {
            if (_readOnly)
            {
                MessageBox.Show(this, "数据库当前以只读模式打开，不能保存。", "保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_lockService.HasWriteLocks && !_lockService.AcquireWriteLocks(out string reason))
            {
                MessageBox.Show(this, reason, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<string> validationMessages = new ValidationService(_metadata).Validate();
            if (validationMessages.Count > 0)
            {
                string text = string.Join("\r\n", validationMessages.Take(20));
                if (validationMessages.Count > 20)
                    text += $"\r\n... 还有 {validationMessages.Count - 20} 条。";

                if (MessageBox.Show(this, $"保存前发现以下问题：\r\n\r\n{text}\r\n\r\n仍然继续保存吗？", "验证", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }

            try
            {
                _lockService.ReleaseWriteLocks();
                _lockService.Backup();
                _session.Save(true);
                MirDbSessionFactory.Open(SessionMode.Both);

                if (!_lockService.AcquireWriteLocks(out string relockReason))
                {
                    _readOnly = true;
                    _statusLabel.Text = "已保存，但重新获取数据库写锁失败，编辑器将关闭。";
                    MessageBox.Show(this, relockReason, "保存完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Close();
                    return;
                }

                MessageBox.Show(this, "数据库已保存，并已完成重新读取校验。", "保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (!_readOnly && !_lockService.HasWriteLocks)
                    _lockService.AcquireWriteLocks(out _);

                MessageBox.Show(this, ex.ToString(), "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportCurrent()
        {
            if (_currentType == null || _readOnly) return;

            try
            {
                _jsonService.Import(_currentType, this);
                _rootGrid?.RefreshBinding();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportCurrent()
        {
            if (_currentType == null || _activeGrid == null) return;

            try
            {
                _jsonService.Export(_currentType, _activeGrid.SelectedObjects, this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
