using Client.Controls;
using Client.Envir;
using Client.Models;
using Shared.Rendering;
using Client.Scenes.Views.Character;
using Client.UserModels;
using Library;
using Library.SystemModels;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using C = Library.Network.ClientPackets;

namespace Client.Scenes.Views
{
    public sealed class HeroDialog : DXWindow
    {
        public override WindowType Type => WindowType.HeroBox;
        public override bool CustomSize => false;
        public override bool AutomaticVisibility => true;

        private DXTabControl TabControl;
        private DXTab CharacterTab, InventoryTab, DisciplineTab, SkillTab;
        private DXVScrollBar SkillScrollBar;

        public DXLabel NameLabel, ClassLabel, LevelLabel, GradeLabel, HealthLabel, ManaLabel, ExperienceLabel, WeightLabel, StateLabel, BehaviourLabel, CreateInfoLabel, CreateCountLabel, SkillLabel, AutoPotLabel, HPItemLabel, MPItemLabel, PromoteCompensationLabel, PromoteEventLabel;
        private DXLabel WearWeightLabel, HandWeightLabel, ManageLabel, DisciplineLevelLabel, DisciplineExperienceLabel, DisciplineMessageLabel, EmptySkillLabel;
        public DXButton PreviousButton, NextButton, CurrentButton, RenameButton, DeleteButton, MoveUpButton, MoveDownButton, CreateButton, ClassButton, GenderButton, SpawnButton, AttackButton, CounterButton, FollowButton, CustomButton, PromoteButton, SetHPButton, SetMPButton, SaveAutoPotButton;
        private DXButton SortButton;
        public DXCheckBox AutoPotCheckBox;
        public DXNumberBox HPPercentBox, MPPercentBox;
        public DXItemGrid InventoryGrid;
        public DXItemCell HPItemCell, MPItemCell;
        public DXItemCell[] EquipmentGrid;

        private readonly Dictionary<Stat, DXLabel> DisplayStats = new();
        private readonly List<HeroMagicCell> SkillCells = new();
        private readonly List<DXControl> SkillScrollControls = new();
        private readonly List<HeroDisciplineMagicIcon> DisciplineMagicCells = new();

        public int SelectedHeroIndex;
        public MirClass CreateClass = MirClass.Warrior;
        public MirGender CreateGender = MirGender.Male;
        public bool Updating;
        private readonly Dictionary<int, ClientUserHero> InspectedHeroes = new();
        private readonly HashSet<int> RequestedHeroIndexes = new();
        private DateTime GradeHighlightUntil;

        public bool IsViewingCurrentHero => SelectedHeroIndex > 0 && SelectedHeroIndex == (GameScene.Game?.CurrentHeroIndex ?? 0);
        public bool IsHeroItemPanelWritable => IsViewingCurrentHero && GameScene.Game?.CurrentHero != null;
        private static bool SupportsMultiInspectCache => GameScene.Game?.SupportsHeroMultiInspectCache == true;

        private static readonly MirClass[] ClassCycle = { MirClass.Warrior, MirClass.Wizard, MirClass.Taoist, MirClass.Assassin };

        private ClientUserHero DisplayedHero
        {
            get
            {
                if (IsViewingCurrentHero)
                    return GameScene.Game?.CurrentHero;

                if (!SupportsMultiInspectCache) return null;

                return InspectedHeroes.TryGetValue(SelectedHeroIndex, out ClientUserHero hero) ? hero : null;
            }
        }

        private ClientHeroInformation SelectedSummary => GameScene.Game?.Heroes?.FirstOrDefault(x => x.Index == SelectedHeroIndex);

        public bool IsDisplayingHero(int index)
        {
            return index > 0 && SelectedHeroIndex == index && DisplayedHero?.Index == index;
        }

        public bool TryGetDisplayedHeroGrid(GridType grid, out ClientUserItem[] items)
        {
            ClientUserHero hero = DisplayedHero;

            items = grid switch
            {
                GridType.HeroInventory => hero?.InventoryArray,
                GridType.HeroEquipment => hero?.EquipmentArray,
                _ => null
            };

            return items != null;
        }

        public void RefreshHeroItems(int heroIndex, bool fullRefresh)
        {
            if (!IsDisplayingHero(heroIndex)) return;

            if (fullRefresh)
                HeroChanged();
            else
                UpdateLabels();
        }

        public HeroDialog()
        {
            TitleLabel.Text = "英雄";
            HasFooter = false;
            SetClientSize(new Size(520, 508));

            TabControl = new DXTabControl
            {
                Parent = this,
                Location = new Point(ClientArea.Left + 8, ClientArea.Top + 8),
                Size = new Size(ClientArea.Width - 16, ClientArea.Height - 16),
                MarginLeft = 18,
                Padding = 2,
                BackColour = Color.Empty
            };

            CharacterTab = CreateTab("角色");
            InventoryTab = CreateTab("包裹");
            DisciplineTab = CreateTab("修仙");
            SkillTab = CreateTab("技能");

            TabControl.SelectedTab = CharacterTab;

            CreateCharacterTab();
            CreateInventoryTab();
            CreateDisciplineTab();
            CreateSkillTab();

            UpdateCreateInfo();
            HeroChanged();
        }

        private DXTab CreateTab(string text)
        {
            return new DXTab
            {
                Parent = TabControl,
                TabButton = { Label = { Text = text } },
                BackColour = Color.Empty,
                Location = new Point(0, 26),
                MinimumTabWidth = 58
            };
        }

        private void CreateCharacterTab()
        {
            NameLabel = CreateLabel(CharacterTab, "未选择英雄", 12, 8, 220, true);
            ClassLabel = CreateLabel(CharacterTab, "职业: -", 12, 30, 120);
            LevelLabel = CreateLabel(CharacterTab, "等级: -", 140, 30, 66);
            GradeLabel = CreateLabel(CharacterTab, "品质: -", 210, 30, 110);
            StateLabel = CreateLabel(CharacterTab, "状态: -", 285, 8, 145);
            BehaviourLabel = CreateLabel(CharacterTab, "模式: -", 325, 30, 170);

            HealthLabel = CreateLabel(CharacterTab, "生命: -", 12, 54, 180);
            ManaLabel = CreateLabel(CharacterTab, "魔法: -", 210, 54, 180);
            PromoteCompensationLabel = CreateLabel(CharacterTab, "补偿: -", 400, 54, 88);
            ExperienceLabel = CreateLabel(CharacterTab, "经验: -", 12, 76, 260);
            WeightLabel = CreateLabel(CharacterTab, "负重: -", 285, 76, 145);

            PreviousButton = CreateButtonControl(CharacterTab, "上一个", 12, 103, 64);
            PreviousButton.MouseClick += (o, e) => SelectRelative(-1);

            NextButton = CreateButtonControl(CharacterTab, "下一个", 78, 103, 64);
            NextButton.MouseClick += (o, e) => SelectRelative(1);

            CurrentButton = CreateButtonControl(CharacterTab, "设为当前", 144, 103, 56);
            CurrentButton.MouseClick += (o, e) => SetSelectedCurrent();

            SpawnButton = CreateButtonControl(CharacterTab, "召唤", 202, 103, 56);
            SpawnButton.MouseClick += (o, e) => CEnvir.Enqueue(new C.ToggleHeroSpawn());

            AttackButton = CreateButtonControl(CharacterTab, "主动", 264, 103, 48);
            AttackButton.MouseClick += (o, e) => CEnvir.Enqueue(new C.SetHeroBehaviour { Behaviour = HeroBehaviour.Attack });

            CounterButton = CreateButtonControl(CharacterTab, "反击", 316, 103, 48);
            CounterButton.MouseClick += (o, e) => CEnvir.Enqueue(new C.SetHeroBehaviour { Behaviour = HeroBehaviour.CounterAttack });

            FollowButton = CreateButtonControl(CharacterTab, "跟随", 368, 103, 48);
            FollowButton.MouseClick += (o, e) => CEnvir.Enqueue(new C.SetHeroBehaviour { Behaviour = HeroBehaviour.Follow });

            CustomButton = CreateButtonControl(CharacterTab, "自定义", 420, 103, 68);
            CustomButton.Hint = "点击切换到自定义模式；已在自定义模式时再次点击可轮换策略。";
            CustomButton.MouseClick += (o, e) => SetOrCycleCustomBehaviour();

            CreateInfoLabel = CreateLabel(CharacterTab, "创建: 战士 男", 12, 132, 145);

            ClassButton = CreateButtonControl(CharacterTab, "职业", 162, 129, 52);
            ClassButton.MouseClick += (o, e) =>
            {
                int index = Array.IndexOf(ClassCycle, CreateClass);
                CreateClass = ClassCycle[(index + 1) % ClassCycle.Length];
                UpdateCreateInfo();
            };

            GenderButton = CreateButtonControl(CharacterTab, "性别", 220, 129, 52);
            GenderButton.MouseClick += (o, e) =>
            {
                CreateGender = CreateGender == MirGender.Male ? MirGender.Female : MirGender.Male;
                UpdateCreateInfo();
            };

            CreateButton = CreateButtonControl(CharacterTab, "新建英雄", 278, 129, 86);
            CreateButton.MouseClick += (o, e) => OpenCreateWindow();

            CreateCountLabel = CreateLabel(CharacterTab, "数量: 0/0", 374, 132, 120);
            ManageLabel = CreateLabel(CharacterTab, "管理: -", 12, 424, 160, true);

            RenameButton = CreateButtonControl(CharacterTab, "改名", 12, 448, 52);
            RenameButton.MouseClick += (o, e) => OpenRenameWindow();

            DeleteButton = CreateButtonControl(CharacterTab, "删除", 70, 448, 52);
            DeleteButton.MouseClick += (o, e) => ConfirmDeleteHero();

            MoveUpButton = CreateButtonControl(CharacterTab, "上移", 128, 448, 52);
            MoveUpButton.MouseClick += (o, e) => MoveSelectedHero(-1);

            MoveDownButton = CreateButtonControl(CharacterTab, "下移", 186, 448, 52);
            MoveDownButton.MouseClick += (o, e) => MoveSelectedHero(1);

            PromoteButton = CreateButtonControl(CharacterTab, "培养", 244, 448, 60);
            PromoteButton.Hint = "左键预览品质培养消耗；右键打开确认培养窗口。";
            PromoteButton.MouseClick += (o, e) => PromoteHeroGrade(e.Button);

            PromoteEventLabel = CreateLabel(CharacterTab, "活动: 无", 312, 451, 176);

            CharacterTab.BeforeChildrenDraw += CharacterTab_BeforeChildrenDraw;

            CreateEquipmentGrid();
            CreateStatsPanel();
        }

