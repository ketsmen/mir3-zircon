using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LibraryEditor
{
    public partial class LMain : Form
    {
        private readonly Dictionary<int, int> _indexList = new Dictionary<int, int>();
        private Mir3Library _library;
        private Mir3Library.Mir3Image _selectedImage, _exportImage;
        private Image _zoomImage;
        private bool _libraryDirty;
        private const int ImagePreviewPadding = 8;
        private const int MaxImportedImagePixels = 16777216;

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public LMain()
        {
            InitializeComponent();
            splitContainer1.Panel2.Resize += PreviewListPanel_Resize;
            panel.Resize += ImagePanel_Resize;
            RefreshPreviewListLayout();
            RefreshImageBoxLayout();

            SendMessage(PreviewListView.Handle, 4149, 0, 5242946); //80 x 66

            this.AllowDrop = true;
            this.DragEnter += new DragEventHandler(Form1_DragEnter);
            this.DragDrop += new DragEventHandler(Form1_DragDrop);
            if (Program.openFileWith.Length > 0 &&
                File.Exists(Program.openFileWith))
            {
                try
                {
                    OpenLibraryDialog.FileName = Program.openFileWith;
                    _library = new Mir3Library(OpenLibraryDialog.FileName);
                    PreviewListView.VirtualListSize = _library.Images.Count;
                    RefreshPreviewListLayout();

                    _libraryDirty = false;
                    UpdateWindowTitle();

                    PreviewListView.SelectedIndices.Clear();

                    if (PreviewListView.Items.Count > 0)
                        PreviewListView.Items[0].Selected = true;

                    radioButtonImage.Enabled = true;
                    radioButtonShadow.Enabled = true;
                    radioButtonOverlay.Enabled = true;
                }
                catch (Exception ex)
                {
                    ReleaseCurrentLibrary();
                    MessageBox.Show(
                        "打开图库失败，已启动空编辑器。\r\n\r\n" + ex,
                        "打开图库",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ReleaseCurrentLibrary();
            base.OnFormClosed(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ConfirmSaveOrDiscardLibraryChanges())
            {
                e.Cancel = true;
                return;
            }

            base.OnFormClosing(e);
        }

        private void Form1_DragDrop(object sender, DragEventArgs e)
        {
            // 拖拽导入暂未启用。
        }

        private void Form1_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void ClearInterface()
        {
            _selectedImage = null;
            SetImageBoxImage(null);
            ZoomTrackBar.Value = 1;
            RefreshImageBoxLayout(true);

            WidthLabel.Text = "无图片";
            HeightLabel.Text = "无图片";
            ShadowTypeLabel.Text = "无图片";
            OffSetXTextBox.Text = string.Empty;
            OffSetYTextBox.Text = string.Empty;
            OffSetXTextBox.BackColor = SystemColors.Window;
            OffSetYTextBox.BackColor = SystemColors.Window;
        }

        private bool TryGetSelectedIndex(out int index)
        {
            index = -1;

            if (PreviewListView.SelectedIndices.Count == 0)
                return false;

            int selectedIndex = PreviewListView.SelectedIndices[0];
            if (selectedIndex < 0 || selectedIndex >= PreviewListView.Items.Count)
                return false;

            index = selectedIndex;
            return true;
        }

        private bool TryEnsureImageVisible(int index)
        {
            if (index < 0 || index >= PreviewListView.Items.Count)
                return false;

            PreviewListView.Items[index].EnsureVisible();
            return true;
        }

        private void PreviewListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_library == null)
            {
                ClearInterface();
                return;
            }

            if (!TryGetSelectedIndex(out int selectedIndex))
            {
                ClearInterface();
                return;
            }

            _selectedImage = _library.GetImage(selectedIndex);

            if (_selectedImage == null)
            {
                ClearInterface();
                return;
            }

            if (radioButtonImage.Checked)
            {
                WidthLabel.Text = _selectedImage.Width.ToString();
                HeightLabel.Text = _selectedImage.Height.ToString();

                ShadowTypeLabel.Text = _selectedImage.ShadowType.ToString();

                OffSetXTextBox.Text = _selectedImage.OffSetX.ToString();
                OffSetYTextBox.Text = _selectedImage.OffSetY.ToString();

                SetImageBoxImage(_selectedImage.Image);
            }
            else if (radioButtonShadow.Checked)
            {
                WidthLabel.Text = _selectedImage.ShadowWidth.ToString();
                HeightLabel.Text = _selectedImage.ShadowHeight.ToString();

                ShadowTypeLabel.Text = _selectedImage.ShadowType.ToString();

                OffSetXTextBox.Text = _selectedImage.ShadowOffSetX.ToString();
                OffSetYTextBox.Text = _selectedImage.ShadowOffSetY.ToString();

                SetImageBoxImage(_selectedImage.ShadowImage);
            }
            if (radioButtonOverlay.Checked)
            {
                WidthLabel.Text = _selectedImage.OverlayWidth.ToString();
                HeightLabel.Text = _selectedImage.OverlayHeight.ToString();

                ShadowTypeLabel.Text = _selectedImage.ShadowType.ToString();

                OffSetXTextBox.Text = _selectedImage.OffSetX.ToString();
                OffSetYTextBox.Text = _selectedImage.OffSetY.ToString();

                SetImageBoxImage(_selectedImage.OverlayImage);
            }

            RefreshImageBoxLayout(true);

            // Keep track of what image/s are selected.
            if (PreviewListView.SelectedIndices.Count > 1)
            {
                toolStripStatusLabel.ForeColor = Color.Red;
                toolStripStatusLabel.Text = "已选择多张图片。";
            }
            else
            {
                toolStripStatusLabel.ForeColor = SystemColors.ControlText;
                toolStripStatusLabel.Text = "当前图片: " + string.Format("{0} / {1}",
                selectedIndex.ToString(),
                (PreviewListView.Items.Count - 1).ToString());
            }

            nudJump.Value = selectedIndex;
        }

        private void PreviewListView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            int index;

            if (_indexList.TryGetValue(e.ItemIndex, out index))
            {
                e.Item = new ListViewItem { ImageIndex = index, Text = e.ItemIndex.ToString() };
                return;
            }

            index = ImageList.Images.Count;
            _indexList.Add(e.ItemIndex, index);
            if (radioButtonImage.Checked)
                ImageList.Images.Add(_library.GetPreview(e.ItemIndex, ImageType.Image));
            else if (radioButtonShadow.Checked)
                ImageList.Images.Add(_library.GetPreview(e.ItemIndex, ImageType.Shadow));
            else if (radioButtonOverlay.Checked)
                ImageList.Images.Add(_library.GetPreview(e.ItemIndex, ImageType.Overlay));
            e.Item = new ListViewItem { ImageIndex = index, Text = e.ItemIndex.ToString() };
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (_library.FileName == null) return;

            if (ImportImageDialog.ShowDialog() != DialogResult.OK) return;

            List<string> fileNames = new List<string>(ImportImageDialog.FileNames);

            //fileNames.Sort();
            toolStripProgressBar.Value = 0;
            toolStripProgressBar.Maximum = fileNames.Count;
            List<string> failures = new List<string>();
            int failureCount = 0;
            int successCount = 0;

            for (int i = 0; i < fileNames.Count; i++)
            {
                string fileName = fileNames[i];
                Bitmap image = null;

                try
                {
                    image = LoadBitmapDetached(fileName);

                    string placementFile = Path.Combine(Path.GetDirectoryName(fileName) ?? string.Empty, "Placements", Path.GetFileNameWithoutExtension(fileName));
                    placementFile = Path.ChangeExtension(placementFile, ".txt");

                    short x = 0;
                    short y = 0;

                    if (File.Exists(placementFile))
                    {
                        string[] placements = File.ReadAllLines(placementFile);

                        if (placements.Length > 0)
                            short.TryParse(placements[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x);
                        if (placements.Length > 1)
                            short.TryParse(placements[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
                    }

                    _library.AddImage(image, x, y);
                    image = null;
                    successCount++;
                    toolStripProgressBar.Value++;
                }
                catch (Exception ex)
                {
                    image?.Dispose();
                    AddImageImportFailure(failures, ref failureCount, fileName, ex);
                }
            }

            PreviewListView.VirtualListSize = _library.Images.Count;
            toolStripProgressBar.Value = 0;
            ShowImageImportFailures("添加图片", failures, failureCount);
            if (successCount > 0)
                MarkLibraryDirty("已添加图片，尚未保存。");
        }

        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ConfirmSaveOrDiscardLibraryChanges()) return;
            if (SaveLibraryDialog.ShowDialog() != DialogResult.OK) return;

            Mir3Library newLibrary = Mir3Library.CreateEmpty(SaveLibraryDialog.FileName);
            try
            {
                newLibrary.Save(SaveLibraryDialog.FileName);
            }
            catch (Exception ex)
            {
                newLibrary.Dispose();
                MessageBox.Show(
                    "新建图库失败，当前已打开的图库未受影响。\r\n\r\n" + ex,
                    "新建图库",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            ReleaseCurrentLibrary();
            _library = newLibrary;
            PreviewListView.VirtualListSize = _library.Images.Count;
            OpenLibraryDialog.FileName = _library.FileName;
            _libraryDirty = false;
            UpdateWindowTitle();
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ConfirmSaveOrDiscardLibraryChanges()) return;
            if (OpenLibraryDialog.ShowDialog() != DialogResult.OK) return;

            Mir3Library library;
            try
            {
                library = new Mir3Library(OpenLibraryDialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "打开图库失败，当前已打开的图库未受影响。\r\n\r\n" + ex,
                    "打开图库",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            ReleaseCurrentLibrary();
            _library = library;
            PreviewListView.VirtualListSize = _library.Images.Count;
            RefreshPreviewListLayout();

            _libraryDirty = false;
            UpdateWindowTitle();

            PreviewListView.SelectedIndices.Clear();

            if (PreviewListView.Items.Count > 0)
                PreviewListView.Items[0].Selected = true;

            radioButtonImage.Enabled = true;
            radioButtonShadow.Enabled = true;
            radioButtonOverlay.Enabled = true;
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (_library.FileName == null) return;

            TrySaveLibrary(_library.FileName, "保存图库");
        }

        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (SaveLibraryDialog.ShowDialog() != DialogResult.OK) return;

            if (!TrySaveLibrary(SaveLibraryDialog.FileName, "另存图库"))
                return;

            OpenLibraryDialog.FileName = _library.FileName;
            UpdateWindowTitle();
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (_library.FileName == null) return;
            if (PreviewListView.SelectedIndices.Count == 0) return;

            if (MessageBox.Show("确定要删除选中的图片吗？",
                "删除选中图片",
                MessageBoxButtons.YesNoCancel) != DialogResult.Yes) return;

            var removeList = PreviewListView.SelectedIndices.Cast<int>().ToList();

            removeList.Sort();

            SetImageBoxImage(null);
            _selectedImage = null;

            for (int i = removeList.Count - 1; i >= 0; i--)
                _library.RemoveImage(removeList[i]);

            ImageList.Images.Clear();
            _indexList.Clear();
            PreviewListView.VirtualListSize -= removeList.Count;
            MarkLibraryDirty("已删除图片，尚未保存。");
        }

        private void convertToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (OpenWeMadeDialog.ShowDialog() != DialogResult.OK) return;

            toolStripProgressBar.Maximum = OpenWeMadeDialog.FileNames.Length;
            toolStripProgressBar.Value = 0;
            List<string> failures = new List<string>();
            int failureCount = 0;
            int successCount = 0;

            foreach (string fileName in OpenWeMadeDialog.FileNames)
            {
                try
                {
                    ConvertLegacyLibraryFile(fileName);
                    successCount++;
                }
                catch (Exception ex)
                {
                    AddImageImportFailure(failures, ref failureCount, fileName, ex);
                }
                finally
                {
                    toolStripProgressBar.Value++;
                }
            }

            toolStripProgressBar.Value = 0;
            ShowImageImportFailures("转换图库", failures, failureCount);

            MessageBox.Show(string.Format("已成功转换 {0} 个图库。",
                successCount.ToString(CultureInfo.InvariantCulture)));
        }

        private void copyToToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (PreviewListView.SelectedIndices.Count == 0) return;
            if (SaveLibraryDialog.ShowDialog() != DialogResult.OK) return;

            string targetPath = SaveLibraryDialog.FileName;

            try
            {
                List<int> copyList = new List<int>();

                for (int i = 0; i < PreviewListView.SelectedIndices.Count; i++)
                    copyList.Add(PreviewListView.SelectedIndices[i]);

                copyList.Sort();

                if (IsSameFilePath(targetPath, _library.FileName))
                {
                    CopySelectedImagesToCurrentLibrary(copyList);
                    return;
                }

                using Mir3Library tempLibrary = new Mir3Library(targetPath);

                for (int i = 0; i < copyList.Count; i++)
                {
                    Mir3Library.Mir3Image image = _library.GetImage(copyList[i]);
                    tempLibrary.Images.Add(CloneMir3Image(image, tempLibrary));
                }

                tempLibrary.Save(targetPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "复制到图库失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CopySelectedImagesToCurrentLibrary(List<int> copyList)
        {
            int originalCount = _library.Images.Count;

            try
            {
                for (int i = 0; i < copyList.Count; i++)
                {
                    Mir3Library.Mir3Image image = _library.GetImage(copyList[i]);
                    _library.Images.Add(CloneMir3Image(image, _library));
                }

                ImageList.Images.Clear();
                _indexList.Clear();
                PreviewListView.VirtualListSize = _library.Images.Count;

                if (!TrySaveLibrary(_library.FileName, "复制到图库"))
                    MarkLibraryDirty("已复制图片，但保存失败。");
            }
            catch (Exception ex)
            {
                RollBackImages(originalCount);
                ImageList.Images.Clear();
                _indexList.Clear();
                PreviewListView.VirtualListSize = _library.Images.Count;
                MessageBox.Show(ex.ToString(), "复制到图库失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void removeBlanksToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (MessageBox.Show("确定要移除空白图片吗？",
                "移除空白图片",
                MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            SetImageBoxImage(null);
            _selectedImage = null;
            int originalCount = _library.Images.Count;
            _library.RemoveBlanks();
            ImageList.Images.Clear();
            _indexList.Clear();
            PreviewListView.VirtualListSize = _library.Images.Count;

            if (_library.Images.Count != originalCount)
                MarkLibraryDirty("已移除空白图片，尚未保存。");
        }

        private void countBlanksToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenLibraryDialog.Multiselect = true;

            if (OpenLibraryDialog.ShowDialog() != DialogResult.OK)
            {
                OpenLibraryDialog.Multiselect = false;
                return;
            }

            int count = 0;
            try
            {
                MLibraryV2.Load = false;

                for (int i = 0; i < OpenLibraryDialog.FileNames.Length; i++)
                {
                    MLibraryV2 library = new MLibraryV2(OpenLibraryDialog.FileNames[i]);
                    try
                    {
                        for (int x = 0; x < library.Count; x++)
                        {
                            if (library.Images[x]?.Length <= 8)
                                count++;
                        }
                    }
                    finally
                    {
                        library.Close();
                    }

                }
            }
            finally
            {
                MLibraryV2.Load = true;
                OpenLibraryDialog.Multiselect = false;
            }

            MessageBox.Show(count.ToString());
        }

        private void OffSetXTextBox_TextChanged(object sender, EventArgs e)
        {
            TextBox control = sender as TextBox;

            if (control == null || !control.Focused) return;

            short temp;

            if (!short.TryParse(control.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out temp))
            {
                control.BackColor = Color.Red;
                return;
            }

            control.BackColor = SystemColors.Window;

            if (ApplyOffsetChange(temp, isXOffset: true))
                MarkLibraryDirty(radioButtonShadow.Checked
                    ? "已修改阴影 X 偏移，尚未保存。"
                    : "已修改图片 X 偏移，尚未保存。");
        }

        private void OffSetYTextBox_TextChanged(object sender, EventArgs e)
        {
            TextBox control = sender as TextBox;

            if (control == null || !control.Focused) return;

            short temp;

            if (!short.TryParse(control.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out temp))
            {
                control.BackColor = Color.Red;
                return;
            }

            control.BackColor = SystemColors.Window;

            if (ApplyOffsetChange(temp, isXOffset: false))
                MarkLibraryDirty(radioButtonShadow.Checked
                    ? "已修改阴影 Y 偏移，尚未保存。"
                    : "已修改图片 Y 偏移，尚未保存。");
        }

        private bool ApplyOffsetChange(short value, bool isXOffset)
        {
            if (_library == null || radioButtonOverlay.Checked) return false;

            bool changed = false;
            for (int i = 0; i < PreviewListView.SelectedIndices.Count; i++)
            {
                Mir3Library.Mir3Image image = _library.GetImage(PreviewListView.SelectedIndices[i]);
                if (image == null) continue;

                if (radioButtonShadow.Checked)
                {
                    if (isXOffset)
                    {
                        if (image.ShadowOffSetX == value) continue;
                        image.ShadowOffSetX = value;
                    }
                    else
                    {
                        if (image.ShadowOffSetY == value) continue;
                        image.ShadowOffSetY = value;
                    }
                }
                else
                {
                    if (isXOffset)
                    {
                        if (image.OffSetX == value) continue;
                        image.OffSetX = value;
                    }
                    else
                    {
                        if (image.OffSetY == value) continue;
                        image.OffSetY = value;
                    }
                }

                changed = true;
            }

            return changed;
        }

        private void InsertImageButton_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (_library.FileName == null) return;
            if (!TryGetSelectedIndex(out int index)) return;
            if (ImportImageDialog.ShowDialog() != DialogResult.OK) return;

            List<string> fileNames = new List<string>(ImportImageDialog.FileNames);

            //fileNames.Sort();

            toolStripProgressBar.Value = 0;
            toolStripProgressBar.Maximum = fileNames.Count;
            List<string> failures = new List<string>();
            int failureCount = 0;
            int successCount = 0;

            for (int i = fileNames.Count - 1; i >= 0; i--)
            {
                string fileName = fileNames[i];

                Bitmap image = null;

                try
                {
                    image = LoadBitmapDetached(fileName);

                    string placementFile = Path.Combine(Path.GetDirectoryName(fileName) ?? string.Empty, "Placements", Path.GetFileNameWithoutExtension(fileName));
                    placementFile = Path.ChangeExtension(placementFile, ".txt");

                    short x = 0;
                    short y = 0;

                    if (File.Exists(placementFile))
                    {
                        string[] placements = File.ReadAllLines(placementFile);

                        if (placements.Length > 0)
                            short.TryParse(placements[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x);
                        if (placements.Length > 1)
                            short.TryParse(placements[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
                    }

                    _library.InsertImage(index, image, x, y);
                    image = null;

                    successCount++;
                    toolStripProgressBar.Value++;
                }
                catch (Exception ex)
                {
                    image?.Dispose();
                    AddImageImportFailure(failures, ref failureCount, fileName, ex);
                }
            }

            ImageList.Images.Clear();
            _indexList.Clear();
            PreviewListView.VirtualListSize = _library.Images.Count;
            toolStripProgressBar.Value = 0;
            ShowImageImportFailures("插入图片", failures, failureCount);
            if (successCount > 0)
                MarkLibraryDirty("已插入图片，尚未保存。");
        }

        private void safeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (MessageBox.Show("确定要移除空白图片吗？",
                "移除空白图片", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            SetImageBoxImage(null);
            _selectedImage = null;
            int originalCount = _library.Images.Count;
            _library.RemoveBlanks(true);
            ImageList.Images.Clear();
            _indexList.Clear();
            PreviewListView.VirtualListSize = _library.Images.Count;

            if (_library.Images.Count != originalCount)
                MarkLibraryDirty("已移除空白图片，尚未保存。");
        }

        private const int HowDeepToScan = 6;

        /*public static void ProcessDir(string sourceDir, int recursionLvl, string outputDir)
        {
            if (recursionLvl <= HowDeepToScan)
            {
                // Process the list of files found in the directory.
                string[] fileEntries = Directory.GetFiles(sourceDir);
                foreach (string fileName in fileEntries)
                {
                    if (Directory.Exists(outputDir) != true) Directory.CreateDirectory(outputDir);
                    MLibraryv0 OldLibrary = new MLibraryv0(fileName);
                    MLibraryV2 NewLibrary = new MLibraryV2(outputDir + Path.GetFileName(fileName)) { Images = new List<MLibraryV2.MImage>(), IndexList = new List<int>(), Count = OldLibrary.Images.Count }; ;
                    for (int i = 0; i < OldLibrary.Images.Count; i++)
                        NewLibrary.Images.Add(null);
                    for (int j = 0; j < OldLibrary.Images.Count; j++)
                    {
                        MLibraryv0.MImage oldimage = OldLibrary.GetMImage(j);
                        NewLibrary.Images[j] = new MLibraryV2.MImage(oldimage.FBytes, oldimage.Width, oldimage.Height) { X = oldimage.X, Y = oldimage.Y };
                    }
                    NewLibrary.Save();
                    for (int i = 0; i < NewLibrary.Images.Count; i++)
                    {
                        if (NewLibrary.Images[i].Preview != null)
                            NewLibrary.Images[i].Preview.Dispose();
                        if (NewLibrary.Images[i].Image != null)
                            NewLibrary.Images[i].Image.Dispose();
                        if (NewLibrary.Images[i].MaskImage != null)
                            NewLibrary.Images[i].MaskImage.Dispose();
                    }
                    for (int i = 0; i < OldLibrary.Images.Count; i++)
                    {
                        if (OldLibrary.Images[i].Preview != null)
                            OldLibrary.Images[i].Preview.Dispose();
                        if (OldLibrary.Images[i].Image != null)
                            OldLibrary.Images[i].Image.Dispose();
                    }
                    NewLibrary.Images.Clear();
                    NewLibrary.IndexList.Clear();
                    OldLibrary.Images.Clear();
                    OldLibrary.IndexList.Clear();
                    NewLibrary.Close();
                    OldLibrary.Close();
                    NewLibrary = null;
                    OldLibrary = null;
                }

                // Recurse into subdirectories of this directory.
                string[] subdirEntries = Directory.GetDirectories(sourceDir);
                foreach (string subdir in subdirEntries)
                {
                    // Do not iterate through re-parse points.
                    if (Path.GetFileName(Path.GetFullPath(subdir).TrimEnd(Path.DirectorySeparatorChar)) == Path.GetFileName(Path.GetFullPath(outputDir).TrimEnd(Path.DirectorySeparatorChar))) continue;
                    if ((File.GetAttributes(subdir) &
                         FileAttributes.ReparsePoint) !=
                             FileAttributes.ReparsePoint)
                        ProcessDir(subdir, recursionLvl + 1, outputDir + " \\" + Path.GetFileName(Path.GetFullPath(subdir).TrimEnd(Path.DirectorySeparatorChar)) + "\\");
                }
            }
        }*/

        // Export a single image.
        private void ExportButton_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (_library.FileName == null) return;
            if (PreviewListView.SelectedIndices.Count == 0) return;

            string libraryName = Path.GetFileNameWithoutExtension(_library.FileName);
            if (string.IsNullOrWhiteSpace(libraryName))
                libraryName = "Library";

            string _folder = Path.Combine(Application.StartupPath, "Exported", libraryName) + Path.DirectorySeparatorChar;

            using Bitmap blank = new Bitmap(1, 1);

            List<int> selectedIndices = PreviewListView.SelectedIndices.Cast<int>().OrderBy(x => x).ToList();
            string placementFolder = Path.Combine(_folder, "Placements");

            toolStripProgressBar.Value = 0;
            toolStripProgressBar.Maximum = selectedIndices.Count;

            try
            {
                Directory.CreateDirectory(_folder);
                Directory.CreateDirectory(placementFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show("创建导出目录失败。\r\n\r\n" + ex, "导出图片", MessageBoxButtons.OK, MessageBoxIcon.Error);
                toolStripProgressBar.Value = 0;
                return;
            }

            List<string> failures = new List<string>();
            int failureCount = 0;
            int successCount = 0;

            foreach (int i in selectedIndices)
            {
                string imageFileName = Path.Combine(_folder, i.ToString(CultureInfo.InvariantCulture) + ".png");

                try
                {
                    _exportImage = _library.GetImage(i);

                    LibraryFileWriter.WriteFileAtomically(
                        imageFileName,
                        tempPath =>
                        {
                            if (_exportImage?.Image == null)
                                blank.Save(tempPath, ImageFormat.Png);
                            else
                                _exportImage.Image.Save(tempPath, ImageFormat.Png);
                        });

                    int offSetX = _exportImage?.OffSetX ?? 0;
                    int offSetY = _exportImage?.OffSetY ?? 0;

                    LibraryFileWriter.WriteAllLinesAtomically(
                        Path.Combine(placementFolder, i.ToString(CultureInfo.InvariantCulture) + ".txt"),
                        new[]
                        {
                            offSetX.ToString(CultureInfo.InvariantCulture),
                            offSetY.ToString(CultureInfo.InvariantCulture)
                        });

                    successCount++;
                }
                catch (Exception ex)
                {
                    AddImageImportFailure(failures, ref failureCount, imageFileName, ex);
                }
                finally
                {
                    toolStripProgressBar.Value++;
                }
            }

            toolStripProgressBar.Value = 0;
            ShowImageImportFailures("导出图片", failures, failureCount);

            if (successCount > 0)
                MessageBox.Show(
                    string.Format(CultureInfo.InvariantCulture, "已导出 {0} 张图片到 {1}", successCount, _folder),
                    "图片已导出",
                    MessageBoxButtons.OK);
        }

        // Don't let the splitter go out of sight on resizing.
        private void LMain_Resize(object sender, EventArgs e)
        {
            ClampMainSplitter();
            RefreshPreviewListLayout();
        }

        private void PreviewListPanel_Resize(object sender, EventArgs e)
        {
            RefreshPreviewListLayout();
        }

        private void ImagePanel_Resize(object sender, EventArgs e)
        {
            RefreshImageBoxLayout();
        }

        private void ClampMainSplitter()
        {
            const int minimumPreviewHeight = 150;

            int maxDistance = splitContainer1.ClientSize.Height - splitContainer1.SplitterWidth - minimumPreviewHeight;

            if (maxDistance < splitContainer1.Panel1MinSize)
                maxDistance = splitContainer1.Panel1MinSize;

            if (splitContainer1.SplitterDistance > maxDistance)
            {
                splitContainer1.SplitterDistance = maxDistance;
            }
        }

        private void RefreshPreviewListLayout()
        {
            PreviewListView.Dock = DockStyle.Fill;
            PreviewListView.Bounds = splitContainer1.Panel2.ClientRectangle;
            PreviewListView.Invalidate();
        }

        private void RefreshImageBoxLayout(bool resetScroll = false)
        {
            if (ImageBox.Image == null)
            {
                panel.AutoScrollMinSize = Size.Empty;
                ImageBox.Location = new Point(ImagePreviewPadding, ImagePreviewPadding);
                return;
            }

            if (resetScroll)
                panel.AutoScrollPosition = Point.Empty;

            ImageBox.Size = ImageBox.Image.Size;

            Size viewport = panel.ClientSize;
            Size imageSize = ImageBox.Size;
            Size paddedImageSize = new Size(
                imageSize.Width + ImagePreviewPadding * 2,
                imageSize.Height + ImagePreviewPadding * 2);

            panel.AutoScrollMinSize = paddedImageSize;

            bool needsHorizontalScroll = paddedImageSize.Width > viewport.Width;
            bool needsVerticalScroll = paddedImageSize.Height > viewport.Height;

            int x = needsHorizontalScroll
                ? ImagePreviewPadding
                : Math.Max(ImagePreviewPadding, (viewport.Width - imageSize.Width) / 2);

            int y = needsVerticalScroll
                ? ImagePreviewPadding
                : Math.Max(ImagePreviewPadding, (viewport.Height - imageSize.Height) / 2);

            ImageBox.Location = new Point(x, y);
        }

        private Image GetSelectedImage()
        {
            if (_selectedImage == null) return null;

            if (radioButtonShadow.Checked)
                return _selectedImage.ShadowImage;

            if (radioButtonOverlay.Checked)
                return _selectedImage.OverlayImage;

            return _selectedImage.Image;
        }

        private void SetImageBoxImage(Image image)
        {
            if (_zoomImage != null)
            {
                if (ReferenceEquals(ImageBox.Image, _zoomImage))
                    ImageBox.Image = null;

                _zoomImage.Dispose();
                _zoomImage = null;
            }

            ImageBox.Image = image;
        }

        // Zoom in and out.
        private void ZoomTrackBar_Scroll(object sender, EventArgs e)
        {
            Image sourceImage = GetSelectedImage();

            if (sourceImage == null)
            {
                ZoomTrackBar.Value = 1;
                SetImageBoxImage(null);
                RefreshImageBoxLayout(true);
                return;
            }

            if (ZoomTrackBar.Value == 1)
            {
                SetImageBoxImage(sourceImage);
                RefreshImageBoxLayout(true);
                return;
            }

            if (ZoomTrackBar.Value > 0)
            {
                Bitmap newBmp = null;
                try
                {
                    if (!TryGetSelectedIndex(out int selectedIndex) || !TryEnsureImageVisible(selectedIndex))
                        return;

                    int zoom = ZoomTrackBar.Value;
                    long zoomWidth = (long)sourceImage.Width * zoom;
                    long zoomHeight = (long)sourceImage.Height * zoom;
                    if (!CanAllocatePreviewBitmap(zoomWidth, zoomHeight))
                    {
                        SetImageBoxImage(sourceImage);
                        ZoomTrackBar.Value = 1;
                        RefreshImageBoxLayout(true);
                        toolStripStatusLabel.ForeColor = Color.Red;
                        toolStripStatusLabel.Text = $"预览缩放尺寸过大，已恢复 1x: {zoomWidth:#,##0}x{zoomHeight:#,##0}";
                        return;
                    }

                    newBmp = new Bitmap((int)zoomWidth, (int)zoomHeight);
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(newBmp))
                    {
                        if (checkBoxPreventAntiAliasing.Checked == true)
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.CompositingMode = CompositingMode.SourceCopy;
                        }

                        if (checkBoxQuality.Checked == true)
                        {
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        }

                        g.DrawImage(sourceImage, new Rectangle(0, 0, newBmp.Width, newBmp.Height));
                    }

                    SetImageBoxImage(newBmp);
                    _zoomImage = newBmp;
                    newBmp = null;
                    RefreshImageBoxLayout(true);

                    toolStripStatusLabel.ForeColor = SystemColors.ControlText;
                    toolStripStatusLabel.Text = "当前图片: " + string.Format("{0} / {1}",
                        selectedIndex.ToString(),
                        (PreviewListView.Items.Count - 1).ToString());
                }
                catch
                {
                    newBmp?.Dispose();
                    return;
                }
            }
        }

        // Swap the image panel background colour Black/White.
        private void pictureBox_Click(object sender, EventArgs e)
        {
            if (panel.BackColor == Color.Black)
            {
                panel.BackColor = Color.GhostWhite;
            }
            else
            {
                panel.BackColor = Color.Black;
            }
        }

        private void PreviewListView_VirtualItemsSelectionRangeChanged(object sender, ListViewVirtualItemsSelectionRangeChangedEventArgs e)
        {
            // Keep track of what image/s are selected.
            ListView.SelectedIndexCollection _col = PreviewListView.SelectedIndices;

            if (_col.Count > 1)
            {
                toolStripStatusLabel.ForeColor = Color.Red;
                toolStripStatusLabel.Text = "已选择多张图片。";
            }
        }

        private void buttonReplace_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (_library.FileName == null) return;
            if (!TryGetSelectedIndex(out int selectedIndex)) return;

            using OpenFileDialog ofd = new OpenFileDialog();
            if (ofd.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(ofd.FileName)) return;

            ImageList.Images.Clear();
            _indexList.Clear();
            SetImageBoxImage(null);
            _selectedImage = null;

            Bitmap newBmp = null;
            try
            {
                short offsetX = _library.Images[selectedIndex]?.OffSetX ?? 0;
                short offsetY = _library.Images[selectedIndex]?.OffSetY ?? 0;

                newBmp = LoadBitmapDetached(ofd.FileName);
                _library.ReplaceImage(selectedIndex, newBmp, offsetX, offsetY);
                newBmp = null;
            }
            catch (Exception ex)
            {
                newBmp?.Dispose();
                MessageBox.Show(ex.ToString(), "替换图片失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            PreviewListView.VirtualListSize = _library.Images.Count;

            try
            {
                if (PreviewListView.Items.Count > 0)
                    PreviewListView.RedrawItems(0, PreviewListView.Items.Count - 1, true);

                if (selectedIndex < 0 || selectedIndex >= _library.Images.Count)
                    return;

                _selectedImage = _library.Images[selectedIndex];
                SetImageBoxImage(_selectedImage.Image);
                RefreshImageBoxLayout(true);
                MarkLibraryDirty("已替换图片，尚未保存。");
            }
            catch (Exception)
            {
                return;
            }
        }

        private void previousImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!PreviewListView.Visible || PreviewListView.Items.Count == 0) return;

            int index = TryGetSelectedIndex(out int selectedIndex)
                ? selectedIndex - 1
                : PreviewListView.Items.Count - 1;

            while (index >= 0)
            {
                PreviewListView.SelectedIndices.Clear();
                PreviewListView.Items[index].Selected = true;
                PreviewListView.Items[index].EnsureVisible();

                if (!IsBlankSlot(_selectedImage) || index == 0)
                    return;

                index--;
            }

            PreviewListView.SelectedIndices.Clear();
            PreviewListView.Items[0].Selected = true;
        }

        private void nextImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!PreviewListView.Visible || PreviewListView.Items.Count == 0) return;

            int index = TryGetSelectedIndex(out int selectedIndex)
                ? selectedIndex + 1
                : 0;

            while (index < PreviewListView.Items.Count)
            {
                PreviewListView.SelectedIndices.Clear();
                PreviewListView.Items[index].Selected = true;
                PreviewListView.Items[index].EnsureVisible();

                if (!IsBlankSlot(_selectedImage) || index == 0)
                    return;

                index++;
            }

            PreviewListView.SelectedIndices.Clear();
            PreviewListView.Items[0].Selected = true;
        }

        // Move Left and Right through images.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Left)
            {
                previousImageToolStripMenuItem_Click(null, null);
                return true;
            }

            if (keyData == Keys.Right)
            {
                nextImageToolStripMenuItem_Click(null, null);
                return true;
            }

            if (keyData == Keys.Up) //Not 100% accurate but works for now.
            {
                if (PreviewListView.Items.Count == 0 || !TryGetSelectedIndex(out int selectedIndex))
                    return base.ProcessCmdKey(ref msg, keyData);

                double d = Math.Floor((double)(PreviewListView.Width / 67));
                int index = selectedIndex - (int)d;

                PreviewListView.SelectedIndices.Clear();
                if (index < 0)
                    index = 0;

                this.PreviewListView.Items[index].Selected = true;

                return true;
            }

            if (keyData == Keys.Down) //Not 100% accurate but works for now.
            {
                if (PreviewListView.Items.Count == 0 || !TryGetSelectedIndex(out int selectedIndex))
                    return base.ProcessCmdKey(ref msg, keyData);

                double d = Math.Floor((double)(PreviewListView.Width / 67));
                int index = selectedIndex + (int)d;

                PreviewListView.SelectedIndices.Clear();
                if (index > PreviewListView.Items.Count - 1)
                    index = PreviewListView.Items.Count - 1;

                this.PreviewListView.Items[index].Selected = true;

                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void buttonSkipNext_Click(object sender, EventArgs e)
        {
            nextImageToolStripMenuItem_Click(null, null);
        }

        private void buttonSkipPrevious_Click(object sender, EventArgs e)
        {
            previousImageToolStripMenuItem_Click(null, null);
        }

        private void checkBoxQuality_CheckedChanged(object sender, EventArgs e)
        {
            ZoomTrackBar_Scroll(null, null);
        }

        private void checkBoxPreventAntiAliasing_CheckedChanged(object sender, EventArgs e)
        {
            ZoomTrackBar_Scroll(null, null);
        }

        private void nudJump_ValueChanged(object sender, EventArgs e)
        {
            int index = (int)nudJump.Value;
            if (index < 0 || index >= PreviewListView.Items.Count)
                return;

            PreviewListView.SelectedIndices.Clear();
            PreviewListView.Items[index].Selected = true;
            PreviewListView.Items[index].EnsureVisible();
        }

        private void OpenLibraryDialog_FileOk(object sender, System.ComponentModel.CancelEventArgs e)
        {

        }

        private void RefreshPreviewMode(bool enableImageEditing)
        {
            if (_library == null) return;

            bool enableOffsetEditing = !radioButtonOverlay.Checked;
            int index = PreviewListView.SelectedIndices.Count > 0
                ? PreviewListView.SelectedIndices[0]
                : -1;

            ImageList.Images.Clear();
            _indexList.Clear();

            PreviewListView.SelectedIndices.Clear();
            PreviewListView.VirtualListSize = 0;
            PreviewListView.VirtualListSize = _library.Images.Count;

            bool hasSelection = index >= 0 && index < PreviewListView.Items.Count;

            OffSetXTextBox.Enabled = enableOffsetEditing && hasSelection;
            OffSetYTextBox.Enabled = enableOffsetEditing && hasSelection;
            AddButton.Enabled = enableImageEditing;
            DeleteButton.Enabled = enableImageEditing && hasSelection;
            buttonReplace.Enabled = enableImageEditing && hasSelection;
            InsertImageButton.Enabled = enableImageEditing && hasSelection;

            if (!hasSelection)
            {
                ClearInterface();
                return;
            }

            PreviewListView.Items[index].Selected = true;
            PreviewListView.Items[index].EnsureVisible();
        }

        private void radioButtonImage_CheckedChanged(object sender, EventArgs e)
        {
            if (!radioButtonImage.Checked) return;

            RefreshPreviewMode(true);
        }

        private void radioButtonShadow_CheckedChanged(object sender, EventArgs e)
        {
            if (!radioButtonShadow.Checked) return;

            RefreshPreviewMode(false);
        }

        private void radioButtonOverlay_CheckedChanged(object sender, EventArgs e)
        {
            if (!radioButtonOverlay.Checked) return;

            RefreshPreviewMode(false);
        }

        private void nudJump_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                int index = (int)nudJump.Value;
                if (TryEnsureImageVisible(index))
                {
                    PreviewListView.SelectedIndices.Clear();
                    PreviewListView.Items[index].Selected = true;
                }

                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }
        public static DialogResult InputBox(string title, string promptText, ref string value)
        {
            using Form form = new Form();
            Label label = new Label();
            TextBox textBox = new TextBox();
            Button buttonOk = new Button();
            Button buttonCancel = new Button();

            form.Text = title;
            label.Text = promptText;
            textBox.Text = value;

            buttonOk.Text = "确定";
            buttonCancel.Text = "取消";
            buttonOk.DialogResult = DialogResult.OK;
            buttonCancel.DialogResult = DialogResult.Cancel;

            label.SetBounds(12, 18, 416, 18);
            textBox.SetBounds(12, 42, 416, 23);
            buttonOk.SetBounds(260, 78, 80, 26);
            buttonCancel.SetBounds(348, 78, 80, 26);

            label.AutoSize = true;
            textBox.Anchor = textBox.Anchor | AnchorStyles.Right;
            buttonOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            buttonCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            form.ClientSize = new Size(440, 116);
            form.Controls.AddRange(new Control[] { label, textBox, buttonOk, buttonCancel });
            form.ClientSize = new Size(Math.Max(440, label.Right + 12), form.ClientSize.Height);
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.MinimizeBox = false;
            form.MaximizeBox = false;
            form.AcceptButton = buttonOk;
            form.CancelButton = buttonCancel;

            DialogResult dialogResult = form.ShowDialog();

            value = textBox.Text;



            return dialogResult;
        }
        private void AddBlanksButton_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (_library.FileName == null) return;

            string value = "0";
            if (InputBox("添加空白图片", "要在图库末尾添加多少张空白图片：", ref value) != DialogResult.OK)
                return;

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int blankCount))
            {
                MessageBox.Show("请输入数字。");
                return;
            }
            if (blankCount <= 0)
            {
                MessageBox.Show("数量必须至少为 1。");
                return;
            }

            _library.AddBlankImages(blankCount);
            PreviewListView.VirtualListSize = _library.Images.Count;
            MarkLibraryDirty("已添加空白图片，尚未保存。");
        }
        private void InsertBlanksButton_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (_library.FileName == null) return;
            if (!TryGetSelectedIndex(out int index)) return;

            string value = "0";
            if (InputBox("插入空白图片", "要在当前选中位置插入多少张空白图片：", ref value) != DialogResult.OK)
                return;

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int blankCount))
            {
                MessageBox.Show("请输入数字。");
                return;
            }
            if (blankCount <= 0)
            {
                MessageBox.Show("数量必须至少为 1。");
                return;
            }

            _library.InsertBlankImages(index, blankCount);

            ImageList.Images.Clear();
            _indexList.Clear();
            PreviewListView.VirtualListSize = _library.Images.Count;
            MarkLibraryDirty("已插入空白图片，尚未保存。");
        }
        private void MergeButton_Click(object sender, EventArgs e)
        {
            if (_library == null) return;
            if (_library.FileName == null) return;

            string value = "1000";
            if (InputBox("合并图库", "每个对象预留多少张图片：", ref value) != DialogResult.OK)
                return;

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int reservedImages))
            {
                MessageBox.Show("请输入数字。");
                return;
            }
            if (reservedImages <= 0)
            {
                MessageBox.Show("数量必须至少为 1。");
                return;
            }

            if (OpenMergeDialog.ShowDialog() != DialogResult.OK) return;

            toolStripProgressBar.Maximum = OpenMergeDialog.FileNames.Length;
            toolStripProgressBar.Value = 0;

            int originalImageCount = _library.Images.Count;

            try
            {
                _library.AddBlanks(reservedImages);

                foreach (string file in OpenMergeDialog.FileNames)
                {
                    if (Path.GetExtension(file).ToUpper() == ".ZL")
                    {
                        using Mir3Library newLib = new Mir3Library(file);
                        foreach (Mir3Library.Mir3Image image in newLib.Images)
                        {
                            _library.Images.Add(CloneMir3Image(image, _library));
                        }

                        _library.AddBlanks(reservedImages);
                    }
                    else if (Path.GetExtension(file).ToUpper() == ".WTL")
                    {
                        WTLLibrary WTLlib = new WTLLibrary(file);
                        try
                        {
                            WTLlib.MergeToMLibrary(_library, reservedImages);
                        }
                        finally
                        {
                            WTLlib.Close();
                        }
                    }
                    else if (Path.GetExtension(file).ToUpper() == ".LIB")
                    {
                        int CurrentVersion = ReadMirLibraryVersion(file);
                        if (CurrentVersion == 1)
                        {
                            MLibrary v1Lib = new MLibrary(file);
                            try
                            {
                                v1Lib.MergeToMLibrary(_library, reservedImages);
                            }
                            finally
                            {
                                v1Lib.Close();
                            }
                        }
                        else
                        {
                            MLibraryV2 v2Lib = new MLibraryV2(file);
                            try
                            {
                                v2Lib.MergeToMLibrary(_library, reservedImages);
                            }
                            finally
                            {
                                v2Lib.Close();
                            }
                        }
                    }
                    else
                    {
                        WeMadeLibrary WILlib = new WeMadeLibrary(file);
                        try
                        {
                            WILlib.MergeToMLibrary(_library, reservedImages);
                        }
                        finally
                        {
                            WILlib.Close();
                        }

                    }
                    toolStripProgressBar.Value++;
                }
            }
            catch (Exception ex)
            {
                RollBackImages(originalImageCount);
                ImageList.Images.Clear();
                _indexList.Clear();
                PreviewListView.VirtualListSize = _library.Images.Count;
                MessageBox.Show("合并图库失败，已撤销本次合并新增的图片，未保存图库。\r\n\r\n" + ex, "合并图库", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                toolStripProgressBar.Value = 0;
            }

            ImageList.Images.Clear();
            _indexList.Clear();
            PreviewListView.VirtualListSize = _library.Images.Count;

            if (!TrySaveLibrary(_library.FileName, "合并图库"))
            {
                MarkLibraryDirty("合并图库已修改，但保存失败。");
                return;
            }

            MessageBox.Show(string.Format("已成功合并 {0} 个图库。",
                (OpenMergeDialog.FileNames.Length).ToString()));
        }

        private static Bitmap CloneBitmap(Bitmap image)
        {
            return image == null ? new Bitmap(1, 1) : new Bitmap(image);
        }

        private static Bitmap LoadBitmapDetached(string fileName)
        {
            using Bitmap source = new Bitmap(fileName);
            ValidateImportedBitmapDimensions(source, Path.GetFileName(fileName));
            return new Bitmap(source);
        }

        private static void ValidateImportedBitmapDimensions(Bitmap bitmap, string description)
        {
            if (bitmap == null)
                throw new InvalidDataException($"{description}: 图片为空。");

            if (bitmap.Width <= 0 ||
                bitmap.Height <= 0 ||
                bitmap.Width > short.MaxValue ||
                bitmap.Height > short.MaxValue ||
                (long)Pad4(bitmap.Width) * Pad4(bitmap.Height) > MaxImportedImagePixels)
                throw new InvalidDataException($"{description}: 图片尺寸无效或过大: {bitmap.Width}x{bitmap.Height}。");
        }

        private static bool CanAllocatePreviewBitmap(long width, long height)
        {
            return width > 0 &&
                   height > 0 &&
                   width <= int.MaxValue &&
                   height <= int.MaxValue &&
                   width * height <= MaxImportedImagePixels;
        }

        private static int Pad4(int value)
        {
            return value + (4 - value % 4) % 4;
        }

        private static Bitmap CloneNullableBitmap(Bitmap image)
        {
            return image == null ? null : new Bitmap(image);
        }

        private static Mir3Library.Mir3Image CloneMir3Image(Mir3Library.Mir3Image source, Mir3Library targetLibrary)
        {
            if (source?.Image == null)
                return null;

            return new Mir3Library.Mir3Image(
                CloneNullableBitmap(source.Image),
                CloneNullableBitmap(source.ShadowImage),
                CloneNullableBitmap(source.OverlayImage),
                targetLibrary.Version,
                targetLibrary.UseBlackKeyTransparency)
            {
                OffSetX = source.OffSetX,
                OffSetY = source.OffSetY,
                ShadowOffSetX = source.ShadowOffSetX,
                ShadowOffSetY = source.ShadowOffSetY,
                ShadowType = source.ShadowType
            };
        }

        private static bool IsBlankSlot(Mir3Library.Mir3Image image)
        {
            return image == null || (image.Width <= 1 && image.Height <= 1);
        }

        private static bool IsSameFilePath(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            try
            {
                string normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static int ReadMirLibraryVersion(string fileName)
        {
            using FileStream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader reader = new BinaryReader(stream);

            return reader.ReadInt32();
        }

        private static void ConvertLegacyLibraryFile(string fileName)
        {
            string extension = Path.GetExtension(fileName).ToUpperInvariant();

            if (extension == ".WTL")
            {
                WTLLibrary library = new WTLLibrary(fileName);
                try
                {
                    library.ToMLibrary();
                }
                finally
                {
                    library.Close();
                }

                return;
            }

            if (extension == ".LIB")
            {
                int currentVersion = ReadMirLibraryVersion(fileName);
                if (currentVersion == 1)
                {
                    MLibrary library = new MLibrary(fileName);
                    try
                    {
                        library.ToMLibrary();
                    }
                    finally
                    {
                        library.Close();
                    }
                }
                else
                {
                    MLibraryV2 library = new MLibraryV2(fileName);
                    try
                    {
                        library.ToMLibrary();
                    }
                    finally
                    {
                        library.Close();
                    }
                }

                return;
            }

            WeMadeLibrary wilLibrary = new WeMadeLibrary(fileName);
            try
            {
                wilLibrary.ToMLibrary();
            }
            finally
            {
                wilLibrary.Close();
            }
        }

        private static void AddImageImportFailure(List<string> failures, ref int failureCount, string fileName, Exception ex)
        {
            failureCount++;
            if (failures.Count >= 20) return;

            failures.Add($"{Path.GetFileName(fileName)}: {ex.Message}");
        }

        private void ShowImageImportFailures(string title, List<string> failures, int failureCount)
        {
            if (failures == null || failureCount == 0) return;

            string message = string.Join("\r\n", failures);
            if (failureCount > failures.Count)
                message += $"\r\n... 另有 {failureCount - failures.Count} 项未显示。";

            MessageBox.Show(
                message,
                $"{title}失败 {failureCount} 项",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private bool TrySaveLibrary(string path, string title)
        {
            if (_library == null) return false;

            try
            {
                _library.Save(path);
                _libraryDirty = false;
                UpdateWindowTitle();
                toolStripStatusLabel.ForeColor = SystemColors.ControlText;
                toolStripStatusLabel.Text = "图库已保存。";
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "图库保存失败，当前内存中的修改尚未写入文件。\r\n\r\n" + ex,
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }
        }

        private void MarkLibraryDirty(string message)
        {
            if (_library == null) return;

            _libraryDirty = true;
            UpdateWindowTitle();

            if (!string.IsNullOrWhiteSpace(message))
            {
                toolStripStatusLabel.ForeColor = Color.Red;
                toolStripStatusLabel.Text = message;
            }
        }

        private void UpdateWindowTitle()
        {
            string title = _library?.FileName;
            Text = string.IsNullOrWhiteSpace(title)
                ? "Mir3 图库编辑器"
                : title + (_libraryDirty ? " *" : string.Empty);
        }

        private bool ConfirmSaveOrDiscardLibraryChanges()
        {
            if (!_libraryDirty || _library == null)
                return true;

            DialogResult result = MessageBox.Show(
                this,
                "当前图库有未保存修改，是否先保存？",
                "未保存的图库",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Cancel)
                return false;

            if (result == DialogResult.No)
                return true;

            string path = _library.FileName;
            if (string.IsNullOrWhiteSpace(path))
            {
                if (SaveLibraryDialog.ShowDialog(this) != DialogResult.OK)
                    return false;

                path = SaveLibraryDialog.FileName;
            }

            return TrySaveLibrary(path, "保存图库");
        }

        private void RollBackImages(int count)
        {
            while (_library != null && _library.Images.Count > count)
                _library.RemoveImage(_library.Images.Count - 1);
        }

        private void ReleaseCurrentLibrary()
        {
            SetImageBoxImage(null);
            _selectedImage = null;
            _exportImage = null;

            PreviewListView.SelectedIndices.Clear();
            PreviewListView.VirtualListSize = 0;
            ImageList.Images.Clear();
            _indexList.Clear();

            _library?.Dispose();
            _library = null;
            _libraryDirty = false;
        }

    }
}
