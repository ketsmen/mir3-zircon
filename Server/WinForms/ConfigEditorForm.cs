using Library;
using MirDB;
using Server.Envir;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;
using S = Library.Network.ServerPackets;

namespace Server.WinForms
{
    public sealed class ConfigEditorForm : Form
    {
        private readonly Assembly _configAssembly = Assembly.GetAssembly(typeof(Config));
        private readonly TabControl _tabs = new TabControl();
        private readonly StatusStrip _statusStrip = new StatusStrip();
        private readonly ToolStripStatusLabel _statusLabel = new ToolStripStatusLabel();
        private readonly Dictionary<string, DataGridView> _sectionGrids = new Dictionary<string, DataGridView>();
        private readonly Dictionary<string, DataGridViewRow> _propertyRows = new Dictionary<string, DataGridViewRow>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ConfigSetting> _settings = new List<ConfigSetting>();

        public ConfigEditorForm()
        {
            Text = "服务端配置";
            Width = 1120;
            Height = 760;
            StartPosition = FormStartPosition.CenterParent;

            BuildLayout();
            LoadSettings();
        }

        private void BuildLayout()
        {
            ToolStrip toolStrip = new ToolStrip();
            AddButton(toolStrip, "保存", (_, _) => SaveSettings());
            AddButton(toolStrip, "重新加载", (_, _) => ReloadSettings());
            AddButton(toolStrip, "检查版本", (_, _) => CheckVersion());
            toolStrip.Items.Add(new ToolStripSeparator());
            AddButton(toolStrip, "选择版本文件", (_, _) => SelectFileProperty("VersionPath", "选择客户端版本文件"));
            AddButton(toolStrip, "选择地图目录", (_, _) => SelectFolderProperty("MapPath", "选择地图目录"));
            AddButton(toolStrip, "选择客户端目录", (_, _) => SelectFolderProperty("ClientPath", "选择客户端目录"));
            toolStrip.Items.Add(new ToolStripSeparator());
            AddButton(toolStrip, "生成加密密钥", (_, _) => GenerateEncryptionKey());
            AddButton(toolStrip, "应用数据库加密", (_, _) => ApplyDatabaseEncryption());
            toolStrip.Items.Add(new ToolStripSeparator());
            AddButton(toolStrip, "同步本地数据库", (_, _) => SynchronizeLocalDatabase());
            AddButton(toolStrip, "同步远程数据库", async (_, _) => await SynchronizeRemoteDatabaseAsync());

            _tabs.Dock = DockStyle.Fill;

            _statusLabel.Text = GetConfigPathText();
            _statusStrip.Items.Add(_statusLabel);
            _statusStrip.Dock = DockStyle.Bottom;

            Controls.Add(_tabs);
            Controls.Add(_statusStrip);
            Controls.Add(toolStrip);
            toolStrip.Dock = DockStyle.Top;
        }

        private static ToolStripButton AddButton(ToolStrip toolStrip, string text, EventHandler click)
        {
            ToolStripButton button = new ToolStripButton(text);
            button.Click += click;
            toolStrip.Items.Add(button);
            return button;
        }

        private void LoadSettings()
        {
            _tabs.TabPages.Clear();
            _sectionGrids.Clear();
            _propertyRows.Clear();
            _settings.Clear();

            string currentSection = null;
            foreach (PropertyInfo property in typeof(Config).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                ConfigSection section = property.GetCustomAttribute<ConfigSection>();
                if (section != null)
                    currentSection = section.Section;

                if (currentSection == null) continue;
                if (!property.CanRead || !property.CanWrite) continue;
                if (property.GetCustomAttribute<ConfigPropertyIgnore>() != null) continue;

                ConfigSetting setting = new ConfigSetting(currentSection, property);
                _settings.Add(setting);

                DataGridView grid = GetSectionGrid(currentSection);
                AddSettingRow(grid, setting);
            }

            if (_tabs.TabPages.Count > 0)
                _tabs.SelectedIndex = 0;

            _statusLabel.Text = $"已加载 {_settings.Count} 个配置项。{GetConfigPathText()}";
        }