        private void CreateEquipmentGrid()
        {
            EquipmentGrid = new DXItemCell[Globals.EquipmentSize];

            AddEquipmentCell(EquipmentSlot.Weapon, 58, 165, new Size(65, 90), false, true);
            AddEquipmentCell(EquipmentSlot.Armour, 120, 166, new Size(70, 150), false, true);
            AddEquipmentCell(EquipmentSlot.Shield, 170, 213, null, true, true);
            AddEquipmentCell(EquipmentSlot.Helmet, 140, 133, new Size(35, 35), true, true);

            AddEquipmentCell(EquipmentSlot.Costume, 10, 161, null, true, false, 34);
            AddEquipmentCell(EquipmentSlot.Emblem, 244, 161, null, true, false, 104);
            AddEquipmentCell(EquipmentSlot.HorseArmour, 283, 161, null, true, false, 82);
            AddEquipmentCell(EquipmentSlot.Torch, 10, 239, null, true, false, 38);
            AddEquipmentCell(EquipmentSlot.Necklace, 10, 200, null, true, false, 33);
            AddEquipmentCell(EquipmentSlot.BraceletL, 244, 200, null, true, false, 32);
            AddEquipmentCell(EquipmentSlot.BraceletR, 283, 200, null, true, false, 32);
            AddEquipmentCell(EquipmentSlot.RingL, 244, 239, null, true, false, 31);
            AddEquipmentCell(EquipmentSlot.RingR, 283, 239, null, true, false, 31);
            AddEquipmentCell(EquipmentSlot.Flower, 244, 278, null, true, false, 81);
            AddEquipmentCell(EquipmentSlot.Poison, 244, 317, null, true, false, 40);
            AddEquipmentCell(EquipmentSlot.Amulet, 283, 278, new Size(36, 75), true, false, 39, true);
            AddEquipmentCell(EquipmentSlot.Shoes, 10, 278, new Size(36, 75), true, false, 36);

            AddHiddenEquipmentCell(EquipmentSlot.Hook);
            AddHiddenEquipmentCell(EquipmentSlot.Float);
            AddHiddenEquipmentCell(EquipmentSlot.Bait);
            AddHiddenEquipmentCell(EquipmentSlot.Finder);
            AddHiddenEquipmentCell(EquipmentSlot.Reel);
        }

        private void AddEquipmentCell(EquipmentSlot slot, int x, int y, Size? size = null, bool border = true, bool hidden = false, int emptyIndex = 0, bool drawAfter = false)
        {
            DXItemCell cell = new DXItemCell
            {
                Parent = CharacterTab,
                Location = new Point(x, y),
                ItemGrid = GameScene.Game?.CurrentHero?.EquipmentArray,
                Slot = (int)slot,
                GridType = GridType.HeroEquipment,
                Border = border,
                FixedBorder = border,
                Hidden = hidden
            };

            if (size != null)
                cell.Size = size.Value;

            if (emptyIndex > 0)
                cell.BeforeDraw += (o, e) => DrawEquipmentSlot((DXItemCell)o, emptyIndex);

            if (drawAfter)
                cell.AfterDraw += (o, e) => DrawItemEffect((DXItemCell)o);

            cell.MouseEnter += EquipmentCell_MouseEnter;
            cell.MouseLeave += EquipmentCell_MouseLeave;

            EquipmentGrid[(int)slot] = cell;
        }

        private void AddHiddenEquipmentCell(EquipmentSlot slot)
        {
            EquipmentGrid[(int)slot] = new DXItemCell
            {
                Parent = CharacterTab,
                Location = new Point(-1000, -1000),
                ItemGrid = GameScene.Game?.CurrentHero?.EquipmentArray,
                Slot = (int)slot,
                GridType = GridType.HeroEquipment,
                Visible = false,
                Hidden = true
            };
        }

        private void CreateStatsPanel()
        {
            CreateLabel(CharacterTab, "属性", 336, 159, 150, true);

            int x = 336, y = 184, row = 22;
            AddStatLabel(Stat.MaxDC, "破坏", x, y);
            AddStatLabel(Stat.MaxMC, "自然", x, y += row);
            AddStatLabel(Stat.MaxSC, "灵魂", x, y += row);
            AddStatLabel(Stat.MaxAC, "防御", x, y += row);
            AddStatLabel(Stat.MaxMR, "魔防", x, y += row);
            AddStatLabel(Stat.Health, "生命", x, y += row);
            AddStatLabel(Stat.Mana, "魔法", x, y += row);
            AddStatLabel(Stat.Accuracy, "准确", x, y += row);
            AddStatLabel(Stat.Agility, "敏捷", x, y += row);

            WearWeightLabel = CreateLabel(CharacterTab, "穿戴: -", 336, 398, 150);
            HandWeightLabel = CreateLabel(CharacterTab, "腕力: -", 336, 420, 150);
        }

        private void AddStatLabel(Stat stat, string title, int x, int y)
        {
            CreateLabel(CharacterTab, $"{title}:", x, y, 58);

            DisplayStats[stat] = CreateLabel(CharacterTab, "0", x + 62, y, 90);
            DisplayStats[stat].DrawFormat = TextFormatFlags.VerticalCenter | TextFormatFlags.Right;
        }

        private void CreateInventoryTab()
        {
            InventoryGrid = new DXItemGrid
            {
                Parent = InventoryTab,
                GridSize = new Size(6, 7),
                GridType = GridType.HeroInventory,
                Location = new Point(20, 18),
                GridPadding = 1,
                BackColour = Color.Empty,
                Border = false,
            };

            CreateLabel(InventoryTab, "英雄背包", 278, 18, 160, true);
            CreateLabel(InventoryTab, $"容量: {Globals.HeroInventorySize}", 278, 44, 210);

            SortButton = CreateButtonControl(InventoryTab, "整理", 278, 76, 70);
            SortButton.MouseClick += (o, e) =>
            {
                if (GameScene.Game.Observer || !IsHeroItemPanelWritable) return;
                CEnvir.Enqueue(new C.ItemSort { Grid = GridType.HeroInventory, HeroIndex = GameScene.Game?.CurrentHeroIndex ?? 0 });
            };

            AutoPotLabel = CreateLabel(InventoryTab, "HP自动: 未设置\r\nMP自动: 未设置", 278, 116, 210, true);
            AutoPotLabel.Size = new Size(210, 40);

            HPItemCell = CreatePotionCell(GridType.HeroHPItem, 278, 166, "HP");
            MPItemCell = CreatePotionCell(GridType.HeroMPItem, 318, 166, "MP");

            AutoPotCheckBox = new DXCheckBox
            {
                Parent = InventoryTab,
                Location = new Point(366, 171),
                Label = { Text = "启用" },
            };
            AutoPotCheckBox.CheckedChanged += (o, e) =>
            {
                if (!Updating) SendAutoPot();
            };

            CreateLabel(InventoryTab, "HP%", 278, 216, 40);
            HPPercentBox = new DXNumberBox
            {
                Parent = InventoryTab,
                Location = new Point(322, 214),
                MinValue = 1,
                MaxValue = 100,
                Change = 5,
                Value = 50
            };

            CreateLabel(InventoryTab, "MP%", 278, 252, 40);
            MPPercentBox = new DXNumberBox
            {
                Parent = InventoryTab,
                Location = new Point(322, 250),
                MinValue = 1,
                MaxValue = 100,
                Change = 5,
                Value = 50
            };

            SetHPButton = CreateButtonControl(InventoryTab, "设HP", 278, 292, 70);
            SetHPButton.MouseClick += (o, e) => SetPotion(true);

            SetMPButton = CreateButtonControl(InventoryTab, "设MP", 356, 292, 70);
            SetMPButton.MouseClick += (o, e) => SetPotion(false);

            SaveAutoPotButton = CreateButtonControl(InventoryTab, "保存", 278, 328, 70);
            SaveAutoPotButton.MouseClick += (o, e) => SendAutoPot();
        }

        private void CreateDisciplineTab()
        {
            DXImageControl disciplineLevel = new DXImageControl
            {
                Parent = DisciplineTab,
                Index = 215,
                LibraryFile = LibraryFile.Interface,
                Size = new Size(256, 192)
            };
            disciplineLevel.Location = new Point((DisciplineTab.Size.Width - disciplineLevel.Size.Width) / 2, 42);

            CreateLabel(DisciplineTab, "修仙等级:", 34, 260, 88);
            DisciplineLevelLabel = CreateLabel(DisciplineTab, "未开放", 126, 260, 100);

            CreateLabel(DisciplineTab, "修仙经验:", 34, 286, 88);
            DisciplineExperienceLabel = CreateLabel(DisciplineTab, "0/0", 126, 286, 180);

            DisciplineMessageLabel = CreateLabel(DisciplineTab, "当前英雄数据没有独立修仙信息，暂不显示玩家本体修仙内容。", 34, 330, 430);
            DisciplineMessageLabel.Size = new Size(430, 40);
            DisciplineMessageLabel.DrawFormat = TextFormatFlags.WordBreak | TextFormatFlags.Left;
        }

