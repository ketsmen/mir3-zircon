using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinSCP;

namespace PatchManager
{
    public partial class PMain : System.Windows.Forms.Form
    {
        public const string PListFileName = "PList.Bin";

        public const string ClientFileName = "Zircon.exe";

        public const string TempDownloadDirectory = "In";

        public long TotalUpload, TotalProgress, CurrentProgress, TotalProgressPercent;
        public long Speed;
        public bool Error;

        public PMain()
        {
            InitializeComponent();
        }

        #region Event Handlers
        private void PMain_Load(object sender, EventArgs e)
        {
            CleanClientButtonEdit.Text = Config.CleanClient;
            HostTextEdit.Text = Config.Host;
            UseLoginCheckEdit.Checked = Config.UseLogin;
            UsernameTextEdit.Text = Config.Username;
            PasswordTextEdit.Text = Config.Password;
            ProtocolDropDown.Text = Config.Protocol;
        }

        private void CleanClientButtonEdit_EditValueChanged(object sender, EventArgs e)
        {
            Config.CleanClient = CleanClientButtonEdit.Text;
        }

        private void HostTextEdit_EditValueChanged(object sender, EventArgs e)
        {
            Config.Host = HostTextEdit.Text;
        }

        private void UseLoginCheckEdit_CheckedChanged(object sender, EventArgs e)
        {
            Config.UseLogin = UseLoginCheckEdit.Checked;
        }

        private void UsernameTextEdit_EditValueChanged(object sender, EventArgs e)
        {
            Config.Username = UsernameTextEdit.Text;
        }

        private void PasswordTextEdit_EditValueChanged(object sender, EventArgs e)
        {
            Config.Password = PasswordTextEdit.Text;
        }

        private void ProtocolDropDown_SelectedIndexChanged(object sender, EventArgs e)
        {
            Config.Protocol = ProtocolDropDown.Text;
        }

        private void UploadPatchButton_Click(object sender, EventArgs e)
        {
            CreatePatch();
        }
        #endregion

        private void InterfaceLock(bool enabled)
        {
            CleanClientButtonEdit.Enabled = enabled;
            HostTextEdit.Enabled = enabled;
            UseLoginCheckEdit.Enabled = enabled;
            UsernameTextEdit.Enabled = enabled;
            PasswordTextEdit.Enabled = enabled;
            UploadPatchButton.Enabled = enabled;
            ProtocolDropDown.Enabled = enabled;
        }


        private async void CreatePatch()
        {
            InterfaceLock(false);
            Error = false;
            TotalUpload = 0;
            TotalProgress = 0;
            TotalProgressPercent = 0;
            Speed = 0;
            bool completed = false;

            IProgress<string> progress = new Progress<string>(s => StatusLabel.Text = s);

            try
            {
                List<PatchInformation> currentVersion = await Task.Run(() => CreateVersion(progress));
                if (currentVersion == null)
                {
                    Error = true;
                    return;
                }

                ValidatePatchArchiveNames(currentVersion);

                List<PatchInformation> liveVersion = await Task.Run(() => GetPatchInformation(progress));

                List<PatchInformation> patch = await Task.Run(() => CalculatePatch(currentVersion, liveVersion, progress));

                bool uploaded = true;

                if (patch.Count > 0)
                {
                    Task<bool> task = Task.Run(() => UploadFiles(patch, progress));

                    while (!task.IsCompleted)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1));
                        CreateSizeLabel();
                    }

                    uploaded = await task;
                }

                if (!uploaded)
                    Error = true;

