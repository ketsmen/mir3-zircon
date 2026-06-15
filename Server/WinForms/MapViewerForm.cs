using Library;
using Library.SystemModels;
using Server.Envir;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Server.WinForms
{
    public sealed class MapViewerForm : Form
    {
        private const int BaseCellWidth = 48;
        private const int BaseCellHeight = 32;

        private readonly ToolStripButton _saveButton = new ToolStripButton("保存");
        private readonly ToolStripButton _cancelButton = new ToolStripButton("取消");
        private readonly ToolStripButton _attributesButton = new ToolStripButton("属性");
        private readonly ToolStripButton _selectionButton = new ToolStripButton("选择");
        private readonly ToolStripButton _blockedOnlyButton = new ToolStripButton("属性选择");
        private readonly ToolStripTextBox _jumpXText = new ToolStripTextBox();
        private readonly ToolStripTextBox _jumpYText = new ToolStripTextBox();
        private readonly ToolStripButton _jumpButton = new ToolStripButton("跳转");
        private readonly ToolStripButton _copyCoordinateButton = new ToolStripButton("复制坐标");
        private readonly Panel _canvas = new DoubleBufferedCanvas();
        private readonly HScrollBar _hScroll = new HScrollBar();
        private readonly VScrollBar _vScroll = new VScrollBar();
        private readonly StatusStrip _status = new StatusStrip();
        private readonly ToolStripStatusLabel _mapFileLabel = new ToolStripStatusLabel("地图: 未加载") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolStripStatusLabel _mapSizeLabel = new ToolStripStatusLabel("地图大小: 0,0");
        private readonly ToolStripStatusLabel _positionLabel = new ToolStripStatusLabel("坐标: 0,0");
        private readonly ToolStripStatusLabel _selectedCellsLabel = new ToolStripStatusLabel("已选格子: 0");

        private MapData _map = MapData.Empty;
        private HashSet<Point> _selection = new HashSet<Point>();
        private HashSet<Point> _originalSelection = new HashSet<Point>();
        private Point _mouseLocation = Point.Empty;
        private string _mapPath;
        private float _zoom = 1F;
        private int _radius;
        private bool _drawAttributes;
        private bool _drawSelection = true;
        private bool _attributeSelection;

        public event EventHandler RegionSaved;

        public MapViewerForm()
        {
            Text = "地图查看器";
            Width = 1100;
            Height = 720;
            KeyPreview = true;

            BuildLayout();
            UpdateCommands();
            UpdateStatus();
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MapRegion MapRegion
        {
            get => _mapRegion;
            set
            {
                if (_mapRegion == value) return;

                _mapRegion = value;
                _selection.Clear();
                _originalSelection.Clear();

                if (_mapRegion?.Map != null)
                {
                    LoadMap(_mapRegion.Map.FileName);
                    _selection = _mapRegion.GetPoints(_map.Width);
                    _originalSelection = new HashSet<Point>(_selection);
                }
                else
                {
                    _map = MapData.Empty;
                    _mapPath = null;
                }

                UpdateCommands();
                UpdateScrollBars();
                UpdateStatus();
                _canvas.Invalidate();
            }
        }
        private MapRegion _mapRegion;

        public void LoadMap(string path)
        {
            string resolved = ResolveMapPath(path);

            if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
            {
                _map = MapData.Empty;
                _mapPath = resolved;
                _selection.Clear();
                UpdateCommands();
                UpdateScrollBars();
                UpdateStatus();
                _canvas.Invalidate();
                return;
            }

            _map = MapData.Load(resolved);
            _mapPath = resolved;

            if (MapRegion == null)
            {
                _selection.Clear();
                _originalSelection.Clear();
            }

            UpdateCommands();
            UpdateScrollBars();
            UpdateStatus();
            _canvas.Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Control && e.KeyCode == Keys.O)
            {
                OpenMap();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.S)
            {
                Save();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.G)
            {
                FocusCoordinateJump();
                e.Handled = true;
            }
            else if (e.Control && e.Shift && e.KeyCode == Keys.C)
            {
                CopyCurrentCoordinate();
                e.Handled = true;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (IsSelectionDirty())
            {
                DialogResult result = MessageBox.Show(
                    this,
                    "地图区域有未保存的选择变更。\r\n\r\n是：写入当前 MapRegion 后关闭。\r\n否：放弃变更并关闭。\r\n取消：继续编辑。",
                    "未保存的地图区域",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (result == DialogResult.Yes)
                    Save(showMessage: false);
            }

            base.OnFormClosing(e);
        }

        private void BuildLayout()
        {
            ToolStrip toolStrip = new ToolStrip();
            AddButton(toolStrip, "打开", (_, _) => OpenMap());
            toolStrip.Items.Add(new ToolStripSeparator());
            AddButton(toolStrip, "重置", (_, _) => SetZoom(1F));
            AddButton(toolStrip, "放大", (_, _) => SetZoom(_zoom * 2F));
            AddButton(toolStrip, "缩小", (_, _) => SetZoom(_zoom / 2F));
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(new ToolStripLabel("X"));
            ConfigureCoordinateTextBox(_jumpXText);
            toolStrip.Items.Add(_jumpXText);
            toolStrip.Items.Add(new ToolStripLabel("Y"));
            ConfigureCoordinateTextBox(_jumpYText);
            toolStrip.Items.Add(_jumpYText);
            _jumpButton.Click += (_, _) => JumpToTextCoordinate();
            toolStrip.Items.Add(_jumpButton);
            _copyCoordinateButton.Click += (_, _) => CopyCurrentCoordinate();
            toolStrip.Items.Add(_copyCoordinateButton);
            toolStrip.Items.Add(new ToolStripSeparator());

            _attributesButton.CheckOnClick = true;
            _attributesButton.Click += (_, _) =>
            {
                _drawAttributes = _attributesButton.Checked;
                _canvas.Invalidate();
            };
            toolStrip.Items.Add(_attributesButton);

            _blockedOnlyButton.CheckOnClick = true;
            _blockedOnlyButton.Click += (_, _) =>
            {
                _attributeSelection = _blockedOnlyButton.Checked;
                _canvas.Invalidate();
            };
            toolStrip.Items.Add(_blockedOnlyButton);

            _selectionButton.CheckOnClick = true;
            _selectionButton.Checked = _drawSelection;
            _selectionButton.Click += (_, _) =>
            {
                _drawSelection = _selectionButton.Checked;
                _canvas.Invalidate();
            };
            toolStrip.Items.Add(_selectionButton);

            toolStrip.Items.Add(new ToolStripSeparator());
            _saveButton.Click += (_, _) => Save();
            _cancelButton.Click += (_, _) => CancelSelection();
            toolStrip.Items.Add(_saveButton);
            toolStrip.Items.Add(_cancelButton);

            _canvas.Dock = DockStyle.Fill;
            _canvas.BackColor = Color.FromArgb(24, 28, 32);
            _canvas.Paint += Canvas_Paint;
            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseEnter += (_, _) => _canvas.Focus();
            _canvas.MouseWheel += Canvas_MouseWheel;
            _canvas.Resize += (_, _) =>
            {
                UpdateScrollBars();
                _canvas.Invalidate();
            };

            _hScroll.Dock = DockStyle.Bottom;
            _hScroll.ValueChanged += (_, _) =>
            {
                _canvas.Invalidate();
                UpdateStatus();
            };

            _vScroll.Dock = DockStyle.Right;
            _vScroll.ValueChanged += (_, _) =>
            {
                _canvas.Invalidate();
                UpdateStatus();
            };

            Panel host = new Panel { Dock = DockStyle.Fill };
            host.Controls.Add(_canvas);
            host.Controls.Add(_vScroll);
            host.Controls.Add(_hScroll);

            _status.Items.Add(_mapFileLabel);
            _status.Items.Add(_mapSizeLabel);
            _status.Items.Add(_positionLabel);
            _status.Items.Add(_selectedCellsLabel);

            Controls.Add(host);
            Controls.Add(_status);
            Controls.Add(toolStrip);

            toolStrip.Dock = DockStyle.Top;
            _status.Dock = DockStyle.Bottom;
        }

        private static void AddButton(ToolStrip toolStrip, string text, EventHandler click)
        {
            ToolStripButton button = new ToolStripButton(text);
            button.Click += click;
            toolStrip.Items.Add(button);
        }

        private void ConfigureCoordinateTextBox(ToolStripTextBox textBox)
        {
            textBox.AutoSize = false;
            textBox.Width = 56;
            textBox.KeyDown += CoordinateTextBox_KeyDown;
        }

        private void CoordinateTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;

            JumpToTextCoordinate();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void OpenMap()
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "地图文件 (*.map)|*.map|所有文件 (*.*)|*.*",
                InitialDirectory = Directory.Exists(Config.MapPath) ? Config.MapPath : AppDomain.CurrentDomain.BaseDirectory,
                RestoreDirectory = true
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                MapRegion = null;
                LoadMap(dialog.FileName);
            }
        }

        private void Save(bool showMessage = true)
        {
            if (MapRegion == null || _map.IsEmpty) return;

            BitArray bitRegion = null;
            Point[] pointRegion = null;

            if (_selection.Count * 8 * 8 > _map.Width * _map.Height)
            {
                bitRegion = new BitArray(_map.Width * _map.Height);

                foreach (Point point in _selection)
                    bitRegion[point.Y * _map.Width + point.X] = true;
            }
            else
            {
                pointRegion = _selection.ToArray();
            }

            MapRegion.BitRegion = bitRegion;
            MapRegion.PointRegion = pointRegion;
            MapRegion.Size = _selection.Count;
            _originalSelection = new HashSet<Point>(_selection);

            UpdateCommands();
            UpdateStatus();
            RegionSaved?.Invoke(this, EventArgs.Empty);

            if (showMessage)
                MessageBox.Show(this, "地图区域选择已写入当前 MapRegion，请在数据库编辑器中保存数据库。", "地图查看器", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void CancelSelection()
        {
            if (MapRegion == null || _map.IsEmpty) return;

            _selection = new HashSet<Point>(_originalSelection);
            UpdateCommands();
            UpdateStatus();
            _canvas.Invalidate();
        }

        private void SetZoom(float value)
        {
            float zoom = Math.Max(0.125F, Math.Min(4F, value));
            if (Math.Abs(_zoom - zoom) < 0.001F) return;

            _zoom = zoom;
            UpdateScrollBars();
            _canvas.Invalidate();
        }

        private void UpdateCommands()
        {
            bool hasMap = !_map.IsEmpty;
            bool canSaveRegion = hasMap && MapRegion != null;
            bool dirty = canSaveRegion && IsSelectionDirty();

            _saveButton.Enabled = dirty;
            _cancelButton.Enabled = dirty;
            _attributesButton.Enabled = hasMap;
            _selectionButton.Enabled = hasMap;
            _blockedOnlyButton.Enabled = hasMap;
            _jumpXText.Enabled = hasMap;
            _jumpYText.Enabled = hasMap;
            _jumpButton.Enabled = hasMap;
            _copyCoordinateButton.Enabled = hasMap;
            _hScroll.Enabled = hasMap;
            _vScroll.Enabled = hasMap;
        }

        private void UpdateScrollBars()
        {
            if (_map.IsEmpty || _canvas.Width <= 0 || _canvas.Height <= 0)
            {
                _hScroll.Enabled = false;
                _vScroll.Enabled = false;
                _hScroll.Value = 0;
                _vScroll.Value = 0;
                return;
            }

            int visibleColumns = Math.Max(1, (int)Math.Ceiling(_canvas.Width / CellWidth));
            int visibleRows = Math.Max(1, (int)Math.Ceiling(_canvas.Height / CellHeight));

            ConfigureScrollBar(_hScroll, _map.Width, visibleColumns);
            ConfigureScrollBar(_vScroll, _map.Height, visibleRows);
            UpdateCommands();
        }

        private static void ConfigureScrollBar(ScrollBar scrollBar, int total, int visible)
        {
            scrollBar.Enabled = total > visible;
            scrollBar.Minimum = 0;
            scrollBar.LargeChange = Math.Max(1, visible);
            scrollBar.SmallChange = 1;
            scrollBar.Maximum = Math.Max(0, total - 1);

            int maxValue = Math.Max(0, scrollBar.Maximum - scrollBar.LargeChange + 1);
            if (scrollBar.Value > maxValue)
                scrollBar.Value = maxValue;
        }

        private void UpdateStatus()
        {
            string mapName = GetMapDisplayName();
            Text = string.IsNullOrWhiteSpace(mapName) ? "地图查看器" : $"地图查看器 - {mapName}";
            _mapFileLabel.Text = string.IsNullOrWhiteSpace(mapName) ? "地图: 未加载" : $"地图: {mapName}";
            _mapFileLabel.ToolTipText = _mapPath ?? string.Empty;
            _mapSizeLabel.Text = $"地图大小: {_map.Width},{_map.Height}";
            _positionLabel.Text = $"坐标: {_mouseLocation.X},{_mouseLocation.Y} 半径: {_radius} 缩放: {_zoom:0.###}x";
            _selectedCellsLabel.Text = IsSelectionDirty() ? $"已选格子: {_selection.Count}（未保存）" : $"已选格子: {_selection.Count}";
        }

        private string GetMapDisplayName()
        {
            if (MapRegion?.Map != null)
            {
                string mapName = MapRegion.Map.ServerDescription;
                if (!string.IsNullOrWhiteSpace(MapRegion.Description))
                    return $"{mapName} / {MapRegion.Description}";

                return mapName;
            }

            return string.IsNullOrWhiteSpace(_mapPath) ? null : Path.GetFileName(_mapPath);
        }

        private bool IsSelectionDirty()
        {
            return MapRegion != null && !_selection.SetEquals(_originalSelection);
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(24, 28, 32));

            if (_map.IsEmpty)
            {
                string text = string.IsNullOrWhiteSpace(_mapPath) ? "选择“打开”加载地图文件。" : $"地图文件不存在: {_mapPath}";
                DrawMessage(e.Graphics, text);
                return;
            }

            int startX = _hScroll.Value;
            int startY = _vScroll.Value;
            int maxX = Math.Min(_map.Width - 1, startX + (int)Math.Ceiling(_canvas.Width / CellWidth));
            int maxY = Math.Min(_map.Height - 1, startY + (int)Math.Ceiling(_canvas.Height / CellHeight));

            using Pen gridPen = new Pen(Color.FromArgb(42, 48, 54));
            using Pen hoverPen = new Pen(Color.Lime);
            using Brush passableBrush = new SolidBrush(Color.FromArgb(38, 52, 58));
            using Brush blockedBrush = new SolidBrush(Color.FromArgb(72, 38, 38));
            using Brush attributeBrush = new SolidBrush(Color.FromArgb(90, Color.Red));
            using Brush selectionBrush = new SolidBrush(Color.FromArgb(130, Color.Gold));

            for (int y = startY; y <= maxY; y++)
            {
                for (int x = startX; x <= maxX; x++)
                {
                    RectangleF rect = GetCellRectangle(x, y, startX, startY);
                    MapCell cell = _map.Cells[x, y];

                    e.Graphics.FillRectangle(cell.Flag ? passableBrush : blockedBrush, rect);

                    if (_drawAttributes && cell.Flag != _attributeSelection)
                        e.Graphics.FillRectangle(attributeBrush, rect);

                    if (_drawSelection && _selection.Contains(new Point(x, y)))
                        e.Graphics.FillRectangle(selectionBrush, rect);

                    if (_zoom >= 0.25F)
                        e.Graphics.DrawRectangle(gridPen, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }

            if (_mouseLocation.X >= startX && _mouseLocation.X <= maxX && _mouseLocation.Y >= startY && _mouseLocation.Y <= maxY)
            {
                RectangleF rect = GetCellRectangle(_mouseLocation.X, _mouseLocation.Y, startX, startY);
                e.Graphics.DrawRectangle(hoverPen, rect.X, rect.Y, rect.Width, rect.Height);

                if (_radius > 0)
                {
                    RectangleF radiusRect = GetCellRectangle(_mouseLocation.X - _radius, _mouseLocation.Y - _radius, startX, startY);
                    radiusRect.Width = CellWidth * (_radius * 2 + 1);
                    radiusRect.Height = CellHeight * (_radius * 2 + 1);
                    e.Graphics.DrawRectangle(hoverPen, radiusRect.X, radiusRect.Y, radiusRect.Width, radiusRect.Height);
                }
            }
        }

        private void DrawMessage(Graphics graphics, string text)
        {
            using Font font = new Font(FontFamily.GenericSansSerif, 11F);
            using Brush brush = new SolidBrush(Color.Gainsboro);
            graphics.DrawString(text, font, brush, new RectangleF(24, 24, Math.Max(10, _canvas.Width - 48), Math.Max(10, _canvas.Height - 48)));
        }

        private RectangleF GetCellRectangle(int x, int y, int startX, int startY)
        {
            return new RectangleF((x - startX) * CellWidth, (y - startY) * CellHeight, CellWidth, CellHeight);
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (_map.IsEmpty) return;

            bool changed = UpdateMouseLocation(e);

            if (e.Button == MouseButtons.Middle)
                changed |= FloodToggleSelection();
            else
                changed |= ApplySelection(e.Button);

            UpdateStatus();
            if (changed)
            {
                UpdateCommands();
                _canvas.Invalidate();
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_map.IsEmpty) return;

            bool changed = UpdateMouseLocation(e);

            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
                changed |= ApplySelection(e.Button);

            if (!changed) return;

            UpdateCommands();
            UpdateStatus();
            _canvas.Invalidate();
        }

        private void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                SetZoom(e.Delta > 0 ? _zoom * 2F : _zoom / 2F);
                return;
            }

            int radius = Math.Max(0, _radius - e.Delta / SystemInformation.MouseWheelScrollDelta);
            if (_radius == radius) return;

            _radius = radius;
            UpdateStatus();
            _canvas.Invalidate();
        }

        private bool UpdateMouseLocation(MouseEventArgs e)
        {
            int x = Math.Min(_map.Width - 1, Math.Max(0, (int)(e.X / CellWidth) + _hScroll.Value));
            int y = Math.Min(_map.Height - 1, Math.Max(0, (int)(e.Y / CellHeight) + _vScroll.Value));
            Point location = new Point(x, y);

            if (_mouseLocation == location) return false;

            _mouseLocation = location;
            UpdateJumpText(location);
            return true;
        }

        private bool ApplySelection(MouseButtons button)
        {
            bool changed = false;

            for (int y = _mouseLocation.Y - _radius; y <= _mouseLocation.Y + _radius; y++)
            {
                for (int x = _mouseLocation.X - _radius; x <= _mouseLocation.X + _radius; x++)
                {
                    if (!CanSelectCell(x, y)) continue;

                    Point point = new Point(x, y);
                    if (button == MouseButtons.Left)
                        changed |= _selection.Add(point);
                    else if (button == MouseButtons.Right)
                        changed |= _selection.Remove(point);
                }
            }

            return changed;
        }

        private bool FloodToggleSelection()
        {
            if (!CanSelectCell(_mouseLocation.X, _mouseLocation.Y)) return false;

            bool removing = _selection.Contains(_mouseLocation);
            bool changed = false;
            HashSet<Point> visited = new HashSet<Point> { _mouseLocation };
            Queue<Point> pending = new Queue<Point>();
            pending.Enqueue(_mouseLocation);

            while (pending.Count > 0)
            {
                Point point = pending.Dequeue();

                if (removing)
                    changed |= _selection.Remove(point);
                else
                    changed |= _selection.Add(point);

                foreach (Point next in GetNeighbours(point))
                {
                    if (!CanSelectCell(next.X, next.Y)) continue;
                    if (!visited.Add(next)) continue;
                    if (removing != _selection.Contains(next)) continue;

                    pending.Enqueue(next);
                }
            }

            return changed;
        }

        private IEnumerable<Point> GetNeighbours(Point point)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    if (x == 0 && y == 0) continue;
                    yield return new Point(point.X + x, point.Y + y);
                }
            }
        }

        private bool CanSelectCell(int x, int y)
        {
            return x >= 0 &&
                   y >= 0 &&
                   x < _map.Width &&
                   y < _map.Height &&
                   _map.Cells[x, y].Flag == _attributeSelection;
        }

        private float CellWidth => BaseCellWidth * _zoom;
        private float CellHeight => BaseCellHeight * _zoom;

        private void FocusCoordinateJump()
        {
            if (_map.IsEmpty) return;

            UpdateJumpText(_mouseLocation, force: true);
            _jumpXText.Focus();
            _jumpXText.SelectAll();
        }

        private void JumpToTextCoordinate()
        {
            if (_map.IsEmpty) return;

            if (!int.TryParse(_jumpXText.Text, out int x) || !int.TryParse(_jumpYText.Text, out int y))
            {
                MessageBox.Show(this, "请输入有效的地图坐标。", "跳转坐标", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            CenterOnCoordinate(x, y);
        }

        private void CenterOnCoordinate(int x, int y)
        {
            if (_map.IsEmpty) return;

            Point location = new Point(
                Math.Min(_map.Width - 1, Math.Max(0, x)),
                Math.Min(_map.Height - 1, Math.Max(0, y)));

            int visibleColumns = Math.Max(1, (int)Math.Ceiling(_canvas.Width / CellWidth));
            int visibleRows = Math.Max(1, (int)Math.Ceiling(_canvas.Height / CellHeight));
            SetScrollValue(_hScroll, location.X - visibleColumns / 2);
            SetScrollValue(_vScroll, location.Y - visibleRows / 2);

            _mouseLocation = location;
            UpdateJumpText(location, force: true);
            UpdateStatus();
            _canvas.Focus();
            _canvas.Invalidate();
        }

        private static void SetScrollValue(ScrollBar scrollBar, int value)
        {
            int maximum = Math.Max(scrollBar.Minimum, scrollBar.Maximum - scrollBar.LargeChange + 1);
            scrollBar.Value = Math.Min(maximum, Math.Max(scrollBar.Minimum, value));
        }

        private void CopyCurrentCoordinate()
        {
            if (_map.IsEmpty)
            {
                MessageBox.Show(this, "当前没有已加载的地图。", "复制坐标", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Clipboard.SetText($"{_mouseLocation.X},{_mouseLocation.Y}");
            _positionLabel.Text = $"坐标: {_mouseLocation.X},{_mouseLocation.Y} 已复制 半径: {_radius} 缩放: {_zoom:0.###}x";
        }

        private void UpdateJumpText(Point location, bool force = false)
        {
            if (!force && (_jumpXText.Focused || _jumpYText.Focused)) return;

            _jumpXText.Text = location.X.ToString();
            _jumpYText.Text = location.Y.ToString();
        }

        private static string ResolveMapPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            if (Path.IsPathRooted(value))
                return value;

            string fileName = value.EndsWith(".map", StringComparison.OrdinalIgnoreCase) ? value : value + ".map";
            return Path.Combine(Config.MapPath, fileName);
        }

        private sealed class MapData
        {
            public static MapData Empty { get; } = new MapData();

            public int Width { get; private set; }
            public int Height { get; private set; }
            public MapCell[,] Cells { get; private set; }
            public bool IsEmpty => Width <= 0 || Height <= 0 || Cells == null;

            public static MapData Load(string path)
            {
                MapData data = new MapData();

                using MemoryStream stream = new MemoryStream(File.ReadAllBytes(path));
                using BinaryReader reader = new BinaryReader(stream);

                stream.Seek(22, SeekOrigin.Begin);
                data.Width = reader.ReadInt16();
                data.Height = reader.ReadInt16();

                stream.Seek(28, SeekOrigin.Begin);
                data.Cells = new MapCell[data.Width, data.Height];

                for (int x = 0; x < data.Width; x++)
                    for (int y = 0; y < data.Height; y++)
                        data.Cells[x, y] = new MapCell();

                for (int x = 0; x < data.Width / 2; x++)
                {
                    for (int y = 0; y < data.Height / 2; y++)
                    {
                        data.Cells[x * 2, y * 2].BackFile = reader.ReadByte();
                        data.Cells[x * 2, y * 2].BackImage = reader.ReadUInt16();
                    }
                }

                for (int x = 0; x < data.Width; x++)
                {
                    for (int y = 0; y < data.Height; y++)
                    {
                        byte flag = reader.ReadByte();
                        data.Cells[x, y].MiddleAnimationFrame = reader.ReadByte();

                        byte value = reader.ReadByte();
                        data.Cells[x, y].FrontAnimationFrame = value == 255 ? 0 : value;

                        data.Cells[x, y].FrontFile = reader.ReadByte();
                        data.Cells[x, y].MiddleFile = reader.ReadByte();
                        data.Cells[x, y].MiddleImage = reader.ReadUInt16() + 1;
                        data.Cells[x, y].FrontImage = reader.ReadUInt16() + 1;

                        stream.Seek(3, SeekOrigin.Current);

                        data.Cells[x, y].Light = (byte)(reader.ReadByte() & 0x0F) * 2;

                        stream.Seek(1, SeekOrigin.Current);

                        data.Cells[x, y].Flag = ((flag & 0x01) != 1) || ((flag & 0x02) != 2);
                    }
                }

                return data;
            }
        }

        private sealed class MapCell
        {
            public int BackFile;
            public int BackImage;
            public int MiddleFile;
            public int MiddleImage;
            public int FrontFile;
            public int FrontImage;
            public int FrontAnimationFrame;
            public int MiddleAnimationFrame;
            public int Light;
            public bool Flag;
        }

        private sealed class DoubleBufferedCanvas : Panel
        {
            public DoubleBufferedCanvas()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw, true);
                UpdateStyles();
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
            }
        }
    }
}