        private void CreateSkillTab()
        {
            SkillScrollBar = new DXVScrollBar
            {
                Parent = SkillTab,
                Location = new Point(SkillTab.Size.Width - 30, 5),
                Size = new Size(20, SkillTab.Size.Height - 10),
                BackColour = Color.Empty,
                Border = false,
                UpButton = { Index = 61, LibraryFile = LibraryFile.Interface },
                DownButton = { Index = 62, LibraryFile = LibraryFile.Interface },
                PositionBar = { Index = 60, LibraryFile = LibraryFile.Interface },
                ShowBackgroundSlider = true,
            };
            SkillScrollBar.ValueChanged += (o, e) => UpdateSkillLocations();
            SkillTab.MouseWheel += SkillScrollBar.DoMouseWheel;

            SkillLabel = CreateLabel(SkillTab, "英雄技能", 14, 10, 240, true);

            EmptySkillLabel = CreateLabel(SkillTab, "当前英雄没有可显示的技能。", 14, 42, 300);
        }

        private DXLabel CreateLabel(DXControl parent, string text, int x, int y, int width, bool bold = false)
        {
            return new DXLabel
            {
                Parent = parent,
                Text = text,
                AutoSize = false,
                Size = new Size(width, 18),
                Location = new Point(x, y),
                ForeColour = Color.White,
                Outline = true,
                OutlineColour = Color.Black,
                Font = new Font(Config.FontName, CEnvir.FontSize(9F), bold ? FontStyle.Bold : FontStyle.Regular),
                IsControl = false
            };
        }

        private DXButton CreateButtonControl(DXControl parent, string text, int x, int y, int width)
        {
            return new DXButton
            {
                Parent = parent,
                Location = new Point(x, y),
                Size = new Size(width, DefaultHeight),
                Label = { Text = text }
            };
        }

        private DXItemCell CreatePotionCell(GridType gridType, int x, int y, string text)
        {
            DXItemCell cell = new DXItemCell
            {
                Parent = InventoryTab,
                Location = new Point(x, y),
                GridType = gridType,
                AllowLink = true,
                FixedBorder = true,
                Border = true,
            };

            DXLabel label = new DXLabel
            {
                Parent = cell,
                Text = text,
                Font = new Font(Config.FontName, CEnvir.FontSize(7F), FontStyle.Bold),
                IsControl = false,
                Location = new Point(1, 0),
                ForeColour = Color.White,
                Outline = true,
                OutlineColour = Color.Black,
            };

            if (gridType == GridType.HeroHPItem)
                HPItemLabel = label;
            else
                MPItemLabel = label;

            return cell;
        }

        private void OpenCreateWindow()
        {
            DXInputWindow window = new DXInputWindow("请输入英雄名字", "创建英雄");
            window.ConfirmButton.Enabled = false;
            window.ValueTextBox.TextBox.TextChanged += (o, e) =>
            {
                string name = window.ValueTextBox.TextBox.Text.Trim();
                bool valid = Globals.CharacterReg.IsMatch(name);

                window.ConfirmButton.Enabled = valid;
                window.ValueTextBox.BorderColour = string.IsNullOrEmpty(name) ? Color.FromArgb(198, 166, 99) : valid ? Color.Green : Color.Red;
            };

            window.ConfirmButton.MouseClick += (o, e) =>
            {
                string name = window.Value?.Trim();
                if (string.IsNullOrEmpty(name) || !Globals.CharacterReg.IsMatch(name)) return;

                CEnvir.Enqueue(new C.NewHero
                {
                    Name = name,
                    Class = CreateClass,
                    Gender = CreateGender
                });
            };
        }

        private void OpenRenameWindow()
        {
            ClientHeroInformation summary = SelectedSummary;
            if (summary == null) return;

            DXInputWindow window = new DXInputWindow("请输入新的英雄名字", "重命名英雄");
            window.ValueTextBox.TextBox.Text = summary.Name ?? string.Empty;
            window.ConfirmButton.Enabled = Globals.CharacterReg.IsMatch(window.ValueTextBox.TextBox.Text.Trim());
            window.ValueTextBox.TextBox.TextChanged += (o, e) =>
            {
                string name = window.ValueTextBox.TextBox.Text.Trim();
                bool valid = Globals.CharacterReg.IsMatch(name);

                window.ConfirmButton.Enabled = valid;
                window.ValueTextBox.BorderColour = string.IsNullOrEmpty(name) ? Color.FromArgb(198, 166, 99) : valid ? Color.Green : Color.Red;
            };

            window.ConfirmButton.MouseClick += (o, e) =>
            {
                string name = window.Value?.Trim();
                if (string.IsNullOrEmpty(name) || !Globals.CharacterReg.IsMatch(name)) return;

                CEnvir.Enqueue(new C.RenameHero { Index = summary.Index, Name = name });
            };
        }

        private void ConfirmDeleteHero()
        {
            ClientHeroInformation summary = SelectedSummary;
            if (summary == null) return;

            DXMessageBox box = new DXMessageBox($"确定删除英雄 {summary.Name}？\r\n请先清空英雄背包和装备。", "删除英雄", DXMessageBoxButtons.YesNo);
            box.YesButton.MouseClick += (o, e) => CEnvir.Enqueue(new C.DeleteHero { Index = summary.Index });
        }

        private void MoveSelectedHero(int direction)
        {
            List<ClientHeroInformation> heroes = GameScene.Game?.Heroes;
            if (heroes == null || heroes.Count <= 1) return;

            int index = heroes.FindIndex(x => x.Index == SelectedHeroIndex);
            if (index < 0) return;

            int target = Math.Max(0, Math.Min(heroes.Count - 1, index + direction));
            if (target == index) return;

            CEnvir.Enqueue(new C.MoveHeroSlot { Index = SelectedHeroIndex, Slot = target });
        }

        private void SetSelectedCurrent()
        {
            ClientHeroInformation summary = SelectedSummary;
            if (summary == null || summary.Index == GameScene.Game?.CurrentHeroIndex) return;

            CEnvir.Enqueue(new C.ChangeHero { Index = summary.Index });
        }

        private void SetOrCycleCustomBehaviour()
        {
            if (!IsViewingCurrentHero) return;

            if ((GameScene.Game?.HeroBehaviour ?? HeroBehaviour.Attack) != HeroBehaviour.Custom)
            {
                CEnvir.Enqueue(new C.SetHeroBehaviour { Behaviour = HeroBehaviour.Custom });
                return;
            }

            CEnvir.Enqueue(new C.SetHeroCustomBehaviourProfile
            {
                Profile = NextCustomProfile(GameScene.Game.HeroCustomBehaviourProfile)
            });
        }

        private void PromoteHeroGrade(MouseButtons button)
        {
            if (!IsViewingCurrentHero || GameScene.Game?.CurrentHero == null) return;
            if (button != MouseButtons.Left && button != MouseButtons.Right) return;

            if (GameScene.Game?.SupportsHeroGradePromotePreview == true)
            {
                CEnvir.Enqueue(new C.ZZHeroGradePromoteRequest
                {
                    RequestConfirmation = button == MouseButtons.Right,
                    HeroIndex = GameScene.Game.CurrentHero.Index,
                });
                return;
            }

            if (button != MouseButtons.Right)
            {
                CEnvir.Enqueue(new C.Chat { Text = "@PROMOTEHEROGRADE preview" });
                return;
            }

            ClientUserHero hero = GameScene.Game.CurrentHero;
            int confirmHeroIndex = hero.Index;
            byte confirmGrade = hero.Grade;
            CEnvir.Enqueue(new C.Chat { Text = "@PROMOTEHEROGRADE preview" });

            DXMessageBox box = new DXMessageBox(
                $"{hero.Name}当前品质：{HeroGradeName(hero.Grade)}(+{HeroGradeBonusRate(hero)}%)\r\n" +
                $"基础属性加成：+{HeroGradeBonusRate(hero)}% -> +{HeroNextGradeBonusRate(hero)}%\r\n\r\n" +
                $"活动：{FormatHeroGradePromoteEvent(hero.HeroGradePromoteEventCostRate, hero.HeroGradePromoteEventSuccessBonus, hero.HeroGradePromoteEventEnd, false)}\r\n\r\n" +
                "服务端已把本次预计消耗发送到聊天窗口。\r\n确认后会重新校验资源、成功率和保护材料，并按服务端当前配置扣费。",
                "确认品质培养",
                DXMessageBoxButtons.YesNo);
            box.YesButton.MouseClick += (o, e) =>
            {
                if (!IsCurrentHero(confirmHeroIndex, confirmGrade))
                {
                    DXMessageBox.Show("当前英雄或品质已经变化，请重新预览后再确认培养。", "品质培养");
                    return;
                }

                CEnvir.Enqueue(new C.Chat { Text = "@PROMOTEHEROGRADE" });
            };
        }