                if (!Error)
                {
                    await Task.Run(() => SaveVersion(currentVersion));
                    completed = true;
                }
            }
            catch (Exception ex)
            {
                Error = true;
                progress.Report(ex.Message);
                MessageBox.Show(this, ex.ToString(), "创建补丁失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                InterfaceLock(true);

                TryDeleteDirectory(".\\Patch\\");
                TryDeleteDirectory(TempDownloadDirectory);

                if (completed && !Error)
                {
                    StatusLabel.Text = "Complete.";
                    UploadSizeLabel.Text = "Complete.";
                    UploadSpeedLabel.Text = "Complete.";
                }
                else
                {
                    StatusLabel.Text = "Failed.";
                    UploadSizeLabel.Text = "Failed.";
                    UploadSpeedLabel.Text = "Failed.";
                }
            }
        }

        private void CreateSizeLabel()
        {
            const decimal KB = 1024;
            const decimal MB = KB * 1024;
            const decimal GB = MB * 1024;

            long progress = TotalProgress;

            StringBuilder text = new StringBuilder();

            if (progress > GB)
                text.Append($"{progress / GB:#,##0.0}GB");
            else if (progress > MB)
                text.Append($"{progress / MB:#,##0.0}MB");
            else if (progress > KB)
                text.Append($"{progress / KB:#,##0}KB");
            else
                text.Append($"{progress:#,##0}B");

            if (TotalUpload > GB)
                text.Append($" / {TotalUpload / GB:#,##0.0}GB");
            else if (TotalUpload > MB)
                text.Append($" / {TotalUpload / MB:#,##0.0}MB");
            else if (TotalUpload > KB)
                text.Append($" / {TotalUpload / KB:#,##0}KB");
            else
                text.Append($" / {TotalUpload:#,##0}B");

            UploadSizeLabel.Text = text.ToString();

            if (TotalUpload > 0)
                TotalProgressBar.Value = (int)Math.Max(0, Math.Min(100, TotalProgressPercent));

            long speed = Speed;

            if (speed > GB)
                UploadSpeedLabel.Text = $"{speed / GB:#,##0.0}GBps";
            else if (speed > MB)
                UploadSpeedLabel.Text = $"{speed / MB:#,##0.0}MBps";
            else if (speed > KB)
                UploadSpeedLabel.Text = $"{speed / KB:#,##0}KBps";
            else
                UploadSpeedLabel.Text = $"{speed:#,##0}Bps";
        }


        private void OpenSession(Session session)
        {
            if (session.Opened) return;

            Uri uri = null;

            if (!string.IsNullOrEmpty(Config.Host))
            {
                uri = new Uri(Config.Host);
            }

            if (!Protocol.TryParse(Config.Protocol, true, out Protocol protocol))
            {
                protocol = Protocol.Ftp;
            }

            SessionOptions sessionOptions = new SessionOptions
            {
                Protocol = protocol,
                HostName = uri.Host,
                UserName = Config.Username,
                Password = Config.Password
            };

            if (sessionOptions.Protocol == Protocol.Sftp)
            {
                var fingerprint = session.ScanFingerprint(sessionOptions, "SHA-256");

                sessionOptions.SshHostKeyFingerprint = fingerprint;
            }

            session.Open(sessionOptions);
        }


        private List<PatchInformation> CreateVersion(IProgress<string> progress)
        {
            try
            {
                string[] files = Directory.GetFiles(Config.CleanClient, "*.*", SearchOption.AllDirectories);

                PatchInformation[] list = new PatchInformation[files.Length];
                ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = 8 };
                int count = 0;
                Parallel.For(0, files.Length, po, i =>
                {
                    list[i] = new PatchInformation(files[i]);
                    progress.Report($"Version Created: File {Interlocked.Increment(ref count)} of {files.Length}");
                });

                return list.ToList();
            }
            catch (Exception ex)
            {
                progress.Report(ex.Message);
            }

            return null;
        }
        private List<PatchInformation> GetPatchInformation(IProgress<string> progress)
        {
            using Session session = new Session();
            OpenSession(session);

            try
            {
                progress.Report("Downloading Patch Information");

                if (!Directory.Exists(TempDownloadDirectory))
                    Directory.CreateDirectory(TempDownloadDirectory);

                var rootPath = (new Uri(Config.Host)).AbsolutePath;

                TransferOptions transferOptions = new TransferOptions
                {
                    TransferMode = TransferMode.Binary,
                    OverwriteMode = OverwriteMode.Overwrite
                };

                string remotePatchListPath = CombineRemotePath(rootPath, PListFileName);
                if (!session.FileExists(remotePatchListPath))
                {
                    progress.Report("Patch Information Not Found");

                    return null;
                }

                var result = session.GetFiles(remotePatchListPath, Path.Combine(TempDownloadDirectory, PListFileName), options: transferOptions);
                result.Check();

                using BinaryReader reader = new BinaryReader(File.Open(Path.Combine(TempDownloadDirectory, PListFileName), FileMode.Open, FileAccess.Read));

                List<PatchInformation> list = new List<PatchInformation>();

                while (reader.BaseStream.Position < reader.BaseStream.Length)
                    list.Add(new PatchInformation(reader));

                return list;
            }
            catch (Exception ex)
            {
                progress.Report(ex.Message);
            }

            return null;
        }
        private List<PatchInformation> CalculatePatch(List<PatchInformation> current, List<PatchInformation> live, IProgress<string> progress)
        {
            List<PatchInformation> patch = new List<PatchInformation>();

            if (current == null) return patch;

            int count = 0;

            Parallel.For(0, current.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            {
                PatchInformation file = current[i];
                PatchInformation lFile = live?.FirstOrDefault(x => x.FileName == file.FileName);

                if (lFile != null && IsMatch(lFile.CheckSum, file.CheckSum))
                {
                    file.CompressedLength = lFile.CompressedLength;
                    return;
                }

                if (!Directory.Exists(".\\Patch\\"))
                    Directory.CreateDirectory(".\\Patch\\");

                string webFileName = GetPatchArchiveName(file.FileName);

                file.UploadFileName = ".\\Patch\\" + webFileName;
                file.PatchFileName = Path.Combine(Directory.GetCurrentDirectory(), $"Patch\\{webFileName}");

                file.CompressedLength = Compress(GetCleanClientFilePath(file.FileName), file.PatchFileName);
                Interlocked.Add(ref TotalUpload, file.CompressedLength);

                lock (patch)
                    patch.Add(file);

                progress.Report($"File Created: {Interlocked.Increment(ref count)} of {current.Count}");

            });

            return patch;
        }

        private bool UploadFiles(List<PatchInformation> patch, IProgress<string> progress)
        {
            var rootPath = (new Uri(Config.Host)).AbsolutePath;

            using Session session = new Session();

            int current = 0;

            session.FileTransferProgress += (o, e) =>
            {
                Speed = e.CPS;

                TotalProgress = (long)(e.OverallProgress * TotalUpload);
                TotalProgressPercent = (long)(e.OverallProgress * 100);
            };

            session.FileTransferred += (o, e) =>
            {
                progress.Report($"Files Uploaded: {Interlocked.Increment(ref current)} of {patch.Count}");
            };

            OpenSession(session);

            TransferOptions transferOptions = new TransferOptions
            {
                TransferMode = TransferMode.Binary,
                OverwriteMode = OverwriteMode.Overwrite
            };

            if (!session.FileExists(rootPath))
            {
                session.CreateDirectory(rootPath);
            }

            var result = session.PutFilesToDirectory(".\\Patch\\", rootPath, options: transferOptions);
            result.Check();

            return true;
        }

        private void SaveVersion(List<PatchInformation> current)
        {
            if (!Directory.Exists(".\\Patch\\"))
                Directory.CreateDirectory(".\\Patch\\");

            using (MemoryStream mStream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(mStream))
            {
                foreach (PatchInformation info in current)
                    info.Save(writer);

                var tempFilePath = Path.Combine(".\\Patch\\", PListFileName).Replace(@"\", "/");

                WriteAllBytesAtomically(tempFilePath, mStream.ToArray());

                var rootPath = (new Uri(Config.Host)).AbsolutePath;

                using Session session = new Session();

                session.FileTransferProgress += (o, e) =>
                {
                };

                session.FileTransferred += (o, e) =>
                {
                };

                OpenSession(session);

                TransferOptions transferOptions = new TransferOptions
                {
                    TransferMode = TransferMode.Binary,
                    OverwriteMode = OverwriteMode.Overwrite
                };

                TransferEventArgs result = session.PutFileToDirectory(tempFilePath, rootPath, options: transferOptions);
                if (result.Error != null)
                    throw result.Error;
            }
        }


        #region Helpers
        public static bool IsMatch(byte[] a, byte[] b, long offSet = 0)
        {
            if (b == null || a == null || b.Length + offSet > a.Length || offSet < 0) return false;

            for (int i = 0; i < b.Length; i++)
                if (a[offSet + i] != b[i])
                    return false;

            return true;
        }
        private static long Compress(string sourceFile, string destFile)
        {
            string fullPath = Path.GetFullPath(destFile);
            string tempPath = GetTempWritePath(fullPath);

            try
            {
                using (FileStream tofile = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (FileStream fromfile = File.OpenRead(sourceFile))
                {
                    using (GZipStream gStream = new GZipStream(tofile, CompressionMode.Compress, true))
                        fromfile.CopyTo(gStream);
                }

                long length = new FileInfo(tempPath).Length;
                ReplaceOrMoveTempFile(tempPath, fullPath);
                return length;
            }
            catch
            {
                TryDeleteTempFile(tempPath);
                throw;
            }
        }

        private static void WriteAllBytesAtomically(string path, byte[] data)
        {
            string fullPath = Path.GetFullPath(path);
            string tempPath = GetTempWritePath(fullPath);

            try
            {
                File.WriteAllBytes(tempPath, data);
                ReplaceOrMoveTempFile(tempPath, fullPath);
            }
            catch
            {
                TryDeleteTempFile(tempPath);
                throw;
            }
        }

        private static void ReplaceOrMoveTempFile(string tempPath, string destinationPath)
        {
            if (File.Exists(destinationPath))
                File.Replace(tempPath, destinationPath, null);
            else
                File.Move(tempPath, destinationPath);
        }

        private static string GetTempWritePath(string fullPath)
        {
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
            return Path.Combine(tempDirectory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        }

        private static string GetPatchArchiveName(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidDataException("Patch file name cannot be empty.");

            string archiveName = relativePath.Replace('\\', '-').Replace('/', '-') + ".gz";
            if (archiveName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException($"Patch file name contains invalid characters: {relativePath}");

            return archiveName;
        }

        private static void ValidatePatchArchiveNames(IEnumerable<PatchInformation> version)
        {
            Dictionary<string, string> archives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (PatchInformation file in version ?? Enumerable.Empty<PatchInformation>())
            {
                string archiveName = GetPatchArchiveName(file.FileName);
                if (!archives.TryGetValue(archiveName, out string existing))
                {
                    archives[archiveName] = file.FileName;
                    continue;
                }

                if (!string.Equals(existing, file.FileName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Patch archive name conflict: `{existing}` and `{file.FileName}` both map to `{archiveName}`.");
            }
        }

        private static string GetCleanClientFilePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidDataException("Patch file path cannot be empty.");
            if (Path.IsPathRooted(relativePath))
                throw new InvalidDataException($"Patch file path must be relative: {relativePath}");

            string rootPath = EnsureTrailingDirectorySeparator(Path.GetFullPath(Config.CleanClient));
            string fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));

            if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Patch file path is outside clean client root: {relativePath}");

            return fullPath;
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                return path;

            return path + Path.DirectorySeparatorChar;
        }

        private static string CombineRemotePath(string directory, string fileName)
        {
            string remoteDirectory = string.IsNullOrWhiteSpace(directory) ? "/" : directory.Replace('\\', '/');
            string remoteFileName = (fileName ?? string.Empty).Replace('\\', '/').TrimStart('/');

            if (remoteDirectory == "/")
                return "/" + remoteFileName;

            return remoteDirectory.TrimEnd('/') + "/" + remoteFileName;
        }

        private static void TryDeleteTempFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
            }
        }
        #endregion

        private void CleanClientBrowseButton_Click(object sender, EventArgs e)
        {
            using FolderBrowserDialog dialog = new FolderBrowserDialog();

            if (dialog.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                CleanClientButtonEdit.Text = dialog.SelectedPath;
        }
    }
}
