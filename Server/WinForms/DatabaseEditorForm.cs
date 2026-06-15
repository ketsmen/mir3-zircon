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

        private MirDbGridControl _rootGrid;
        private MirDbGridControl _activeGrid;
        private Type _currentType;
        private readonly string _initialTypeName;
        private bool _dirty;
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

            Text = onlineMode ? "MirDB 数据库编辑器 - 在线模式" : "MirDB 数据库编辑器";
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
                _lockService.Dispose();

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
            AddButton(toolStrip, "导出当前视图", (_, _) => _activeGrid?.ExportVisibleRows(this));
            toolStrip.Items.Add(new ToolStripSeparator());
            AddButton(toolStrip, "导入 Mir2 掉落", (_, _) => ImportMir2Drops());
            AddButton(toolStrip, "导出 Mir2 掉落", (_, _) => ExportMir2Drops());
            AddButton(toolStrip, "迁移怪物形象", (_, _) => MigrateLegacyMonsterImages());
            AddButton(toolStrip, "验证文本 NPC", (_, _) => ValidateTextNpcs());
            AddButton(toolStrip, "导入文本 NPC", (_, _) => ImportTextNpcs());
            AddButton(toolStrip, "导出 NPC 文本", (_, _) => ExportTextNpcs());
            toolStrip.Items.Add(new ToolStripSeparator());
            _openMapButton = AddButton(toolStrip, "打开地图", (_, _) => OpenSelectedMap());
            _mapRegionButton = AddButton(toolStrip, "编辑地图区域", (_, _) => OpenSelectedMapRegion());
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(new ToolStripLabel("表"));
            _tableSearchText.AutoSize = false;
            _tableSearchText.Width = 170;
            _tableSearchText.ToolTipText = "按表名过滤左侧导航，例如 Item、Monster、NPC。";
            _tableSearchText.TextChanged += (_, _) => BuildTree();
            toolStrip.Items.Add(_tableSearchText);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(new ToolStripLabel("搜索"));
            _searchText.AutoSize = false;
            _searchText.Width = 220;
            _searchText.TextChanged += (_, _) =>
            {
                _activeGrid?.ApplySearch(_searchText.Text);
                UpdateGridStatus();
            };
            toolStrip.Items.Add(_searchText);
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
            _propertyGrid.Dock = DockStyle.Fill;
            _propertyGrid.Enabled = !_readOnly;
            _propertyGrid.PropertySort = PropertySort.Categorized;
            _propertyGrid.ToolbarVisible = true;
            _propertyGrid.PropertyValueChanged += (_, _) =>
            {
                MarkDirty();
                _rootGrid?.RefreshBinding();
                _activeGrid?.RefreshBinding();
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
            return string.IsNullOrWhiteSpace(filter) ||
                   type.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
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

            _tree.BeginUpdate();
            _tree.Nodes.Clear();

            TreeNode systemNode = new TreeNode("系统数据");
            TreeNode userNode = new TreeNode("用户数据");
            Dictionary<string, TreeNode> systemGroups = new Dictionary<string, TreeNode>();
            Dictionary<string, TreeNode> userGroups = new Dictionary<string, TreeNode>();

            foreach (Type type in _metadata.ObjectTypes)
            {
                if (!MatchesTypeFilter(type, filter)) continue;

                TreeNode node = new TreeNode($"{type.Name} ({_metadata.GetCount(type)})")
                {
                    Tag = type
                };

                if (_metadata.IsUserObject(type))
                    GetGroupNode(userNode, userGroups, GetTypeGroup(type)).Nodes.Add(node);
                else
                    GetGroupNode(systemNode, systemGroups, GetTypeGroup(type)).Nodes.Add(node);
            }

            _tree.Nodes.Add(systemNode);
            _tree.Nodes.Add(userNode);
            systemNode.Expand();

            foreach (TreeNode group in systemGroups.Values)
                if (!string.IsNullOrWhiteSpace(filter))
                    group.Expand();

            foreach (TreeNode group in userGroups.Values)
                if (!string.IsNullOrWhiteSpace(filter))
                    group.Expand();

            if (selectedType != null)
                SelectTypeNode(selectedType);

            _tree.EndUpdate();
        }

        private void Tree_AfterSelect(object sender, TreeViewEventArgs e)
        {
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
                };
                _rootGrid.BecameActive += (_, _) => SetActiveGrid(_rootGrid);
                _rootGrid.ReferenceOpenRequested += Grid_ReferenceOpenRequested;
                _rootGrid.ContentChanged += (_, _) => MarkDirty();

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
            _childTabs.SuspendLayout();
            try
            {
                _childTabs.TabPages.Clear();
                _propertyGrid.SelectedObject = owner;

                TabPage propertyPage = new TabPage("属性");
                propertyPage.Controls.Add(_propertyGrid);
                _childTabs.TabPages.Add(propertyPage);

                if (owner == null || _currentType == null) return;

                foreach (PropertyInfo property in _metadata.GetChildListProperties(_currentType))
                {
                    IList list = property.GetValue(owner) as IList;
                    if (list == null) continue;

                    Type childType = property.PropertyType.GetGenericArguments()[0];
                    TabPage page = new TabPage($"{property.Name} ({list.Count})");
                    MirDbGridControl grid = new MirDbGridControl(_session, _metadata, childType, list, null, _readOnly, owner);
                    grid.CurrentObjectChanged += (_, _) => UpdateMapRegionCommand();
                    grid.BecameActive += (_, _) => SetActiveGrid(grid);
                    grid.ReferenceOpenRequested += Grid_ReferenceOpenRequested;
                    grid.ContentChanged += (_, _) => MarkDirty();
                    page.Controls.Add(grid);
                    _childTabs.TabPages.Add(page);
                }
            }
            finally
            {
                _childTabs.ResumeLayout();
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
            _activeGrid = grid;
            UpdateGridStatus();
            UpdateMapRegionCommand();
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
            string dirty = _dirty ? "，未保存" : string.Empty;
            _statusLabel.Text = $"{_activeGrid.ItemTypeName}: {_activeGrid.VisibleCount}/{_activeGrid.TotalCount} 行{search}{suffix}{dirty}";
        }

        private void MarkDirty()
        {
            if (_readOnly || _dirty) return;

            _dirty = true;
            UpdateGridStatus();
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

            MapViewerForm viewer = new MapViewerForm();
            viewer.LoadMap(map.FileName);
            viewer.Show(this);
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
            viewer.RegionSaved += (_, _) => MarkDirty();
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
                    MessageBox.Show(this, "数据库验证完成，未发现重复身份字段或断开的对象引用。", "验证", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string text = FormatValidationMessages(validationMessages, 50);
                MessageBox.Show(this, $"数据库验证完成，发现 {validationMessages.Count:#,##0} 条问题：\r\n\r\n{text}", "验证", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            DiagnosticsForm form = new DiagnosticsForm(_session);
            form.DatabaseChanged += (_, _) => MarkDirty();
            form.Show(this);
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
                    details += "\r\n\r\nTODO 示例:\r\n" + string.Join("\r\n", report.Todos.Take(20));

                MessageBox.Show(this, details, "导出 NPC 文本完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "导出 NPC 文本失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
                    LastTypeName = _currentType?.Name
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
        }
    }
}