        private DataGridView GetSectionGrid(string section)
        {
            if (_sectionGrids.TryGetValue(section, out DataGridView grid))
                return grid;

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.None,
                EditMode = DataGridViewEditMode.EditOnEnter,
                MultiSelect = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.DataError += (_, e) => e.ThrowException = false;

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "配置项",
                Name = "NameColumn",
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = 230
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "类型",
                Name = "TypeColumn",
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = 120
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "值",
                Name = "ValueColumn",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });

            TabPage page = new TabPage(section);
            page.Controls.Add(grid);
            _tabs.TabPages.Add(page);
            _sectionGrids[section] = grid;
            return grid;
        }

        private void AddSettingRow(DataGridView grid, ConfigSetting setting)
        {
            int rowIndex = grid.Rows.Add();
            DataGridViewRow row = grid.Rows[rowIndex];
            row.Tag = setting;
            row.Cells[0].Value = setting.Property.Name;
            row.Cells[1].Value = GetTypeText(setting.Property.PropertyType);
            row.Cells[0].ReadOnly = true;
            row.Cells[1].ReadOnly = true;

            SetRowValue(row, setting.Property.PropertyType, setting.Property.GetValue(null));
            _propertyRows[setting.Property.Name] = row;
        }

        private static void SetRowValue(DataGridViewRow row, Type type, object value)
        {
            if (type == typeof(bool))
            {
                DataGridViewCheckBoxCell cell = new DataGridViewCheckBoxCell
                {
                    TrueValue = true,
                    FalseValue = false,
                    Value = value is bool boolValue && boolValue
                };
                row.Cells[2] = cell;
            }
            else
            {
                row.Cells[2].Value = FormatValue(type, value);
            }
        }

        private void SaveSettings()
        {
            if (!TryApplySettings(true))
                return;

            MessageBox.Show(this, "配置已保存到 Server.ini。", "配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ReloadSettings()
        {
            if (MessageBox.Show(this, "重新加载会丢弃当前未保存的修改，确定继续吗？", "重新加载", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            ConfigReader.Load(_configAssembly);
            Config.LoadVersion();
            LoadSettings();
        }

        private bool TryApplySettings(bool saveFile)
        {
            EndGridEdits();

            List<string> errors = new List<string>();
            List<ConfigValue> values = new List<ConfigValue>();

            foreach (ConfigSetting setting in _settings)
            {
                DataGridViewRow row = _propertyRows[setting.Property.Name];
                object rawValue = row.Cells[2].Value;

                try
                {
                    values.Add(new ConfigValue(setting.Property, ParseValue(setting.Property.PropertyType, rawValue)));
                }
                catch (Exception ex)
                {
                    errors.Add($"{setting.Property.Name}: {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                string message = string.Join("\r\n", errors.Take(20));
                if (errors.Count > 20)
                    message += $"\r\n... 还有 {errors.Count - 20} 条。";

                MessageBox.Show(this, message, "配置格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            foreach (ConfigValue value in values)
                value.Property.SetValue(null, value.Value);

            if (saveFile)
            {
                ConfigReader.Save(_configAssembly);
                Config.LoadVersion();

                if (SEnvir.Started)
                    SEnvir.ServerBuffChanged = true;

                _statusLabel.Text = $"配置已保存。{GetConfigPathText()}";
            }
            else
            {
                _statusLabel.Text = "配置已应用到当前管理端内存，尚未写入 Server.ini。";
            }

            return true;
        }

        private void EndGridEdits()
        {
            foreach (DataGridView grid in _sectionGrids.Values)
            {
                if (grid.IsCurrentCellDirty)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);

                grid.EndEdit();
            }
        }

        private void CheckVersion()
        {
            if (!TryApplySettings(false))
                return;

            byte[] oldHash = Config.ClientHash == null ? null : (byte[])Config.ClientHash.Clone();
            Config.LoadVersion();

            bool changed = !AreBytesEqual(oldHash, Config.ClientHash);
            if (changed && SEnvir.Started)
            {
                SEnvir.Broadcast(new S.Chat { Text = "新版本已发布，请尽快更新。", Type = MessageType.Announcement });
                MessageBox.Show(this, "客户端版本哈希已更新，并已通知在线玩家。", "检查版本", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (changed)
            {
                MessageBox.Show(this, "客户端版本哈希已更新。服务端未运行，未发送在线公告。", "检查版本", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, "客户端版本哈希没有变化。", "检查版本", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void SelectFileProperty(string propertyName, string title)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = title;
                dialog.CheckFileExists = true;

                string current = GetPropertyText(propertyName);
                if (!string.IsNullOrWhiteSpace(current))
                {
                    try
                    {
                        string fullPath = Path.GetFullPath(current);
                        dialog.InitialDirectory = Path.GetDirectoryName(fullPath);
                        dialog.FileName = Path.GetFileName(fullPath);
                    }
                    catch
                    {
                        dialog.FileName = current;
                    }
                }

                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                SetPropertyText(propertyName, dialog.FileName);
            }
        }

        private void SelectFolderProperty(string propertyName, string description)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = description;
                string current = GetPropertyText(propertyName);
                if (!string.IsNullOrWhiteSpace(current))
                {
                    try
                    {
                        string fullPath = Path.GetFullPath(current);
                        if (Directory.Exists(fullPath))
                            dialog.SelectedPath = fullPath;
                    }
                    catch
                    {
                    }
                }

                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                SetPropertyText(propertyName, dialog.SelectedPath);
            }
        }

        private string GetPropertyText(string propertyName)
        {
            if (!_propertyRows.TryGetValue(propertyName, out DataGridViewRow row))
                return string.Empty;

            return Convert.ToString(row.Cells[2].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private void SetPropertyText(string propertyName, string value)
        {
            if (!_propertyRows.TryGetValue(propertyName, out DataGridViewRow row))
                return;

            row.Cells[2].Value = value ?? string.Empty;
            FocusRow(row);
        }

        private void SetPropertyValue(string propertyName, object value)
        {
            if (!_propertyRows.TryGetValue(propertyName, out DataGridViewRow row))
                return;

            ConfigSetting setting = (ConfigSetting)row.Tag;
            SetRowValue(row, setting.Property.PropertyType, value);
            FocusRow(row);
        }

        private void FocusRow(DataGridViewRow row)
        {
            if (row.DataGridView?.Parent is TabPage page)
                _tabs.SelectedTab = page;

            row.DataGridView.CurrentCell = row.Cells[2];
        }

        private void GenerateEncryptionKey()
        {
            byte[] random = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                rng.GetBytes(random);

            SetPropertyText("EncryptionKey", Convert.ToBase64String(random));
            _statusLabel.Text = "已生成 32 字节 Base64 数据库加密密钥，尚未保存。";
        }

        private void ApplyDatabaseEncryption()
        {
            if (SEnvir.EnvirThread != null)
            {
                MessageBox.Show(this, "应用数据库加密会重写 System.db 和 Users.db。请先停止服务端再执行。", "数据库加密", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!TryApplySettings(false))
                return;

            byte[] key = null;
            if (Config.EncryptionEnabled)
            {
                if (!TryGetEncryptionKey(out key, out string reason))
                {
                    MessageBox.Show(this, reason, "数据库加密", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            string action = Config.EncryptionEnabled ? "使用当前密钥重新加密数据库" : "移除数据库加密";
            if (MessageBox.Show(this, $"{action}会重写 System.db 和 Users.db，并由 MirDB 创建备份。确定继续吗？", "数据库加密", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            try
            {
                Session session = new Session(SessionMode.Both)
                {
                    BackUpDelay = 60
                };
                session.Initialize(MirDbSessionFactory.DatabaseAssemblies);

                Encryption.SetKey(Config.EncryptionEnabled ? key : null);
                session.Save(true);

                ConfigReader.Save(_configAssembly);
                MirDbSessionFactory.ConfigureEncryption();

                _statusLabel.Text = "数据库加密设置已应用并保存。";
                MessageBox.Show(this, "数据库加密设置已应用。", "数据库加密", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "数据库加密失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SynchronizeLocalDatabase()
        {
            if (!TryApplySettings(true))
                return;

            if (string.IsNullOrWhiteSpace(Config.ClientPath))
            {
                MessageBox.Show(this, "请先设置 ClientPath。", "同步本地数据库", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                string systemPath = SaveSystemDatabase();
                string dataPath = Path.Combine(Config.ClientPath, "Data");
                Directory.CreateDirectory(dataPath);

                string targetPath = Path.Combine(dataPath, Path.GetFileName(systemPath));
                File.Copy(systemPath, targetPath, true);

                SEnvir.Log("本地数据库同步完成。");
                MessageBox.Show(this, $"本地数据库已同步到：\r\n{targetPath}", "同步本地数据库", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "同步本地数据库失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task SynchronizeRemoteDatabaseAsync()
        {
            if (!TryApplySettings(true))
                return;

            if (string.IsNullOrWhiteSpace(Config.SyncRemotePreffix))
            {
                MessageBox.Show(this, "请先设置 SyncRemotePreffix。", "同步远程数据库", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                string systemPath = SaveSystemDatabase();
                string key = Uri.EscapeDataString(Config.SyncKey ?? string.Empty);
                string url = $"{Config.SyncRemotePreffix}?Type={WebServer.SystemDBSyncCommand}&Key={key}";

                using (HttpClient client = new HttpClient())
                using (ByteArrayContent content = new ByteArrayContent(File.ReadAllBytes(systemPath)))
                {
                    HttpResponseMessage response = await client.PostAsync(url, content);
                    response.EnsureSuccessStatusCode();
                }

                MessageBox.Show(this, "远程数据库同步完成。", "同步远程数据库", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "同步远程数据库失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string SaveSystemDatabase()
        {
            if (SEnvir.Session != null)
            {
                lock (SEnvir.Session)
                {
                    SEnvir.Session.Save(true);
                    return SEnvir.Session.SystemPath;
                }
            }

            Session session = MirDbSessionFactory.Open(SessionMode.System);
            session.Save(true);
            return session.SystemPath;
        }

        private static bool TryGetEncryptionKey(out byte[] key, out string reason)
        {
            key = null;
            reason = null;

            try
            {
                key = Convert.FromBase64String(Config.EncryptionKey ?? string.Empty);
                if (key.Length != 32)
                {
                    reason = "EncryptionKey 必须是 32 字节的 Base64 字符串。";
                    return false;
                }

                return true;
            }
            catch
            {
                reason = "EncryptionKey 不是有效的 Base64 字符串。";
                return false;
            }
        }

        private static object ParseValue(Type type, object value)
        {
            if (type == typeof(bool))
            {
                if (value is bool boolValue) return boolValue;
                if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out bool result)) return result;
                throw new FormatException("请输入 True 或 False。");
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

            if (type == typeof(string)) return text;
            if (type == typeof(TimeSpan))
            {
                if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan result)) return result;
                if (TimeSpan.TryParse(text, out result)) return result;
                throw new FormatException("请输入有效的 TimeSpan，例如 00:05:00。");
            }
            if (type == typeof(DateTime))
            {
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result)) return result;
                if (DateTime.TryParse(text, out result)) return result;
                throw new FormatException("请输入有效的日期时间。");
            }
            if (type == typeof(Point)) return ParsePoint(text);
            if (type == typeof(Size)) return ParseSize(text);
            if (type == typeof(SizeF)) return ParseSizeF(text);
            if (type == typeof(Color)) return ParseColor(text);
            if (type.IsEnum) return Enum.Parse(type, text, true);

            try
            {
                return Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
            }
            catch
            {
                TypeConverter converter = TypeDescriptor.GetConverter(type);
                if (converter.CanConvertFrom(typeof(string)))
                    return converter.ConvertFromString(null, CultureInfo.InvariantCulture, text);

                throw;
            }
        }

        private static Point ParsePoint(string text)
        {
            string[] parts = SplitPair(text);
            return new Point(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        private static Size ParseSize(string text)
        {
            string[] parts = SplitPair(text);
            return new Size(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        private static SizeF ParseSizeF(string text)
        {
            string[] parts = SplitPair(text);
            return new SizeF(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        private static string[] SplitPair(string text)
        {
            string[] parts = text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                throw new FormatException("请输入两个以逗号分隔的值。");

            parts[0] = parts[0].Trim();
            parts[1] = parts[1].Trim();
            return parts;
        }

        private static Color ParseColor(string text)
        {
            if (text.StartsWith("[A:", StringComparison.OrdinalIgnoreCase) && text.EndsWith("]", StringComparison.Ordinal))
            {
                string[] parts = text.Trim('[', ']').Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                Dictionary<string, int> values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (string part in parts)
                {
                    string[] pair = part.Split(new[] { ':' }, 2);
                    if (pair.Length != 2) continue;

                    if (int.TryParse(pair[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int channel))
                        values[pair[0].Trim()] = Math.Min(byte.MaxValue, Math.Max(byte.MinValue, channel));
                }

                if (values.TryGetValue("A", out int a) &&
                    values.TryGetValue("R", out int r) &&
                    values.TryGetValue("G", out int g) &&
                    values.TryGetValue("B", out int b))
                    return Color.FromArgb(a, r, g, b);
            }

            TypeConverter converter = TypeDescriptor.GetConverter(typeof(Color));
            if (converter.CanConvertFrom(typeof(string)))
                return (Color)converter.ConvertFromString(null, CultureInfo.InvariantCulture, text);

            throw new FormatException("请输入有效的颜色值。");
        }

        private static string FormatValue(Type type, object value)
        {
            if (value == null) return string.Empty;

            if (type == typeof(DateTime))
                return ((DateTime)value).ToString(CultureInfo.InvariantCulture);
            if (type == typeof(TimeSpan))
                return ((TimeSpan)value).ToString();
            if (type == typeof(Point))
            {
                Point point = (Point)value;
                return $"{point.X}, {point.Y}";
            }
            if (type == typeof(Size))
            {
                Size size = (Size)value;
                return $"{size.Width}, {size.Height}";
            }
            if (type == typeof(SizeF))
            {
                SizeF size = (SizeF)value;
                return $"{size.Width.ToString(CultureInfo.InvariantCulture)}, {size.Height.ToString(CultureInfo.InvariantCulture)}";
            }
            if (type == typeof(Color))
            {
                Color color = (Color)value;
                return $"[A:{color.A}, R:{color.R}, G:{color.G}, B:{color.B}]";
            }
            if (type == typeof(float))
                return ((float)value).ToString(CultureInfo.InvariantCulture);
            if (type == typeof(double))
                return ((double)value).ToString(CultureInfo.InvariantCulture);
            if (type == typeof(decimal))
                return ((decimal)value).ToString(CultureInfo.InvariantCulture);

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string GetTypeText(Type type)
        {
            if (type == typeof(string)) return "文本";
            if (type == typeof(bool)) return "布尔";
            if (type == typeof(TimeSpan)) return "时间间隔";
            if (type == typeof(DateTime)) return "日期时间";
            if (type == typeof(ushort)) return "UInt16";
            if (type == typeof(uint)) return "UInt32";
            if (type == typeof(ulong)) return "UInt64";
            if (type == typeof(short)) return "Int16";
            if (type == typeof(int)) return "Int32";
            if (type == typeof(long)) return "Int64";
            if (type.IsEnum) return "枚举";
            return type.Name;
        }

        private string GetConfigPathText()
        {
            ConfigPath path = typeof(Config).GetCustomAttribute<ConfigPath>();
            if (path == null) return string.Empty;

            return $"配置文件: {ConfigReader.AdjustPath(path.Path, _configAssembly)}";
        }

        private static bool AreBytesEqual(byte[] first, byte[] second)
        {
            if (ReferenceEquals(first, second)) return true;
            if (first == null || second == null) return false;
            if (first.Length != second.Length) return false;

            for (int i = 0; i < first.Length; i++)
                if (first[i] != second[i])
                    return false;

            return true;
        }

        private sealed class ConfigSetting
        {
            public ConfigSetting(string section, PropertyInfo property)
            {
                Section = section;
                Property = property;
            }

            public string Section { get; }
            public PropertyInfo Property { get; }
        }

        private sealed class ConfigValue
        {
            public ConfigValue(PropertyInfo property, object value)
            {
                Property = property;
                Value = value;
            }

            public PropertyInfo Property { get; }
            public object Value { get; }
        }
    }
}
