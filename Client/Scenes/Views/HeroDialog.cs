using Client.Controls;
using Client.Envir;
using Client.UserModels;
using Library;
using Library.SystemModels;
using System;
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

        public DXLabel NameLabel, ClassLabel, LevelLabel, HealthLabel, ManaLabel, ExperienceLabel, WeightLabel, StateLabel, BehaviourLabel, CreateInfoLabel, CreateCountLabel, SkillLabel, AutoPotLabel, HPItemLabel, MPItemLabel;
        public DXButton PreviousButton, NextButton, CreateButton, ClassButton, GenderButton, SpawnButton, AttackButton, CounterButton, FollowButton, CustomButton, SetHPButton, SetMPButton, SaveAutoPotButton;
        public DXCheckBox AutoPotCheckBox;
        public DXNumberBox HPPercentBox, MPPercentBox;
        public DXItemGrid InventoryGrid;
        public DXItemCell HPItemCell, MPItemCell;
        public DXItemCell[] EquipmentGrid;

        public int SelectedHeroIndex;
        public MirClass CreateClass = MirClass.Warrior;
        public MirGender CreateGender = MirGender.Male;
        public bool Updating;

        private static readonly MirClass[] ClassCycle = { MirClass.Warrior, MirClass.Wizard, MirClass.Taoist, MirClass.Assassin };

        public HeroDialog()
        {
            TitleLabel.Text = "英雄";
            HasFooter = true;
            SetClientSize(new Size(520, 470));

            NameLabel = CreateLabel("未选择英雄", 12, 8, 250, true);
            ClassLabel = CreateLabel("职业: -", 12, 30, 120);
            LevelLabel = CreateLabel("等级: -", 140, 30, 120);
            HealthLabel = CreateLabel("生命: -", 12, 52, 180);
            ManaLabel = CreateLabel("魔法: -", 210, 52, 180);
            ExperienceLabel = CreateLabel("经验: -", 12, 74, 260);
            WeightLabel = CreateLabel("负重: -", 285, 74, 145);
            StateLabel = CreateLabel("状态: -", 285, 8, 145);
            BehaviourLabel = CreateLabel("模式: -", 285, 30, 145);

            PreviousButton = CreateButtonControl("上一个", 12, 102, 64);
            PreviousButton.MouseClick += (o, e) => SelectRelative(-1);

            NextButton = CreateButtonControl("下一个", 82, 102, 64);
            NextButton.MouseClick += (o, e) => SelectRelative(1);

            SpawnButton = CreateButtonControl("召唤/召回", 152, 102, 86);
            SpawnButton.MouseClick += (o, e) => CEnvir.Enqueue(new C.ToggleHeroSpawn());

            AttackButton = CreateButtonControl("主动", 244, 102, 52);
            AttackButton.MouseClick += (o, e) => CEnvir.Enqueue(new C.SetHeroBehaviour { Behaviour = HeroBehaviour.Attack });

            CounterButton = CreateButtonControl("反击", 302, 102, 52);
            CounterButton.MouseClick += (o, e) => CEnvir.Enqueue(new C.SetHeroBehaviour { Behaviour = HeroBehaviour.CounterAttack });

            FollowButton = CreateButtonControl("跟随", 360, 102, 52);
            FollowButton.MouseClick += (o, e) => CEnvir.Enqueue(new C.SetHeroBehaviour { Behaviour = HeroBehaviour.Follow });

            CustomButton = CreateButtonControl("自定", 418, 102, 52);
            CustomButton.MouseClick += (o, e) => CEnvir.Enqueue(new C.SetHeroBehaviour { Behaviour = HeroBehaviour.Custom });

            CreateInfoLabel = CreateLabel("创建: 战士 男", 12, 135, 160);

            ClassButton = CreateButtonControl("职业", 178, 132, 52);
            ClassButton.MouseClick += (o, e) =>
            {
                int index = Array.IndexOf(ClassCycle, CreateClass);
                CreateClass = ClassCycle[(index + 1) % ClassCycle.Length];
                UpdateCreateInfo();
            };

            GenderButton = CreateButtonControl("性别", 236, 132, 52);
            GenderButton.MouseClick += (o, e) =>
            {
                CreateGender = CreateGender == MirGender.Male ? MirGender.Female : MirGender.Male;
                UpdateCreateInfo();
            };

            CreateButton = CreateButtonControl("新建英雄", 294, 132, 86);
            CreateButton.MouseClick += (o, e) => OpenCreateWindow();

            CreateCountLabel = CreateLabel("数量: 0/0", 390, 135, 120);

            SkillLabel = CreateLabel("技能: -", 12, 152, 420);

            EquipmentGrid = new DXItemCell[Globals.EquipmentSize];
            for (int i = 0; i < EquipmentGrid.Length; i++)
            {
                EquipmentGrid[i] = new DXItemCell
                {
                    Parent = this,
                    Location = new Point(ClientArea.Left + 10 + i % 5 * 39, ClientArea.Top + 168 + i / 5 * 39),
                    FixedBorder = true,
                    Border = true,
                    Slot = i,
                    GridType = GridType.HeroEquipment,
                };
            }

            InventoryGrid = new DXItemGrid
            {
                Parent = this,
                GridSize = new Size(7, 6),
                GridType = GridType.HeroInventory,
                Location = new Point(ClientArea.Left + 226, ClientArea.Top + 168),
                GridPadding = 1,
                BackColour = Color.Empty,
                Border = false,
            };

            AutoPotLabel = CreateLabel("HP药: 未设置  MP药: 未设置", 12, 410, 490, true);

            HPItemCell = CreatePotionCell(GridType.HeroHPItem, 12, 430, "HP");
            MPItemCell = CreatePotionCell(GridType.HeroMPItem, 52, 430, "MP");

            AutoPotCheckBox = new DXCheckBox
            {
                Parent = this,
                Location = new Point(ClientArea.Left + 92, ClientArea.Top + 434),
                Label = { Text = "启用" },
            };
            AutoPotCheckBox.CheckedChanged += (o, e) =>
            {
                if (!Updating) SendAutoPot();
            };

            HPPercentBox = new DXNumberBox
            {
                Parent = this,
                Location = new Point(ClientArea.Left + 145, ClientArea.Top + 432),
                MinValue = 1,
                MaxValue = 100,
                Change = 5,
                Value = 50
            };

            MPPercentBox = new DXNumberBox
            {
                Parent = this,
                Location = new Point(ClientArea.Left + 238, ClientArea.Top + 432),
                MinValue = 1,
                MaxValue = 100,
                Change = 5,
                Value = 50
            };

            SetHPButton = CreateButtonControl("设HP药", 330, 430, 58);
            SetHPButton.MouseClick += (o, e) => SetPotion(true);

            SetMPButton = CreateButtonControl("设MP药", 392, 430, 58);
            SetMPButton.MouseClick += (o, e) => SetPotion(false);

            SaveAutoPotButton = CreateButtonControl("保存", 454, 430, 58);
            SaveAutoPotButton.MouseClick += (o, e) => SendAutoPot();

            UpdateCreateInfo();
            HeroChanged();
        }

        private DXLabel CreateLabel(string text, int x, int y, int width, bool bold = false)
        {
            return new DXLabel
            {
                Parent = this,
                Text = text,
                AutoSize = false,
                Size = new Size(width, 18),
                Location = new Point(ClientArea.Left + x, ClientArea.Top + y),
                ForeColour = Color.White,
                Outline = true,
                OutlineColour = Color.Black,
                Font = new Font(Config.FontName, CEnvir.FontSize(9F), bold ? FontStyle.Bold : FontStyle.Regular),
                IsControl = false
            };
        }

        private DXButton CreateButtonControl(string text, int x, int y, int width)
        {
            return new DXButton
            {
                Parent = this,
                Location = new Point(ClientArea.Left + x, ClientArea.Top + y),
                Size = new Size(width, DefaultHeight),
                Label = { Text = text }
            };
        }

        private DXItemCell CreatePotionCell(GridType gridType, int x, int y, string text)
        {
            DXItemCell cell = new DXItemCell
            {
                Parent = this,
                Location = new Point(ClientArea.Left + x, ClientArea.Top + y),
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
            window.ConfirmButton.MouseClick += (o, e) =>
            {
                string name = window.Value?.Trim();
                if (string.IsNullOrEmpty(name)) return;

                CEnvir.Enqueue(new C.NewHero
                {
                    Name = name,
                    Class = CreateClass,
                    Gender = CreateGender
                });
            };
        }

        private void SelectRelative(int direction)
        {
            if (GameScene.Game.Heroes == null || GameScene.Game.Heroes.Count == 0) return;

            int current = Math.Max(0, GameScene.Game.Heroes.FindIndex(x => x.Index == SelectedHeroIndex));
            current = (current + direction + GameScene.Game.Heroes.Count) % GameScene.Game.Heroes.Count;

            ClientHeroInformation hero = GameScene.Game.Heroes[current];
            SelectedHeroIndex = hero.Index;
            CEnvir.Enqueue(new C.ChangeHero { Index = hero.Index });
            UpdateLabels();
        }

        public void HeroChanged()
        {
            ClientUserHero hero = GameScene.Game?.CurrentHero;
            var heroes = GameScene.Game?.Heroes;

            if (hero != null)
                SelectedHeroIndex = hero.Index;
            else if (heroes != null && heroes.Count > 0)
            {
                int currentHeroIndex = GameScene.Game?.CurrentHeroIndex ?? 0;

                if (currentHeroIndex > 0 && heroes.Any(x => x.Index == currentHeroIndex))
                    SelectedHeroIndex = currentHeroIndex;
                else if (SelectedHeroIndex == 0 || heroes.All(x => x.Index != SelectedHeroIndex))
                    SelectedHeroIndex = heroes[0].Index;
            }
            else
                SelectedHeroIndex = 0;

            if (DXItemCell.SelectedCell?.GridType == GridType.HeroInventory || DXItemCell.SelectedCell?.GridType == GridType.HeroEquipment)
                DXItemCell.SelectedCell = null;

            InventoryGrid.ItemGrid = hero?.InventoryArray;
            foreach (DXItemCell cell in InventoryGrid.Grid)
            {
                cell.Locked = false;
                cell.Selected = false;
            }

            for (int i = 0; i < EquipmentGrid.Length; i++)
            {
                EquipmentGrid[i].ItemGrid = hero?.EquipmentArray;
                EquipmentGrid[i].Locked = false;
                EquipmentGrid[i].Selected = false;
            }

            Updating = true;
            HPItemCell.QuickInfo = PotionInfo(hero?.HPItemIndex ?? 0);
            MPItemCell.QuickInfo = PotionInfo(hero?.MPItemIndex ?? 0);
            AutoPotCheckBox.Checked = hero?.AutoPot ?? false;
            HPPercentBox.Value = Math.Max(1, (int)(hero?.AutoHPPercent ?? 50));
            MPPercentBox.Value = Math.Max(1, (int)(hero?.AutoMPPercent ?? 50));
            Updating = false;

            UpdateLabels();
        }

        public void UpdateLabels()
        {
            ClientUserHero hero = GameScene.Game?.CurrentHero;
            ClientHeroInformation summary = null;

            if (hero == null && SelectedHeroIndex > 0)
                summary = GameScene.Game?.Heroes?.FirstOrDefault(x => x.Index == SelectedHeroIndex);

            NameLabel.Text = hero?.Name ?? summary?.Name ?? "未选择英雄";
            ClassLabel.Text = hero != null ? $"职业: {ClassName(hero.Class)} {GenderName(hero.Gender)}" :
                              summary != null ? $"职业: {ClassName(summary.Class)} {GenderName(summary.Gender)}" : "职业: -";
            LevelLabel.Text = hero != null ? $"等级: {hero.Level}" :
                              summary != null ? $"等级: {summary.Level}" : "等级: -";
            HealthLabel.Text = hero == null ? "生命: -" : $"生命: {hero.CurrentHP:#,##0}/{hero.MaxHP:#,##0}";
            ManaLabel.Text = hero == null ? "魔法: -" : $"魔法: {hero.CurrentMP:#,##0}/{hero.MaxMP:#,##0}";
            ExperienceLabel.Text = hero == null || hero.MaxExperience <= 0 ? "经验: -" : $"经验: {hero.Experience:#,##0}/{hero.MaxExperience:#,##0}";
            long bagWeight = hero?.InventoryArray?.Where(x => x != null).Sum(x => (long)x.Weight) ?? 0;
            int maxBagWeight = hero?.Stats == null ? 0 : hero.Stats[Stat.BagWeight];
            WeightLabel.Text = hero == null ? "负重: -" : $"负重: {bagWeight:#,##0}/{maxBagWeight:#,##0}";
            WeightLabel.ForeColour = hero != null && bagWeight > maxBagWeight ? Color.Red : Color.White;
            HeroSpawnState spawnState = GameScene.Game?.HeroSpawnState ?? HeroSpawnState.None;
            StateLabel.Text = $"状态: {StateName(spawnState)}";
            HeroBehaviour behaviour = GameScene.Game?.HeroBehaviour ?? HeroBehaviour.Attack;
            BehaviourLabel.Text = $"模式: {BehaviourName(behaviour)}";
            SkillLabel.Text = hero == null ? "技能: -" : $"技能: {SkillText(hero)}";
            AutoPotLabel.Text = hero == null ? "HP药: 未设置  MP药: 未设置" : $"HP药: {PotionText(hero, hero.HPItemIndex)}  MP药: {PotionText(hero, hero.MPItemIndex)}";

            int heroCount = GameScene.Game?.Heroes?.Count ?? 0;
            int maximumHeroCount = Math.Max(0, GameScene.Game?.MaximumHeroCount ?? 0);
            CreateCountLabel.Text = maximumHeroCount <= 0 ? $"数量: {heroCount}/关闭" : $"数量: {heroCount}/{maximumHeroCount}";

            bool hasHero = hero != null;
            bool canCreateHero = maximumHeroCount > 0 && heroCount < maximumHeroCount;
            bool canSelectHero = heroCount > 1 || heroCount == 1 && !hasHero;
            PreviousButton.Enabled = canSelectHero;
            NextButton.Enabled = canSelectHero;
            ClassButton.Enabled = canCreateHero;
            GenderButton.Enabled = canCreateHero;
            CreateButton.Enabled = canCreateHero;
            SpawnButton.Label.Text = SpawnButtonText(spawnState);
            SpawnButton.Enabled = hasHero;
            AttackButton.Enabled = hasHero;
            CounterButton.Enabled = hasHero;
            FollowButton.Enabled = hasHero;
            CustomButton.Enabled = hasHero;
            HPItemCell.Enabled = hasHero;
            MPItemCell.Enabled = hasHero;
            AutoPotCheckBox.Enabled = hasHero;
            HPPercentBox.Enabled = hasHero;
            MPPercentBox.Enabled = hasHero;
            SetHPButton.Enabled = hasHero;
            SetMPButton.Enabled = hasHero;
            SaveAutoPotButton.Enabled = hasHero;
            UpdateBehaviourButtonColours(behaviour);
        }

        public void UpdateCreateInfo()
        {
            CreateInfoLabel.Text = $"创建: {ClassName(CreateClass)} {GenderName(CreateGender)}";
        }

        private void UpdateBehaviourButtonColours(HeroBehaviour behaviour)
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

        private void SetPotion(bool hp)
        {
            ClientUserItem item = InventoryGrid.Grid.FirstOrDefault(x => x.Selected)?.Item;

            if (item == null || item.Info.ItemType != ItemType.Consumable || item.Info.Shape != 0 || !item.Info.CanAutoPot)
            {
                DXMessageBox.Show("请先选中英雄背包里的药品。", "英雄自动喝药");
                return;
            }

            Stat stat = hp ? Stat.Health : Stat.Mana;
            if (item.Info.Stats[stat] <= 0)
            {
                DXMessageBox.Show(hp ? "请选择可以恢复生命的药品。" : "请选择可以恢复魔法的药品。", "英雄自动喝药");
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
            ClientUserHero hero = GameScene.Game?.CurrentHero;
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

        private static string BehaviourName(HeroBehaviour value)
        {
            return value switch
            {
                HeroBehaviour.Attack => "主动",
                HeroBehaviour.CounterAttack => "反击",
                HeroBehaviour.Follow => "跟随",
                HeroBehaviour.Custom => "自定义",
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

        private static string SkillText(ClientUserHero hero)
        {
            if (hero.Magics != null && hero.Magics.Count > 0)
                return string.Join(", ", hero.Magics.Where(x => x.Info != null).Select(x => $"{x.Info.Name} Lv{x.Level}").Take(4));

            return hero.Class switch
            {
                MirClass.Warrior => hero.Level >= 25 ? "近战, 烈火" : hero.Level >= 15 ? "强化近战, 烈火(25级)" : "近战, 强化(15级), 烈火(25级)",
                MirClass.Wizard => hero.Level >= 20 ? "雷电" : "火球, 雷电(20级)",
                MirClass.Taoist => "毒符, 治疗",
                MirClass.Assassin => hero.Level >= 20 ? "放血" : "近战, 放血(20级)",
                _ => "基础攻击"
            };
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing) return;

            NameLabel = null;
            ClassLabel = null;
            LevelLabel = null;
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

            PreviousButton = null;
            NextButton = null;
            CreateButton = null;
            ClassButton = null;
            GenderButton = null;
            SpawnButton = null;
            AttackButton = null;
            CounterButton = null;
            FollowButton = null;
            CustomButton = null;
            SetHPButton = null;
            SetMPButton = null;
            SaveAutoPotButton = null;
            AutoPotCheckBox = null;
            HPPercentBox = null;
            MPPercentBox = null;
            InventoryGrid = null;
            HPItemCell = null;
            MPItemCell = null;
            EquipmentGrid = null;
        }
    }
}