        public void ShowHeroGradePromotePreview(ClientHeroGradePromotePreview preview, bool confirmRequested, int heroIndex)
        {
            if (preview == null) return;

            if (!string.IsNullOrEmpty(preview.ErrorText))
            {
                DXMessageBox.Show(preview.ErrorText, "品质培养");
                return;
            }

            string text =
                $"{preview.HeroName}：{HeroGradeName(preview.CurrentGrade)} -> {HeroGradeName(preview.NextGrade)}\r\n" +
                $"基础属性加成: +{preview.CurrentGradeBonusRate}% -> +{preview.NextGradeBonusRate}%\r\n" +
                $"活动: {FormatHeroGradePromoteEvent(preview.CostRate, preview.EventSuccessBonus, preview.EventEnd, false)}\r\n" +
                $"成功率: {FormatPreviewSuccessRate(preview)}\r\n" +
                $"{preview.GrowthText}\r\n\r\n" +
                $"金币: {FormatPreviewCost(preview.GoldCost, preview.GoldAvailable)}\r\n" +
                $"材料: {FormatPreviewNamedCost(preview.MaterialName, preview.MaterialCount, preview.MaterialAvailable)}\r\n" +
                $"货币: {FormatPreviewNamedCost(preview.CurrencyName, preview.CurrencyAmount, preview.CurrencyAvailable)}\r\n" +
                $"保护: {FormatPreviewProtection(preview)}";

            if (!string.IsNullOrEmpty(preview.WarningText))
                text += $"\r\n\r\n{preview.WarningText}";

            if (confirmRequested && heroIndex <= 0)
                text += "\r\n\r\n本次预览缺少英雄索引，请重新打开英雄页后再尝试。";

            DXMessageBox box = new DXMessageBox(text, confirmRequested ? "确认品质培养" : "品质培养预览", confirmRequested ? DXMessageBoxButtons.YesNo : DXMessageBoxButtons.OK);
            if (confirmRequested)
            {
                box.YesButton.Enabled = preview.CanPromote && heroIndex > 0;
                box.YesButton.MouseClick += (o, e) =>
                {
                    if (GameScene.Game?.SupportsHeroGradePromotePreview != true) return;
                    if (!IsCurrentHero(heroIndex, preview.CurrentGrade))
                    {
                        DXMessageBox.Show("当前英雄或品质已经变化，请重新预览后再确认培养。", "品质培养");
                        return;
                    }

                    CEnvir.Enqueue(new C.ZZHeroGradePromoteRequest
                    {
                        Confirm = true,
                        HeroIndex = heroIndex,
                    });
                };
            }
        }

        private static bool IsCurrentHero(int heroIndex, byte grade)
        {
            ClientUserHero hero = GameScene.Game?.CurrentHero;

            return hero != null && hero.Index == heroIndex && hero.Grade == grade;
        }

        public void HighlightGradeLabel()
        {
            GradeHighlightUntil = CEnvir.Now.AddSeconds(3);
            UpdateLabels();
        }

        private static HeroCustomBehaviourProfile NextCustomProfile(HeroCustomBehaviourProfile profile)
        {
            return profile switch
            {
                HeroCustomBehaviourProfile.AttackCompatible => HeroCustomBehaviourProfile.GuardOwner,
                HeroCustomBehaviourProfile.GuardOwner => HeroCustomBehaviourProfile.FocusOwnerTarget,
                _ => HeroCustomBehaviourProfile.AttackCompatible,
            };
        }

        private void SelectRelative(int direction)
        {
            if (GameScene.Game.Heroes == null || GameScene.Game.Heroes.Count == 0) return;

            int current = Math.Max(0, GameScene.Game.Heroes.FindIndex(x => x.Index == SelectedHeroIndex));
            current = (current + direction + GameScene.Game.Heroes.Count) % GameScene.Game.Heroes.Count;

            ClientHeroInformation hero = GameScene.Game.Heroes[current];
            SelectHero(hero.Index);
        }

        public void SelectHero(int index, bool requestDetails = true)
        {
            if (SelectedHeroIndex == index && (!requestDetails || DisplayedHero != null))
            {
                UpdateLabels();
                return;
            }

            SelectedHeroIndex = index;

            if (requestDetails)
                RequestSelectedHeroDetails();

            HeroChanged();
        }

        public void SetInspectedHero(ClientUserHero hero)
        {
            if (hero == null) return;

            RequestedHeroIndexes.Remove(hero.Index);

            if (!SupportsMultiInspectCache && hero.Index != (GameScene.Game?.CurrentHeroIndex ?? 0)) return;

            if (hero.Index == (GameScene.Game?.CurrentHeroIndex ?? 0))
            {
                InspectedHeroes.Remove(hero.Index);

                if (hero.Index == SelectedHeroIndex)
                    HeroChanged();

                return;
            }

            InspectedHeroes[hero.Index] = hero;

            if (hero.Index == SelectedHeroIndex)
                HeroChanged();
        }

        private void RequestSelectedHeroDetails()
        {
            if (SelectedHeroIndex <= 0) return;
            if (!IsViewingCurrentHero && !SupportsMultiInspectCache) return;
            if (DisplayedHero != null) return;
            if (RequestedHeroIndexes.Contains(SelectedHeroIndex)) return;

            RequestedHeroIndexes.Add(SelectedHeroIndex);
            CEnvir.Enqueue(new C.InspectHero { Index = SelectedHeroIndex });
        }

        public void HeroChanged()
        {
            ClientUserHero currentHero = GameScene.Game?.CurrentHero;
            var heroes = GameScene.Game?.Heroes;

            if (heroes != null && heroes.Count > 0)
            {
                int currentHeroIndex = GameScene.Game?.CurrentHeroIndex ?? 0;
                HashSet<int> activeHeroIndexes = new HashSet<int>(heroes.Select(x => x.Index));
                bool supportsMultiInspectCache = SupportsMultiInspectCache;

                foreach (int index in InspectedHeroes.Keys.ToList())
                {
                    if (!supportsMultiInspectCache || !activeHeroIndexes.Contains(index) || index == currentHeroIndex)
                        InspectedHeroes.Remove(index);
                }

                foreach (int index in RequestedHeroIndexes.ToList())
                {
                    if (!supportsMultiInspectCache || !activeHeroIndexes.Contains(index) || index == currentHeroIndex)
                        RequestedHeroIndexes.Remove(index);
                }

                if (SelectedHeroIndex == 0 || heroes.All(x => x.Index != SelectedHeroIndex))
                {
                    if (currentHeroIndex > 0 && heroes.Any(x => x.Index == currentHeroIndex))
                        SelectedHeroIndex = currentHeroIndex;
                    else
                        SelectedHeroIndex = heroes[0].Index;
                }
                else if (currentHero != null && SelectedHeroIndex == currentHero.Index)
                    InspectedHeroes.Remove(SelectedHeroIndex);
            }
            else
            {
                SelectedHeroIndex = 0;
                InspectedHeroes.Clear();
                RequestedHeroIndexes.Clear();
            }

            if (heroes != null)
            {
                foreach (ClientHeroInformation summary in heroes)
                {
                    if (!InspectedHeroes.TryGetValue(summary.Index, out ClientUserHero inspectedHero)) continue;

                    inspectedHero.Name = summary.Name;
                    inspectedHero.Class = summary.Class;
                    inspectedHero.Gender = summary.Gender;
                    inspectedHero.Level = summary.Level;
                }
            }

            ClientUserHero hero = DisplayedHero;
            RequestSelectedHeroDetails();

            if (IsViewingCurrentHero && currentHero != null)
                SelectedHeroIndex = currentHero.Index;
            else if (SelectedHeroIndex == 0 && currentHero != null)
                SelectedHeroIndex = currentHero.Index;

            if (DXItemCell.SelectedCell?.GridType == GridType.HeroInventory || DXItemCell.SelectedCell?.GridType == GridType.HeroEquipment)
                DXItemCell.SelectedCell = null;

            InventoryGrid.ItemGrid = hero?.InventoryArray;
            bool canEdit = hero != null && IsHeroItemPanelWritable;

            foreach (DXItemCell cell in InventoryGrid.Grid)
            {
                cell.Locked = false;
                cell.ReadOnly = !canEdit;
                cell.Selected = false;
            }

            for (int i = 0; i < EquipmentGrid.Length; i++)
            {
                if (EquipmentGrid[i] == null) continue;

                EquipmentGrid[i].ItemGrid = hero?.EquipmentArray;
                EquipmentGrid[i].Locked = false;
                EquipmentGrid[i].ReadOnly = !canEdit;
                EquipmentGrid[i].Selected = false;
            }

            Updating = true;
            HPItemCell.QuickInfo = PotionInfo(hero?.HPItemIndex ?? 0);
            MPItemCell.QuickInfo = PotionInfo(hero?.MPItemIndex ?? 0);
            AutoPotCheckBox.Checked = hero?.AutoPot ?? false;
            HPPercentBox.Value = Math.Max(1, (int)(hero?.AutoHPPercent ?? 50));
            MPPercentBox.Value = Math.Max(1, (int)(hero?.AutoMPPercent ?? 50));
            Updating = false;

            UpdateSkills();
            UpdateLabels();
        }

        public void UpdateMagic(MagicInfo info)
        {
            if (info == null || !IsViewingCurrentHero) return;

            ClientUserHero hero = DisplayedHero;
            ClientUserMagic magic = hero?.Magics.FirstOrDefault(x => x.Info == info);

            foreach (HeroMagicCell cell in SkillCells)
            {
                if (cell.Info != info) continue;

                cell.Magic = magic;
                cell.Refresh();
                return;
            }
        }

