using Library.SystemModels;
using MirDB;
using Server.DBModels;
using Server.Envir;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace Server.WinForms
{
    public sealed class DatabaseEditorForm : Form
    {
        private readonly DatabaseLockService _lockService = new DatabaseLockService();
        private readonly Session _session;
        private readonly MirDbMetadata _metadata;
        private readonly JsonImportExportService _jsonService;
        private readonly bool _onlineMode;
        private bool _readOnly;

        private readonly TreeView _tree = new TreeView();
        private readonly SplitContainer _split = new SplitContainer();
        private readonly SplitContainer _rightSplit = new SplitContainer();
        private readonly Panel _gridHost = new Panel();
        private readonly TabControl _childTabs = new TabControl();
        private readonly PropertyGrid _propertyGrid = new PropertyGrid();
        private readonly ToolStripTextBox _tableSearchText = new ToolStripTextBox();
        private readonly ToolStripTextBox _searchText = new ToolStripTextBox();
        private readonly ToolStripTextBox _indexText = new ToolStripTextBox();
        private readonly ToolStripLabel _statusLabel = new ToolStripLabel();
        private ToolStripButton _openMapButton;
        private ToolStripButton _mapRegionButton;
        private DiagnosticsForm _diagnosticsForm;
        private MapViewerForm _mapViewerForm;

        private MirDbGridControl _rootGrid;
        private MirDbGridControl _activeGrid;
        private Type _currentType;
        private readonly string _initialTypeName;
        private bool _buildingTree;
        private bool _dirty;
        private const string PropertyTabKey = "__Property";
        private static readonly string UiCachePath = Path.Combine(Application.StartupPath, "DatabaseEditor.WinForms.cache.json");

        public DatabaseEditorForm()
            : this(null, false, null)
        {
        }

        public DatabaseEditorForm(string initialTypeName)
            : this(null, false, initialTypeName)
        {
        }

        public DatabaseEditorForm(Session session, bool onlineMode)
            : this(session, onlineMode, null)
        {
        }

        public DatabaseEditorForm(Session session, bool onlineMode, string initialTypeName)
        {
            _onlineMode = onlineMode;
            _initialTypeName = initialTypeName;

            UpdateWindowTitle();
            Width = 1280;
            Height = 760;
            KeyPreview = true;
            KeyDown += DatabaseEditorForm_KeyDown;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            string lockReason = string.Empty;
            if (onlineMode)
            {
                _session = session ?? throw new ArgumentNullException(nameof(session));
                _readOnly = false;
            }
            else
            {
                _session = MirDbSessionFactory.Open(SessionMode.Both);
                _readOnly = !_lockService.AcquireWriteLocks(out lockReason);
            }

            _metadata = new MirDbMetadata(_session);
            _jsonService = new JsonImportExportService(_session);

            BuildLayout();
            BuildTree();
            SelectInitialType();

            if (_readOnly)
                MessageBox.Show(this, lockReason, "只读模式", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public bool HasUnsavedChanges => _dirty;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            LoadUiCache();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_diagnosticsForm != null && !_diagnosticsForm.IsDisposed)
                    _diagnosticsForm.Close();

                if (_mapViewerForm != null && !_mapViewerForm.IsDisposed)
                    _mapViewerForm.Close();

                _lockService.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_readOnly && _dirty)
            {
                DialogResult result = MessageBox.Show(
                    this,
                    "当前数据库编辑器有未保存的修改。\r\n\r\n是：先保存再关闭。\r\n否：放弃修改并关闭。\r\n取消：继续编辑。",
                    "未保存修改",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (result == DialogResult.Yes)
                {
                    SaveDatabase();
                    if (_dirty)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }

            SaveUiCache();
            base.OnFormClosing(e);
        }

        public bool TryNavigateToType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return true;

            Type type = _metadata.ObjectTypes.FirstOrDefault(x => x.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (type == null)
            {
                MessageBox.Show(this, $"数据库表 `{typeName}` 不存在。", "数据库", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (!SelectTypeNode(type) && !string.IsNullOrWhiteSpace(_tableSearchText.Text))
            {
                _tableSearchText.Text = string.Empty;
                SelectTypeNode(type);
            }

            if (_currentType != type)
                ShowCollection(type);

            return true;
        }

        public bool TryNavigateToFilteredType(string filter, string typeName)
        {
            _tableSearchText.Text = filter ?? string.Empty;
            return TryNavigateToType(typeName);
        }

        private void BuildLayout()
        {
            ToolStrip toolStrip = new ToolStrip();
            AddButton(toolStrip, "保存", (_, _) => SaveDatabase());
            AddButton(toolStrip, "验证", (_, _) => ValidateDatabase());
            AddButton(toolStrip, "诊断", (_, _) => OpenDiagnostics());
            AddButton(toolStrip, "新增", (_, _) => _activeGrid?.AddNew());
            AddButton(toolStrip, "插入到当前行后", (_, _) => _activeGrid?.InsertAfterCurrent());
            AddButton(toolStrip, "复制当前", (_, _) => _activeGrid?.DuplicateCurrent());
            AddButton(toolStrip, "删除", (_, _) => _activeGrid?.DeleteSelected());
            toolStrip.Items.Add(new ToolStripSeparator());
            AddButton(toolStrip, "导入", (_, _) => ImportCurrent());
            AddButton(toolStrip, "导出选中", (_, _) => ExportCurrent());
            AddButton(toolStrip, "导出全部", (_, _) => ExportAllCurrent());
            AddButton(toolStrip, "复制当前视图", (_, _) => _activeGrid?.CopyVisibleRows(this));
            AddButton(toolStrip, "导出当前视图", (_, _) => _activeGrid?.ExportVisibleRows(this));
            toolStrip.Items.Add(new ToolStripSeparator());
            AddButton(toolStrip, "导入 Mir2 掉落", (_, _) => ImportMir2Drops());
            AddButton(toolStrip, "导出 Mir2 掉落", (_, _) => ExportMir2Drops());
            AddButton(toolStrip, "迁移怪物形象", (_, _) => MigrateLegacyMonsterImages());
            AddButton(toolStrip, "验证文本 NPC", (_, _) => ValidateTextNpcs());
            AddButton(toolStrip, "导入文本 NPC", (_, _) => ImportTextNpcs());
            AddButton(toolStrip, "导出 NPC 文本", (_, _) => ExportTextNpcs());
            AddButton(toolStrip, "验证自定义系统", (_, _) => ValidateCustomSystem());
            AddButton(toolStrip, "导入自定义系统", (_, _) => ImportCustomSystem());
            AddButton(toolStrip, "导出自定义系统", (_, _) => ExportCustomSystem());
            AddButton(toolStrip, "自定义目录", (_, _) => OpenCustomSystemDirectory());
            AddButton(toolStrip, "自定义帮助", (_, _) => ShowCustomSystemHelp());
            AddButton(toolStrip, "AI迁移报告", (_, _) => ShowCustomAiMigrationReport());
            toolStrip.Items.Add(new ToolStripSeparator());
            _openMapButton = AddButton(toolStrip, "打开地图", (_, _) => OpenSelectedMap());
            _mapRegionButton = AddButton(toolStrip, "编辑地图区域", (_, _) => OpenSelectedMapRegion());
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(new ToolStripLabel("表"));
            _tableSearchText.AutoSize = false;
            _tableSearchText.Width = 170;
            _tableSearchText.ToolTipText = "按表名或中文分组过滤左侧导航，例如 Item、地图、任务、NPC。";
            _tableSearchText.TextChanged += (_, _) => BuildTree();
            _tableSearchText.KeyDown += TableSearchText_KeyDown;
            toolStrip.Items.Add(_tableSearchText);
            AddButton(toolStrip, "清空表过滤", (_, _) => _tableSearchText.Text = string.Empty);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(new ToolStripLabel("搜索"));
            _searchText.AutoSize = false;
            _searchText.Width = 220;
            _searchText.TextChanged += (_, _) =>
            {
                _activeGrid?.ApplySearch(_searchText.Text);
                UpdateGridStatus();
            };
            _searchText.KeyDown += SearchText_KeyDown;
            toolStrip.Items.Add(_searchText);
            AddButton(toolStrip, "清空搜索", (_, _) => _searchText.Text = string.Empty);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(new ToolStripLabel("Index"));
            _indexText.AutoSize = false;
            _indexText.Width = 80;
            _indexText.ToolTipText = "输入对象 Index 后跳转到当前表或当前子表的对应行。";
            _indexText.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;

                JumpToIndex();
                e.Handled = true;
                e.SuppressKeyPress = true;
            };
            toolStrip.Items.Add(_indexText);
            AddButton(toolStrip, "跳转", (_, _) => JumpToIndex());
            toolStrip.Items.Add(new ToolStripSeparator());
            _statusLabel.Text = GetModeText();
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
            _childTabs.SelectedIndexChanged += (_, _) => UpdateActiveGridFromSelectedChildTab();
            _propertyGrid.Dock = DockStyle.Fill;
            _propertyGrid.Enabled = !_readOnly;
            _propertyGrid.PropertySort = PropertySort.Categorized;
            _propertyGrid.ToolbarVisible = true;
            _propertyGrid.PropertyValueChanged += (_, _) =>
            {
                MarkDirty();
                _rootGrid?.RefreshBinding();
                _activeGrid?.RefreshBinding();
                UpdateVisibleTypeNodeCounts();
                UpdateGridStatus();
            };
            _rightSplit.Panel1.Controls.Add(_gridHost);
            _rightSplit.Panel2.Controls.Add(_childTabs);
            _split.Panel2.Controls.Add(_rightSplit);

            Controls.Add(_split);
            Controls.Add(toolStrip);
            toolStrip.Dock = DockStyle.Top;

            UpdateMapRegionCommand();
        }

        private void DatabaseEditorForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                SaveDatabase();
                SuppressKey(e);
                return;
            }

            if (e.Control && e.KeyCode == Keys.F)
            {
                FocusToolStripTextBox(_searchText);
                SuppressKey(e);
                return;
            }

            if (e.Control && e.KeyCode == Keys.L)
            {
                FocusToolStripTextBox(_tableSearchText);
                SuppressKey(e);
                return;
            }

            if (e.Control && e.KeyCode == Keys.G)
            {
                FocusToolStripTextBox(_indexText);
                SuppressKey(e);
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                RefreshActiveGrid();
                SuppressKey(e);
                return;
            }

            if (_readOnly || IsEditingText()) return;

            if (e.Control && e.Shift && e.KeyCode == Keys.N)
            {
                _activeGrid?.InsertAfterCurrent();
                SuppressKey(e);
                return;
            }

            if (e.Control && e.KeyCode == Keys.N)
            {
                _activeGrid?.AddNew();
                SuppressKey(e);
                return;
            }

            if (e.Control && e.KeyCode == Keys.D)
            {
                _activeGrid?.DuplicateCurrent();
                SuppressKey(e);
                return;
            }
        }

        private void TableSearchText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                _tableSearchText.Text = string.Empty;
                SuppressKey(e);
                return;
            }

            if (e.KeyCode != Keys.Enter) return;

            SelectFirstVisibleTypeNode();
            SuppressKey(e);
        }

        private void SearchText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                _searchText.Text = string.Empty;
                SuppressKey(e);
                return;
            }

            if (e.KeyCode != Keys.Enter) return;

            _activeGrid?.FocusGrid();
            SuppressKey(e);
        }

        private static void SuppressKey(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private static void FocusToolStripTextBox(ToolStripTextBox textBox)
        {
            textBox.Focus();
            textBox.TextBox.SelectAll();
        }

        private bool IsEditingText()
        {
            Control focused = ActiveControl;
            return focused is TextBoxBase || _tableSearchText.Focused || _searchText.Focused || _indexText.Focused;
        }

        private void RefreshActiveGrid()
        {
            _activeGrid?.RefreshBinding();
            UpdateVisibleTypeNodeCounts();
            ShowChildLists(_rootGrid?.CurrentObject);
            UpdateGridStatus();
        }

        private void MarkExternalDatabaseChanged()
        {
            MarkDirty();
            _rootGrid?.RefreshBinding();
            _activeGrid?.RefreshBinding();
            _propertyGrid.Refresh();
            UpdateVisibleTypeNodeCounts();
            ShowChildLists(_rootGrid?.CurrentObject);
            UpdateGridStatus();
        }

        private static TreeNode GetGroupNode(TreeNode root, Dictionary<string, TreeNode> groups, string groupName)
        {
            if (groups.TryGetValue(groupName, out TreeNode node))
                return node;

            node = new TreeNode(groupName);
            groups[groupName] = node;
            root.Nodes.Add(node);
            return node;
        }

        private static bool MatchesTypeFilter(Type type, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;

            return TextMatchesFilter(type.Name, filter) ||
                   TextMatchesFilter(GetTypeGroup(type), filter);
        }

        private static bool TextMatchesFilter(string text, string filter)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetTypeGroup(Type type)
        {
            string name = type.Name;

            if (name.Contains("Item", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Bundle", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("LootBox", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Set", StringComparison.OrdinalIgnoreCase))
                return "物品与装备";

            if (name.Contains("Monster", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Respawn", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Drop", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Movement", StringComparison.OrdinalIgnoreCase))
                return "怪物与掉落";

            if (name.Contains("Map", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Region", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("SafeZone", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Castle", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Conquest", StringComparison.OrdinalIgnoreCase))
                return "地图与区域";

            if (name.Contains("NPC", StringComparison.OrdinalIgnoreCase))
                return "NPC";

            if (name.Contains("Quest", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Event", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Milestone", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Fame", StringComparison.OrdinalIgnoreCase))
                return "任务与事件";

            if (name.Contains("Store", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Payment", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Currency", StringComparison.OrdinalIgnoreCase))
                return "商城与货币";

            if (name.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Character", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Mail", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Chat", StringComparison.OrdinalIgnoreCase))
                return "账号与角色";

            if (name.Contains("Magic", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Discipline", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Companion", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Fishing", StringComparison.OrdinalIgnoreCase))
                return "技能与系统";

            return "其他";
        }

        private void SelectInitialType()
        {
            if (string.IsNullOrWhiteSpace(_initialTypeName)) return;

            Type type = _metadata.ObjectTypes.FirstOrDefault(x => x.Name.Equals(_initialTypeName, StringComparison.OrdinalIgnoreCase));
            if (type == null)
            {
                MessageBox.Show(this, $"数据库表 `{_initialTypeName}` 不存在，已打开完整数据库编辑器。", "数据库", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SelectTypeNode(type);
            ShowCollection(type);
        }

        private bool SelectTypeNode(Type type)
        {
            TreeNode node = FindNodeByType(_tree.Nodes, type);
            if (node == null) return false;

            TreeNode parent = node.Parent;
            while (parent != null)
            {
                parent.Expand();
                parent = parent.Parent;
            }

            _tree.SelectedNode = node;
            node.EnsureVisible();
            return true;
        }

        private void SelectFirstVisibleTypeNode()
        {
            TreeNode node = FindFirstTypeNode(_tree.Nodes);
            if (node?.Tag is not Type type) return;

            SelectTypeNode(type);
            ShowCollection(type);
        }

        private static TreeNode FindNodeByType(TreeNodeCollection nodes, Type type)
        {
            foreach (TreeNode node in nodes)
            {
                if (ReferenceEquals(node.Tag, type)) return node;

                TreeNode child = FindNodeByType(node.Nodes, type);
                if (child != null) return child;
            }

            return null;
        }

        private static TreeNode FindFirstTypeNode(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is Type) return node;

                TreeNode child = FindFirstTypeNode(node.Nodes);
                if (child != null) return child;
            }

            return null;
        }

        private static ToolStripButton AddButton(ToolStrip toolStrip, string text, EventHandler click)
        {
            ToolStripButton button = new ToolStripButton(text);
            button.Click += click;
            toolStrip.Items.Add(button);
            return button;
        }

        private void BuildTree()
        {
            Type selectedType = _tree.SelectedNode?.Tag as Type;
            string filter = _tableSearchText.Text?.Trim();

            _buildingTree = true;
            _tree.BeginUpdate();
            try
            {
                _tree.Nodes.Clear();

                TreeNode systemNode = new TreeNode("系统数据");
                TreeNode userNode = new TreeNode("用户数据");
                Dictionary<string, TreeNode> systemGroups = new Dictionary<string, TreeNode>();
                Dictionary<string, TreeNode> userGroups = new Dictionary<string, TreeNode>();

                foreach (Type type in _metadata.ObjectTypes)
                {
                    if (!MatchesTypeFilter(type, filter)) continue;

                    TreeNode node = new TreeNode(GetTypeNodeText(type))
                    {
                        Tag = type
                    };

                    if (_metadata.IsUserObject(type))
                        GetGroupNode(userNode, userGroups, GetTypeGroup(type)).Nodes.Add(node);
                    else
                        GetGroupNode(systemNode, systemGroups, GetTypeGroup(type)).Nodes.Add(node);
                }

                bool hasFilter = !string.IsNullOrWhiteSpace(filter);
                bool hasMatches = systemNode.Nodes.Count > 0 || userNode.Nodes.Count > 0;

                if (!hasFilter || systemNode.Nodes.Count > 0)
                    _tree.Nodes.Add(systemNode);

                if (!hasFilter || userNode.Nodes.Count > 0)
                    _tree.Nodes.Add(userNode);

                if (hasFilter && !hasMatches)
                    _tree.Nodes.Add(new TreeNode("无匹配表"));

                if (_tree.Nodes.Contains(systemNode))
                    systemNode.Expand();

                if (hasFilter && userNode.Nodes.Count > 0)
                    userNode.Expand();

                foreach (TreeNode group in systemGroups.Values)
                    if (hasFilter)
                        group.Expand();

                foreach (TreeNode group in userGroups.Values)
                    if (hasFilter)
                        group.Expand();

                if (selectedType != null)
                    SelectTypeNode(selectedType);
            }
            finally
            {
                _tree.EndUpdate();
                _buildingTree = false;
            }
        }

        private void Tree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (_buildingTree) return;
            if (e.Node?.Tag is not Type type) return;
            ShowCollection(type);
        }

        private void ShowCollection(Type type)
        {
            SuspendLayout();
            _gridHost.SuspendLayout();
            _childTabs.SuspendLayout();

            try
            {
                _currentType = type;
                _gridHost.Controls.Clear();
                _childTabs.TabPages.Clear();

                IList source = _metadata.GetBinding(type);
                _rootGrid = new MirDbGridControl(_session, _metadata, type, source, _metadata.GetCollection(type), _readOnly);
                _rootGrid.CurrentObjectChanged += (_, _) =>
                {
                    ShowChildLists(_rootGrid.CurrentObject);
                    UpdateMapRegionCommand();
                    UpdateGridStatus();
                };
                _rootGrid.BecameActive += (_, _) => SetActiveGrid(_rootGrid);
                _rootGrid.ReferenceOpenRequested += Grid_ReferenceOpenRequested;
                _rootGrid.ContentChanged += (_, _) =>
                {
                    MarkDirty();
                    UpdateVisibleTypeNodeCounts();
                };

                _gridHost.Controls.Add(_rootGrid);
                SetActiveGrid(_rootGrid);
                _searchText.Text = string.Empty;
                UpdateGridStatus();

                ShowChildLists(_rootGrid.CurrentObject);
            }
            finally
            {
                _childTabs.ResumeLayout();
                _gridHost.ResumeLayout();
                ResumeLayout();
            }
        }

        private void ShowChildLists(object owner)
        {
            string selectedTabKey = _childTabs.SelectedTab?.Tag as string;
            _childTabs.SuspendLayout();
            try
            {
                _childTabs.TabPages.Clear();
                _propertyGrid.SelectedObject = owner;

                TabPage propertyPage = new TabPage("属性") { Tag = PropertyTabKey };
                propertyPage.Controls.Add(_propertyGrid);
                _childTabs.TabPages.Add(propertyPage);

                if (owner == null || _currentType == null)
                {
                    RestoreChildTab(selectedTabKey);
                    return;
                }

                foreach (PropertyInfo property in _metadata.GetChildListProperties(_currentType))
                {
                    IList list = property.GetValue(owner) as IList;
                    if (list == null) continue;

                    Type childType = property.PropertyType.GetGenericArguments()[0];
                    TabPage page = new TabPage($"{property.Name} ({list.Count})") { Tag = property.Name };
                    MirDbGridControl grid = new MirDbGridControl(_session, _metadata, childType, list, null, _readOnly, owner);
                    grid.CurrentObjectChanged += (_, _) =>
                    {
                        UpdateMapRegionCommand();
                        UpdateGridStatus();
                    };
                    grid.BecameActive += (_, _) => SetActiveGrid(grid);
                    grid.ReferenceOpenRequested += Grid_ReferenceOpenRequested;
                    grid.ContentChanged += (_, _) =>
                    {
                        page.Text = $"{property.Name} ({list.Count})";
                        MarkDirty();
                    };
                    page.Controls.Add(grid);
                    _childTabs.TabPages.Add(page);
                }

                RestoreChildTab(selectedTabKey);
            }
            finally
            {
                _childTabs.ResumeLayout();
            }

            UpdateActiveGridFromSelectedChildTab();
        }

        private void RestoreChildTab(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            foreach (TabPage page in _childTabs.TabPages)
            {
                if (!string.Equals(page.Tag as string, key, StringComparison.Ordinal)) continue;

                _childTabs.SelectedTab = page;
                return;
            }
        }

        private void Grid_ReferenceOpenRequested(object sender, DBObjectReferenceEventArgs e)
        {
            if (e.Reference == null) return;

            Type type = e.Reference.GetType();
            if (!SelectTypeNode(type) && !string.IsNullOrWhiteSpace(_tableSearchText.Text))
            {
                _tableSearchText.Text = string.Empty;
                SelectTypeNode(type);
            }

            if (!_metadata.ObjectTypes.Contains(type))
            {
                MessageBox.Show(this, $"找不到引用对象类型 {type.Name} 的数据库集合。", "打开引用对象", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_currentType != type)
                ShowCollection(type);

            _searchText.Text = string.Empty;
            if (!_rootGrid.SelectByIndex(e.Reference.Index))
                MessageBox.Show(this, $"已打开 {type.Name}，但没有找到 Index={e.Reference.Index} 的对象。", "打开引用对象", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SetActiveGrid(MirDbGridControl grid)
        {
            if (ReferenceEquals(_activeGrid, grid))
            {
                UpdateGridStatus();
                UpdateMapRegionCommand();
                return;
            }

            _activeGrid = grid;
            _activeGrid?.ApplySearch(_searchText.Text);
            UpdateGridStatus();
            UpdateMapRegionCommand();
        }

        private void UpdateActiveGridFromSelectedChildTab()
        {
            MirDbGridControl grid = FindChildGrid(_childTabs.SelectedTab);
            SetActiveGrid(grid ?? _rootGrid);
        }

        private static MirDbGridControl FindChildGrid(Control parent)
        {
            if (parent == null) return null;
            if (parent is MirDbGridControl grid) return grid;

            foreach (Control child in parent.Controls)
            {
                MirDbGridControl result = FindChildGrid(child);
                if (result != null) return result;
            }

            return null;
        }

        private string GetTypeNodeText(Type type)
        {
            return $"{type.Name} ({_metadata.GetCount(type)})";
        }

        private void UpdateVisibleTypeNodeCounts()
        {
            foreach (TreeNode node in GetAllTreeNodes(_tree.Nodes))
            {
                if (node.Tag is Type type)
                    node.Text = GetTypeNodeText(type);
            }
        }

        private void UpdateGridStatus()
        {
            if (_activeGrid == null)
            {
                _statusLabel.Text = GetModeText();
                return;
            }

            string suffix = _readOnly ? "，只读" : string.Empty;
            if (_onlineMode)
                suffix += "，在线";

            string search = string.IsNullOrWhiteSpace(_searchText.Text) ? string.Empty : $"，过滤: {_searchText.Text}";
            string current = string.IsNullOrWhiteSpace(_activeGrid.CurrentIndexText) ? string.Empty : $"，当前 Index={_activeGrid.CurrentIndexText}";
            int selectedCount = _activeGrid.SelectedObjectCount;
            string selected = selectedCount > 1 ? $"，选中 {selectedCount:#,##0}" : string.Empty;
            string sort = string.IsNullOrWhiteSpace(_activeGrid.SortText) ? string.Empty : $"，排序: {_activeGrid.SortText}";
            string dirty = _dirty ? "，未保存" : string.Empty;
            _statusLabel.Text = $"{_activeGrid.ItemTypeName}: {_activeGrid.VisibleCount}/{_activeGrid.TotalCount} 行{current}{selected}{search}{sort}{suffix}{dirty}";
        }

        private void MarkDirty()
        {
            if (_readOnly) return;

            if (!_dirty)
            {
                _dirty = true;
                UpdateWindowTitle();
            }

            UpdateGridStatus();
        }

        private void UpdateWindowTitle()
        {
            Text = (_onlineMode ? "MirDB 数据库编辑器 - 在线模式" : "MirDB 数据库编辑器") + (_dirty ? " *" : string.Empty);
        }

        private void UpdateMapRegionCommand()
        {
            if (_mapRegionButton == null || _openMapButton == null) return;

            object current = _activeGrid?.CurrentObject;
            _openMapButton.Enabled = current is MapInfo || current is MapRegion { Map: not null };
            _mapRegionButton.Enabled = !_readOnly && current is MapRegion;
        }

        private void OpenSelectedMap()
        {
            object current = _activeGrid?.CurrentObject;
            MapInfo map = current switch
            {
                MapInfo mapInfo => mapInfo,
                MapRegion region => region.Map,
                _ => null
            };

            if (map == null)
            {
                MessageBox.Show(this, "请先选择 MapInfo，或选择已关联 Map 的 MapRegion。", "地图", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(map.FileName))
            {
                MessageBox.Show(this, "当前地图没有 FileName，无法打开地图文件。", "地图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_mapViewerForm == null || _mapViewerForm.IsDisposed)
            {
                _mapViewerForm = new MapViewerForm();
                _mapViewerForm.FormClosed += (_, _) => _mapViewerForm = null;
                _mapViewerForm.Show(this);
            }
            else
            {
                ActivateForm(_mapViewerForm);
            }

            _mapViewerForm.MapRegion = null;
            _mapViewerForm.LoadMap(map.FileName);
        }

        private void JumpToIndex()
        {
            if (_activeGrid == null) return;

            if (!int.TryParse(_indexText.Text?.Trim(), out int index))
            {
                MessageBox.Show(this, "请输入有效的数字 Index。", "跳转", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_activeGrid.SelectByIndex(index))
            {
                UpdateGridStatus();
                return;
            }

            if (!string.IsNullOrWhiteSpace(_searchText.Text))
            {
                DialogResult result = MessageBox.Show(
                    this,
                    $"当前过滤结果中没有 Index={index} 的对象。\r\n\r\n是否清空搜索后重试？",
                    "跳转",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    _searchText.Text = string.Empty;
                    if (_activeGrid.SelectByIndex(index))
                    {
                        UpdateGridStatus();
                        return;
                    }
                }
            }

            MessageBox.Show(this, $"当前表中没有找到 Index={index} 的对象。", "跳转", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenSelectedMapRegion()
        {
            if (_readOnly)
            {
                MessageBox.Show(this, "数据库当前以只读模式打开，不能编辑地图区域。", "地图区域", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_activeGrid?.CurrentObject is not MapRegion region)
            {
                MessageBox.Show(this, "请先在 MapRegion 表，或 MapInfo 的 Regions 子表中选择一个地图区域。", "地图区域", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (region.Map == null)
            {
                MessageBox.Show(this, "当前 MapRegion 没有关联 Map，不能打开地图区域编辑器。", "地图区域", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MapViewerForm viewer = new MapViewerForm
            {
                MapRegion = region
            };
            viewer.RegionSaved += (_, _) => MarkExternalDatabaseChanged();
            viewer.Show(this);
        }

        private void SaveDatabase()
        {
            if (_readOnly)
            {
                MessageBox.Show(this, "数据库当前以只读模式打开，不能保存。", "保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_onlineMode && !_lockService.HasWriteLocks && !_lockService.AcquireWriteLocks(out string reason))
            {
                MessageBox.Show(this, reason, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<string> validationMessages = ValidateDatabaseCore();
            if (validationMessages.Count > 0)
            {
                string text = FormatValidationMessages(validationMessages, 20);

                if (MessageBox.Show(this, $"保存前发现以下问题：\r\n\r\n{text}\r\n\r\n仍然继续保存吗？", "验证", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }

            try
            {
                string runtimeRefresh = string.Empty;
                if (_onlineMode)
                {
                    lock (_session)
                    {
                        _lockService.Backup();
                        _session.Save(true);
                        runtimeRefresh = RefreshOnlineRuntimeCaches();
                    }
                }
                else
                {
                    _lockService.ReleaseWriteLocks();
                    _lockService.Backup();
                    _session.Save(true);
                }

                MirDbSessionFactory.Open(SessionMode.Both);

                if (!_onlineMode && !_lockService.AcquireWriteLocks(out string relockReason))
                {
                    _readOnly = true;
                    _statusLabel.Text = "已保存，但重新获取数据库写锁失败，编辑器将关闭。";
                    MessageBox.Show(this, relockReason, "保存完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Close();
                    return;
                }

                string message = "数据库已保存，并已完成重新读取校验。";
                if (!string.IsNullOrWhiteSpace(runtimeRefresh))
                    message += "\r\n\r\n" + runtimeRefresh;

                _dirty = false;
                UpdateWindowTitle();
                UpdateGridStatus();
                MessageBox.Show(this, message, "保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (!_onlineMode && !_readOnly && !_lockService.HasWriteLocks)
                    _lockService.AcquireWriteLocks(out _);

                MessageBox.Show(this, ex.ToString(), "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ValidateDatabase()
        {
            try
            {
                UseWaitCursor = true;
                List<string> validationMessages = ValidateDatabaseCore();

                if (validationMessages.Count == 0)
                {
                    MessageBox.Show(this, "数据库验证完成，未发现重复身份字段、断开的对象引用或业务规则问题。", "验证", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                OpenDiagnosticsForm()?.ShowReport(BuildValidationReport(validationMessages));

                string text = FormatValidationMessages(validationMessages, 20);
                MessageBox.Show(this, $"数据库验证完成，发现 {validationMessages.Count:#,##0} 条问题，已在诊断窗口显示完整报告：\r\n\r\n{text}", "验证", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "验证失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void OpenDiagnostics()
        {
            OpenDiagnosticsForm()?.ShowReportPage();
        }

        private DiagnosticsForm OpenDiagnosticsForm()
        {
            if (_diagnosticsForm == null || _diagnosticsForm.IsDisposed)
            {
                _diagnosticsForm = new DiagnosticsForm(_session);
                _diagnosticsForm.DatabaseChanged += (_, _) => MarkExternalDatabaseChanged();
                _diagnosticsForm.FormClosed += (_, _) => _diagnosticsForm = null;
                _diagnosticsForm.Show(this);
                return _diagnosticsForm;
            }
            else
            {
                ActivateForm(_diagnosticsForm);
            }

            return _diagnosticsForm;
        }

        private static void ActivateForm(Form form)
        {
            if (form.WindowState == FormWindowState.Minimized)
                form.WindowState = FormWindowState.Normal;

            form.BringToFront();
            form.Activate();
        }

        private List<string> ValidateDatabaseCore()
        {
            return new ValidationService(_metadata).Validate();
        }

        private static string FormatValidationMessages(List<string> messages, int maxMessages)
        {
            string text = string.Join("\r\n", messages.Take(maxMessages));
            if (messages.Count > maxMessages)
                text += $"\r\n... 还有 {messages.Count - maxMessages} 条。";

            return text;
        }

        private static string BuildValidationReport(List<string> messages)
        {
            if (messages.Count == 0)
                return "数据库验证完成，未发现重复身份字段、断开的对象引用或业务规则问题。";

            return $"数据库验证完成，发现 {messages.Count:#,##0} 条问题。\r\n\r\n" +
                   string.Join("\r\n", messages);
        }

        private string RefreshOnlineRuntimeCaches()
        {
            if (!_onlineMode) return string.Empty;

            List<string> refreshed = new List<string>();
            Type itemType = _activeGrid?.ItemType ?? _currentType;

            if (itemType != null && (itemType == typeof(MonsterInfo) || itemType.Name.Contains("Drop", StringComparison.OrdinalIgnoreCase)))
            {
                SEnvir.RebuildMonsterDropTable();
                refreshed.Add("已刷新怪物掉落缓存。");
            }

            if (itemType == typeof(MapInfo) || itemType == typeof(MapInfoStat))
            {
                foreach (MapInfo map in SEnvir.MapInfoList?.Binding ?? Enumerable.Empty<MapInfo>())
                    map.StatsChanged();

                refreshed.Add("已刷新地图 BuffStats 统计。");
            }

            if (itemType == typeof(UserDrop) || itemType == typeof(AccountInfo))
            {
                foreach (AccountInfo account in SEnvir.AccountInfoList?.Binding ?? Enumerable.Empty<AccountInfo>())
                    account.RebuildUserDropCache();

                refreshed.Add("已刷新用户掉落缓存。");
            }

            if (refreshed.Count == 0) return string.Empty;

            return string.Join("\r\n", refreshed);
        }

        private void ImportCurrent()
        {
            if (_activeGrid == null || _readOnly) return;

            try
            {
                int imported = _jsonService.Import(_activeGrid.ItemType, this);
                if (imported > 0)
                    MarkDirty();

                _rootGrid?.RefreshBinding();
                _activeGrid?.RefreshBinding();
                UpdateVisibleTypeNodeCounts();
                ShowChildLists(_rootGrid?.CurrentObject);
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
                _jsonService.Export(_activeGrid.ItemType, _activeGrid.SelectedObjects, this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportAllCurrent()
        {
            if (_currentType == null || _activeGrid == null) return;

            try
            {
                _jsonService.Export(_activeGrid.ItemType, _activeGrid.AllObjects, this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportMir2Drops()
        {
            if (_readOnly)
            {
                MessageBox.Show(this, "数据库当前以只读模式打开，不能导入 Mir2 掉落。", "导入 Mir2 掉落", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "选择 Mir2 服务端 Envir\\Drops 目录",
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            bool replaceExisting = MessageBox.Show(
                this,
                "是否清空匹配怪物现有掉落后重新导入？\r\n\r\n选择“是”会避免重复掉落，这是推荐方式；选择“否”会合并导入。",
                "导入 Mir2 掉落",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;

            try
            {
                Mir2DropImporter importer = new Mir2DropImporter(_session);
                Mir2DropImportReport report = importer.Import(dialog.SelectedPath, replaceExisting);

                if (_onlineMode)
                    SEnvir.RebuildMonsterDropTable();

                MarkDirty();
                _rootGrid?.RefreshBinding();
                UpdateVisibleTypeNodeCounts();
                ShowChildLists(_rootGrid?.CurrentObject);

                string details = report.ToDisplayText();
                if (report.MissingItems.Count > 0)
                    details += "\r\n\r\n缺失物品示例:\r\n" + string.Join("\r\n", report.MissingItems.Distinct(StringComparer.OrdinalIgnoreCase).Take(20));
                if (report.MissingMonsters.Count > 0)
                    details += "\r\n\r\n缺失怪物示例:\r\n" + string.Join("\r\n", report.MissingMonsters.Distinct(StringComparer.OrdinalIgnoreCase).Take(20));
                if (report.QuestOnlyLines.Count > 0)
                    details += "\r\n\r\n任务专属行示例:\r\n" + string.Join("\r\n", report.QuestOnlyLines.Take(20));
                if (report.Failures.Count > 0)
                    details += "\r\n\r\n失败示例:\r\n" + string.Join("\r\n", report.Failures.Take(20));

                MessageBox.Show(this, details, "导入 Mir2 掉落完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "导入 Mir2 掉落失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportMir2Drops()
        {
            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "请选择导出 Mir2 掉落文件的输出目录",
                UseDescriptionForTitle = true,
                SelectedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mir2DropsExport")
            };

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;

            try
            {
                Mir2DropExporter exporter = new Mir2DropExporter(_session);
                Mir2DropExportReport report = exporter.Export(dialog.SelectedPath);

                string details =
                    $"输出目录: {dialog.SelectedPath}\r\n" +
                    $"已导出怪物: {report.MonstersExported}\r\n" +
                    $"普通掉落行: {report.DropLines}\r\n" +
                    $"GROUP 行: {report.GroupLines}\r\n" +
                    $"组内物品行: {report.GroupItemLines}";

                MessageBox.Show(this, details, "导出 Mir2 掉落完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "导出 Mir2 掉落失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MigrateLegacyMonsterImages()
        {
            if (_readOnly)
            {
                MessageBox.Show(this, "数据库当前以只读模式打开，不能迁移怪物形象。", "迁移怪物形象", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<MonsterInfo> monsters = _session.GetCollection<MonsterInfo>().Binding.ToList();
            MonsterImageMigrationService service = new MonsterImageMigrationService();
            MonsterImageMigrationReport preview = service.Preview(monsters);

            if (preview.LegacyMarkers == 0)
            {
                MessageBox.Show(
                    this,
                    "没有检测到旧格式怪物形象标记。\r\n\r\n旧数据通常会存在 Image 为 1-9 的怪物；当前数据库看起来已经使用新枚举，未执行迁移。",
                    "迁移怪物形象",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string confirmText = preview.ToDisplayText() +
                "\r\n\r\n此操作会按旧服务器编辑器的 OldMonsterImage 顺序重写 MonsterInfo.Image，执行后仍需要保存数据库才会落盘。确定继续吗？";

            if (MessageBox.Show(this, confirmText, "迁移怪物形象", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            MonsterImageMigrationReport report = service.Migrate(monsters);
            if (report.Updated > 0)
                MarkDirty();

            _rootGrid?.RefreshBinding();
            UpdateVisibleTypeNodeCounts();
            ShowChildLists(_rootGrid?.CurrentObject);

            string message = report.ToDisplayText();
            if (_onlineMode)
                message += "\r\n\r\n在线模式下已更新内存对象；请保存数据库，并根据怪物刷新情况评估是否需要重启或重刷地图。";

            MessageBox.Show(this, message, "迁移怪物形象", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ValidateTextNpcs()
        {
            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "选择包含 NPCInfo.txt 的文本 NPC 目录",
                UseDescriptionForTitle = true,
                SelectedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Envir", "NPCs")
            };

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;

            try
            {
                TextNpcDatabaseImporter importer = new TextNpcDatabaseImporter(_session);
                TextNpcDatabaseImportReport report = importer.Import(dialog.SelectedPath, TextNpcDatabaseImportMode.ValidateOnly);

                MessageBox.Show(this, BuildTextNpcImportDetails(report), "验证文本 NPC 完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "验证文本 NPC 失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportTextNpcs()
        {
            if (_readOnly)
            {
                MessageBox.Show(this, "数据库当前以只读模式打开，不能导入文本 NPC。", "导入文本 NPC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "选择包含 NPCInfo.txt 的文本 NPC 目录",
                UseDescriptionForTitle = true,
                SelectedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Envir", "NPCs")
            };

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;

            DialogResult choice = MessageBox.Show(
                this,
                "请选择导入方式：\r\n\r\n是：覆盖匹配 NPC 的旧页面树并导入新脚本。\r\n否：合并导入，保留旧页面树但把入口页切到新脚本。\r\n取消：只验证文本，不写入数据库。",
                "导入文本 NPC",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            TextNpcDatabaseImportMode mode = choice switch
            {
                DialogResult.Yes => TextNpcDatabaseImportMode.ReplaceExisting,
                DialogResult.No => TextNpcDatabaseImportMode.Merge,
                _ => TextNpcDatabaseImportMode.ValidateOnly
            };

            try
            {
                TextNpcDatabaseImporter importer = new TextNpcDatabaseImporter(_session);
                TextNpcDatabaseImportReport report = importer.Import(dialog.SelectedPath, mode);

                if (mode != TextNpcDatabaseImportMode.ValidateOnly)
                    MarkDirty();

                _rootGrid?.RefreshBinding();
                UpdateVisibleTypeNodeCounts();
                ShowChildLists(_rootGrid?.CurrentObject);

                string details = BuildTextNpcImportDetails(report);
                if (_onlineMode && mode != TextNpcDatabaseImportMode.ValidateOnly)
                    details += "\r\n\r\n在线模式已写入数据库对象；已刷出的 NPC 需要重启或重新创建地图 NPC 后才会完全使用新入口页。";

                MessageBox.Show(this, details, "导入文本 NPC 完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "导入文本 NPC 失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportTextNpcs()
        {
            List<NPCInfo> selected = _activeGrid?.SelectedObjects.OfType<NPCInfo>().Where(x => x != null).Distinct().ToList() ?? new List<NPCInfo>();
            List<NPCInfo> source;

            if (selected.Count > 0)
            {
                DialogResult exportSelected = MessageBox.Show(
                    this,
                    $"当前选中了 {selected.Count} 个 NPC。\r\n\r\n选择“是”只导出选中 NPC；选择“否”导出全部 NPC；选择“取消”停止导出。",
                    "导出 NPC 文本",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (exportSelected == DialogResult.Cancel) return;
                source = exportSelected == DialogResult.Yes ? selected : _session.GetCollection<NPCInfo>().Binding.ToList();
            }
            else
            {
                source = _session.GetCollection<NPCInfo>().Binding.ToList();
            }

            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "请选择导出 NPC 文本脚本的输出目录",
                UseDescriptionForTitle = true,
                SelectedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NPCTextExport")
            };

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;

            bool overwrite = MessageBox.Show(
                this,
                "是否覆盖输出目录中已有的 NPCInfo.txt 和同名脚本文件？",
                "导出 NPC 文本",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;

            try
            {
                TextNpcDatabaseExporter exporter = new TextNpcDatabaseExporter();
                TextNpcExportReport report = exporter.Export(dialog.SelectedPath, source, overwrite);

                string details = report.ToDisplayText();
                if (report.Todos.Count > 0)
                {
                    OpenDiagnosticsForm()?.ShowReport(report.BuildTodoDetailsText());
                    details += "\r\n\r\n完整 TODO 明细已写入报告文件，并已在诊断窗口显示，可使用“复制报告”复制。";
                    details += "\r\n\r\nTODO 示例:\r\n" + string.Join("\r\n", report.Todos.Take(20));
                }

                MessageBox.Show(this, details, "导出 NPC 文本完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "导出 NPC 文本失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ValidateCustomSystem()
        {
            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "选择包含 Resources.txt、Monsters、Skills 的自定义系统目录",
                UseDescriptionForTitle = true,
                SelectedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Envir", "Custom")
            };

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;

            try
            {
                CustomSystemDatabaseImporter importer = new CustomSystemDatabaseImporter(_session);
                CustomSystemTextReport report = importer.Import(dialog.SelectedPath, CustomSystemImportMode.ValidateOnly);
                string details = BuildCustomSystemDetails(report);
                OpenDiagnosticsForm()?.ShowReport(details);

                MessageBox.Show(this, details + "\r\n\r\n完整报告已在诊断窗口显示，可使用“复制报告”复制。", "验证自定义系统完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "验证自定义系统失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportCustomSystem()
        {
            if (_readOnly)
            {
                MessageBox.Show(this, "数据库当前以只读模式打开，不能导入自定义系统。", "导入自定义系统", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "选择包含 Resources.txt、Monsters、Skills 的自定义系统目录",
                UseDescriptionForTitle = true,
                SelectedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Envir", "Custom")
            };

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;

            DialogResult choice = MessageBox.Show(
                this,
                "请选择导入方式：\r\n\r\n是：覆盖匹配资源、怪物属性、怪物掉落、怪物视觉、怪物行为和技能配置。\r\n否：合并导入，保留未在文本中出现的旧配置；动作、规则、技能特效按名称或帧段更新，不会重复追加，掉落仍会追加。\r\n取消：只验证文本，不写入数据库。",
                "导入自定义系统",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            CustomSystemImportMode mode = choice switch
            {
                DialogResult.Yes => CustomSystemImportMode.ReplaceExisting,
                DialogResult.No => CustomSystemImportMode.Merge,
                _ => CustomSystemImportMode.ValidateOnly
            };

            try
            {
                CustomSystemDatabaseImporter importer = new CustomSystemDatabaseImporter(_session);
                CustomSystemTextReport report = importer.Import(dialog.SelectedPath, mode);

                if (mode != CustomSystemImportMode.ValidateOnly && report.Failures.Count == 0)
                    MarkDirty();

                _rootGrid?.RefreshBinding();
                UpdateVisibleTypeNodeCounts();
                ShowChildLists(_rootGrid?.CurrentObject);

                string details = BuildCustomSystemDetails(report);
                if (_onlineMode && mode != CustomSystemImportMode.ValidateOnly)
                    details += "\r\n\r\n在线模式已写入数据库对象；已刷出的怪物、已加载的技能和客户端资源缓存建议重启后完整生效。";

                OpenDiagnosticsForm()?.ShowReport(details);
                MessageBox.Show(this, details, "导入自定义系统完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "导入自定义系统失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportCustomSystem()
        {
            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "请选择导出自定义资源、怪物和技能文本的输出目录",
                UseDescriptionForTitle = true,
                SelectedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CustomSystemExport")
            };

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;

            bool overwrite = MessageBox.Show(
                this,
                "是否覆盖输出目录中已有的 Resources.txt、怪物脚本和技能脚本？",
                "导出自定义系统",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;

            try
            {
                CustomSystemDatabaseExporter exporter = new CustomSystemDatabaseExporter(_session);
                CustomSystemTextReport report = exporter.Export(dialog.SelectedPath, overwrite);
                string details = BuildCustomSystemDetails(report);
                OpenDiagnosticsForm()?.ShowReport(details);

                MessageBox.Show(this, details + "\r\n\r\n完整报告已在诊断窗口显示，可使用“复制报告”复制。", "导出自定义系统完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "导出自定义系统失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenCustomSystemDirectory()
        {
            string path = GetDefaultCustomSystemDirectory();
            Directory.CreateDirectory(path);

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "打开自定义目录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowCustomSystemHelp()
        {
            OpenDiagnosticsForm()?.ShowReport(
                "自定义怪物和技能系统速查\r\n\r\n" +
                $"默认目录: {GetDefaultCustomSystemDirectory()}\r\n\r\n" +
                "推荐工作流:\r\n" +
                "1. 导出自定义系统到文本。\r\n" +
                "2. 在 Envir\\Custom\\Resources.txt、Monsters、Skills 中修改资源、动作、AI 和技能。\r\n" +
                "3. 使用“验证自定义系统”检查缺资源、缺怪物、缺技能、规则引用和语法错误。\r\n" +
                "4. 调试期可在游戏内使用 GM 命令 @RELOADCUSTOMSYSTEM 在线重载文本覆盖并重建怪物掉落表。\r\n" +
                "5. 使用“导入自定义系统”写回数据库；稳定后可以删除文本目录或关闭文本覆盖。\r\n\r\n" +
                "迁移旧 AI:\r\n" +
                "- 使用“AI迁移报告”按 MonsterInfo.AI 统计当前数据库怪物，并查看建议模板和迁移优先级。\r\n" +
                "- High 优先级一般可直接套用 Templates 中的 RuleSet；Medium 需要人工补技能参数或专属字段；Low 建议暂保留 C#。\r\n\r\n" +
                "模板目录:\r\n" +
                "- Monsters\\Templates 和 Skills\\Templates 保存完整可验证样板，只参与验证，不参与运行时覆盖或正式导入。\r\n" +
                "- Examples 目录里的 .example 是片段说明，不参与校验。\r\n\r\n" +
                "关键表:\r\n" +
                "- CustomResourceLibraryInfo: 自定义 ZL 资源 Key、路径、版本和用途。\r\n" +
                "- CustomMonsterVisualInfo / CustomMonsterAnimationInfo: 怪物资源、动作、帧段、方向、声音和特效。\r\n" +
                "- CustomMonsterBehaviorInfo: 怪物行为模板、追击/攻击参数、拾取随从和 RuleSet。\r\n" +
                "- CustomMonsterAiActionInfo / CustomMonsterAiRuleInfo: AI 动作、组合条件、阶段计数器、目标选择。\r\n" +
                "- CustomSkillInfo / CustomSkillEffectInfo: 玩家和怪物共用技能模板、形状、多波、控制位移和自定义特效。\r\n\r\n" +
                "新增常用字段:\r\n" +
                "- AIRule.Conditions + ConditionJoin: 组合多个条件，例如 HealthBelow:50;CounterEquals:1:Phase。\r\n" +
                "- AIAction.SetCounter / ChangeCounter: 保存 Boss 阶段或机制状态。\r\n" +
                "- AIAction.MaxTargets / RandomTargetCount: 限制或随机选取集合目标。\r\n" +
                "- Behavior.Passive / NeedHarvest / HarvestCount: 迁移被动采集怪。\r\n" +
                "- Behavior.DisableMovement / DisableAttack: 迁移固定炮台、树、门、纯阻挡物。\r\n" +
                "- Behavior.DisableRegen: 关闭自然回血，迁移 QuartzTree 等固定阶段召唤物。\r\n" +
                "- Behavior.AlwaysActive: 附近没有玩家也保持激活，迁移传送门、陷阱、光环、全局事件功能怪。\r\n" +
                "- Behavior.DamageProxy: 迁移 CursedDoll/替身娃娃，支持显现延迟、出生特效、显示名格式和受击转嫁伤害。\r\n" +
                "- Behavior.IgnoreShield / IgnoreArmour: 迁移旧 C# 怪物对象上的特殊攻击标记。\r\n" +
                "- Behavior.PoisonType / PoisonRate / PoisonTicks / PoisonFrequency: 迁移基础攻击命中附毒怪。\r\n" +
                "- Behavior.PoisonStackable: 迁移强绿毒、持续叠毒类怪物。\r\n" +
                "- Behavior.MaxMinions: 控制自定义召唤类怪物维护的召唤物上限。\r\n" +
                "- Behavior.UseBaseFallback=False: RuleSet 完全接管行为，避免冷却时退回旧硬编码攻击。\r\n" +
                "- AIAction.MoveAwayFromTarget: 迁移远程怪贴脸后撤、保持距离行为。\r\n" +
                "- AIAction.TeleportNearTarget: 迁移瞬移到目标附近、偷袭和下一击强化行为。\r\n" +
                "- AIAction.BroadcastMessage / TeleportTargetsToRegion / DespawnSelf: 迁移定时传送门，全服提示、周期区域传送和自毁都可配置。\r\n" +
                "- AIAction.LineAttack + AIRule.TargetInLine: 迁移蜘蛛、声波蜥蜴、直线法师类物理直线攻击。\r\n" +
                "- AIRule.TargetIsPlayer/TargetIsMonster/TargetIsPetMonster: 迁移按目标类型切换近战、远程、治疗或守卫逻辑。\r\n" +
                "- AIRule.TargetClassWarrior/TargetClassWizard/TargetClassTaoist/TargetClassAssassin: 按玩家或英雄职业切换 Boss 行为。\r\n" +
                "- AIAction.ScheduleAction + DelayActionTime/DelayMoveTime/DelayCellTime: 迁移出生延迟、显隐动画后再改状态、阶段动作排程。\r\n" +
                "- AIAction.KillSelf + MassAreaDamage Options=AllowDead=True: 迁移自爆怪、死后爆炸和陷阱怪。\r\n" +
                "- AIAction.HazardRoam: 迁移 Tornado、移动毒云、移动火墙这类非阻挡移动危险区。\r\n" +
                "- AIRule.Trigger=OnMove: 坐标变化后执行，可清理旋转、蓄力、伏击等临时状态。\r\n" +
                "- AIAction.MapEffect/ObjectEffect Options=Effect=SummonSkeleton: 迁移出生召唤、死亡、变身等表现特效。\r\n" +
                "- AIAction.SpawnMinions Target=CurrentTarget/TargetRadius: 支持在目标附近生成召唤物。\r\n" +
                "- AIAction.Target=Summoner/SummonerGroup: 召唤物可稳定选择主人、Master 或主人队伍，适合辅助宠物和拾取随从。\r\n" +
                "- AIAction.SpawnMinions Target=ExperienceOwnerGroup Options=PerTarget=True,MaxTotal=12: 支持在经验归属玩家队伍附近生成召唤物。\r\n" +
                "- AIAction.SwapRandomMinionLocations / KillMinions: 迁移本体和分身随机换位、死亡清理分身的镜像 Boss。\r\n" +
                "- AIAction.CastSkill Options=PerTarget=True: 对集合目标逐个落地面技能，迁移多点 DeathCloud。\r\n" +
                "- AIAction.Options=OnHitAction=动作名 / ActionChance=百分比: 命中后执行附加动作，迁移命中附毒、吸血、连锁状态。\r\n" +
                "- AIAction.PullTarget / PushTarget 支持集合目标: 迁移全屏拉人、群体击退。\r\n" +
                "- AIAction.Options=ClassPowerRates=Warrior:60|Wizard:70|Taoist:40|Assassin:80: 迁移按职业调整伤害的 Boss 技能。\r\n" +
                "- AIAction.Options=NoDamage=True: 只播放攻击动作，不结算伤害，适合组合 AddBuff/SpawnMinions/ObjectEffect。\r\n" +
                "- AIAction.Options=DrainMP=True: 按实际伤害额外扣除目标 MP，迁移扣蓝攻击怪。\r\n" +
                "- ReviveSelf Options=HealthDecay=True: 复活生命随次数递减，迁移亡灵复活怪。\r\n" +
                "- Skill.Shape: Circle、Line、Cone、Rectangle。\r\n" +
                "- Skill.WaveCount / WaveInterval: 多段命中。\r\n" +
                "- Skill.KnockbackDistance / PullDistance: 模板化击退和拉拽。\r\n" +
                "- Skill.IgnoreShield / IgnoreArmour: 自定义玩家、英雄、怪物技能单独忽略护盾或防御。\r\n" +
                "- Skill.Template=DamageProxy + SummonMonster: 生成配置了 Behavior.DamageProxy 的替身，迁移 CursedDoll 类技能。\r\n" +
                "- 英雄 v1 自动使用敌对自定义技能: Direct/Projectile/Chain/Area/PersistentArea/Debuff/Control/Move；Move 会保守突进到目标附近空格。\r\n" +
                "- 道士英雄支援循环支持自定义 Heal/Buff/Summon；Summon 使用 SummonMonster/MaxSummons/SummonDuration。\r\n\r\n" +
                "示例目录:\r\n" +
                Path.Combine(GetDefaultCustomSystemDirectory(), "Monsters", "Examples") + "\r\n" +
                Path.Combine(GetDefaultCustomSystemDirectory(), "Skills", "Examples") + "\r\n\r\n" +
                "可验证模板目录:\r\n" +
                Path.Combine(GetDefaultCustomSystemDirectory(), "Monsters", "Templates") + "\r\n" +
                Path.Combine(GetDefaultCustomSystemDirectory(), "Skills", "Templates"));
        }

        private void ShowCustomAiMigrationReport()
        {
            try
            {
                CustomMonsterAiMigrationReport report = CustomMonsterAiMigrationReportBuilder.Build(_session);
                OpenDiagnosticsForm()?.ShowReport(report.ToDisplayText());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "AI迁移报告失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string GetDefaultCustomSystemDirectory()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Envir", "Custom");
        }

        private static string BuildTextNpcImportDetails(TextNpcDatabaseImportReport report)
        {
            string details = report.ToDisplayText();

            if (report.LoadReport?.MissingMaps.Count > 0)
                details += "\r\n\r\n缺失地图示例:\r\n" + string.Join("\r\n", report.LoadReport.MissingMaps.Distinct(StringComparer.OrdinalIgnoreCase).Take(20));
            if (report.LoadReport?.MissingItems.Count > 0)
                details += "\r\n\r\n缺失物品示例:\r\n" + string.Join("\r\n", report.LoadReport.MissingItems.Distinct(StringComparer.OrdinalIgnoreCase).Take(20));
            if (report.LoadReport?.MissingCurrencies.Count > 0)
                details += "\r\n\r\n缺失货币示例:\r\n" + string.Join("\r\n", report.LoadReport.MissingCurrencies.Distinct(StringComparer.OrdinalIgnoreCase).Take(20));
            if (report.LoadReport?.UnknownCommands.Count > 0)
                details += "\r\n\r\n未知命令示例:\r\n" + string.Join("\r\n", report.LoadReport.UnknownCommands.Take(20));
            if (report.LoadReport?.Failures.Count > 0)
                details += "\r\n\r\n解析错误示例:\r\n" + string.Join("\r\n", report.LoadReport.Failures.Take(20));
            if (report.Failures.Count > 0)
                details += "\r\n\r\n数据库映射错误示例:\r\n" + string.Join("\r\n", report.Failures.Take(20));

            return details;
        }

        private static string BuildCustomSystemDetails(CustomSystemTextReport report)
        {
            string details = report.ToDisplayText();

            details = AppendCustomSystemDetails(details, "缺失资源完整列表", report.MissingResources.Distinct(StringComparer.OrdinalIgnoreCase));
            details = AppendCustomSystemDetails(details, "缺失怪物完整列表", report.MissingMonsters.Distinct(StringComparer.OrdinalIgnoreCase));
            details = AppendCustomSystemDetails(details, "缺失技能完整列表", report.MissingMagics.Distinct(StringComparer.OrdinalIgnoreCase));
            details = AppendCustomSystemDetails(details, "缺失物品完整列表", report.MissingItems.Distinct(StringComparer.OrdinalIgnoreCase));
            details = AppendCustomSystemDetails(details, "错误完整列表", report.Failures);

            return details;
        }

        private static string AppendCustomSystemDetails(string details, string title, IEnumerable<string> values)
        {
            List<string> lines = values?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList() ?? new List<string>();

            if (lines.Count == 0) return details;

            return details + "\r\n\r\n" + title + ":\r\n" + string.Join("\r\n", lines);
        }

        private string GetModeText()
        {
            if (_readOnly) return "只读";
            return _onlineMode ? "在线可编辑" : "离线可编辑";
        }

        private void LoadUiCache()
        {
            try
            {
                if (!File.Exists(UiCachePath)) return;

                DatabaseEditorUiCache cache = JsonSerializer.Deserialize<DatabaseEditorUiCache>(File.ReadAllText(UiCachePath));
                if (cache == null) return;

                if (cache.Width >= 1000 && cache.Height >= 640)
                    Size = new System.Drawing.Size(cache.Width, cache.Height);

                if (cache.Maximized)
                    WindowState = FormWindowState.Maximized;

                if (cache.LeftSplitterDistance > 160)
                    _split.SplitterDistance = Math.Min(cache.LeftSplitterDistance, Math.Max(160, _split.Width - 260));

                if (cache.RightSplitterDistance > 240)
                    _rightSplit.SplitterDistance = Math.Min(cache.RightSplitterDistance, Math.Max(240, _rightSplit.Height - 180));

                ApplyExpandedTreePaths(cache.ExpandedTreePaths);

                if (string.IsNullOrWhiteSpace(_initialTypeName) && _currentType == null && !string.IsNullOrWhiteSpace(cache.LastTypeName))
                {
                    Type type = _metadata.ObjectTypes.FirstOrDefault(x => x.Name.Equals(cache.LastTypeName, StringComparison.OrdinalIgnoreCase));
                    if (type != null)
                    {
                        SelectTypeNode(type);
                        ShowCollection(type);
                    }
                }
            }
            catch (Exception ex)
            {
                SEnvir.Log($"加载数据库编辑器布局失败: {ex}");
            }
        }

        private void SaveUiCache()
        {
            try
            {
                DatabaseEditorUiCache cache = new DatabaseEditorUiCache
                {
                    Width = RestoreBounds.Width > 0 ? RestoreBounds.Width : Width,
                    Height = RestoreBounds.Height > 0 ? RestoreBounds.Height : Height,
                    Maximized = WindowState == FormWindowState.Maximized,
                    LeftSplitterDistance = _split.SplitterDistance,
                    RightSplitterDistance = _rightSplit.SplitterDistance,
                    LastTypeName = _currentType?.Name,
                    ExpandedTreePaths = GetExpandedTreePaths().ToList()
                };

                File.WriteAllText(UiCachePath, JsonSerializer.Serialize(cache, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            }
            catch (Exception ex)
            {
                SEnvir.Log($"保存数据库编辑器布局失败: {ex}");
            }
        }

        private sealed class DatabaseEditorUiCache
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public bool Maximized { get; set; }
            public int LeftSplitterDistance { get; set; }
            public int RightSplitterDistance { get; set; }
            public string LastTypeName { get; set; }
            public List<string> ExpandedTreePaths { get; set; } = new List<string>();
        }

        private IEnumerable<string> GetExpandedTreePaths()
        {
            foreach (TreeNode node in GetExpandedTreeNodes(_tree.Nodes))
                yield return node.FullPath;
        }

        private static IEnumerable<TreeNode> GetExpandedTreeNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.IsExpanded)
                    yield return node;

                foreach (TreeNode child in GetExpandedTreeNodes(node.Nodes))
                    yield return child;
            }
        }

        private void ApplyExpandedTreePaths(IReadOnlyCollection<string> expandedPaths)
        {
            if (expandedPaths == null) return;

            HashSet<string> expanded = new HashSet<string>(expandedPaths, StringComparer.OrdinalIgnoreCase);
            _tree.CollapseAll();

            foreach (TreeNode node in GetAllTreeNodes(_tree.Nodes))
            {
                if (expanded.Contains(node.FullPath))
                    node.Expand();
            }
        }

        private static IEnumerable<TreeNode> GetAllTreeNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                yield return node;

                foreach (TreeNode child in GetAllTreeNodes(node.Nodes))
                    yield return child;
            }
        }
    }
}
