using Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Launcher
{
    public partial class LMain : Form
    {
        public const string PListFileName = "PList.Bin";
        public const string ClientPath = ".\\";
        public const string ClientFileName = "Zircon.exe";

        public DateTime LastSpeedCheck;
        public long TotalDownload, TotalProgress, CurrentProgress, LastDownloadProcess;
        public bool NeedUpdate;

        public static bool HasError;

        public LMain()
        {
            InitializeComponent();

        }

        private void LMain_Load(object sender, EventArgs e)
        {
            CheckPatch(false);
        }

        private void PatchNotesHyperlinkControl_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            PatchNotesHyperlinkControl.LinkVisited = true;
            Process.Start(new ProcessStartInfo("https://www.zirconserver.com") { UseShellExecute = true });
        }

        private void RepairButton_Click(object sender, EventArgs e)
        {
            CheckPatch(true);
        }

        private async void CheckPatch(bool repair)
        {
            HasError = false;
            RepairButton.Enabled = false;
            StartGameButton.Enabled = false;
            TotalDownload = 0;
            TotalProgress = 0;
            CurrentProgress = 0;
            LastDownloadProcess = 0;
            TotalProgressBar.Value = 0;
            LastSpeedCheck = Time.Now;
            NeedUpdate = false;

            Progress<string> progress = new Progress<string>(s => StatusLabel.Text = s);

            try
            {
                List<PatchInformation> liveVersion = await GetPatchInformation(progress);

                if (liveVersion == null)
                {
                    DownloadSizeLabel.Text = "Downloading failed.";
                    return;
                }

                ValidatePatchArchiveNames(liveVersion);

                List<PatchInformation> currentVersion = repair ? null : await LoadVersion(progress);
                List<PatchInformation> patch = await CalculatePatch(liveVersion, currentVersion, progress);

                StatusLabel.Text = "Downloading";
                CreateSizeLabel();

                Task task = DownloadPatch(patch, progress, new Progress<int>(percent =>
                {
                    // Update progress bar or label with the percentage downloaded
                    CreateSizeLabel();
                }));

                await task;

                CreateSizeLabel();

                if (!HasError)
                    SaveVersion(liveVersion);

                if (HasError)
                {
                    StatusLabel.Text = "Completed with errors";
                    DownloadSizeLabel.Text = "Retry required.";
                    DownloadSpeedLabel.Text = "Complete.";
                }
                else
                {
                    StatusLabel.Text = "Complete";
                    DownloadSizeLabel.Text = "Complete.";
                    DownloadSpeedLabel.Text = "Complete.";
                }

                TryDeleteDirectory(ClientPath + "Patch\\");

                if (!HasError && NeedUpdate)
                {
                    WriteAllBytesAtomically(Program.PatcherFileName, Properties.Resources.Patcher);
                    Process.Start(Program.PatcherFileName, $"\"{Application.ExecutablePath}.tmp\" \"{Application.ExecutablePath}\"");
                    Environment.Exit(0);
                }

                try
                {
                    if (File.Exists(Program.PatcherFileName))
                        File.Delete(Program.PatcherFileName);
                }
                catch (Exception) { }
            }
            catch (Exception ex)
            {
                HasError = true;
                StatusLabel.Text = ex.Message;
                DownloadSizeLabel.Text = "Failed.";
                DownloadSpeedLabel.Text = "Failed.";
                MessageBox.Show(this, ex.ToString(), "Patch Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                RepairButton.Enabled = true;
                StartGameButton.Enabled = true;
            }
        }
        private void CreateSizeLabel()
        {
            const decimal KB = 1024;
            const decimal MB = KB * 1024;
            const decimal GB = MB * 1024;

            long progress = TotalProgress + CurrentProgress;

            StringBuilder text = new StringBuilder();

            if (progress > GB)
                text.Append($"{progress / GB:#,##0.0}GB");
            else if (progress > MB)
                text.Append($"{progress / MB:#,##0.0}MB");
            else if (progress > KB)
                text.Append($"{progress / KB:#,##0}KB");
            else
                text.Append($"{progress:#,##0}B");

            if (TotalDownload > GB)
                text.Append($" / {TotalDownload / GB:#,##0.0}GB");
            else if (TotalDownload > MB)
                text.Append($" / {TotalDownload / MB:#,##0.0}MB");
            else if (TotalDownload > KB)
                text.Append($" / {TotalDownload / KB:#,##0}KB");
            else
                text.Append($" / {TotalDownload:#,##0}B");

            DownloadSizeLabel.Text = text.ToString();

            if (TotalDownload > 0)
                TotalProgressBar.Value = Math.Max(0, Math.Min(100, (int)(progress * 100 / TotalDownload)));

            long speed = 0;
            DateTime now = Time.Now;
            long elapsedTicks = now.Ticks - LastSpeedCheck.Ticks;
            if (elapsedTicks > 0)
            {
                speed = Math.Max(0, progress - LastDownloadProcess) * TimeSpan.TicksPerSecond / elapsedTicks;
                LastDownloadProcess = progress;
                LastSpeedCheck = now;
            }

            if (speed > GB)
                DownloadSpeedLabel.Text = $"{speed / GB:#,##0.0}GBps";
            else if (speed > MB)
                DownloadSpeedLabel.Text = $"{speed / MB:#,##0.0}MBps";
            else if (speed > KB)
                DownloadSpeedLabel.Text = $"{speed / KB:#,##0}KBps";
            else
                DownloadSpeedLabel.Text = $"{speed:#,##0}Bps";
        }

        private async Task<List<PatchInformation>> LoadVersion(IProgress<string> progress)
        {
            List<PatchInformation> list = new List<PatchInformation>();

            try
            {
                if (File.Exists(ClientPath + "Version.bin"))
                {
                    using (MemoryStream mStream = new MemoryStream(await File.ReadAllBytesAsync(ClientPath + "Version.bin")))
                    using (BinaryReader reader = new BinaryReader(mStream))
                        while (reader.BaseStream.Position < reader.BaseStream.Length)
                            list.Add(new PatchInformation(reader));

                    progress.Report("Calculating Patch.");
                    return list;
                }

                progress.Report("Version Info is missing, Running Repairing");
            }
            catch (Exception ex)
            {
                progress.Report(ex.Message);
            }

            return null;
        }
        private async Task<List<PatchInformation>> GetPatchInformation(IProgress<string> progress)
        {
            try
            {
                progress.Report("Downloading Patch Information");

                // Create a handler with explicit TLS settings
                var handler = new HttpClientHandler
                {
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                                 | System.Security.Authentication.SslProtocols.Tls13
                };

                using (HttpClient client = new HttpClient(handler))
                {
                    if (Config.UseLogin)
                    {
                        var byteArray = Encoding.ASCII.GetBytes($"{Config.Username}:{Config.Password}");
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                    }

                    using (HttpResponseMessage response = await client.GetAsync(Config.Host + PListFileName + "?nocache=" + Guid.NewGuid().ToString("N")))
                    {
                        response.EnsureSuccessStatusCode();

                        byte[] data = await response.Content.ReadAsByteArrayAsync();
                        using (MemoryStream mStream = new MemoryStream(data))
                        using (BinaryReader reader = new BinaryReader(mStream))
                        {
                            List<PatchInformation> list = new List<PatchInformation>();

                            while (reader.BaseStream.Position < reader.BaseStream.Length)
                                list.Add(new PatchInformation(reader));

                            return list;
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                progress.Report(ex.Message);
            }

            return null;
        }
        private async Task<List<PatchInformation>> CalculatePatch(IReadOnlyList<PatchInformation> list, List<PatchInformation> current, IProgress<string> progress)
        {
            List<PatchInformation> patch = new List<PatchInformation>();

            if (list == null) return patch;

            for (int i = 0; i < list.Count; i++)
            {
                progress.Report($"Files Checked: {i + 1} of {list.Count}");

                PatchInformation file = list[i];
                if (current != null && current.Any(x => x.FileName == file.FileName && IsMatch(x.CheckSum, file.CheckSum))) continue;

                string clientFilePath = GetClientFilePath(file.FileName);
                if (File.Exists(clientFilePath))
                {
                    byte[] CheckSum;
                    using (MD5 md5 = MD5.Create())
                    {
                        using (FileStream stream = File.OpenRead(clientFilePath))
                            CheckSum = await md5.ComputeHashAsync(stream);
                    }

                    if (IsMatch(CheckSum, file.CheckSum))
                        continue;
                }

                patch.Add(file);
                TotalDownload += file.CompressedLength;
            }

            return patch;
        }

        public bool IsMatch(byte[] a, byte[] b, long offSet = 0)
        {
            if (b == null || a == null || b.Length + offSet > a.Length || offSet < 0) return false;

            for (int i = 0; i < b.Length; i++)
                if (a[offSet + i] != b[i])
                    return false;

            return true;
        }

        private void SaveVersion(List<PatchInformation> version)
        {
            WriteFileAtomically(ClientPath + "Version.bin", tempPath =>
            {
                using (FileStream fStream = File.Create(tempPath))
                using (BinaryWriter writer = new BinaryWriter(fStream))
                {
                    foreach (PatchInformation info in version)
                        info.Save(writer);
                }
            });
        }

        private async Task DownloadPatch(List<PatchInformation> patch, IProgress<string> progress, IProgress<int> downloadProgress)
        {
            List<Task> tasks = new List<Task>();

            foreach (PatchInformation file in patch)
            {
                if (!await Download(file, downloadProgress)) continue;

                tasks.Add(Extract(file));
            }

            if (tasks.Count == 0) return;

            progress.Report("Downloaded, Extracting.");

            await Task.WhenAll(tasks);
        }


        private void StartGameButton_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start(ClientPath + ClientFileName);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private async Task<bool> Download(PatchInformation file, IProgress<int> progress)
        {
            string webFileName = GetPatchArchiveName(file.FileName);

            try
            {
                // Create a handler with explicit TLS settings
                var handler = new HttpClientHandler
                {
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                                 | System.Security.Authentication.SslProtocols.Tls13
                };

                using (HttpClient client = new HttpClient(handler))
                {
                    if (Config.UseLogin)
                    {
                        var byteArray = Encoding.ASCII.GetBytes($"{Config.Username}:{Config.Password}");
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                    }

                    client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
                    using HttpResponseMessage response = await client.GetAsync(Config.Host + Uri.EscapeDataString(webFileName), HttpCompletionOption.ResponseHeadersRead);

                    using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        response.EnsureSuccessStatusCode();

                        if (!Directory.Exists(ClientPath + "Patch\\"))
                            Directory.CreateDirectory(ClientPath + "Patch\\");

                        await WriteFileAtomicallyAsync(GetPatchFilePath(webFileName), async tempPath =>
                        {
                            using (FileStream fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
                            {
                                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                                long totalDownloadedBytes = 0;
                                byte[] buffer = new byte[8192];
                                int bytesRead;
                                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                                    totalDownloadedBytes += bytesRead;
                                    CurrentProgress = file.CompressedLength > 0 ? Math.Min(file.CompressedLength, totalDownloadedBytes) : totalDownloadedBytes;
                                    if (totalBytes > 0)
                                        progress.Report((int)(totalDownloadedBytes * 100 / totalBytes));
                                }
                            }
                        });
                    }

                    CurrentProgress = 0;
                    TotalProgress += file.CompressedLength;

                    return true;
                }
            }
            catch (Exception)
            {
                HasError = true;
                CurrentProgress = 0;
                file.CheckSum = new byte[8];
            }

            return false;
        }

        private async Task Extract(PatchInformation file)
        {
            string webFileName = GetPatchArchiveName(file.FileName);

            try
            {
                string toPath = GetClientFilePath(file.FileName);

                if (string.Equals(Path.GetFullPath(Application.ExecutablePath), toPath, StringComparison.OrdinalIgnoreCase))
                {
                    toPath += ".tmp";
                    NeedUpdate = true;
                }

                await Decompress(GetPatchFilePath(webFileName), toPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                file.CheckSum = new byte[8];

                if (HasError) return;
                HasError = true;
                MessageBox.Show(ex.Message + "\n\nFile might be in use, please make sure the game is closed.", "File Error", MessageBoxButtons.OK);
            }
            catch (Exception)
            {
                HasError = true;
                file.CheckSum = new byte[8];
            }
        }
        private static async Task Decompress(string sourceFile, string destFile)
        {
            await WriteFileAtomicallyAsync(destFile, async tempPath =>
            {
                using (FileStream tofile = File.Create(tempPath))
                using (FileStream fromfile = File.OpenRead(sourceFile))
                using (GZipStream gStream = new GZipStream(fromfile, CompressionMode.Decompress))
                {
                    await gStream.CopyToAsync(tofile);
                }
            });
        }

        private static void WriteAllBytesAtomically(string path, byte[] data)
        {
            WriteFileAtomically(path, tempPath => File.WriteAllBytes(tempPath, data));
        }

        private static void WriteFileAtomically(string path, Action<string> writeTempFile)
        {
            string fullPath = Path.GetFullPath(path);
            string tempPath = GetTempWritePath(fullPath);

            try
            {
                writeTempFile(tempPath);
                ReplaceOrMoveTempFile(tempPath, fullPath);
            }
            catch
            {
                TryDeleteTempFile(tempPath);
                throw;
            }
        }

        private static async Task WriteFileAtomicallyAsync(string path, Func<string, Task> writeTempFile)
        {
            string fullPath = Path.GetFullPath(path);
            string tempPath = GetTempWritePath(fullPath);

            try
            {
                await writeTempFile(tempPath);
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

        private static string GetClientFilePath(string relativePath)
        {
            return GetPathInsideRoot(ClientPath, relativePath, "补丁目标文件");
        }

        private static string GetPatchFilePath(string archiveName)
        {
            return GetPathInsideRoot(Path.Combine(ClientPath, "Patch"), archiveName, "补丁压缩包");
        }

        private static string GetPatchArchiveName(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidDataException("补丁文件名不能为空。");

            string archiveName = relativePath.Replace('\\', '-').Replace('/', '-') + ".gz";
            if (archiveName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException($"补丁文件名包含非法字符: {relativePath}");

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
                    throw new InvalidDataException($"补丁压缩包名称冲突: `{existing}` 和 `{file.FileName}` 都会生成 `{archiveName}`。");
            }
        }

        private static string GetPathInsideRoot(string root, string relativePath, string description)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidDataException($"{description}路径不能为空。");
            if (Path.IsPathRooted(relativePath))
                throw new InvalidDataException($"{description}必须是相对路径: {relativePath}");

            string rootPath = EnsureTrailingDirectorySeparator(Path.GetFullPath(root));
            string fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));

            if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{description}超出客户端目录: {relativePath}");

            return fullPath;
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                return path;

            return path + Path.DirectorySeparatorChar;
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
    }
}