        public void UpdateLabels()
        {
            ClientUserHero hero = DisplayedHero;
            ClientHeroInformation summary = SelectedSummary;

            bool selectedIsCurrent = IsViewingCurrentHero;
            string name = hero?.Name ?? summary?.Name ?? "未选择英雄";
            if (summary != null)
                name += selectedIsCurrent ? " (当前)" : " (查看)";

            NameLabel.Text = name;
            ClassLabel.Text = hero != null ? $"职业: {ClassName(hero.Class)} {GenderName(hero.Gender)}" :
                              summary != null ? $"职业: {ClassName(summary.Class)} {GenderName(summary.Gender)}" : "职业: -";
            LevelLabel.Text = hero != null ? $"等级: {hero.Level}" :
                              summary != null ? $"等级: {summary.Level}" : "等级: -";
            GradeLabel.Text = hero == null ? "品质: -" : $"品质: {HeroGradeName(hero.Grade)}(+{HeroGradeBonusRate(hero)}%)";
            GradeLabel.ForeColour = hero == null ? Color.White : CEnvir.Now < GradeHighlightUntil ? Color.Gold : HeroGradeColour(hero.Grade);
            HealthLabel.Text = hero == null ? "生命: -" : $"生命: {hero.CurrentHP:#,##0}/{hero.MaxHP:#,##0}";
            ManaLabel.Text = hero == null ? "魔法: -" : $"魔法: {hero.CurrentMP:#,##0}/{hero.MaxMP:#,##0}";
            PromoteCompensationLabel.Text = hero == null ? "补偿: -" : $"补偿: {Math.Max(0, hero.HeroGradePromoteFailureCount)}次";
            PromoteEventLabel.Text = hero == null ? "活动: 无" : $"活动: {FormatHeroGradePromoteEvent(hero.HeroGradePromoteEventCostRate, hero.HeroGradePromoteEventSuccessBonus, hero.HeroGradePromoteEventEnd, true)}";
            PromoteEventLabel.Hint = hero == null ? string.Empty : FormatHeroGradePromoteEvent(hero.HeroGradePromoteEventCostRate, hero.HeroGradePromoteEventSuccessBonus, hero.HeroGradePromoteEventEnd, false);
            ExperienceLabel.Text = hero == null || hero.MaxExperience <= 0 ? "经验: -" : $"经验: {hero.Experience:#,##0}/{hero.MaxExperience:#,##0}";

            long bagWeight = hero?.InventoryArray?.Where(x => x != null).Sum(x => (long)x.Weight) ?? 0;
            int maxBagWeight = hero?.Stats == null ? 0 : hero.Stats[Stat.BagWeight];
            WeightLabel.Text = hero == null ? "负重: -" : $"负重: {bagWeight:#,##0}/{maxBagWeight:#,##0}";
            WeightLabel.ForeColour = hero != null && bagWeight > maxBagWeight ? Color.Red : Color.White;

            int wearWeight = HeroWearWeight(hero);
            int handWeight = HeroHandWeight(hero);
            int maxWearWeight = hero?.Stats == null ? 0 : hero.Stats[Stat.WearWeight];
            int maxHandWeight = hero?.Stats == null ? 0 : hero.Stats[Stat.HandWeight];
            WearWeightLabel.Text = hero == null ? "穿戴: -" : $"穿戴: {wearWeight}/{maxWearWeight}";
            HandWeightLabel.Text = hero == null ? "腕力: -" : $"腕力: {handWeight}/{maxHandWeight}";
            WearWeightLabel.ForeColour = hero != null && wearWeight > maxWearWeight ? Color.Red : Color.White;
            HandWeightLabel.ForeColour = hero != null && handWeight > maxHandWeight ? Color.Red : Color.White;

            HeroSpawnState spawnState = GameScene.Game?.HeroSpawnState ?? HeroSpawnState.None;
            StateLabel.Text = selectedIsCurrent ? $"状态: {StateName(spawnState)}" : "状态: 非当前";
            HeroBehaviour behaviour = GameScene.Game?.HeroBehaviour ?? HeroBehaviour.Attack;
            BehaviourLabel.Text = selectedIsCurrent ? $"模式: {BehaviourName(behaviour, GameScene.Game?.HeroCustomBehaviourProfileName)}" : "模式: -";
            AutoPotLabel.Text = hero == null ? "HP自动: 未设置\r\nMP自动: 未设置" : $"HP自动: {PotionText(hero, hero.HPItemIndex)}\r\nMP自动: {PotionText(hero, hero.MPItemIndex)}";

            int heroCount = GameScene.Game?.Heroes?.Count ?? 0;
            int maximumHeroCount = Math.Max(0, GameScene.Game?.MaximumHeroCount ?? 0);
            CreateCountLabel.Text = maximumHeroCount <= 0 ? $"数量: {heroCount}/关闭" : $"数量: {heroCount}/{maximumHeroCount}";

            bool hasSelectedHero = summary != null || hero != null;
            bool hasFullHero = hero != null;
            bool canEditHero = hasFullHero && IsHeroItemPanelWritable;
            bool canCreateHero = maximumHeroCount > 0 && heroCount < maximumHeroCount;
            bool canSelectHero = heroCount > 1 || heroCount == 1 && !hasSelectedHero;
            int selectedPosition = GameScene.Game?.Heroes?.FindIndex(x => x.Index == SelectedHeroIndex) ?? -1;
            ManageLabel.Text = selectedPosition < 0 ? "管理: -" : $"管理: 第 {selectedPosition + 1}/{heroCount} 位";
            PreviousButton.Enabled = canSelectHero;
            NextButton.Enabled = canSelectHero;
            CurrentButton.Enabled = hasSelectedHero && !selectedIsCurrent && !GameScene.Game.Observer;
            RenameButton.Enabled = hasSelectedHero && !GameScene.Game.Observer;
            DeleteButton.Enabled = hasSelectedHero && !GameScene.Game.Observer;
            MoveUpButton.Enabled = hasSelectedHero && selectedPosition > 0 && !GameScene.Game.Observer;
            MoveDownButton.Enabled = hasSelectedHero && selectedPosition >= 0 && selectedPosition < heroCount - 1 && !GameScene.Game.Observer;
            ClassButton.Enabled = canCreateHero;
            GenderButton.Enabled = canCreateHero;
            CreateButton.Enabled = canCreateHero;
            SpawnButton.Label.Text = selectedIsCurrent ? SpawnButtonText(spawnState) : "召唤";
            SpawnButton.Enabled = canEditHero;
            AttackButton.Enabled = canEditHero;
            CounterButton.Enabled = canEditHero;
            FollowButton.Enabled = canEditHero;
            CustomButton.Enabled = canEditHero;
            PromoteButton.Enabled = canEditHero;
            SortButton.Enabled = canEditHero;
            HPItemCell.Enabled = canEditHero;
            MPItemCell.Enabled = canEditHero;
            AutoPotCheckBox.Enabled = canEditHero;
            HPPercentBox.Enabled = canEditHero;
            MPPercentBox.Enabled = canEditHero;
            SetHPButton.Enabled = canEditHero;
            SetMPButton.Enabled = canEditHero;
            SaveAutoPotButton.Enabled = canEditHero;
            UpdateBehaviourButtonColours(selectedIsCurrent ? behaviour : (HeroBehaviour?)null);
            UpdateStats(hero);
            UpdateDiscipline(hero);
        }

        public void UpdateCreateInfo()
        {
            CreateInfoLabel.Text = $"创建: {ClassName(CreateClass)} {GenderName(CreateGender)}";
        }

        private void UpdateBehaviourButtonColours(HeroBehaviour? behaviour)
        {
            SetBehaviourButtonColour(AttackButton, behaviour == HeroBehaviour.Attack);
            SetBehaviourButtonColour(CounterButton, behaviour == HeroBehaviour.CounterAttack);
            SetBehaviourButtonColour(FollowButton, behaviour == HeroBehaviour.Follow);
            SetBehaviourButtonColour(CustomButton, behaviour == HeroBehaviour.Custom);
        }

        private static void SetBehaviourButtonColour(DXButton button, bool active)
        {
            if (button?.Label == null) return;

            button.Label.ForeColour = active ? Constants.ActiveTabColour : Constants.InactiveTabColour;
        }

        private void UpdateStats(ClientUserHero hero)
        {
            foreach (KeyValuePair<Stat, DXLabel> pair in DisplayStats)
                pair.Value.Text = hero?.Stats?.GetFormat(pair.Key) ?? "0";
        }

        private void UpdateDiscipline(ClientUserHero hero)
        {
            foreach (HeroDisciplineMagicIcon cell in DisciplineMagicCells)
                cell.Dispose();

            DisciplineMagicCells.Clear();

            DisciplineLevelLabel.Text = hero == null ? "-" : "未开放";
            DisciplineExperienceLabel.Text = "0/0";
            DisciplineMessageLabel.Text = hero == null
                ? "当前没有选中的英雄。"
                : "当前英雄数据没有独立修仙信息，暂不显示玩家本体修仙内容。";

            if (hero == null) return;

            List<MagicInfo> magicInfos = Globals.MagicInfoList.Binding
                .Where(x => x.School == MagicSchool.Discipline && x.Class == hero.Class)
                .OrderBy(x => x.NeedLevel1)
                .Take(4)
                .ToList();

            int x = 90;
            foreach (MagicInfo info in magicInfos)
            {
                HeroDisciplineMagicIcon cell = new HeroDisciplineMagicIcon
                {
                    Parent = DisciplineTab,
                    Info = info,
                    Learned = hero.Magics.Any(magic => magic.Info == info),
                    HeroLevel = hero.Level,
                    Location = new Point(x, 386)
                };

                DisciplineMagicCells.Add(cell);
                x += 62;
            }
        }

        private void UpdateSkills()
        {
            foreach (HeroMagicCell cell in SkillCells)
                cell.Dispose();

            SkillCells.Clear();
            SkillScrollControls.Clear();

            ClientUserHero hero = DisplayedHero;
            List<MagicInfo> magicInfos = new();

            if (hero != null)
            {
                magicInfos = Globals.MagicInfoList.Binding
                    .Where(x => x.Class == hero.Class && x.School != MagicSchool.None && x.School != MagicSchool.Discipline)
                    .OrderBy(x => x.NeedLevel1)
                    .ThenBy(x => x.School)
                    .ThenBy(x => x.Name)
                    .ToList();

                foreach (ClientUserMagic magic in hero.Magics.Where(x => x?.Info != null && x.Info.School != MagicSchool.None && !magicInfos.Contains(x.Info)))
                    magicInfos.Add(magic.Info);
            }

            EmptySkillLabel.Visible = magicInfos.Count == 0;

            foreach (MagicInfo info in magicInfos)
            {
                ClientUserMagic magic = hero?.Magics.FirstOrDefault(x => x.Info == info);

                HeroMagicCell cell = new HeroMagicCell
                {
                    Parent = SkillTab,
                    Info = info,
                    Magic = magic,
                    HeroLevel = hero?.Level ?? 0,
                    BackColour = Color.Empty
                };
                cell.MouseWheel += SkillScrollBar.DoMouseWheel;
                SkillCells.Add(cell);
                SkillScrollControls.Add(cell);
            }

            SkillScrollBar.Value = 0;
            UpdateSkillLocations();
        }

