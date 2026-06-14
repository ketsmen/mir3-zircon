namespace Server.Views
{
    partial class MonsterInfoView
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            DevExpress.XtraGrid.GridLevelNode gridLevelNode1 = new DevExpress.XtraGrid.GridLevelNode();
            DevExpress.XtraGrid.GridLevelNode gridLevelNode2 = new DevExpress.XtraGrid.GridLevelNode();
            DevExpress.XtraGrid.GridLevelNode gridLevelNode3 = new DevExpress.XtraGrid.GridLevelNode();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MonsterInfoView));
            MonsterInfoStatsGridView = new DevExpress.XtraGrid.Views.Grid.GridView();
            colStat = new DevExpress.XtraGrid.Columns.GridColumn();
            StatComboBox = new DevExpress.XtraEditors.Repository.RepositoryItemImageComboBox();
            colAmount = new DevExpress.XtraGrid.Columns.GridColumn();
            MonsterInfoGridControl = new DevExpress.XtraGrid.GridControl();
            RespawnsGridView = new DevExpress.XtraGrid.Views.Grid.GridView();
            colRegion = new DevExpress.XtraGrid.Columns.GridColumn();
            RegionLookUpEdit = new DevExpress.XtraEditors.Repository.RepositoryItemLookUpEdit();
            colDelay = new DevExpress.XtraGrid.Columns.GridColumn();
            colCount = new DevExpress.XtraGrid.Columns.GridColumn();
            DropsGridView = new DevExpress.XtraGrid.Views.Grid.GridView();
            ColItem = new DevExpress.XtraGrid.Columns.GridColumn();
            ItemLookUpEdit = new DevExpress.XtraEditors.Repository.RepositoryItemLookUpEdit();
            colChance = new DevExpress.XtraGrid.Columns.GridColumn();
            colDAmount = new DevExpress.XtraGrid.Columns.GridColumn();
            gridColumn1 = new DevExpress.XtraGrid.Columns.GridColumn();
            gridColumn6 = new DevExpress.XtraGrid.Columns.GridColumn();
            gridColumn7 = new DevExpress.XtraGrid.Columns.GridColumn();
            MonsterInfoGridView = new DevExpress.XtraGrid.Views.Grid.GridView();
            colMonsterName = new DevExpress.XtraGrid.Columns.GridColumn();
            colImage = new DevExpress.XtraGrid.Columns.GridColumn();
            MonsterImageComboBox = new DevExpress.XtraEditors.Repository.RepositoryItemImageComboBox();
            colAI = new DevExpress.XtraGrid.Columns.GridColumn();
            colLevel = new DevExpress.XtraGrid.Columns.GridColumn();
            colExperience = new DevExpress.XtraGrid.Columns.GridColumn();
            colViewRange = new DevExpress.XtraGrid.Columns.GridColumn();
            colCoolEye = new DevExpress.XtraGrid.Columns.GridColumn();
            colAttackDelay = new DevExpress.XtraGrid.Columns.GridColumn();
            colMoveDelay = new DevExpress.XtraGrid.Columns.GridColumn();
            colIsBoss = new DevExpress.XtraGrid.Columns.GridColumn();
            colUndead = new DevExpress.XtraGrid.Columns.GridColumn();
            gridColumn3 = new DevExpress.XtraGrid.Columns.GridColumn();
            gridColumn4 = new DevExpress.XtraGrid.Columns.GridColumn();
            gridColumn5 = new DevExpress.XtraGrid.Columns.GridColumn();
            colFaceImage = new DevExpress.XtraGrid.Columns.GridColumn();
            ribbon = new DevExpress.XtraBars.Ribbon.RibbonControl();
            SaveButton = new DevExpress.XtraBars.BarButtonItem();
            ImportButton = new DevExpress.XtraBars.BarButtonItem();
            ExportButton = new DevExpress.XtraBars.BarButtonItem();
            UpdateMonsterImageButton = new DevExpress.XtraBars.BarButtonItem();
            ribbonPage1 = new DevExpress.XtraBars.Ribbon.RibbonPage();
            ribbonPageGroup1 = new DevExpress.XtraBars.Ribbon.RibbonPageGroup();
            JsonImportExport = new DevExpress.XtraBars.Ribbon.RibbonPageGroup();
            ribbonPageGroup2 = new DevExpress.XtraBars.Ribbon.RibbonPageGroup();
            ((System.ComponentModel.ISupportInitialize)MonsterInfoStatsGridView).BeginInit();
            ((System.ComponentModel.ISupportInitialize)StatComboBox).BeginInit();
            ((System.ComponentModel.ISupportInitialize)MonsterInfoGridControl).BeginInit();
            ((System.ComponentModel.ISupportInitialize)RespawnsGridView).BeginInit();
            ((System.ComponentModel.ISupportInitialize)RegionLookUpEdit).BeginInit();
            ((System.ComponentModel.ISupportInitialize)DropsGridView).BeginInit();
            ((System.ComponentModel.ISupportInitialize)ItemLookUpEdit).BeginInit();
            ((System.ComponentModel.ISupportInitialize)MonsterInfoGridView).BeginInit();
            ((System.ComponentModel.ISupportInitialize)MonsterImageComboBox).BeginInit();
            ((System.ComponentModel.ISupportInitialize)ribbon).BeginInit();
            SuspendLayout();
            // 
            // MonsterInfoStatsGridView
            // 
            MonsterInfoStatsGridView.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] { colStat, colAmount });
            MonsterInfoStatsGridView.GridControl = MonsterInfoGridControl;
            MonsterInfoStatsGridView.Name = "MonsterInfoStatsGridView";
            MonsterInfoStatsGridView.OptionsView.EnableAppearanceEvenRow = true;
            MonsterInfoStatsGridView.OptionsView.EnableAppearanceOddRow = true;
            MonsterInfoStatsGridView.OptionsView.NewItemRowPosition = DevExpress.XtraGrid.Views.Grid.NewItemRowPosition.Top;
            MonsterInfoStatsGridView.OptionsView.ShowButtonMode = DevExpress.XtraGrid.Views.Base.ShowButtonModeEnum.ShowAlways;
            MonsterInfoStatsGridView.OptionsView.ShowGroupPanel = false;
            // 
            // colStat
            // 
            colStat.ColumnEdit = StatComboBox;
            colStat.FieldName = "Stat";
            colStat.Caption = "属性";
            colStat.Name = "colStat";
            colStat.Visible = true;
            colStat.VisibleIndex = 0;
            // 
            // StatComboBox
            // 
            StatComboBox.AutoHeight = false;
            StatComboBox.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] { new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo) });
            StatComboBox.Name = "StatComboBox";
            // 
            // colAmount
            // 
            colAmount.FieldName = "Amount";
            colAmount.Caption = "数量";
            colAmount.Name = "colAmount";
            colAmount.Visible = true;
            colAmount.VisibleIndex = 1;
            // 
            // MonsterInfoGridControl
            // 
            MonsterInfoGridControl.Dock = System.Windows.Forms.DockStyle.Fill;
            gridLevelNode1.LevelTemplate = MonsterInfoStatsGridView;
            gridLevelNode1.RelationName = "MonsterInfoStats";
            gridLevelNode2.LevelTemplate = RespawnsGridView;
            gridLevelNode2.RelationName = "Respawns";
            gridLevelNode3.LevelTemplate = DropsGridView;
            gridLevelNode3.RelationName = "Drops";
            MonsterInfoGridControl.LevelTree.Nodes.AddRange(new DevExpress.XtraGrid.GridLevelNode[] { gridLevelNode1, gridLevelNode2, gridLevelNode3 });
            MonsterInfoGridControl.Location = new System.Drawing.Point(0, 144);
            MonsterInfoGridControl.MainView = MonsterInfoGridView;
            MonsterInfoGridControl.MenuManager = ribbon;
            MonsterInfoGridControl.Name = "MonsterInfoGridControl";
            MonsterInfoGridControl.RepositoryItems.AddRange(new DevExpress.XtraEditors.Repository.RepositoryItem[] { MonsterImageComboBox, StatComboBox, ItemLookUpEdit, RegionLookUpEdit });
            MonsterInfoGridControl.ShowOnlyPredefinedDetails = true;
            MonsterInfoGridControl.Size = new System.Drawing.Size(761, 366);
            MonsterInfoGridControl.TabIndex = 2;
            MonsterInfoGridControl.ViewCollection.AddRange(new DevExpress.XtraGrid.Views.Base.BaseView[] { RespawnsGridView, DropsGridView, MonsterInfoGridView, MonsterInfoStatsGridView });
            // 
            // RespawnsGridView
            // 
            RespawnsGridView.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] { colRegion, colDelay, colCount });
            RespawnsGridView.GridControl = MonsterInfoGridControl;
            RespawnsGridView.Name = "RespawnsGridView";
            RespawnsGridView.OptionsView.EnableAppearanceEvenRow = true;
            RespawnsGridView.OptionsView.EnableAppearanceOddRow = true;
            RespawnsGridView.OptionsView.NewItemRowPosition = DevExpress.XtraGrid.Views.Grid.NewItemRowPosition.Top;
            RespawnsGridView.OptionsView.ShowButtonMode = DevExpress.XtraGrid.Views.Base.ShowButtonModeEnum.ShowAlways;
            RespawnsGridView.OptionsView.ShowGroupPanel = false;
            // 
            // colRegion
            // 
            colRegion.ColumnEdit = RegionLookUpEdit;
            colRegion.FieldName = "Region";
            colRegion.Caption = "区域";
            colRegion.Name = "colRegion";
            colRegion.SortMode = DevExpress.XtraGrid.ColumnSortMode.DisplayText;
            colRegion.Visible = true;
            colRegion.VisibleIndex = 0;
            // 
            // RegionLookUpEdit
            // 
            RegionLookUpEdit.AutoHeight = false;
            RegionLookUpEdit.BestFitMode = DevExpress.XtraEditors.Controls.BestFitMode.BestFitResizePopup;
            RegionLookUpEdit.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] { new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo) });
            RegionLookUpEdit.Columns.AddRange(new DevExpress.XtraEditors.Controls.LookUpColumnInfo[] { new DevExpress.XtraEditors.Controls.LookUpColumnInfo("Index", "序号"), new DevExpress.XtraEditors.Controls.LookUpColumnInfo("FileName", "文件名"), new DevExpress.XtraEditors.Controls.LookUpColumnInfo("Description", "描述") });
            RegionLookUpEdit.DisplayMember = "ServerDescription";
            RegionLookUpEdit.Name = "RegionLookUpEdit";
            RegionLookUpEdit.NullText = "[未选择区域]";
            // 
            // colDelay
            // 
            colDelay.FieldName = "Delay";
            colDelay.Caption = "延迟";
            colDelay.Name = "colDelay";
            colDelay.Visible = true;
            colDelay.VisibleIndex = 1;
            // 
            // colCount
            // 
            colCount.FieldName = "Count";
            colCount.Caption = "数量";
            colCount.Name = "colCount";
            colCount.Visible = true;
            colCount.VisibleIndex = 2;
            // 
            // DropsGridView
            // 
            DropsGridView.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] { ColItem, colChance, colDAmount, gridColumn1, gridColumn6, gridColumn7 });
            DropsGridView.GridControl = MonsterInfoGridControl;
            DropsGridView.Name = "DropsGridView";
            DropsGridView.OptionsView.EnableAppearanceEvenRow = true;
            DropsGridView.OptionsView.EnableAppearanceOddRow = true;
            DropsGridView.OptionsView.NewItemRowPosition = DevExpress.XtraGrid.Views.Grid.NewItemRowPosition.Top;
            DropsGridView.OptionsView.ShowButtonMode = DevExpress.XtraGrid.Views.Base.ShowButtonModeEnum.ShowAlways;
            DropsGridView.OptionsView.ShowGroupPanel = false;
            // 
            // ColItem
            // 
            ColItem.ColumnEdit = ItemLookUpEdit;
            ColItem.FieldName = "Item";
            ColItem.Caption = "物品";
            ColItem.Name = "ColItem";
            ColItem.OptionsColumn.AllowSort = DevExpress.Utils.DefaultBoolean.True;
            ColItem.SortMode = DevExpress.XtraGrid.ColumnSortMode.DisplayText;
            ColItem.Visible = true;
            ColItem.VisibleIndex = 0;
            // 
            // ItemLookUpEdit
            // 
            ItemLookUpEdit.AutoHeight = false;
            ItemLookUpEdit.BestFitMode = DevExpress.XtraEditors.Controls.BestFitMode.BestFitResizePopup;
            ItemLookUpEdit.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] { new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo) });
            ItemLookUpEdit.Columns.AddRange(new DevExpress.XtraEditors.Controls.LookUpColumnInfo[] { new DevExpress.XtraEditors.Controls.LookUpColumnInfo("Index", "序号"), new DevExpress.XtraEditors.Controls.LookUpColumnInfo("ItemName", "物品名"), new DevExpress.XtraEditors.Controls.LookUpColumnInfo("ItemType", "物品类型"), new DevExpress.XtraEditors.Controls.LookUpColumnInfo("Price", "价格"), new DevExpress.XtraEditors.Controls.LookUpColumnInfo("StackSize", "堆叠数量"), new DevExpress.XtraEditors.Controls.LookUpColumnInfo("PartCount", "部件数量") });
            ItemLookUpEdit.DisplayMember = "ItemName";
            ItemLookUpEdit.Name = "ItemLookUpEdit";
            ItemLookUpEdit.NullText = "[未选择物品]";
            // 
            // colChance
            // 
            colChance.FieldName = "Chance";
            colChance.Caption = "几率";
            colChance.Name = "colChance";
            colChance.Visible = true;
            colChance.VisibleIndex = 1;
            // 
            // colDAmount
            // 
            colDAmount.FieldName = "Amount";
            colDAmount.Caption = "数量";
            colDAmount.Name = "colDAmount";
            colDAmount.Visible = true;
            colDAmount.VisibleIndex = 2;
            // 
            // gridColumn1
            // 
            gridColumn1.FieldName = "DropSet";
            gridColumn1.Caption = "掉落组";
            gridColumn1.Name = "gridColumn1";
            gridColumn1.Visible = true;
            gridColumn1.VisibleIndex = 3;
            // 
            // gridColumn6
            // 
            gridColumn6.FieldName = "PartOnly";
            gridColumn6.Caption = "仅部件";
            gridColumn6.Name = "gridColumn6";
            gridColumn6.Visible = true;
            gridColumn6.VisibleIndex = 4;
            // 
            // gridColumn7
            // 
            gridColumn7.FieldName = "EasterEvent";
            gridColumn7.Caption = "复活节活动";
            gridColumn7.Name = "gridColumn7";
            gridColumn7.Visible = true;
            gridColumn7.VisibleIndex = 5;
            // 
            // MonsterInfoGridView
            // 
            MonsterInfoGridView.Columns.AddRange(new DevExpress.XtraGrid.Columns.GridColumn[] { colMonsterName, colImage, colAI, colLevel, colExperience, colViewRange, colCoolEye, colAttackDelay, colMoveDelay, colIsBoss, colUndead, gridColumn3, gridColumn4, gridColumn5, colFaceImage });
            MonsterInfoGridView.GridControl = MonsterInfoGridControl;
            MonsterInfoGridView.Name = "MonsterInfoGridView";
            MonsterInfoGridView.OptionsDetail.AllowExpandEmptyDetails = true;
            MonsterInfoGridView.OptionsView.EnableAppearanceEvenRow = true;
            MonsterInfoGridView.OptionsView.EnableAppearanceOddRow = true;
            MonsterInfoGridView.OptionsView.NewItemRowPosition = DevExpress.XtraGrid.Views.Grid.NewItemRowPosition.Top;
            MonsterInfoGridView.OptionsView.ShowButtonMode = DevExpress.XtraGrid.Views.Base.ShowButtonModeEnum.ShowAlways;
            MonsterInfoGridView.OptionsView.ShowGroupPanel = false;
            // 
            // colMonsterName
            // 
            colMonsterName.FieldName = "MonsterName";
            colMonsterName.Caption = "怪物名";
            colMonsterName.Name = "colMonsterName";
            colMonsterName.Visible = true;
            colMonsterName.VisibleIndex = 0;
            // 
            // colImage
            // 
            colImage.ColumnEdit = MonsterImageComboBox;
            colImage.FieldName = "Image";
            colImage.Caption = "图像";
            colImage.Name = "colImage";
            colImage.Visible = true;
            colImage.VisibleIndex = 1;
            // 
            // MonsterImageComboBox
            // 
            MonsterImageComboBox.AutoHeight = false;
            MonsterImageComboBox.Buttons.AddRange(new DevExpress.XtraEditors.Controls.EditorButton[] { new DevExpress.XtraEditors.Controls.EditorButton(DevExpress.XtraEditors.Controls.ButtonPredefines.Combo) });
            MonsterImageComboBox.Name = "MonsterImageComboBox";
            // 
            // colAI
            // 
            colAI.FieldName = "AI";
            colAI.Caption = "AI";
            colAI.Name = "colAI";
            colAI.Visible = true;
            colAI.VisibleIndex = 2;
            // 
            // colLevel
            // 
            colLevel.FieldName = "Level";
            colLevel.Caption = "等级";
            colLevel.Name = "colLevel";
            colLevel.Visible = true;
            colLevel.VisibleIndex = 3;
            // 
            // colExperience
            // 
            colExperience.FieldName = "Experience";
            colExperience.Caption = "经验";
            colExperience.Name = "colExperience";
            colExperience.Visible = true;
            colExperience.VisibleIndex = 4;
            // 
            // colViewRange
            // 
            colViewRange.FieldName = "ViewRange";
            colViewRange.Caption = "视野范围";
            colViewRange.Name = "colViewRange";
            colViewRange.Visible = true;
            colViewRange.VisibleIndex = 5;
            // 
            // colCoolEye
            // 
            colCoolEye.FieldName = "CoolEye";
            colCoolEye.Caption = "反隐";
            colCoolEye.Name = "colCoolEye";
            colCoolEye.Visible = true;
            colCoolEye.VisibleIndex = 6;
            // 
            // colAttackDelay
            // 
            colAttackDelay.FieldName = "AttackDelay";
            colAttackDelay.Caption = "攻击延迟";
            colAttackDelay.Name = "colAttackDelay";
            colAttackDelay.Visible = true;
            colAttackDelay.VisibleIndex = 7;
            // 
            // colMoveDelay
            // 
            colMoveDelay.FieldName = "MoveDelay";
            colMoveDelay.Caption = "移动延迟";
            colMoveDelay.Name = "colMoveDelay";
            colMoveDelay.Visible = true;
            colMoveDelay.VisibleIndex = 8;
            // 
            // colIsBoss
            // 
            colIsBoss.FieldName = "IsBoss";
            colIsBoss.Caption = "Boss";
            colIsBoss.Name = "colIsBoss";
            colIsBoss.Visible = true;
            colIsBoss.VisibleIndex = 9;
            // 
            // colUndead
            // 
            colUndead.FieldName = "Undead";
            colUndead.Caption = "不死系";
            colUndead.Name = "colUndead";
            colUndead.Visible = true;
            colUndead.VisibleIndex = 10;
            // 
            // gridColumn3
            // 
            gridColumn3.FieldName = "CanPush";
            gridColumn3.Caption = "可推动";
            gridColumn3.Name = "gridColumn3";
            gridColumn3.Visible = true;
            gridColumn3.VisibleIndex = 11;
            // 
            // gridColumn4
            // 
            gridColumn4.FieldName = "CanTame";
            gridColumn4.Caption = "可驯服";
            gridColumn4.Name = "gridColumn4";
            gridColumn4.Visible = true;
            gridColumn4.VisibleIndex = 12;
            // 
            // gridColumn5
            // 
            gridColumn5.FieldName = "Flag";
            gridColumn5.Caption = "标记";
            gridColumn5.Name = "gridColumn5";
            gridColumn5.Visible = true;
            gridColumn5.VisibleIndex = 13;
            // 
            // colFaceImage
            // 
            colFaceImage.Caption = "头像图像";
            colFaceImage.FieldName = "FaceImage";
            colFaceImage.Name = "colFaceImage";
            colFaceImage.Visible = true;
            colFaceImage.VisibleIndex = 14;
            // 
            // ribbon
            // 
            ribbon.ExpandCollapseItem.Id = 0;
            ribbon.Items.AddRange(new DevExpress.XtraBars.BarItem[] { ribbon.ExpandCollapseItem, ribbon.SearchEditItem, SaveButton, ImportButton, ExportButton, UpdateMonsterImageButton });
            ribbon.Location = new System.Drawing.Point(0, 0);
            ribbon.MaxItemId = 5;
            ribbon.Name = "ribbon";
            ribbon.Pages.AddRange(new DevExpress.XtraBars.Ribbon.RibbonPage[] { ribbonPage1 });
            ribbon.Size = new System.Drawing.Size(761, 144);
            // 
            // SaveButton
            // 
            SaveButton.Caption = "保存数据库";
            SaveButton.Id = 1;
            SaveButton.ImageOptions.Image = (System.Drawing.Image)resources.GetObject("SaveButton.ImageOptions.Image");
            SaveButton.ImageOptions.LargeImage = (System.Drawing.Image)resources.GetObject("SaveButton.ImageOptions.LargeImage");
            SaveButton.LargeWidth = 60;
            SaveButton.Name = "SaveButton";
            SaveButton.ItemClick += SaveButton_ItemClick;
            // 
            // ImportButton
            // 
            ImportButton.Caption = "导入";
            ImportButton.Id = 2;
            ImportButton.ImageOptions.Image = (System.Drawing.Image)resources.GetObject("ImportButton.ImageOptions.Image");
            ImportButton.ImageOptions.LargeImage = (System.Drawing.Image)resources.GetObject("ImportButton.ImageOptions.LargeImage");
            ImportButton.Name = "ImportButton";
            ImportButton.ItemClick += ImportButton_ItemClick;
            // 
            // ExportButton
            // 
            ExportButton.Caption = "导出";
            ExportButton.Id = 3;
            ExportButton.ImageOptions.Image = (System.Drawing.Image)resources.GetObject("ExportButton.ImageOptions.Image");
            ExportButton.ImageOptions.LargeImage = (System.Drawing.Image)resources.GetObject("ExportButton.ImageOptions.LargeImage");
            ExportButton.Name = "ExportButton";
            ExportButton.ItemClick += ExportButton_ItemClick;
            // 
            // UpdateMonsterImageButton
            // 
            UpdateMonsterImageButton.Caption = "旧图转新图";
            UpdateMonsterImageButton.Id = 4;
            UpdateMonsterImageButton.ImageOptions.Image = (System.Drawing.Image)resources.GetObject("UpdateMonsterImageButton.ImageOptions.Image");
            UpdateMonsterImageButton.ImageOptions.LargeImage = (System.Drawing.Image)resources.GetObject("UpdateMonsterImageButton.ImageOptions.LargeImage");
            UpdateMonsterImageButton.Name = "UpdateMonsterImageButton";
            UpdateMonsterImageButton.ItemClick += UpdateMonsterImageButton_ItemClick;
            // 
            // ribbonPage1
            // 
            ribbonPage1.Groups.AddRange(new DevExpress.XtraBars.Ribbon.RibbonPageGroup[] { ribbonPageGroup1, JsonImportExport, ribbonPageGroup2 });
            ribbonPage1.Name = "ribbonPage1";
            ribbonPage1.Text = "主页";
            // 
            // ribbonPageGroup1
            // 
            ribbonPageGroup1.AllowTextClipping = false;
            ribbonPageGroup1.CaptionButtonVisible = DevExpress.Utils.DefaultBoolean.False;
            ribbonPageGroup1.ItemLinks.Add(SaveButton);
            ribbonPageGroup1.Name = "ribbonPageGroup1";
            ribbonPageGroup1.Text = "保存";
            // 
            // JsonImportExport
            // 
            JsonImportExport.ItemLinks.Add(ImportButton);
            JsonImportExport.ItemLinks.Add(ExportButton);
            JsonImportExport.Name = "JsonImportExport";
            JsonImportExport.Text = "JSON";
            // 
            // ribbonPageGroup2
            // 
            ribbonPageGroup2.ItemLinks.Add(UpdateMonsterImageButton);
            ribbonPageGroup2.Name = "ribbonPageGroup2";
            ribbonPageGroup2.Text = "怪物图像";
            // 
            // MonsterInfoView
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(761, 510);
            Controls.Add(MonsterInfoGridControl);
            Controls.Add(ribbon);
            Name = "MonsterInfoView";
            Ribbon = ribbon;
            Text = "怪物信息";
            ((System.ComponentModel.ISupportInitialize)MonsterInfoStatsGridView).EndInit();
            ((System.ComponentModel.ISupportInitialize)StatComboBox).EndInit();
            ((System.ComponentModel.ISupportInitialize)MonsterInfoGridControl).EndInit();
            ((System.ComponentModel.ISupportInitialize)RespawnsGridView).EndInit();
            ((System.ComponentModel.ISupportInitialize)RegionLookUpEdit).EndInit();
            ((System.ComponentModel.ISupportInitialize)DropsGridView).EndInit();
            ((System.ComponentModel.ISupportInitialize)ItemLookUpEdit).EndInit();
            ((System.ComponentModel.ISupportInitialize)MonsterInfoGridView).EndInit();
            ((System.ComponentModel.ISupportInitialize)MonsterImageComboBox).EndInit();
            ((System.ComponentModel.ISupportInitialize)ribbon).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private DevExpress.XtraBars.Ribbon.RibbonControl ribbon;
        private DevExpress.XtraBars.Ribbon.RibbonPage ribbonPage1;
        private DevExpress.XtraBars.Ribbon.RibbonPageGroup ribbonPageGroup1;
        private DevExpress.XtraBars.BarButtonItem SaveButton;
        private DevExpress.XtraGrid.GridControl MonsterInfoGridControl;
        private DevExpress.XtraGrid.Views.Grid.GridView MonsterInfoGridView;
        private DevExpress.XtraGrid.Views.Grid.GridView MonsterInfoStatsGridView;
        private DevExpress.XtraGrid.Columns.GridColumn colStat;
        private DevExpress.XtraEditors.Repository.RepositoryItemImageComboBox StatComboBox;
        private DevExpress.XtraGrid.Columns.GridColumn colAmount;
        private DevExpress.XtraGrid.Columns.GridColumn colMonsterName;
        private DevExpress.XtraGrid.Columns.GridColumn colImage;
        private DevExpress.XtraEditors.Repository.RepositoryItemImageComboBox MonsterImageComboBox;
        private DevExpress.XtraGrid.Columns.GridColumn colAI;
        private DevExpress.XtraGrid.Columns.GridColumn colLevel;
        private DevExpress.XtraGrid.Columns.GridColumn colExperience;
        private DevExpress.XtraGrid.Columns.GridColumn colViewRange;
        private DevExpress.XtraGrid.Columns.GridColumn colCoolEye;
        private DevExpress.XtraGrid.Columns.GridColumn colAttackDelay;
        private DevExpress.XtraGrid.Columns.GridColumn colMoveDelay;
        private DevExpress.XtraGrid.Views.Grid.GridView RespawnsGridView;
        private DevExpress.XtraGrid.Columns.GridColumn colRegion;
        private DevExpress.XtraGrid.Columns.GridColumn colDelay;
        private DevExpress.XtraGrid.Columns.GridColumn colCount;
        private DevExpress.XtraGrid.Views.Grid.GridView DropsGridView;
        private DevExpress.XtraGrid.Columns.GridColumn ColItem;
        private DevExpress.XtraGrid.Columns.GridColumn colChance;
        private DevExpress.XtraGrid.Columns.GridColumn colDAmount;
        private DevExpress.XtraEditors.Repository.RepositoryItemLookUpEdit RegionLookUpEdit;
        private DevExpress.XtraEditors.Repository.RepositoryItemLookUpEdit ItemLookUpEdit;
        private DevExpress.XtraGrid.Columns.GridColumn colIsBoss;
        private DevExpress.XtraGrid.Columns.GridColumn gridColumn1;
        private DevExpress.XtraGrid.Columns.GridColumn colUndead;
        private DevExpress.XtraGrid.Columns.GridColumn gridColumn3;
        private DevExpress.XtraGrid.Columns.GridColumn gridColumn4;
        private DevExpress.XtraGrid.Columns.GridColumn gridColumn5;
        private DevExpress.XtraGrid.Columns.GridColumn gridColumn6;
        private DevExpress.XtraGrid.Columns.GridColumn gridColumn7;
        private DevExpress.XtraGrid.Columns.GridColumn colFaceImage;
        private DevExpress.XtraBars.BarButtonItem ImportButton;
        private DevExpress.XtraBars.BarButtonItem ExportButton;
        private DevExpress.XtraBars.Ribbon.RibbonPageGroup JsonImportExport;
        private DevExpress.XtraBars.BarButtonItem UpdateMonsterImageButton;
        private DevExpress.XtraBars.Ribbon.RibbonPageGroup ribbonPageGroup2;
    }
}
