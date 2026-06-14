using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ImageManager
{
    public partial class IMain : Form
    {
        public int TotleCount;
        public int ProgressCount;

        public IMain()
        {
            InitializeComponent();
        }

        private async void ConvertLibrariesButton_Click(object sender, EventArgs e)
        {
            DirectoryInfo directory = new DirectoryInfo(SelectedFolderButtonEdit.Text);

            if (!directory.Exists) return;

            FileInfo[] targets = directory.GetFiles("*.WTL", SubFoldersCheckEdit.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            TotleCount = targets.Length;
            ProgressCount = 0;

            if (targets.Length == 0) return;

            Task task = Task.Run(() =>
            {
                ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = 10 };
                Parallel.For(0, targets.Length, po, i =>
                {
                    if (!targets[i].FullName.EndsWith("_S.wtl") && !targets[i].FullName.StartsWith("MonS-"))
                    {
                        using (WTLLibrary wtl = new WTLLibrary(targets[i].FullName))
                        {
                            Mir3Library library = wtl.Convert();

                            library.Save(Path.ChangeExtension(targets[i].FullName, @".Zl"));
                        }
                    }
                    Interlocked.Increment(ref ProgressCount);
                });
            });

            while (!task.IsCompleted)
            {
                UpdateProgress();

                await Task.Delay(100);
            }

            UpdateProgress();
            ProgressLabel.Text = "已完成。";
        }

        public void UpdateProgress()
        {
            ProgressLabel.Text = $"{ProgressCount} of {TotleCount}.";
            if (TotleCount > 0)
                progressBarControl1.Value = Math.Max(0, Math.Min(100, ProgressCount * 100 / TotleCount));
        }

        private void SelectedFolderButtonEdit_ButtonClick(object sender, EventArgs e)
        {
            if (FolderDialog.ShowDialog() == DialogResult.OK)
                SelectedFolderButtonEdit.Text = FolderDialog.SelectedPath;
        }

        private async void CreaetLibrariesButton_Click(object sender, EventArgs e)
        {
            DirectoryInfo directory = new DirectoryInfo(SelectedFolderButtonEdit.Text);

            if (!directory.Exists) return;

            DirectoryInfo[] targets = directory.GetDirectories("*.*", SubFoldersCheckEdit.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            if (targets.Length == 0)
                targets = new[] { directory };

            TotleCount = targets.Length;
            ProgressCount = 0;

            if (targets.Length == 0) return;

            Task task = Task.Run(() =>
            {
                ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = 15 };

                Parallel.For(0, targets.Length, po, i =>
                {
                    if (!File.Exists(targets[i].FullName + "\\Placements.txt")) return;

                    string[] placements = File.ReadAllLines(targets[i].FullName + "\\Placements.txt");

                    Mir3Library lib = new Mir3Library { Images = new Mir3Image[placements.Length] };

                    ParallelOptions po1 = new ParallelOptions { MaxDegreeOfParallelism = 15 };
                    Parallel.For(0, placements.Length, po1, index =>
                    {
                        string fileName = string.Format(targets[i].FullName + "\\{0:00000}.bmp", index);

                        if (!File.Exists(fileName)) return;

                        string[] placement = placements[index].Split(',');

                        short x = short.Parse(placement[0]);
                        short y = short.Parse(placement[1]);

                        using (Bitmap image = new Bitmap(fileName))
                        {
                            lib.Images[index] = new Mir3Image
                            {
                                Width = (short)image.Width,
                                Height = (short)image.Height,
                                OffSetX = x,
                                OffSetY = y,
                                Data = MImage.GetBytes(image)
                            };
                        }
                    });

                    lib.Save(targets[i].FullName + ".Zl");

                    Interlocked.Increment(ref ProgressCount);
                });
            });

            while (!task.IsCompleted)
            {
                UpdateProgress();

                await Task.Delay(100);
            }

            UpdateProgress();
            ProgressLabel.Text = "已完成。";
        }

        private void simpleButton1_Click(object sender, EventArgs e)
        {
            DirectoryInfo dInfo = new DirectoryInfo(@"C:\Zircon Server\Data Works\Game\MiniMap");
            FileInfo[] files = dInfo.GetFiles("*.BMP");

            foreach (FileInfo file in files)
            {
                Bitmap image = new Bitmap(file.FullName);

                if (image.Width <= 800 && image.Height <= 600)
                {
                    image.Dispose();
                    continue;
                }

                Bitmap nImage = new Bitmap(image, new Size(Math.Min(image.Width, 800), Math.Min(image.Height, 600)));

                image.Dispose();
                nImage.Save(file.FullName, ImageFormat.Bmp);

                nImage.Dispose();
            }
        }

        private void IMain_Load(object sender, EventArgs e)
        {
        }
    }
}