        private void UpdateSkillLocations()
        {
            int y = 42 - SkillScrollBar.Value;

            foreach (DXControl control in SkillScrollControls)
            {
                control.Location = new Point(14, y);
                y += control.Size.Height + 5;
            }

            int contentHeight = Math.Max(0, y - 42 + SkillScrollBar.Value + 8);
            SkillScrollBar.MaxValue = contentHeight;
            SkillScrollBar.VisibleSize = Math.Max(0, SkillTab.Size.Height - 44);
            SkillScrollBar.Visible = contentHeight > SkillScrollBar.VisibleSize;
        }

        private void SetPotion(bool hp)
        {
            if (!IsHeroItemPanelWritable) return;

            ClientUserItem item = InventoryGrid.Grid.FirstOrDefault(x => x.Selected)?.Item;

            if (item == null || item.Info.ItemType != ItemType.Consumable || item.Info.Shape != 0 || !item.Info.CanAutoPot)
            {
                DXMessageBox.Show(CEnvir.Language.HeroAutoPotionNeedItem, CEnvir.Language.HeroAutoPotionTitle);
                return;
            }

            Stat stat = hp ? Stat.Health : Stat.Mana;
            if (item.Info.Stats[stat] <= 0)
            {
                DXMessageBox.Show(hp ? CEnvir.Language.HeroAutoPotionNeedHealth : CEnvir.Language.HeroAutoPotionNeedMana, CEnvir.Language.HeroAutoPotionTitle);
                return;
            }

            if (hp)
                HPItemCell.QuickInfo = item.Info;
            else
                MPItemCell.QuickInfo = item.Info;

            SendAutoPot();
        }

        public void UpdatePotionFromCell(DXItemCell cell)
        {
            if (Updating) return;
            if (cell?.GridType != GridType.HeroHPItem && cell?.GridType != GridType.HeroMPItem) return;

            SendAutoPot();
        }

        public void SendAutoPot()
        {
            if (!IsHeroItemPanelWritable) return;

            ClientUserHero hero = DisplayedHero;
            if (hero == null) return;

            hero.AutoPot = AutoPotCheckBox.Checked;
            hero.HPItemIndex = HPItemCell.QuickInfo?.Index ?? 0;
            hero.MPItemIndex = MPItemCell.QuickInfo?.Index ?? 0;
            hero.AutoHPPercent = (byte)Math.Max(1, Math.Min(100, HPPercentBox.Value));
            hero.AutoMPPercent = (byte)Math.Max(1, Math.Min(100, MPPercentBox.Value));

            CEnvir.Enqueue(new C.SetHeroAutoPot
            {
                AutoPot = hero.AutoPot,
                HPItemIndex = hero.HPItemIndex,
                MPItemIndex = hero.MPItemIndex,
                AutoHPPercent = hero.AutoHPPercent,
                AutoMPPercent = hero.AutoMPPercent
            });

            HPItemCell.RefreshItem();
            MPItemCell.RefreshItem();
            UpdateLabels();
        }

        private void EquipmentCell_MouseLeave(object sender, EventArgs e)
        {
            foreach (DXLabel label in DisplayStats.Values)
                label.ForeColour = Color.White;

            WearWeightLabel.ForeColour = Color.White;
            HandWeightLabel.ForeColour = Color.White;
        }

        private void EquipmentCell_MouseEnter(object sender, EventArgs e)
        {
            if (sender is not DXItemCell cell || cell.Item == null) return;

            foreach (KeyValuePair<Stat, DXLabel> pair in DisplayStats)
                pair.Value.ForeColour = HasStat(cell.Item, pair.Key) ? Color.Orange : Color.White;

            WearWeightLabel.ForeColour = cell.Item.Weight > 0 && cell.Item.Info.ItemType != ItemType.Weapon && cell.Item.Info.ItemType != ItemType.Torch ? Color.Orange : Color.White;
            HandWeightLabel.ForeColour = cell.Item.Weight > 0 && (cell.Item.Info.ItemType == ItemType.Weapon || cell.Item.Info.ItemType == ItemType.Torch) ? Color.Orange : Color.White;
        }

        private static bool HasStat(ClientUserItem item, Stat stat)
        {
            return item.Info.Stats[stat] != 0 || item.AddedStats[stat] != 0;
        }

        private void CharacterTab_BeforeChildrenDraw(object sender, EventArgs e)
        {
            if (GradeHighlightUntil != DateTime.MinValue && CEnvir.Now >= GradeHighlightUntil)
            {
                GradeHighlightUntil = DateTime.MinValue;
                UpdateLabels();
            }

            ClientUserHero hero = DisplayedHero;
            if (hero == null) return;

            PromoteEventLabel.Text = $"活动: {FormatHeroGradePromoteEvent(hero.HeroGradePromoteEventCostRate, hero.HeroGradePromoteEventSuccessBonus, hero.HeroGradePromoteEventEnd, true)}";
            PromoteEventLabel.Hint = FormatHeroGradePromoteEvent(hero.HeroGradePromoteEventCostRate, hero.HeroGradePromoteEventSuccessBonus, hero.HeroGradePromoteEventEnd, false);

            int x = 130;
            int y = 313;

            ClientUserItem weapon = EquipmentGrid[(int)EquipmentSlot.Weapon]?.Item;
            ClientUserItem armour = EquipmentGrid[(int)EquipmentSlot.Armour]?.Item;
            ClientUserItem helmet = EquipmentGrid[(int)EquipmentSlot.Helmet]?.Item;
            ClientUserItem shield = EquipmentGrid[(int)EquipmentSlot.Shield]?.Item;
            ClientUserItem costume = EquipmentGrid[(int)EquipmentSlot.Costume]?.Item;

            if (armour != null && costume == null)
            {
                MirImage image = EquipEffectDecider.GetEffectImageOrNull(armour, hero.Gender);
                if (image != null)
                {
                    bool oldBlend = RenderingPipelineManager.IsBlending();
                    float oldRate = RenderingPipelineManager.GetBlendRate();

                    RenderingPipelineManager.SetBlend(true, 0.8F);
                    PresentTexture(image.Image, CharacterTab, new Rectangle(DisplayArea.X + x + image.OffSetX, DisplayArea.Y + y + image.OffSetY, image.Width, image.Height), ForeColour, this);
                    RenderingPipelineManager.SetBlend(oldBlend, oldRate);
                }
            }

            if (!CEnvir.LibraryList.TryGetValue(LibraryFile.ProgUse, out MirLibrary library)) return;

            bool hideBody = PlayerObject.CostumeShapeHideBody.Contains(costume?.Info.Shape ?? -1);
            bool hideHead = costume?.Info != null;

            if (!hideBody)
            {
                int index = hero.Gender == MirGender.Male ? 0 : 1;
                library.Draw(index, DisplayArea.X + x, DisplayArea.Y + y, Color.White, true, 1F, ImageType.Image);
            }

            if (CEnvir.LibraryList.TryGetValue(LibraryFile.Equip, out library))
            {
                if (costume != null)
                {
                    int costumeIndex = costume.Info.Image;
                    library.Draw(costumeIndex, DisplayArea.X + x, DisplayArea.Y + y, Color.White, true, 1F, ImageType.Image);
                }
                else if (armour != null)
                {
                    int armourIndex = armour.Info.Image;
                    library.Draw(armourIndex, DisplayArea.X + x, DisplayArea.Y + y, Color.White, true, 1F, ImageType.Image);
                    library.Draw(armourIndex, DisplayArea.X + x, DisplayArea.Y + y, armour.Colour, true, 1F, ImageType.Overlay);
                }

                if (!hideBody)
                {
                    if (weapon != null)
                    {
                        int weaponIndex = weapon.Info.Image;
                        library.Draw(weaponIndex, DisplayArea.X + x, DisplayArea.Y + y, Color.White, true, 1F, ImageType.Image);
                        library.Draw(weaponIndex, DisplayArea.X + x, DisplayArea.Y + y, weapon.Colour, true, 1F, ImageType.Overlay);

                        MirImage image = EquipEffectDecider.GetEffectImageOrNull(weapon, hero.Gender);
                        if (image != null)
                        {
                            bool oldBlend = RenderingPipelineManager.IsBlending();
                            float oldRate = RenderingPipelineManager.GetBlendRate();

                            RenderingPipelineManager.SetBlend(true, 0.8F);
                            PresentTexture(image.Image, CharacterTab, new Rectangle(DisplayArea.X + x + image.OffSetX, DisplayArea.Y + y + image.OffSetY, image.Width, image.Height), ForeColour, this);
                            RenderingPipelineManager.SetBlend(oldBlend, oldRate);
                        }
                    }

                    if (shield != null)
                    {
                        int shieldIndex = shield.Info.Image;
                        library.Draw(shieldIndex, DisplayArea.X + x, DisplayArea.Y + y, Color.White, true, 1F, ImageType.Image);
                        library.Draw(shieldIndex, DisplayArea.X + x, DisplayArea.Y + y, shield.Colour, true, 1F, ImageType.Overlay);

                        MirImage image = EquipEffectDecider.GetEffectImageOrNull(shield, hero.Gender);
                        if (image != null)
                        {
                            bool oldBlend = RenderingPipelineManager.IsBlending();
                            float oldRate = RenderingPipelineManager.GetBlendRate();

                            RenderingPipelineManager.SetBlend(true, 0.8F);
                            PresentTexture(image.Image, CharacterTab, new Rectangle(DisplayArea.X + x + image.OffSetX, DisplayArea.Y + y + image.OffSetY, image.Width, image.Height), ForeColour, this);
                            RenderingPipelineManager.SetBlend(oldBlend, oldRate);
                        }
                    }
                }
            }

            if (hideHead) return;

            if (helmet != null && library != null)
            {
                int index = helmet.Info.Image;
                library.Draw(index, DisplayArea.X + x, DisplayArea.Y + y, Color.White, true, 1F, ImageType.Image);
                library.Draw(index, DisplayArea.X + x, DisplayArea.Y + y, helmet.Colour, true, 1F, ImageType.Overlay);
            }
            else if (hero.HairType > 0)
            {
                library = CEnvir.LibraryList[LibraryFile.ProgUse];

                switch (hero.Class)
                {
                    case MirClass.Warrior:
                    case MirClass.Wizard:
                    case MirClass.Taoist:
                        switch (hero.Gender)
                        {
                            case MirGender.Male:
                                library.Draw(60 + hero.HairType - 1, DisplayArea.X + x, DisplayArea.Y + y, hero.HairColour, true, 1F, ImageType.Image);
                                break;
                            case MirGender.Female:
                                library.Draw(80 + hero.HairType - 1, DisplayArea.X + x, DisplayArea.Y + y, hero.HairColour, true, 1F, ImageType.Image);
                                break;
                        }
                        break;
                    case MirClass.Assassin:
                        switch (hero.Gender)
                        {
                            case MirGender.Male:
                                library.Draw(1100 + hero.HairType - 1, DisplayArea.X + x, DisplayArea.Y + y, hero.HairColour, true, 1F, ImageType.Image);
                                break;
                            case MirGender.Female:
                                library.Draw(1120 + hero.HairType - 1, DisplayArea.X + x, DisplayArea.Y + y, hero.HairColour, true, 1F, ImageType.Image);
                                break;
                        }
                        break;
                }
            }
        }

