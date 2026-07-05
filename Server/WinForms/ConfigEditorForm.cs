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
using System.Text;
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
        private readonly ToolStripTextBox _filterText = new ToolStripTextBox();
        private readonly ToolStripButton _modifiedOnlyButton = new ToolStripButton("只看修改");
        private readonly ToolStripStatusLabel _statusLabel = new ToolStripStatusLabel();
        private readonly Dictionary<string, DataGridView> _sectionGrids = new Dictionary<string, DataGridView>();
        private readonly Dictionary<string, DataGridViewRow> _propertyRows = new Dictionary<string, DataGridViewRow>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ConfigSetting> _settings = new List<ConfigSetting>();
        private bool _applyingFilter;
        private const string HeroSectionName = "英雄";
        private static readonly HashSet<string> HeroSettingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AllowNewHero",
            "HeroRequiredLevel",
            "HeroMaxCount",
            "HeroReviveDelay",
            "HeroSearchRange",
            "HeroPursuitRange",
            "HeroCustomBehaviourProfile",
            "HeroDefaultGrade",
            "HeroMaxGrade",
            "HeroGradeStatBonusRate",
            "HeroGradePromoteEnabled",
            "HeroGradePromoteBaseGoldCost",
            "HeroGradePromoteGoldCostMultiplier",
            "HeroGradePromoteCostGrowthRate",
            "HeroGradePromoteItemIndex",
            "HeroGradePromoteBaseItemCount",
            "HeroGradePromoteCurrencyIndex",
            "HeroGradePromoteBaseCurrencyAmount",
            "HeroGradePromoteSuccessRate",
            "HeroGradePromoteEventCostRate",
            "HeroGradePromoteEventSuccessBonus",
            "HeroGradePromoteEventEnd",
            "HeroGradePromoteEventSaveOnExpire",
            "HeroGradePromoteEventPresets",
            "HeroGradePromoteFailureBonusRate",
            "HeroGradePromoteFailureBonusMax",
            "HeroGradePromoteProtectionItemIndex",
            "HeroGradePromoteProtectionItemCount",
            "HeroGradePromoteProtectionConsumeOnFailureOnly",
            "HeroExperienceRate"
        };

        public ConfigEditorForm()
        {
            Text = "服务端配置";
            Width = 1120;
            Height = 760;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            KeyDown += ConfigEditorForm_KeyDown;

            BuildLayout();
            LoadSettings();
        }

        public void FocusDatabaseEncryptionSettings()
        {
            FocusSetting("EncryptionKey", "数据库加密设置：可生成密钥、切换 EncryptionEnabled，并点击“应用数据库加密”。");
        }

        public void FocusDatabaseSynchronizationSettings()
        {
            FocusSetting("SyncRemotePreffix", "数据库同步设置：可配置 ClientPath、SyncRemotePreffix、SyncKey，并执行本地或远程同步。");
        }

        public void FocusHeroSettings()
        {
            FocusSetting("AllowNewHero", "英雄配置：可设置创建开关、创建等级、数量上限、复活延迟、搜索/追击距离、自定义策略、品质成长/培养和经验倍率。");
        }

        public bool FocusSetting(string propertyName, string statusText = null)
        {
            if (!_propertyRows.TryGetValue(propertyName, out DataGridViewRow row))
                return false;

            FocusRow(row);
            if (!string.IsNullOrWhiteSpace(statusText))
                _statusLabel.Text = statusText;

            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!EndGridEdits())
            {
                e.Cancel = true;
                return;
            }

            int modified = CountModifiedSettings();
            if (modified > 0)
            {
                DialogResult result = MessageBox.Show(
                    this,
                    $"当前有 {modified:#,##0} 个配置项尚未保存。\r\n\r\n是：保存后关闭。\r\n否：放弃修改并关闭。\r\n取消：继续编辑。",
                    "未保存配置",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (result == DialogResult.Yes)
                {
                    SaveSettings();
                    if (CountModifiedSettings() > 0)
                    {
                        e.Cancel = true;
                        return;
                    }
                }
            }

            base.OnFormClosing(e);
        }

        private void BuildLayout()
        {
            ToolStrip toolStrip = new ToolStrip();
            AddButton(toolStrip, "保存", (_, _) => SaveSettings());
            AddButton(toolStrip, "重新加载", (_, _) => ReloadSettings());
            AddButton(toolStrip, "检查版本", (_, _) => CheckVersion());
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(new ToolStripLabel("过滤"));
            _filterText.AutoSize = false;
            _filterText.Width = 220;
            _filterText.ToolTipText = "按配置项、分组、类型或当前值过滤。";
            _filterText.TextChanged += (_, _) => ApplyFilter();
            _filterText.KeyDown += FilterText_KeyDown;
            toolStrip.Items.Add(_filterText);
            AddButton(toolStrip, "清空过滤", (_, _) => _filterText.Text = string.Empty);
            _modifiedOnlyButton.CheckOnClick = true;
            _modifiedOnlyButton.ToolTipText = "只显示当前窗口中已修改但尚未保存的配置项。";
            _modifiedOnlyButton.CheckedChanged += (_, _) => ApplyFilter();
            toolStrip.Items.Add(_modifiedOnlyButton);
            AddButton(toolStrip, "定位修改", (_, _) => FocusNextModifiedSetting());
            AddButton(toolStrip, "还原当前项", (_, _) => ResetCurrentSetting());
            AddButton(toolStrip, "还原全部修改", (_, _) => ResetAllModifiedSettings());
            AddButton(toolStrip, "复制当前项", (_, _) => CopyCurrentSetting());
            AddButton(toolStrip, "英雄配置", (_, _) => FocusHeroSettings());
            AddButton(toolStrip, "品质试算", (_, _) => ShowHeroGradePromoteCostPreview());
            AddButton(toolStrip, "套用品质活动模板", (_, _) => ShowHeroGradePromoteEventPresetApplier());
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

        private static string GetDisplaySection(PropertyInfo property, string currentSection)
        {
            return HeroSettingNames.Contains(property.Name) ? HeroSectionName : currentSection;
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

                string displaySection = GetDisplaySection(property, currentSection);
                ConfigSetting setting = new ConfigSetting(displaySection, property);
                _settings.Add(setting);

                DataGridView grid = GetSectionGrid(displaySection);
                AddSettingRow(grid, setting);
            }

            if (_tabs.TabPages.Count > 0)
                _tabs.SelectedIndex = 0;

            _statusLabel.Text = $"已加载 {_settings.Count} 个配置项。{GetConfigPathText()}";
            ApplyFilter();
        }

        private void ConfigEditorForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                SaveSettings();
                SuppressKey(e);
                return;
            }

            if (e.Control && e.KeyCode == Keys.F)
            {
                _filterText.Focus();
                _filterText.TextBox.SelectAll();
                SuppressKey(e);
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                ReloadSettings();
                SuppressKey(e);
                return;
            }

            if (e.Control && e.Shift && e.KeyCode == Keys.M)
            {
                FocusNextModifiedSetting();
                SuppressKey(e);
                return;
            }

            if (e.Control && e.Shift && e.KeyCode == Keys.R)
            {
                ResetAllModifiedSettings();
                SuppressKey(e);
                return;
            }

            if (e.Control && e.Shift && e.KeyCode == Keys.C)
            {
                CopyCurrentSetting();
                SuppressKey(e);
                return;
            }

            if (e.Control && e.KeyCode == Keys.M)
            {
                _modifiedOnlyButton.Checked = !_modifiedOnlyButton.Checked;
                SuppressKey(e);
            }
        }

        private void FilterText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                _filterText.Text = string.Empty;
                SuppressKey(e);
                return;
            }

            if (e.KeyCode != Keys.Enter) return;

            FocusFirstVisibleSetting();
            SuppressKey(e);
        }

        private static void SuppressKey(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
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
            grid.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (grid.IsCurrentCellDirty)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += (_, _) =>
            {
                if (!_applyingFilter)
                    ApplyFilter();
            };

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
            if (!EndGridEdits())
                return false;

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

            ValidateConfigValues(values, errors);

            if (errors.Count > 0)
            {
                string message = string.Join("\r\n", errors.Take(20));
                if (errors.Count > 20)
                    message += $"\r\n... 还有 {errors.Count - 20} 条。";

                MessageBox.Show(this, message, "配置格式错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            List<ConfigValue> originalValues = values
                .Select(value => new ConfigValue(value.Property, value.Property.GetValue(null)))
                .ToList();

            foreach (ConfigValue value in values)
                value.Property.SetValue(null, value.Value);

            if (saveFile)
            {
                try
                {
                    ConfigReader.Save(_configAssembly);
                    Config.LoadVersion();
                    SEnvir.AuditHeroGradePromoteConfig("ConfigEditorSave");
                }
                catch (Exception ex)
                {
                    RestoreConfigValues(originalValues);
                    _statusLabel.Text = $"配置未保存，当前内存配置已恢复为保存前状态。{GetConfigPathText()}";
                    MessageBox.Show(this, ex.ToString(), "保存配置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                if (SEnvir.Started)
                    SEnvir.ServerBuffChanged = true;

                BroadcastHeroGradePromoteEventStateIfChanged(originalValues, values);
                UpdateOriginalValues();
                _statusLabel.Text = $"配置已保存。{GetConfigPathText()}";
            }
            else
            {
                BroadcastHeroGradePromoteEventStateIfChanged(originalValues, values);
                _statusLabel.Text = "配置已应用到当前管理端内存，尚未写入 Server.ini。";
            }

            ApplyFilter();
            return true;
        }

        private static void RestoreConfigValues(IEnumerable<ConfigValue> values)
        {
            if (values == null) return;

            foreach (ConfigValue value in values)
                value.Property.SetValue(null, value.Value);
        }

        private static void BroadcastHeroGradePromoteEventStateIfChanged(List<ConfigValue> originalValues, List<ConfigValue> currentValues)
        {
            if (!SEnvir.Started) return;

            if (HasConfigValueChanged(originalValues, currentValues, "HeroGradePromoteEventCostRate") ||
                HasConfigValueChanged(originalValues, currentValues, "HeroGradePromoteEventSuccessBonus") ||
                HasConfigValueChanged(originalValues, currentValues, "HeroGradePromoteEventEnd"))
                SEnvir.BroadcastHeroGradePromoteEventState();
        }

        private static bool HasConfigValueChanged(List<ConfigValue> originalValues, List<ConfigValue> currentValues, string propertyName)
        {
            if (!TryGetConfigValue(originalValues, propertyName, out object originalValue)) return false;
            if (!TryGetConfigValue(currentValues, propertyName, out object currentValue)) return false;

            return !object.Equals(originalValue, currentValue);
        }

        private static void ValidateConfigValues(List<ConfigValue> values, List<string> errors)
        {
            if (TryGetConfigValue(values, "HeroRequiredLevel", out int heroRequiredLevel))
            {
                if (heroRequiredLevel < 0)
                    errors.Add("HeroRequiredLevel: 英雄创建所需等级不能小于 0。");

                if (TryGetConfigValue(values, "MaxLevel", out int maxLevel) && maxLevel >= 0 && heroRequiredLevel > maxLevel)
                    errors.Add($"HeroRequiredLevel: 不能大于 MaxLevel ({maxLevel})，否则角色无法达到创建英雄条件。");
            }

            if (TryGetConfigValue(values, "HeroMaxCount", out int heroMaxCount) &&
                (heroMaxCount < 0 || heroMaxCount > Globals.HeroMaximumCount))
                errors.Add($"HeroMaxCount: 英雄数量上限必须在 0 到 {Globals.HeroMaximumCount} 之间。");

            if (TryGetConfigValue(values, "HeroReviveDelay", out TimeSpan heroReviveDelay) && heroReviveDelay < TimeSpan.Zero)
                errors.Add("HeroReviveDelay: 英雄复活延迟不能为负数。");

            if (TryGetConfigValue(values, "HeroSearchRange", out int heroSearchRange))
            {
                if (heroSearchRange < 1)
                    errors.Add("HeroSearchRange: 英雄搜索距离不能小于 1。");

                if (TryGetConfigValue(values, "MaxViewRange", out int maxViewRangeForSearch) && maxViewRangeForSearch > 0 && heroSearchRange > maxViewRangeForSearch)
                    errors.Add($"HeroSearchRange: 不能大于 MaxViewRange ({maxViewRangeForSearch})。");
            }

            if (TryGetConfigValue(values, "HeroPursuitRange", out int heroPursuitRange))
            {
                if (heroPursuitRange < 1)
                    errors.Add("HeroPursuitRange: 英雄追击距离不能小于 1。");

                if (TryGetConfigValue(values, "MaxViewRange", out int maxViewRangeForPursuit) && maxViewRangeForPursuit > 0 && heroPursuitRange > maxViewRangeForPursuit)
                    errors.Add($"HeroPursuitRange: 不能大于 MaxViewRange ({maxViewRangeForPursuit})。");

                if (TryGetConfigValue(values, "HeroSearchRange", out int configuredHeroSearchRange) && heroPursuitRange < configuredHeroSearchRange)
                    errors.Add("HeroPursuitRange: 英雄追击距离不能小于 HeroSearchRange。");
            }

            if (TryGetConfigValue(values, "HeroExperienceRate", out int heroExperienceRate) && heroExperienceRate < -100)
                errors.Add("HeroExperienceRate: 英雄经验倍率不能小于 -100。");

            if (TryGetConfigValue(values, "HeroMaxGrade", out int heroMaxGrade) &&
                (heroMaxGrade < 0 || heroMaxGrade > 4))
                errors.Add("HeroMaxGrade: 英雄最高品质必须在 0 到 4 之间。");

            if (TryGetConfigValue(values, "HeroDefaultGrade", out int heroDefaultGrade))
            {
                int configuredHeroMaxGrade = TryGetConfigValue(values, "HeroMaxGrade", out int validatedHeroMaxGrade) ? validatedHeroMaxGrade : 4;

                if (heroDefaultGrade < 0)
                    errors.Add("HeroDefaultGrade: 英雄默认品质不能小于 0。");

                if (heroDefaultGrade > configuredHeroMaxGrade)
                    errors.Add($"HeroDefaultGrade: 默认品质不能大于 HeroMaxGrade ({configuredHeroMaxGrade})。");
            }

            if (TryGetConfigValue(values, "HeroGradeStatBonusRate", out int heroGradeStatBonusRate) &&
                (heroGradeStatBonusRate < 0 || heroGradeStatBonusRate > 20))
                errors.Add("HeroGradeStatBonusRate: 每级品质属性加成建议在 0 到 20 之间。");

            if (TryGetConfigValue(values, "HeroGradePromoteBaseGoldCost", out long heroGradePromoteBaseGoldCost) &&
                heroGradePromoteBaseGoldCost < 0)
                errors.Add("HeroGradePromoteBaseGoldCost: 英雄品质提升基础金币消耗不能小于 0。");

            if (TryGetConfigValue(values, "HeroGradePromoteGoldCostMultiplier", out int heroGradePromoteGoldCostMultiplier) &&
                (heroGradePromoteGoldCostMultiplier < 0 || heroGradePromoteGoldCostMultiplier > 1000))
                errors.Add("HeroGradePromoteGoldCostMultiplier: 英雄品质提升金币倍率建议在 0 到 1000 之间。");

            if (TryGetConfigValue(values, "HeroGradePromoteCostGrowthRate", out int heroGradePromoteCostGrowthRate) &&
                (heroGradePromoteCostGrowthRate < 0 || heroGradePromoteCostGrowthRate > 1000))
                errors.Add("HeroGradePromoteCostGrowthRate: 英雄品质提升消耗成长率建议在 0 到 1000 之间。");

            if (TryGetConfigValue(values, "HeroGradePromoteItemIndex", out int heroGradePromoteItemIndex) &&
                heroGradePromoteItemIndex < 0)
                errors.Add("HeroGradePromoteItemIndex: 英雄品质提升材料物品索引不能小于 0。");

            if (TryGetConfigValue(values, "HeroGradePromoteBaseItemCount", out long heroGradePromoteBaseItemCount) &&
                heroGradePromoteBaseItemCount < 0)
                errors.Add("HeroGradePromoteBaseItemCount: 英雄品质提升基础材料数量不能小于 0。");

            if (TryGetConfigValue(values, "HeroGradePromoteCurrencyIndex", out int heroGradePromoteCurrencyIndex) &&
                heroGradePromoteCurrencyIndex < 0)
                errors.Add("HeroGradePromoteCurrencyIndex: 英雄品质提升货币索引不能小于 0。");

            if (TryGetConfigValue(values, "HeroGradePromoteBaseCurrencyAmount", out long heroGradePromoteBaseCurrencyAmount) &&
                heroGradePromoteBaseCurrencyAmount < 0)
                errors.Add("HeroGradePromoteBaseCurrencyAmount: 英雄品质提升基础货币数量不能小于 0。");

            if (TryGetConfigValue(values, "HeroGradePromoteSuccessRate", out int heroGradePromoteSuccessRate) &&
                (heroGradePromoteSuccessRate < 0 || heroGradePromoteSuccessRate > 100))
                errors.Add("HeroGradePromoteSuccessRate: 英雄品质培养成功率必须在 0 到 100 之间。");

            if (TryGetConfigValue(values, "HeroGradePromoteEventCostRate", out int heroGradePromoteEventCostRate) &&
                (heroGradePromoteEventCostRate < 0 || heroGradePromoteEventCostRate > 1000))
                errors.Add("HeroGradePromoteEventCostRate: 英雄品质培养活动消耗倍率建议在 0 到 1000 之间。");

            if (TryGetConfigValue(values, "HeroGradePromoteEventSuccessBonus", out int heroGradePromoteEventSuccessBonus) &&
                (heroGradePromoteEventSuccessBonus < 0 || heroGradePromoteEventSuccessBonus > 100))
                errors.Add("HeroGradePromoteEventSuccessBonus: 英雄品质培养活动成功率加成必须在 0 到 100 之间。");

            if (TryGetConfigValue(values, "HeroGradePromoteEventPresets", out string heroGradePromoteEventPresets) &&
                !HeroGradePromoteEventPresetParser.TryParse(heroGradePromoteEventPresets, out _, out List<string> presetErrors))
                errors.AddRange(presetErrors.Select(x => $"HeroGradePromoteEventPresets: {x}"));

            if (TryGetConfigValue(values, "HeroGradePromoteFailureBonusRate", out int heroGradePromoteFailureBonusRate) &&
                (heroGradePromoteFailureBonusRate < 0 || heroGradePromoteFailureBonusRate > 100))
                errors.Add("HeroGradePromoteFailureBonusRate: 英雄品质培养每次失败补偿成功率必须在 0 到 100 之间。");

            if (TryGetConfigValue(values, "HeroGradePromoteFailureBonusMax", out int heroGradePromoteFailureBonusMax) &&
                (heroGradePromoteFailureBonusMax < 0 || heroGradePromoteFailureBonusMax > 100))
                errors.Add("HeroGradePromoteFailureBonusMax: 英雄品质培养失败补偿总上限必须在 0 到 100 之间。");

            if (TryGetConfigValue(values, "HeroGradePromoteProtectionItemIndex", out int heroGradePromoteProtectionItemIndex) &&
                heroGradePromoteProtectionItemIndex < 0)
                errors.Add("HeroGradePromoteProtectionItemIndex: 英雄品质保护材料物品索引不能小于 0。");

            if (TryGetConfigValue(values, "HeroGradePromoteProtectionItemCount", out long heroGradePromoteProtectionItemCount) &&
                heroGradePromoteProtectionItemCount < 0)
                errors.Add("HeroGradePromoteProtectionItemCount: 英雄品质保护材料数量不能小于 0。");
        }

        private static bool TryGetConfigValue<T>(List<ConfigValue> values, string propertyName, out T value)
        {
            foreach (ConfigValue configValue in values)
            {
                if (!configValue.Property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (configValue.Value is T typedValue)
                {
                    value = typedValue;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private bool EndGridEdits()
        {
            try
            {
                foreach (DataGridView grid in _sectionGrids.Values)
                {
                    if (grid.IsCurrentCellDirty)
                        grid.CommitEdit(DataGridViewDataErrorContexts.Commit);

                    if (!grid.EndEdit())
                        return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "提交编辑失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
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

        private void ShowHeroGradePromoteCostPreview()
        {
            HeroGradePromoteCostPreviewForm form = new HeroGradePromoteCostPreviewForm(BuildHeroGradePromoteCostPreviewData);
            form.Show(this);
        }

        private void ShowHeroGradePromoteEventPresetApplier()
        {
            HeroGradePromoteEventPresetApplyForm form = new HeroGradePromoteEventPresetApplyForm(
                BuildHeroGradePromoteEventPresetApplyData,
                ApplyHeroGradePromoteEventPreset);
            form.Show(this);
        }

        private HeroGradePromoteEventPresetApplyData BuildHeroGradePromoteEventPresetApplyData()
        {
            HeroGradePromoteEventPresetApplyData data = new HeroGradePromoteEventPresetApplyData
            {
                ReferenceTime = GetHeroGradePromoteEventNow()
            };

            if (!EndGridEdits())
            {
                data.Errors.Add("当前表格编辑尚未提交，请先结束编辑后重试。");
                return data;
            }

            List<string> currentErrors = new List<string>();
            TryReadSettingValue("HeroGradePromoteEventCostRate", currentErrors, out int eventCostRate);
            TryReadSettingValue("HeroGradePromoteEventSuccessBonus", currentErrors, out int eventSuccessBonus);
            TryReadSettingValue("HeroGradePromoteEventEnd", currentErrors, out DateTime eventEnd);
            TryReadSettingValue("HeroGradePromoteEventSaveOnExpire", currentErrors, out bool eventSaveOnExpire);
            TryReadSettingValue("HeroGradePromoteEventPresets", data.Errors, out string eventPresets);

            if (!HeroGradePromoteEventPresetParser.TryParse(eventPresets, out List<HeroGradePromoteEventPreset> parsedPresets, out List<string> presetErrors))
                data.Errors.AddRange(presetErrors.Select(x => $"HeroGradePromoteEventPresets: {x}"));

            if (eventCostRate < 0 || eventCostRate > 1000)
                currentErrors.Add("HeroGradePromoteEventCostRate: 英雄品质培养活动消耗倍率建议在 0 到 1000 之间。");

            if (eventSuccessBonus < 0 || eventSuccessBonus > 100)
                currentErrors.Add("HeroGradePromoteEventSuccessBonus: 英雄品质培养活动成功率加成必须在 0 到 100 之间。");

            data.CurrentCostRate = HeroGradePromoteCostCalculator.ClampCostRate(eventCostRate);
            data.CurrentSuccessBonus = HeroGradePromoteCostCalculator.ClampSuccessBonus(eventSuccessBonus);
            data.CurrentEventEnd = eventEnd;
            data.CurrentSaveOnExpire = eventSaveOnExpire;
            data.DefaultSaveOnExpire = eventEnd != DateTime.MinValue && eventSaveOnExpire;
            data.CurrentErrors.AddRange(currentErrors);
            data.Presets.AddRange(parsedPresets);
            return data;
        }

        private void ApplyHeroGradePromoteEventPreset(HeroGradePromoteEventPreset preset, bool saveOnExpire)
        {
            if (preset == null) return;

            DateTime eventEnd = preset.DurationMinutes > 0
                ? GetHeroGradePromoteEventNow().AddMinutes(preset.DurationMinutes)
                : DateTime.MinValue;
            bool effectiveSaveOnExpire = eventEnd != DateTime.MinValue && saveOnExpire;

            SetPropertyValue("HeroGradePromoteEventCostRate", preset.CostRate);
            SetPropertyValue("HeroGradePromoteEventSuccessBonus", preset.SuccessBonus);
            SetPropertyValue("HeroGradePromoteEventEnd", eventEnd);
            SetPropertyValue("HeroGradePromoteEventSaveOnExpire", effectiveSaveOnExpire);
            FocusSetting("HeroGradePromoteEventCostRate");
            ApplyFilter();

            _statusLabel.Text = $"已套用品质活动模板 {preset.Name}，当前配置尚未保存。{GetConfigPathText()}";
        }

        private HeroGradePromoteCostPreviewData BuildHeroGradePromoteCostPreviewData()
        {
            HeroGradePromoteCostPreviewData data = new HeroGradePromoteCostPreviewData();

            if (!EndGridEdits())
            {
                data.Errors.Add("当前表格编辑尚未提交，请先结束编辑后重试。");
                return data;
            }

            TryReadSettingValue("HeroGradePromoteEnabled", data.Errors, out bool enabled);
            TryReadSettingValue("HeroMaxGrade", data.Errors, out int heroMaxGrade);
            TryReadSettingValue("HeroGradePromoteBaseGoldCost", data.Errors, out long baseGoldCost);
            TryReadSettingValue("HeroGradePromoteGoldCostMultiplier", data.Errors, out int goldCostMultiplier);
            TryReadSettingValue("HeroGradePromoteCostGrowthRate", data.Errors, out int costGrowthRate);
            TryReadSettingValue("HeroGradePromoteItemIndex", data.Errors, out int itemIndex);
            TryReadSettingValue("HeroGradePromoteBaseItemCount", data.Errors, out long baseItemCount);
            TryReadSettingValue("HeroGradePromoteCurrencyIndex", data.Errors, out int currencyIndex);
            TryReadSettingValue("HeroGradePromoteBaseCurrencyAmount", data.Errors, out long baseCurrencyAmount);
            TryReadSettingValue("HeroGradePromoteEventCostRate", data.Errors, out int eventCostRate);
            TryReadSettingValue("HeroGradePromoteEventSuccessBonus", data.Errors, out int eventSuccessBonus);
            TryReadSettingValue("HeroGradePromoteEventEnd", data.Errors, out DateTime eventEnd);
            TryReadSettingValue("HeroGradePromoteEventSaveOnExpire", data.Errors, out bool eventSaveOnExpire);
            TryReadSettingValue("HeroGradePromoteEventPresets", data.Errors, out string eventPresets);
            TryReadSettingValue("HeroGradePromoteFailureBonusRate", data.Errors, out int failureBonusRate);
            TryReadSettingValue("HeroGradePromoteFailureBonusMax", data.Errors, out int failureBonusMax);
            TryReadSettingValue("HeroGradePromoteProtectionItemIndex", data.Errors, out int protectionItemIndex);
            TryReadSettingValue("HeroGradePromoteProtectionItemCount", data.Errors, out long protectionItemCount);
            TryReadSettingValue("HeroGradePromoteProtectionConsumeOnFailureOnly", data.Errors, out bool protectionConsumeOnFailureOnly);

            ValidateHeroGradePromotePreviewInputs(
                heroMaxGrade,
                baseGoldCost,
                goldCostMultiplier,
                costGrowthRate,
                itemIndex,
                baseItemCount,
                currencyIndex,
                baseCurrencyAmount,
                eventCostRate,
                eventSuccessBonus,
                failureBonusRate,
                failureBonusMax,
                protectionItemIndex,
                protectionItemCount,
                data.Errors);

            if (!HeroGradePromoteEventPresetParser.TryParse(eventPresets, out List<HeroGradePromoteEventPreset> parsedPresets, out List<string> presetErrors))
                data.Errors.AddRange(presetErrors.Select(x => $"HeroGradePromoteEventPresets: {x}"));

            if (data.Errors.Count > 0)
                return data;

            int maxGrade = Math.Max(0, Math.Min(4, heroMaxGrade));
            int growthRate = HeroGradePromoteCostCalculator.ClampCostGrowthRate(costGrowthRate);
            int costRate = HeroGradePromoteCostCalculator.ClampCostRate(eventCostRate);
            int successBonus = HeroGradePromoteCostCalculator.ClampSuccessBonus(eventSuccessBonus);
            int failureBonus = HeroGradePromoteCostCalculator.ClampSuccessBonus(failureBonusRate);
            int failureBonusLimit = HeroGradePromoteCostCalculator.ClampSuccessBonus(failureBonusMax);
            string eventEndText = FormatHeroGradePromoteEventEnd(eventEnd, eventSaveOnExpire);
            string materialName = GetHeroPromoteItemText(itemIndex);
            string currencyName = GetHeroPromoteCurrencyText(currencyIndex);
            string protectionName = GetHeroPromoteItemText(protectionItemIndex);
            string protectionMode = protectionConsumeOnFailureOnly ? "失败时才消耗" : "启用保护即消耗";

            data.MaxGrade = maxGrade;
            data.MaterialName = materialName;
            data.CurrencyName = currencyName;
            data.Summary = $"{(enabled ? "开启" : "关闭，仅试算")}；最高品质 {HeroGradeName((byte)maxGrade)}；金币倍率 {goldCostMultiplier}%；消耗成长率 {growthRate}%；活动消耗倍率 {costRate}%；活动成功 +{successBonus}%；活动结束 {eventEndText}；活动模板 {parsedPresets.Count}个；失败补偿每次 +{failureBonus}%(上限 +{failureBonusLimit}%)";
            data.MaterialSummary = itemIndex <= 0 || baseItemCount <= 0 ? "材料: 无" : $"材料: {materialName}，基础数量 {baseItemCount:#,##0}";
            data.CurrencySummary = currencyIndex <= 0 || baseCurrencyAmount <= 0 ? "货币: 无" : $"货币: {currencyName}，基础金额 {baseCurrencyAmount:#,##0}";
            data.ProtectionSummary = protectionItemIndex <= 0 || protectionItemCount <= 0 ? "保护: 未配置" : $"保护: {protectionName} x{protectionItemCount:#,##0}，{protectionMode}";

            long totalGoldCost = 0;
            long totalMaterialCount = 0;
            long totalCurrencyAmount = 0;

            for (int current = 0; current < maxGrade; current++)
            {
                byte currentGrade = (byte)current;
                byte nextGrade = (byte)(current + 1);
                decimal growthMultiplier = HeroGradePromoteCostCalculator.GetCostGrowthMultiplier(currentGrade, growthRate);
                long goldCost = HeroGradePromoteCostCalculator.GetGoldCost(currentGrade, baseGoldCost, goldCostMultiplier, growthRate, costRate);
                long materialCount = itemIndex <= 0 ? 0 : HeroGradePromoteCostCalculator.GetScaledCost(currentGrade, baseItemCount, growthRate, costRate);
                long currencyAmount = currencyIndex <= 0 ? 0 : HeroGradePromoteCostCalculator.GetScaledCost(currentGrade, baseCurrencyAmount, growthRate, costRate);
                totalGoldCost = AddCapped(totalGoldCost, goldCost);
                totalMaterialCount = AddCapped(totalMaterialCount, materialCount);
                totalCurrencyAmount = AddCapped(totalCurrencyAmount, currencyAmount);

                data.Rows.Add(new HeroGradePromoteCostPreviewRow
                {
                    Grade = $"{HeroGradeName(currentGrade)} -> {HeroGradeName(nextGrade)}",
                    GrowthMultiplier = $"x{growthMultiplier:0.##}",
                    Gold = FormatCost(goldCost),
                    TotalGold = FormatCost(totalGoldCost),
                    GoldCost = goldCost,
                    Material = FormatNamedCost(materialName, materialCount),
                    TotalMaterial = FormatNamedCost(materialName, totalMaterialCount),
                    MaterialCount = materialCount,
                    Currency = FormatNamedCost(currencyName, currencyAmount),
                    TotalCurrency = FormatNamedCost(currencyName, totalCurrencyAmount),
                    CurrencyAmount = currencyAmount,
                    CurrentGrade = currentGrade,
                    NextGrade = nextGrade,
                    Protection = protectionItemIndex <= 0 || protectionItemCount <= 0 ? "未配置" : $"{protectionName} x{protectionItemCount:#,##0} ({protectionMode})"
                });
            }

            if (maxGrade <= 0)
            {
                data.Rows.Add(new HeroGradePromoteCostPreviewRow
                {
                    Grade = "无可培养档位",
                    GrowthMultiplier = "x1",
                    Gold = "无",
                    TotalGold = "无",
                    Material = "无",
                    TotalMaterial = "无",
                    Currency = "无",
                    TotalCurrency = "无",
                    CurrentGrade = 0,
                    NextGrade = 0,
                    Protection = data.ProtectionSummary.Replace("保护: ", string.Empty)
                });
            }

            return data;
        }

        private static void ValidateHeroGradePromotePreviewInputs(
            int heroMaxGrade,
            long baseGoldCost,
            int goldCostMultiplier,
            int costGrowthRate,
            int itemIndex,
            long baseItemCount,
            int currencyIndex,
            long baseCurrencyAmount,
            int eventCostRate,
            int eventSuccessBonus,
            int failureBonusRate,
            int failureBonusMax,
            int protectionItemIndex,
            long protectionItemCount,
            List<string> errors)
        {
            if (heroMaxGrade < 0 || heroMaxGrade > 4)
                errors.Add("HeroMaxGrade: 英雄最高品质必须在 0 到 4 之间。");
            if (baseGoldCost < 0)
                errors.Add("HeroGradePromoteBaseGoldCost: 英雄品质提升基础金币消耗不能小于 0。");
            if (goldCostMultiplier < 0 || goldCostMultiplier > 1000)
                errors.Add("HeroGradePromoteGoldCostMultiplier: 英雄品质提升金币倍率建议在 0 到 1000 之间。");
            if (costGrowthRate < 0 || costGrowthRate > 1000)
                errors.Add("HeroGradePromoteCostGrowthRate: 英雄品质提升消耗成长率建议在 0 到 1000 之间。");
            if (itemIndex < 0)
                errors.Add("HeroGradePromoteItemIndex: 英雄品质提升材料物品索引不能小于 0。");
            if (baseItemCount < 0)
                errors.Add("HeroGradePromoteBaseItemCount: 英雄品质提升基础材料数量不能小于 0。");
            if (currencyIndex < 0)
                errors.Add("HeroGradePromoteCurrencyIndex: 英雄品质提升货币索引不能小于 0。");
            if (baseCurrencyAmount < 0)
                errors.Add("HeroGradePromoteBaseCurrencyAmount: 英雄品质提升基础货币数量不能小于 0。");
            if (eventCostRate < 0 || eventCostRate > 1000)
                errors.Add("HeroGradePromoteEventCostRate: 英雄品质培养活动消耗倍率建议在 0 到 1000 之间。");
            if (eventSuccessBonus < 0 || eventSuccessBonus > 100)
                errors.Add("HeroGradePromoteEventSuccessBonus: 英雄品质培养活动成功率加成必须在 0 到 100 之间。");
            if (failureBonusRate < 0 || failureBonusRate > 100)
                errors.Add("HeroGradePromoteFailureBonusRate: 英雄品质培养每次失败补偿成功率必须在 0 到 100 之间。");
            if (failureBonusMax < 0 || failureBonusMax > 100)
                errors.Add("HeroGradePromoteFailureBonusMax: 英雄品质培养失败补偿总上限必须在 0 到 100 之间。");
            if (protectionItemIndex < 0)
                errors.Add("HeroGradePromoteProtectionItemIndex: 英雄品质保护材料物品索引不能小于 0。");
            if (protectionItemCount < 0)
                errors.Add("HeroGradePromoteProtectionItemCount: 英雄品质保护材料数量不能小于 0。");
        }

        private static string FormatCost(long amount)
        {
            return amount <= 0 ? "无" : amount.ToString("#,##0", CultureInfo.InvariantCulture);
        }

        private static long AddCapped(long current, long amount)
        {
            if (amount <= 0) return current;
            if (current >= long.MaxValue - amount) return long.MaxValue;

            return current + amount;
        }

        private static string FormatNamedCost(string name, long amount)
        {
            return amount <= 0 ? "无" : $"{name} x{amount:#,##0}";
        }

        private static string FormatHeroGradePromoteEventEnd(DateTime eventEnd, bool saveOnExpire)
        {
            if (eventEnd == DateTime.MinValue)
                return "不自动到期";

            TimeSpan remaining = eventEnd - GetHeroGradePromoteEventNow();
            string state = remaining <= TimeSpan.Zero ? "已过期，服务端会自动恢复" : $"剩余 {FormatDuration(remaining)}";
            string saveText = saveOnExpire ? "，到期写回配置" : "，到期只恢复内存";

            return $"{eventEnd:yyyy-MM-dd HH:mm:ss} UTC({state}{saveText})";
        }

        private static DateTime GetHeroGradePromoteEventNow()
        {
            return SEnvir.Now == default ? DateTime.UtcNow : SEnvir.Now;
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
                return $"{Math.Ceiling(duration.TotalDays)}天";

            if (duration.TotalHours >= 1)
                return $"{Math.Ceiling(duration.TotalHours)}小时";

            if (duration.TotalMinutes >= 1)
                return $"{Math.Ceiling(duration.TotalMinutes)}分钟";

            return $"{Math.Ceiling(Math.Max(0, duration.TotalSeconds))}秒";
        }

        private static string GetHeroPromoteItemText(int itemIndex)
        {
            if (itemIndex <= 0) return "未配置";

            try
            {
                var item = SEnvir.ItemInfoList?.Binding?.FirstOrDefault(x => x.Index == itemIndex);
                if (item != null)
                    return $"{item.ItemName}(#{itemIndex})";
            }
            catch
            {
            }

            return $"物品索引 {itemIndex}";
        }

        private static string GetHeroPromoteCurrencyText(int currencyIndex)
        {
            if (currencyIndex <= 0) return "未配置";

            try
            {
                var currency = SEnvir.CurrencyInfoList?.Binding?.FirstOrDefault(x => x.Index == currencyIndex);
                if (currency != null)
                    return $"{currency.Name}(#{currencyIndex})";
            }
            catch
            {
            }

            return $"货币索引 {currencyIndex}";
        }

        private static string HeroGradeName(byte value)
        {
            return value switch
            {
                0 => "普通",
                1 => "优秀",
                2 => "精良",
                3 => "卓越",
                4 => "传说",
                _ => $"品质{value}"
            };
        }

        private void SelectFileProperty(string propertyName, string title)
        {
            if (!EndGridEdits())
                return;

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
            if (!EndGridEdits())
                return;

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

        private void ApplyFilter()
        {
            if (_applyingFilter) return;

            _applyingFilter = true;
            try
            {
                if (!EndGridEdits())
                    return;

                DataGridView activeGrid = _tabs.SelectedTab?.Controls.OfType<DataGridView>().FirstOrDefault();
                ConfigSetting activeSetting = activeGrid?.CurrentRow?.Tag as ConfigSetting;
                int activeColumn = activeGrid?.CurrentCell?.ColumnIndex ?? 2;
                string filter = _filterText.Text?.Trim();
                bool modifiedOnly = _modifiedOnlyButton.Checked;
                int visible = 0;
                int total = 0;
                int modifiedCount = 0;

                foreach (KeyValuePair<string, DataGridView> pair in _sectionGrids)
                {
                    DataGridView grid = pair.Value;
                    grid.CurrentCell = null;

                    int sectionVisible = 0;
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        if (row.Tag is not ConfigSetting setting) continue;

                        total++;
                        bool modified = IsModified(setting, row);
                        ApplyModifiedStyle(row, modified);
                        if (modified) modifiedCount++;

                        bool match = (!modifiedOnly || modified) && MatchesFilter(setting, row, filter);
                        row.Visible = match;
                        if (!match) continue;

                        visible++;
                        sectionVisible++;
                    }

                    if (grid.Parent is TabPage page)
                        page.Text = string.IsNullOrWhiteSpace(filter) && !modifiedOnly ? pair.Key : $"{pair.Key} ({sectionVisible})";
                }

                string filterSuffix = string.IsNullOrWhiteSpace(filter) ? string.Empty : $"，过滤: {filter}";
                string modifiedSuffix = modifiedOnly ? $"，只看修改: {modifiedCount}" :
                    modifiedCount > 0 ? $"，已修改: {modifiedCount}" : string.Empty;
                _statusLabel.Text = $"配置项: {visible}/{total}{filterSuffix}{modifiedSuffix}。{GetConfigPathText()}";

                RestoreCurrentSetting(activeSetting, activeColumn);
            }
            finally
            {
                _applyingFilter = false;
            }
        }

        private static bool MatchesFilter(ConfigSetting setting, DataGridViewRow row, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;

            string value = Convert.ToString(row.Cells[2].Value, CultureInfo.InvariantCulture) ?? string.Empty;
            return setting.Section.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   setting.Property.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   GetTypeText(setting.Property.PropertyType).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsModified(ConfigSetting setting, DataGridViewRow row)
        {
            return !string.Equals(GetRowValueText(setting, row), setting.OriginalText, StringComparison.Ordinal);
        }

        private static string GetRowValueText(ConfigSetting setting, DataGridViewRow row)
        {
            object value = row.Cells[2].Value;

            if (value == null) return string.Empty;
            if (value.GetType() == setting.Property.PropertyType)
                return FormatValue(setting.Property.PropertyType, value);

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static void ApplyModifiedStyle(DataGridViewRow row, bool modified)
        {
            DataGridViewCell cell = row.Cells[2];
            cell.Style.BackColor = modified ? Color.FromArgb(255, 248, 205) : Color.Empty;
            cell.Style.ForeColor = modified ? Color.DarkBlue : Color.Empty;
        }

        private void ResetCurrentSetting()
        {
            if (!TryGetCurrentSettingRow(out DataGridViewRow row, out ConfigSetting setting)) return;

            SetRowValue(row, setting.Property.PropertyType, setting.OriginalValue);
            FocusRow(row);
            ApplyFilter();
        }

        private void ResetAllModifiedSettings()
        {
            if (!EndGridEdits())
                return;

            List<ConfigSetting> modified = _settings
                .Where(setting => _propertyRows.TryGetValue(setting.Property.Name, out DataGridViewRow row) && IsModified(setting, row))
                .ToList();

            if (modified.Count == 0)
            {
                MessageBox.Show(this, "当前没有未保存的配置修改。", "还原全部修改", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(
                    this,
                    $"确定还原全部 {modified.Count:#,##0} 个未保存的配置修改吗？",
                    "还原全部修改",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            foreach (ConfigSetting setting in modified)
            {
                DataGridViewRow row = _propertyRows[setting.Property.Name];
                SetRowValue(row, setting.Property.PropertyType, setting.OriginalValue);
            }

            ApplyFilter();
            _statusLabel.Text = $"已还原 {modified.Count:#,##0} 个配置修改。{GetConfigPathText()}";
        }

        private void CopyCurrentSetting()
        {
            if (!EndGridEdits())
                return;

            if (!TryGetCurrentSettingRow(out DataGridViewRow row, out ConfigSetting setting))
            {
                MessageBox.Show(this, "当前没有可复制的配置项。", "复制当前项", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string currentValue = GetRowValueText(setting, row);
            string originalValue = setting.OriginalText;
            string modified = IsModified(setting, row) ? "是" : "否";
            string[] headers = { "分组", "配置项", "类型", "当前值", "原始值", "已修改" };
            string[] values =
            {
                setting.Section,
                setting.Property.Name,
                GetTypeText(setting.Property.PropertyType),
                currentValue,
                originalValue,
                modified
            };

            string text = string.Join("\t", headers.Select(EscapeClipboardCell)) +
                          Environment.NewLine +
                          string.Join("\t", values.Select(EscapeClipboardCell));

            try
            {
                Clipboard.SetText(text);
                _statusLabel.Text = $"已复制配置项 {setting.Property.Name}。{GetConfigPathText()}";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"复制配置项失败。{GetConfigPathText()}";
                MessageBox.Show(this, ex.ToString(), "复制当前项失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool TryGetCurrentSettingRow(out DataGridViewRow row, out ConfigSetting setting)
        {
            DataGridView grid = _tabs.SelectedTab?.Controls.OfType<DataGridView>().FirstOrDefault();
            row = grid?.CurrentRow;
            setting = row?.Tag as ConfigSetting;
            return row != null && setting != null;
        }

        private static string EscapeClipboardCell(string value)
        {
            return (value ?? string.Empty)
                .Replace("\t", " ")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private void UpdateOriginalValues()
        {
            bool wasApplyingFilter = _applyingFilter;
            _applyingFilter = true;
            try
            {
                foreach (ConfigSetting setting in _settings)
                    MarkSettingSaved(setting.Property.Name, setting.Property.GetValue(null));
            }
            finally
            {
                _applyingFilter = wasApplyingFilter;
            }
        }

        private int CountModifiedSettings()
        {
            return _settings.Count(setting =>
                _propertyRows.TryGetValue(setting.Property.Name, out DataGridViewRow row) &&
                IsModified(setting, row));
        }

        private void FocusNextModifiedSetting()
        {
            if (!EndGridEdits())
                return;

            List<ConfigSetting> modified = _settings
                .Where(setting => _propertyRows.TryGetValue(setting.Property.Name, out DataGridViewRow row) && IsModified(setting, row))
                .ToList();

            if (modified.Count == 0)
            {
                MessageBox.Show(this, "当前没有未保存的配置修改。", "定位修改", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ConfigSetting current = _tabs.SelectedTab?
                .Controls.OfType<DataGridView>()
                .FirstOrDefault()?
                .CurrentRow?
                .Tag as ConfigSetting;

            int index = current == null ? -1 : modified.FindIndex(x => ReferenceEquals(x, current));
            ConfigSetting next = modified[(index + 1 + modified.Count) % modified.Count];

            if (_propertyRows.TryGetValue(next.Property.Name, out DataGridViewRow nextRow))
                FocusRow(nextRow);
        }

        private void FocusRow(DataGridViewRow row)
        {
            if (!row.Visible)
            {
                _modifiedOnlyButton.Checked = false;
                _filterText.Text = string.Empty;
                ApplyFilter();
            }

            if (row.DataGridView?.Parent is TabPage page)
                _tabs.SelectedTab = page;

            row.DataGridView.CurrentCell = row.Cells[2];
            row.DataGridView.Focus();
        }

        private void FocusFirstVisibleSetting()
        {
            foreach (DataGridView grid in _sectionGrids.Values)
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (!row.Visible || row.Tag is not ConfigSetting) continue;

                    FocusRow(row);
                    return;
                }
            }
        }

        private void RestoreCurrentSetting(ConfigSetting setting, int columnIndex)
        {
            if (setting == null) return;
            if (!_propertyRows.TryGetValue(setting.Property.Name, out DataGridViewRow row)) return;
            if (!row.Visible) return;

            if (row.DataGridView?.Parent is TabPage page)
                _tabs.SelectedTab = page;

            int safeColumn = Math.Max(0, Math.Min(columnIndex, row.Cells.Count - 1));
            row.DataGridView.CurrentCell = row.Cells[safeColumn];
        }

        private void GenerateEncryptionKey()
        {
            if (!EndGridEdits())
                return;

            byte[] random = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
                rng.GetBytes(random);

            SetPropertyText("EncryptionKey", Convert.ToBase64String(random));
            _statusLabel.Text = "已生成 32 字节 Base64 数据库加密密钥，尚未保存。";
        }

        private void ApplyDatabaseEncryption()
        {
            if (!EndGridEdits())
                return;

            if (SEnvir.EnvirThread != null)
            {
                MessageBox.Show(this, "应用数据库加密会重写 System.db 和 Users.db。请先停止服务端再执行。", "数据库加密", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!TryReadEncryptionSettings(out bool encryptionEnabled, out string encryptionKey))
                return;

            byte[] key = null;
            if (encryptionEnabled)
            {
                if (!TryGetEncryptionKey(encryptionKey, out key, out string reason))
                {
                    MessageBox.Show(this, reason, "数据库加密", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            string action = encryptionEnabled ? "使用当前密钥重新加密数据库" : "移除数据库加密";
            if (MessageBox.Show(this, $"{action}会重写 System.db 和 Users.db，并由 MirDB 创建备份。确定继续吗？", "数据库加密", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            bool oldEncryptionEnabled = Config.EncryptionEnabled;
            string oldEncryptionKey = Config.EncryptionKey;
            Dictionary<string, DateTime?> databaseWriteTimes = CaptureDatabaseWriteTimes();
            bool databaseSaved = false;

            try
            {
                Session session = new Session(SessionMode.Both)
                {
                    BackUpDelay = 60
                };
                session.Initialize(MirDbSessionFactory.DatabaseAssemblies);

                Config.EncryptionEnabled = encryptionEnabled;
                Config.EncryptionKey = encryptionKey;
                Encryption.SetKey(encryptionEnabled ? key : null);
                session.Save(true);
                databaseSaved = true;

                ConfigReader.Save(_configAssembly);
                MirDbSessionFactory.ConfigureEncryption();

                MarkEncryptionSettingsSaved();
                ApplyFilter();
                _statusLabel.Text = "数据库加密设置已应用并保存。";
                MessageBox.Show(this, "数据库加密设置已应用。", "数据库加密", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (!databaseSaved)
                {
                    if (!DatabaseFilesChanged(databaseWriteTimes))
                    {
                        RestoreEncryptionConfig(oldEncryptionEnabled, oldEncryptionKey);
                        MessageBox.Show(this, ex.ToString(), "数据库加密失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    _statusLabel.Text = "数据库加密未完整完成，数据库文件可能已部分重写。请检查 MirDB 备份后再启动服务端。";
                    MessageBox.Show(
                        this,
                        ex + "\r\n\r\n数据库重写过程中至少有一个数据库文件的时间戳发生变化，但整个保存流程没有完成。\r\n请先检查 MirDB 自动备份，确认 System.db 和 Users.db 使用同一套加密设置后再启动服务端。",
                        "数据库加密可能部分完成",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                _statusLabel.Text = "数据库已重写，但配置保存或加密状态刷新失败。请检查 Server.ini 后再启动服务端。";
                MessageBox.Show(
                    this,
                    ex + "\r\n\r\n数据库文件已经按当前加密设置重写，但 Server.ini 保存或管理端加密状态刷新失败。\r\n请确认 Server.ini 中的 EncryptionEnabled 和 EncryptionKey 与当前界面一致后再启动服务端；如无法确认，请使用 MirDB 自动备份恢复数据库。",
                    "数据库加密部分完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static Dictionary<string, DateTime?> CaptureDatabaseWriteTimes()
        {
            Session session = new Session(SessionMode.Both);

            return new[] { session.SystemPath, session.UsersPath }
                .ToDictionary(path => path, GetDatabaseWriteTimeUtc, StringComparer.OrdinalIgnoreCase);
        }

        private static bool DatabaseFilesChanged(Dictionary<string, DateTime?> writeTimes)
        {
            if (writeTimes == null || writeTimes.Count == 0) return false;

            try
            {
                return writeTimes.Any(pair => GetDatabaseWriteTimeUtc(pair.Key) != pair.Value);
            }
            catch
            {
                return true;
            }
        }

        private static DateTime? GetDatabaseWriteTimeUtc(string path)
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }

        private static void RestoreEncryptionConfig(bool encryptionEnabled, string encryptionKey)
        {
            Config.EncryptionEnabled = encryptionEnabled;
            Config.EncryptionKey = encryptionKey;

            try
            {
                MirDbSessionFactory.ConfigureEncryption();
            }
            catch (Exception restoreEx)
            {
                SEnvir.Log($"恢复原数据库加密配置失败: {restoreEx}");
            }
        }

        private bool TryReadEncryptionSettings(out bool encryptionEnabled, out string encryptionKey)
        {
            encryptionEnabled = Config.EncryptionEnabled;
            encryptionKey = Config.EncryptionKey;

            List<string> errors = new List<string>();

            if (TryReadSettingValue("EncryptionEnabled", errors, out object enabledValue) && enabledValue is bool parsedEnabled)
                encryptionEnabled = parsedEnabled;

            if (TryReadSettingValue("EncryptionKey", errors, out object keyValue))
                encryptionKey = Convert.ToString(keyValue, CultureInfo.InvariantCulture) ?? string.Empty;

            if (errors.Count == 0) return true;

            MessageBox.Show(this, string.Join("\r\n", errors), "数据库加密", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private bool TryReadSettingValue<T>(string propertyName, List<string> errors, out T value)
        {
            value = default;

            if (!TryReadSettingValue(propertyName, errors, out object rawValue))
                return false;

            if (rawValue is T typedValue)
            {
                value = typedValue;
                return true;
            }

            errors.Add($"{propertyName}: 配置类型不是 {typeof(T).Name}。");
            return false;
        }

        private bool TryReadSettingValue(string propertyName, List<string> errors, out object value)
        {
            value = null;

            if (!_propertyRows.TryGetValue(propertyName, out DataGridViewRow row) || row.Tag is not ConfigSetting setting)
            {
                errors.Add($"{propertyName}: 找不到配置项。");
                return false;
            }

            try
            {
                value = ParseValue(setting.Property.PropertyType, row.Cells[2].Value);
                return true;
            }
            catch (Exception ex)
            {
                errors.Add($"{propertyName}: {ex.Message}");
                return false;
            }
        }

        private void MarkEncryptionSettingsSaved()
        {
            bool wasApplyingFilter = _applyingFilter;
            _applyingFilter = true;
            try
            {
                MarkSettingSaved("EncryptionEnabled", Config.EncryptionEnabled);
                MarkSettingSaved("EncryptionKey", Config.EncryptionKey);
            }
            finally
            {
                _applyingFilter = wasApplyingFilter;
            }
        }

        private void MarkSettingSaved(string propertyName, object value)
        {
            if (!_propertyRows.TryGetValue(propertyName, out DataGridViewRow row) || row.Tag is not ConfigSetting setting)
                return;

            SetRowValue(row, setting.Property.PropertyType, value);
            setting.UpdateOriginalValue(value);
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
                WinFormsFileWriter.CopyFileAtomically(systemPath, targetPath, true);

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
                string url = BuildRemoteSyncUrl(Config.SyncRemotePreffix, Config.SyncKey);

                using (HttpClient client = new HttpClient())
                using (ByteArrayContent content = new ByteArrayContent(File.ReadAllBytes(systemPath)))
                {
                    using HttpResponseMessage response = await client.PostAsync(url, content);
                    response.EnsureSuccessStatusCode();
                }

                MessageBox.Show(this, "远程数据库同步完成。", "同步远程数据库", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "同步远程数据库失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string BuildRemoteSyncUrl(string prefix, string syncKey)
        {
            prefix = (prefix ?? string.Empty).Trim();

            string separator = prefix.Contains("?")
                ? (prefix.EndsWith("?", StringComparison.Ordinal) || prefix.EndsWith("&", StringComparison.Ordinal) ? string.Empty : "&")
                : "?";

            return $"{prefix}{separator}Type={Uri.EscapeDataString(WebServer.SystemDBSyncCommand)}&Key={Uri.EscapeDataString(syncKey ?? string.Empty)}";
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

        private static bool TryGetEncryptionKey(string encryptionKey, out byte[] key, out string reason)
        {
            key = null;
            reason = null;

            try
            {
                key = Convert.FromBase64String(encryptionKey ?? string.Empty);
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
                string boolText = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

                if (bool.TryParse(boolText, out bool result)) return result;
                if (boolText == "1") return true;
                if (boolText == "0") return false;
                if (boolText.Equals("是", StringComparison.OrdinalIgnoreCase) ||
                    boolText.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                    boolText.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                    boolText.Equals("on", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (boolText.Equals("否", StringComparison.OrdinalIgnoreCase) ||
                    boolText.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                    boolText.Equals("n", StringComparison.OrdinalIgnoreCase) ||
                    boolText.Equals("off", StringComparison.OrdinalIgnoreCase))
                    return false;

                throw new FormatException("请输入 True/False、1/0、是/否。");
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
            if (type.IsEnum) return ParseEnum(type, text);

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

        private static object ParseEnum(Type enumType, string text)
        {
            object parsed = Enum.Parse(enumType, text, true);
            if (!IsDefinedEnumValue(enumType, parsed))
                throw new FormatException($"`{text}` 不是 {enumType.Name} 的有效值。");

            return parsed;
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

        private sealed class HeroGradePromoteEventPresetApplyForm : Form
        {
            private readonly Func<HeroGradePromoteEventPresetApplyData> _loadData;
            private readonly Action<HeroGradePromoteEventPreset, bool> _applyPreset;
            private readonly DataGridView _grid = new DataGridView();
            private readonly Label _summaryLabel = new Label();
            private readonly CheckBox _saveOnExpireBox = new CheckBox { Text = "到期写回配置", AutoSize = true };
            private bool _loadingData;
            private HeroGradePromoteEventPresetApplyData _data;

            public HeroGradePromoteEventPresetApplyForm(
                Func<HeroGradePromoteEventPresetApplyData> loadData,
                Action<HeroGradePromoteEventPreset, bool> applyPreset)
            {
                _loadData = loadData;
                _applyPreset = applyPreset;

                Text = "套用品质活动模板";
                Width = 720;
                Height = 420;
                StartPosition = FormStartPosition.CenterParent;

                ToolStrip toolStrip = new ToolStrip();
                ToolStripButton refreshButton = new ToolStripButton("刷新");
                refreshButton.Click += (_, _) => RefreshData();
                toolStrip.Items.Add(refreshButton);

                ToolStripButton applyButton = new ToolStripButton("套用");
                applyButton.Click += (_, _) => ApplySelectedPreset();
                toolStrip.Items.Add(applyButton);

                toolStrip.Items.Add(new ToolStripSeparator());
                toolStrip.Items.Add(new ToolStripControlHost(_saveOnExpireBox));

                _saveOnExpireBox.CheckedChanged += (_, _) => UpdateSummary();

                _summaryLabel.Dock = DockStyle.Top;
                _summaryLabel.AutoSize = false;
                _summaryLabel.Height = 64;
                _summaryLabel.Padding = new Padding(8);

                _grid.Dock = DockStyle.Fill;
                _grid.AllowUserToAddRows = false;
                _grid.AllowUserToDeleteRows = false;
                _grid.AutoGenerateColumns = false;
                _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                _grid.BackgroundColor = SystemColors.Window;
                _grid.BorderStyle = BorderStyle.None;
                _grid.MultiSelect = false;
                _grid.ReadOnly = true;
                _grid.RowHeadersVisible = false;
                _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                _grid.SelectionChanged += (_, _) => UpdateSummary();

                AddPresetColumn("模板", nameof(HeroGradePromoteEventPresetApplyRow.Name), 160);
                AddPresetColumn("消耗倍率", nameof(HeroGradePromoteEventPresetApplyRow.CostRate), 100);
                AddPresetColumn("成功加成", nameof(HeroGradePromoteEventPresetApplyRow.SuccessBonus), 100);
                AddPresetColumn("持续时间", nameof(HeroGradePromoteEventPresetApplyRow.Duration), 140);
                AddPresetColumn("说明", nameof(HeroGradePromoteEventPresetApplyRow.Description), 220);

                Controls.Add(_grid);
                Controls.Add(_summaryLabel);
                Controls.Add(toolStrip);
                toolStrip.Dock = DockStyle.Top;

                RefreshData();
            }

            private void AddPresetColumn(string header, string propertyName, int width)
            {
                _grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = header,
                    DataPropertyName = propertyName,
                    SortMode = DataGridViewColumnSortMode.NotSortable,
                    Width = width
                });
            }

            private void RefreshData()
            {
                string selectedName = GetSelectedPreset()?.Name;
                _data = _loadData();

                if (_data.Errors.Count > 0)
                {
                    _summaryLabel.Text = "活动模板配置存在错误。";
                    _grid.DataSource = Array.Empty<HeroGradePromoteEventPresetApplyRow>();
                    MessageBox.Show(this, string.Join("\r\n", _data.Errors.Take(20)), "套用品质活动模板", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _loadingData = true;
                try
                {
                    _saveOnExpireBox.Checked = _data.DefaultSaveOnExpire;
                    _grid.DataSource = _data.Presets.Select(CreatePresetRow).ToList();

                    if (!string.IsNullOrWhiteSpace(selectedName))
                        SelectPreset(selectedName);

                    if (_grid.Rows.Count > 0 && _grid.CurrentRow == null)
                        _grid.CurrentCell = _grid.Rows[0].Cells[0];
                }
                finally
                {
                    _loadingData = false;
                }

                UpdateSummary();

                if (_data.Presets.Count == 0)
                    MessageBox.Show(this, "当前没有配置活动模板。", "套用品质活动模板", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            private HeroGradePromoteEventPresetApplyRow CreatePresetRow(HeroGradePromoteEventPreset preset)
            {
                return new HeroGradePromoteEventPresetApplyRow
                {
                    Name = preset.Name,
                    CostRate = $"{preset.CostRate}%",
                    SuccessBonus = $"+{preset.SuccessBonus}%",
                    Duration = FormatPresetDuration(preset.DurationMinutes),
                    Description = HeroGradePromoteEventPresetParser.Format(preset),
                    Preset = preset
                };
            }

            private void ApplySelectedPreset()
            {
                HeroGradePromoteEventPreset preset = GetSelectedPreset();
                if (preset == null)
                {
                    MessageBox.Show(this, "请选择一个活动模板。", "套用品质活动模板", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                _applyPreset(preset, _saveOnExpireBox.Checked);
                RefreshData();
                SelectPreset(preset.Name);
            }

            private HeroGradePromoteEventPreset GetSelectedPreset()
            {
                return _grid.CurrentRow?.DataBoundItem is HeroGradePromoteEventPresetApplyRow row ? row.Preset : null;
            }

            private void SelectPreset(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return;

                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (row.DataBoundItem is not HeroGradePromoteEventPresetApplyRow presetRow ||
                        !string.Equals(presetRow.Name, name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    _grid.CurrentCell = row.Cells[0];
                    return;
                }
            }

            private void UpdateSummary()
            {
                if (_loadingData || _data == null)
                    return;

                string current = _data.CurrentErrors.Count > 0
                    ? $"当前：活动字段存在错误（{_data.CurrentErrors[0]}）"
                    : $"当前：消耗倍率 {_data.CurrentCostRate}%，成功率加成 +{_data.CurrentSuccessBonus}%，结束 {FormatHeroGradePromoteEventEnd(_data.CurrentEventEnd, _data.CurrentSaveOnExpire)}";
                HeroGradePromoteEventPreset preset = GetSelectedPreset();
                if (preset == null)
                {
                    _summaryLabel.Text = current;
                    return;
                }

                DateTime eventEnd = preset.DurationMinutes > 0
                    ? _data.ReferenceTime.AddMinutes(preset.DurationMinutes)
                    : DateTime.MinValue;
                bool saveOnExpire = eventEnd != DateTime.MinValue && _saveOnExpireBox.Checked;
                string selected = $"选中：消耗倍率 {preset.CostRate}%，成功率加成 +{preset.SuccessBonus}%，结束 {FormatHeroGradePromoteEventEnd(eventEnd, saveOnExpire)}";
                _summaryLabel.Text = $"{current}\r\n{selected}";
            }

            private static string FormatPresetDuration(int durationMinutes)
            {
                if (durationMinutes <= 0)
                    return "不自动到期";

                return FormatDuration(TimeSpan.FromMinutes(durationMinutes));
            }
        }

        private sealed class HeroGradePromoteCostPreviewForm : Form
        {
            private readonly Func<HeroGradePromoteCostPreviewData> _loadData;
            private readonly DataGridView _grid = new DataGridView();
            private readonly Label _summaryLabel = new Label();
            private readonly ToolStripComboBox _startGradeBox = new ToolStripComboBox();
            private readonly ToolStripComboBox _targetGradeBox = new ToolStripComboBox();
            private bool _updatingGradeBoxes;
            private HeroGradePromoteCostPreviewData _data;

            public HeroGradePromoteCostPreviewForm(Func<HeroGradePromoteCostPreviewData> loadData)
            {
                _loadData = loadData;

                Text = "品质培养试算";
                Width = 1120;
                Height = 460;
                StartPosition = FormStartPosition.CenterParent;

                ToolStrip toolStrip = new ToolStrip();
                ToolStripButton refreshButton = new ToolStripButton("刷新");
                refreshButton.Click += (_, _) => RefreshData();
                toolStrip.Items.Add(refreshButton);

                ToolStripButton copyButton = new ToolStripButton("复制");
                copyButton.Click += (_, _) => CopyData();
                toolStrip.Items.Add(copyButton);

                toolStrip.Items.Add(new ToolStripSeparator());
                toolStrip.Items.Add(new ToolStripLabel("起始"));
                _startGradeBox.DropDownStyle = ComboBoxStyle.DropDownList;
                _startGradeBox.Width = 90;
                _startGradeBox.SelectedIndexChanged += (_, _) => ApplyData();
                toolStrip.Items.Add(_startGradeBox);

                toolStrip.Items.Add(new ToolStripLabel("目标"));
                _targetGradeBox.DropDownStyle = ComboBoxStyle.DropDownList;
                _targetGradeBox.Width = 90;
                _targetGradeBox.SelectedIndexChanged += (_, _) => ApplyData();
                toolStrip.Items.Add(_targetGradeBox);

                _summaryLabel.Dock = DockStyle.Top;
                _summaryLabel.AutoSize = false;
                _summaryLabel.Height = 88;
                _summaryLabel.Padding = new Padding(8);

                _grid.Dock = DockStyle.Fill;
                _grid.AllowUserToAddRows = false;
                _grid.AllowUserToDeleteRows = false;
                _grid.AutoGenerateColumns = false;
                _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                _grid.BackgroundColor = SystemColors.Window;
                _grid.BorderStyle = BorderStyle.None;
                _grid.ReadOnly = true;
                _grid.RowHeadersVisible = false;
                _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

                AddPreviewColumn("档位", nameof(HeroGradePromoteCostPreviewRow.Grade), 130);
                AddPreviewColumn("成长倍率", nameof(HeroGradePromoteCostPreviewRow.GrowthMultiplier), 90);
                AddPreviewColumn("金币", nameof(HeroGradePromoteCostPreviewRow.Gold), 120);
                AddPreviewColumn("累计金币", nameof(HeroGradePromoteCostPreviewRow.TotalGold), 120);
                AddPreviewColumn("材料", nameof(HeroGradePromoteCostPreviewRow.Material), 160);
                AddPreviewColumn("累计材料", nameof(HeroGradePromoteCostPreviewRow.TotalMaterial), 160);
                AddPreviewColumn("货币", nameof(HeroGradePromoteCostPreviewRow.Currency), 160);
                AddPreviewColumn("累计货币", nameof(HeroGradePromoteCostPreviewRow.TotalCurrency), 160);
                AddPreviewColumn("保护", nameof(HeroGradePromoteCostPreviewRow.Protection), 200);

                Controls.Add(_grid);
                Controls.Add(_summaryLabel);
                Controls.Add(toolStrip);
                toolStrip.Dock = DockStyle.Top;

                RefreshData();
            }

            private void AddPreviewColumn(string header, string propertyName, int width)
            {
                _grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = header,
                    DataPropertyName = propertyName,
                    SortMode = DataGridViewColumnSortMode.NotSortable,
                    Width = width
                });
            }

            private void RefreshData()
            {
                _data = _loadData();

                if (_data.Errors.Count > 0)
                {
                    _summaryLabel.Text = "配置存在错误，无法试算。";
                    _grid.DataSource = Array.Empty<HeroGradePromoteCostPreviewRow>();
                    SetGradeBoxesEnabled(false);
                    MessageBox.Show(this, string.Join("\r\n", _data.Errors.Take(20)), "品质培养试算", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                PopulateGradeBoxes();
                ApplyData();
            }

            private void PopulateGradeBoxes()
            {
                int previousStart = GetSelectedGrade(_startGradeBox, 0);
                int previousTarget = GetSelectedGrade(_targetGradeBox, _data.MaxGrade);

                _updatingGradeBoxes = true;
                try
                {
                    _startGradeBox.Items.Clear();
                    _targetGradeBox.Items.Clear();

                    if (_data.MaxGrade <= 0)
                    {
                        SetGradeBoxesEnabled(false);
                        return;
                    }

                    for (int i = 0; i < _data.MaxGrade; i++)
                        _startGradeBox.Items.Add(new GradeOption(i));

                    for (int i = 1; i <= _data.MaxGrade; i++)
                        _targetGradeBox.Items.Add(new GradeOption(i));

                    int start = Math.Max(0, Math.Min(_data.MaxGrade - 1, previousStart));
                    int target = previousTarget <= 0 ? _data.MaxGrade : Math.Max(start + 1, Math.Min(_data.MaxGrade, previousTarget));

                    SelectGrade(_startGradeBox, start);
                    SelectGrade(_targetGradeBox, target);
                    SetGradeBoxesEnabled(true);
                }
                finally
                {
                    _updatingGradeBoxes = false;
                }
            }

            private void ApplyData()
            {
                if (_updatingGradeBoxes || _data == null || _data.Errors.Count > 0) return;

                if (_data.MaxGrade <= 0)
                {
                    _summaryLabel.Text = $"{_data.Summary}\r\n{_data.MaterialSummary}\r\n{_data.CurrencySummary}；{_data.ProtectionSummary}";
                    _grid.DataSource = _data.Rows.ToList();
                    return;
                }

                int start = GetSelectedGrade(_startGradeBox, 0);
                int target = GetSelectedGrade(_targetGradeBox, _data.MaxGrade);
                if (target <= start)
                {
                    target = Math.Min(_data.MaxGrade, start + 1);
                    _updatingGradeBoxes = true;
                    try
                    {
                        SelectGrade(_targetGradeBox, target);
                    }
                    finally
                    {
                        _updatingGradeBoxes = false;
                    }
                }

                long totalGoldCost = 0;
                long totalMaterialCount = 0;
                long totalCurrencyAmount = 0;
                List<HeroGradePromoteCostPreviewRow> rows = new List<HeroGradePromoteCostPreviewRow>();

                foreach (HeroGradePromoteCostPreviewRow source in _data.Rows.Where(x => x.CurrentGrade >= start && x.NextGrade <= target))
                {
                    totalGoldCost = AddCapped(totalGoldCost, source.GoldCost);
                    totalMaterialCount = AddCapped(totalMaterialCount, source.MaterialCount);
                    totalCurrencyAmount = AddCapped(totalCurrencyAmount, source.CurrencyAmount);

                    rows.Add(new HeroGradePromoteCostPreviewRow
                    {
                        Grade = source.Grade,
                        GrowthMultiplier = source.GrowthMultiplier,
                        Gold = source.Gold,
                        TotalGold = FormatCost(totalGoldCost),
                        GoldCost = source.GoldCost,
                        Material = source.Material,
                        TotalMaterial = FormatNamedCost(_data.MaterialName, totalMaterialCount),
                        MaterialCount = source.MaterialCount,
                        Currency = source.Currency,
                        TotalCurrency = FormatNamedCost(_data.CurrencyName, totalCurrencyAmount),
                        CurrencyAmount = source.CurrencyAmount,
                        CurrentGrade = source.CurrentGrade,
                        NextGrade = source.NextGrade,
                        Protection = source.Protection
                    });
                }

                if (rows.Count == 0)
                {
                    rows.Add(new HeroGradePromoteCostPreviewRow
                    {
                        Grade = "无可培养区间",
                        GrowthMultiplier = "x1",
                        Gold = "无",
                        TotalGold = "无",
                        Material = "无",
                        TotalMaterial = "无",
                        Currency = "无",
                        TotalCurrency = "无",
                        Protection = _data.ProtectionSummary.Replace("保护: ", string.Empty)
                    });
                }

                _summaryLabel.Text = $"{_data.Summary}\r\n试算区间 {HeroGradeName((byte)start)} -> {HeroGradeName((byte)target)}\r\n{_data.MaterialSummary}\r\n{_data.CurrencySummary}；{_data.ProtectionSummary}";
                _grid.DataSource = rows;
            }

            private void SetGradeBoxesEnabled(bool enabled)
            {
                _startGradeBox.Enabled = enabled;
                _targetGradeBox.Enabled = enabled;
            }

            private static int GetSelectedGrade(ToolStripComboBox box, int fallback)
            {
                return box.SelectedItem is GradeOption option ? option.Grade : fallback;
            }

            private static void SelectGrade(ToolStripComboBox box, int grade)
            {
                for (int i = 0; i < box.Items.Count; i++)
                {
                    if (box.Items[i] is GradeOption option && option.Grade == grade)
                    {
                        box.SelectedIndex = i;
                        return;
                    }
                }
            }

            private void CopyData()
            {
                if (_data == null || _data.Errors.Count > 0) return;

                StringBuilder builder = new StringBuilder();
                builder.AppendLine(_summaryLabel.Text.Replace("\r\n", " / ").Replace("\n", " / "));
                builder.AppendLine("档位\t成长倍率\t金币\t累计金币\t材料\t累计材料\t货币\t累计货币\t保护");

                foreach (HeroGradePromoteCostPreviewRow row in (_grid.DataSource as IEnumerable<HeroGradePromoteCostPreviewRow>) ?? Enumerable.Empty<HeroGradePromoteCostPreviewRow>())
                    builder.AppendLine($"{row.Grade}\t{row.GrowthMultiplier}\t{row.Gold}\t{row.TotalGold}\t{row.Material}\t{row.TotalMaterial}\t{row.Currency}\t{row.TotalCurrency}\t{row.Protection}");

                Clipboard.SetText(builder.ToString());
            }

            private sealed class GradeOption
            {
                public GradeOption(int grade)
                {
                    Grade = grade;
                }

                public int Grade { get; }

                public override string ToString()
                {
                    return HeroGradeName((byte)Grade);
                }
            }
        }

        private sealed class HeroGradePromoteCostPreviewData
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<HeroGradePromoteCostPreviewRow> Rows = new List<HeroGradePromoteCostPreviewRow>();
            public int MaxGrade { get; set; }
            public string MaterialName { get; set; } = string.Empty;
            public string CurrencyName { get; set; } = string.Empty;
            public string Summary { get; set; } = string.Empty;
            public string MaterialSummary { get; set; } = string.Empty;
            public string CurrencySummary { get; set; } = string.Empty;
            public string ProtectionSummary { get; set; } = string.Empty;

            public string ToClipboardText()
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine(Summary);
                builder.AppendLine(MaterialSummary);
                builder.AppendLine(CurrencySummary);
                builder.AppendLine(ProtectionSummary);
                builder.AppendLine("档位\t成长倍率\t金币\t累计金币\t材料\t累计材料\t货币\t累计货币\t保护");

                foreach (HeroGradePromoteCostPreviewRow row in Rows)
                    builder.AppendLine($"{row.Grade}\t{row.GrowthMultiplier}\t{row.Gold}\t{row.TotalGold}\t{row.Material}\t{row.TotalMaterial}\t{row.Currency}\t{row.TotalCurrency}\t{row.Protection}");

                return builder.ToString();
            }
        }

        private sealed class HeroGradePromoteEventPresetApplyData
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> CurrentErrors = new List<string>();
            public readonly List<HeroGradePromoteEventPreset> Presets = new List<HeroGradePromoteEventPreset>();
            public int CurrentCostRate { get; set; }
            public int CurrentSuccessBonus { get; set; }
            public DateTime CurrentEventEnd { get; set; }
            public bool CurrentSaveOnExpire { get; set; }
            public bool DefaultSaveOnExpire { get; set; }
            public DateTime ReferenceTime { get; set; }
        }

        private sealed class HeroGradePromoteEventPresetApplyRow
        {
            public string Name { get; set; }
            public string CostRate { get; set; }
            public string SuccessBonus { get; set; }
            public string Duration { get; set; }
            public string Description { get; set; }
            public HeroGradePromoteEventPreset Preset { get; set; }
        }

        private sealed class HeroGradePromoteCostPreviewRow
        {
            public string Grade { get; set; }
            public string GrowthMultiplier { get; set; }
            public string Gold { get; set; }
            public string TotalGold { get; set; }
            public long GoldCost { get; set; }
            public string Material { get; set; }
            public string TotalMaterial { get; set; }
            public long MaterialCount { get; set; }
            public string Currency { get; set; }
            public string TotalCurrency { get; set; }
            public long CurrencyAmount { get; set; }
            public int CurrentGrade { get; set; }
            public int NextGrade { get; set; }
            public string Protection { get; set; }
        }

        private sealed class ConfigSetting
        {
            public ConfigSetting(string section, PropertyInfo property)
            {
                Section = section;
                Property = property;
                UpdateOriginalValue(property.GetValue(null));
            }

            public string Section { get; }
            public PropertyInfo Property { get; }
            public object OriginalValue { get; private set; }
            public string OriginalText { get; private set; }

            public void UpdateOriginalValue(object value)
            {
                OriginalValue = value;
                OriginalText = FormatValue(Property.PropertyType, value);
            }
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