        private void DrawEquipmentSlot(DXItemCell cell, int index)
        {
            if (InterfaceLibrary == null || cell.Item != null) return;

            Size s = InterfaceLibrary.GetSize(index);
            int x = (cell.Size.Width - s.Width) / 2 + cell.DisplayArea.X;
            int y = (cell.Size.Height - s.Height) / 2 + cell.DisplayArea.Y;

            InterfaceLibrary.Draw(index, x, y, Color.White, false, 0.2F, ImageType.Image);
        }

        private void DrawItemEffect(DXItemCell cell)
        {
            if (cell.Item == null) return;

            MirImage image = ItemEffectDecider.GetItemEffectImageOrNull(cell.Item.Info.ItemType, cell.Item.Info.Shape, out int x, out int y);
            if (image == null) return;

            bool oldBlend = RenderingPipelineManager.IsBlending();
            float oldRate = RenderingPipelineManager.GetBlendRate();

            RenderingPipelineManager.SetBlend(true, 0.8F);
            PresentTexture(image.Image, CharacterTab, new Rectangle(cell.DisplayArea.X + image.OffSetX + x, cell.DisplayArea.Y + image.OffSetY + y, image.Width, image.Height), ForeColour, this);
            RenderingPipelineManager.SetBlend(oldBlend, oldRate);
        }

        private static int HeroWearWeight(ClientUserHero hero)
        {
            if (hero?.EquipmentArray == null) return 0;

            return hero.EquipmentArray
                .Where(x => x != null && x.Info.ItemType != ItemType.Weapon && x.Info.ItemType != ItemType.Torch && x.Info.ItemType != ItemType.Shield)
                .Sum(x => x.Weight);
        }

        private static int HeroHandWeight(ClientUserHero hero)
        {
            if (hero?.EquipmentArray == null) return 0;

            return hero.EquipmentArray
                .Where(x => x != null && (x.Info.ItemType == ItemType.Weapon || x.Info.ItemType == ItemType.Torch || x.Info.ItemType == ItemType.Shield))
                .Sum(x => x.Weight);
        }

        private static string ClassName(MirClass value)
        {
            return value switch
            {
                MirClass.Warrior => "战士",
                MirClass.Wizard => "法师",
                MirClass.Taoist => "道士",
                MirClass.Assassin => "刺客",
                _ => value.ToString()
            };
        }

        private static string GenderName(MirGender value)
        {
            return value == MirGender.Female ? "女" : "男";
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

        private static Color HeroGradeColour(byte value)
        {
            return value switch
            {
                1 => Color.LightGreen,
                2 => Color.DeepSkyBlue,
                3 => Color.Violet,
                4 => Color.Gold,
                _ => Color.White
            };
        }

        private static int HeroGradeBonusRate(ClientUserHero hero)
        {
            if (hero == null) return 0;

            return Math.Max(0, (int)hero.Grade) * Math.Max(0, hero.GradeStatBonusRate);
        }

        private static int HeroNextGradeBonusRate(ClientUserHero hero)
        {
            if (hero == null) return 0;

            return Math.Max(0, (int)hero.Grade + 1) * Math.Max(0, hero.GradeStatBonusRate);
        }

        private static string FormatPreviewCost(long required, long available)
        {
            return required <= 0 ? "无" : $"{required:#,##0}({FormatPreviewAvailability(required, available)})";
        }

        private static string FormatPreviewNamedCost(string name, long required, long available)
        {
            return required <= 0 ? "无" : $"{name} x{required:#,##0}({FormatPreviewAvailability(required, available)})";
        }

        private static string FormatPreviewAvailability(long required, long available)
        {
            return available >= required ? "足够" : $"不足 {required - available:#,##0}";
        }

        private static string FormatPreviewSuccessRate(ClientHeroGradePromotePreview preview)
        {
            List<string> parts = new List<string> { $"基础 {preview.BaseSuccessRate}%" };

            if (preview.EventSuccessBonus > 0)
                parts.Add($"活动 +{preview.EventSuccessBonus}%");

            if (preview.FailureBonus > 0)
                parts.Add($"失败补偿 +{preview.FailureBonus}%/{preview.FailureBonusMax}%({preview.FailureCount}次)");

            return parts.Count <= 1
                ? $"{preview.SuccessRate}%"
                : $"{preview.SuccessRate}%({string.Join("，", parts)})";
        }

        private static string FormatHeroGradePromoteEvent(int costRate, int successBonus, DateTime eventEnd, bool compact)
        {
            bool active = costRate != 100 || successBonus > 0;
            if (!active) return "无";

            string text = compact ? $"{costRate}%/+{successBonus}%" : $"消耗 {costRate}%，成功 +{successBonus}%";

            if (eventEnd == DateTime.MinValue)
                return text;

            TimeSpan remaining = eventEnd - CEnvir.Now;
            if (remaining <= TimeSpan.Zero)
                return compact ? $"{text} 已到期" : $"{text}，已到期";

            return compact
                ? $"{text} {FormatHeroGradePromoteDuration(remaining)}"
                : $"{text}，剩余 {FormatHeroGradePromoteDuration(remaining)}";
        }

        private static string FormatHeroGradePromoteDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
                return $"{Math.Ceiling(duration.TotalDays)}天";

            if (duration.TotalHours >= 1)
                return $"{Math.Ceiling(duration.TotalHours)}小时";

            if (duration.TotalMinutes >= 1)
                return $"{Math.Ceiling(duration.TotalMinutes)}分钟";

            return $"{Math.Ceiling(Math.Max(0, duration.TotalSeconds))}秒";
        }

        private static string FormatPreviewProtection(ClientHeroGradePromotePreview preview)
        {
            if (preview.ProtectionCount <= 0 || string.IsNullOrEmpty(preview.ProtectionName))
                return "未配置";

            string state;
            if (!preview.UseProtection)
                state = "不足，本次不启用保护";
            else if (preview.ProtectionConsumeOnFailureOnly && preview.SuccessRate >= 100)
                state = "成功率已满，本次不会消耗保护";
            else if (preview.ProtectionConsumeOnFailureOnly)
                state = "失败时才消耗并保底成功";
            else
                state = "确认后立即消耗并保底成功";

            return $"{preview.ProtectionName} x{preview.ProtectionCount:#,##0}，可用 {preview.ProtectionAvailable:#,##0}，{state}";
        }

        private static string StateName(HeroSpawnState value)
        {
            return value switch
            {
                HeroSpawnState.None => "无英雄",
                HeroSpawnState.Unsummoned => "未召唤",
                HeroSpawnState.Summoned => "已召唤",
                HeroSpawnState.Dead => "死亡",
                _ => value.ToString()
            };
        }

        private static string SpawnButtonText(HeroSpawnState value)
        {
            return value switch
            {
                HeroSpawnState.Summoned => "召回",
                HeroSpawnState.Dead => "召回",
                HeroSpawnState.None => "无英雄",
                _ => "召唤"
            };
        }

        private static string BehaviourName(HeroBehaviour value, string customProfileName)
        {
            return value switch
            {
                HeroBehaviour.Attack => "主动",
                HeroBehaviour.CounterAttack => "反击",
                HeroBehaviour.Follow => "跟随",
                HeroBehaviour.Custom => string.IsNullOrWhiteSpace(customProfileName) ? "自定义" : $"自定义({customProfileName})",
                _ => value.ToString()
            };
        }

        private static ItemInfo PotionInfo(int itemIndex)
        {
            if (itemIndex <= 0) return null;

            return Globals.ItemInfoList.Binding.FirstOrDefault(x => x.Index == itemIndex);
        }

        private static string PotionText(ClientUserHero hero, int itemIndex)
        {
            if (itemIndex <= 0) return "未设置";

            long count = hero.InventoryArray.Where(x => x?.Info.Index == itemIndex).Sum(x => x.Count);
            if (count > 0)
            {
                ClientUserItem item = hero.InventoryArray.First(x => x?.Info.Index == itemIndex);
                return count <= 5 ? $"{item.Info.ItemName} x{count}(低)" : $"{item.Info.ItemName} x{count}";
            }

            ItemInfo info = PotionInfo(itemIndex);
            return info == null ? "缺失" : $"{info.ItemName}(缺)";
        }

        private static int MagicBorderIndex(MagicSchool school)
        {
            return school switch
            {
                MagicSchool.Passive => 860,
                MagicSchool.Active => 861,
                MagicSchool.Toggle => 862,
                MagicSchool.Fire => 870,
                MagicSchool.Ice => 871,
                MagicSchool.Lightning => 872,
                MagicSchool.Wind => 873,
                MagicSchool.Phantom => 874,
                MagicSchool.Holy => 880,
                MagicSchool.Dark => 881,
                MagicSchool.Physical => 883,
                MagicSchool.Atrocity => 890,
                MagicSchool.Kill => 891,
                MagicSchool.Assassination => 893,
                MagicSchool.Discipline => 881,
                _ => 860
            };
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing) return;

            TabControl = null;
            CharacterTab = null;
            InventoryTab = null;
            DisciplineTab = null;
            SkillTab = null;
            SkillScrollBar = null;

            NameLabel = null;
            ClassLabel = null;
            LevelLabel = null;
            GradeLabel = null;
            HealthLabel = null;
            ManaLabel = null;
            ExperienceLabel = null;
            WeightLabel = null;
            StateLabel = null;
            BehaviourLabel = null;
            CreateInfoLabel = null;
            CreateCountLabel = null;
            SkillLabel = null;
            AutoPotLabel = null;
            HPItemLabel = null;
            MPItemLabel = null;
            PromoteCompensationLabel = null;
            PromoteEventLabel = null;
            WearWeightLabel = null;
            HandWeightLabel = null;
            ManageLabel = null;
            DisciplineLevelLabel = null;
            DisciplineExperienceLabel = null;
            DisciplineMessageLabel = null;
            EmptySkillLabel = null;

            PreviousButton = null;
            NextButton = null;
            CurrentButton = null;
            RenameButton = null;
            DeleteButton = null;
            MoveUpButton = null;
            MoveDownButton = null;
            CreateButton = null;
            ClassButton = null;
            GenderButton = null;
            SpawnButton = null;
            AttackButton = null;
            CounterButton = null;
            FollowButton = null;
            CustomButton = null;
            PromoteButton = null;
            SetHPButton = null;
            SetMPButton = null;
            SaveAutoPotButton = null;
            SortButton = null;
            AutoPotCheckBox = null;
            HPPercentBox = null;
            MPPercentBox = null;
            InventoryGrid = null;
            HPItemCell = null;
            MPItemCell = null;
            EquipmentGrid = null;
            InspectedHeroes.Clear();
            RequestedHeroIndexes.Clear();
            SkillCells.Clear();
            SkillScrollControls.Clear();
            DisciplineMagicCells.Clear();
        }

        private sealed class HeroDisciplineMagicIcon : DXControl
        {
            private MagicInfo _Info;
            private bool _Learned;
            private int _HeroLevel;

            public MagicInfo Info
            {
                get => _Info;
                set
                {
                    if (_Info == value) return;

                    _Info = value;
                    Refresh();
                }
            }

            public bool Learned
            {
                get => _Learned;
                set
                {
                    if (_Learned == value) return;

                    _Learned = value;
                    Refresh();
                }
            }

            public int HeroLevel
            {
                get => _HeroLevel;
                set
                {
                    if (_HeroLevel == value) return;

                    _HeroLevel = value;
                    Refresh();
                }
            }

            private DXImageControl Image;

            public HeroDisciplineMagicIcon()
            {
                Size = new Size(36, 36);
                DrawTexture = true;

                Image = new DXImageControl
                {
                    Parent = this,
                    LibraryFile = LibraryFile.MagicIcon,
                    Location = new Point(0, 0),
                    IsControl = false
                };
            }

            public void Refresh()
            {
                if (Info == null || Image == null) return;

                Image.Index = Info.Icon;
                Image.GrayScale = !Learned;
                Image.ImageOpacity = Learned || HeroLevel >= Info.NeedLevel1 ? 1F : 0.35F;
            }

            public override void OnMouseEnter()
            {
                GameScene.Game.MouseMagic = Info;
            }

            public override void OnMouseLeave()
            {
                GameScene.Game.MouseMagic = null;
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);

                if (!disposing) return;

                Info = null;
                Image = null;
            }
        }

        private sealed class HeroMagicCell : DXControl
        {
            private MagicInfo _Info;
            private ClientUserMagic _Magic;
            private int _HeroLevel;

            public MagicInfo Info
            {
                get => _Info;
                set
                {
                    if (_Info == value) return;

                    _Info = value;
                    Refresh();
                }
            }

            public ClientUserMagic Magic
            {
                get => _Magic;
                set
                {
                    if (_Magic == value) return;

                    _Magic = value;
                    Refresh();
                }
            }

            public int HeroLevel
            {
                get => _HeroLevel;
                set
                {
                    if (_HeroLevel == value) return;

                    _HeroLevel = value;
                    Refresh();
                }
            }

            private DXImageControl Background, Image, Level4Border, ExperienceBar;
            private DXLabel NameLabel, LevelLabel, ExperienceLabel;

            public HeroMagicCell()
            {
                Size = new Size(369, 54);
                DrawTexture = true;

                Background = new DXImageControl
                {
                    Parent = this,
                    Index = 165,
                    LibraryFile = LibraryFile.Interface,
                    Location = new Point(0, 0),
                    IsControl = false
                };

                Level4Border = new DXImageControl
                {
                    Parent = this,
                    LibraryFile = LibraryFile.GameInter2,
                    Location = new Point(4, 4),
                    Visible = false
                };

                Image = new DXImageControl
                {
                    Parent = this,
                    LibraryFile = LibraryFile.MagicIcon,
                    Location = new Point(9, 9),
                    IsControl = false
                };

                ExperienceBar = new DXImageControl
                {
                    Parent = this,
                    LibraryFile = LibraryFile.GameInter2,
                    Location = new Point(110, 36),
                    IsControl = false
                };
                ExperienceBar.Size = ExperienceBar.Library.GetSize(812);
                ExperienceBar.AfterDraw += ExperienceBar_AfterDraw;

                NameLabel = new DXLabel
                {
                    Parent = this,
                    Location = new Point(55, 1),
                    ForeColour = Color.White,
                    IsControl = false,
                };

                LevelLabel = new DXLabel
                {
                    Parent = this,
                    Location = new Point(57, 30),
                    IsControl = false,
                    ForeColour = Color.FromArgb(198, 166, 99),
                };

                ExperienceLabel = new DXLabel
                {
                    Parent = this,
                    IsControl = false,
                    ForeColour = Color.FromArgb(198, 166, 99),
                };
                ExperienceLabel.SizeChanged += (o, e) => ExperienceLabel.Location = new Point(Size.Width - ExperienceLabel.Size.Width - 6, 17);
            }

            private void ExperienceBar_AfterDraw(object sender, EventArgs e)
            {
                if (Magic == null || Info == null) return;

                MirImage image = ExperienceBar.Library.CreateImage(812, ImageType.Image);
                if (image == null) return;

                int x = (ExperienceBar.Size.Width - image.Width) / 2;
                int y = (ExperienceBar.Size.Height - image.Height) / 2;

                float percent = 1F;

                if (Magic.Level < Globals.MagicMaxLevel)
                {
                    decimal required = MagicExperienceRequirement(Magic);
                    if (required <= 0) return;

                    percent = (float)Math.Min(1, Math.Max(0, Magic.Experience / required));
                }

                if (percent == 0) return;

                PresentTexture(image.Image, this, new Rectangle(ExperienceBar.DisplayArea.X + x, ExperienceBar.DisplayArea.Y + y, (int)(image.Width * percent), image.Height), Color.White, ExperienceBar);
            }

            public void Refresh()
            {
                if (Info == null) return;

                Image.Index = Info.Icon;
                NameLabel.Text = Info.Name;

                bool learned = Magic != null;
                float opacity = learned ? 1F : HeroLevel >= Info.NeedLevel1 ? 0.55F : 0.3F;

                Background.ImageOpacity = opacity;
                Image.ImageOpacity = opacity;
                Level4Border.ImageOpacity = opacity;
                NameLabel.Opacity = opacity;
                LevelLabel.Opacity = opacity;
                ExperienceLabel.Opacity = opacity;

                if (!learned)
                {
                    LevelLabel.Text = "未学习";
                    ExperienceLabel.Text = $"需要等级: {Info.NeedLevel1}";
                    ExperienceLabel.ForeColour = HeroLevel >= Info.NeedLevel1 ? Color.FromArgb(198, 166, 99) : Color.Red;
                }
                else if (Magic.Level < Globals.MagicMaxLevel)
                {
                    decimal required = MagicExperienceRequirement(Magic);
                    ExperienceLabel.Text = required <= 0 ? "经验: -" : $"经验: {Magic.Experience}/{required}";
                    ExperienceLabel.ForeColour = Color.FromArgb(198, 166, 99);
                    LevelLabel.Text = $"等级: {Magic.Level}";
                }
                else
                {
                    LevelLabel.Text = $"等级: {Magic.Level}";
                    ExperienceLabel.Text = "经验: 已满级";
                    ExperienceLabel.ForeColour = Color.FromArgb(198, 166, 99);
                }

                Level4Border.Visible = true;
                Level4Border.Index = MagicBorderIndex(Info.School);
            }

            private static decimal MagicExperienceRequirement(ClientUserMagic magic)
            {
                return magic.Level switch
                {
                    0 => magic.Info.Experience1,
                    1 => magic.Info.Experience2,
                    2 => magic.Info.Experience3,
                    _ => (magic.Level - 2) * 500
                };
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);

                if (!disposing) return;

                Magic = null;
                Background = null;
                Image = null;
                Level4Border = null;
                ExperienceBar = null;
                NameLabel = null;
                LevelLabel = null;
                ExperienceLabel = null;
            }
        }
    }
}
