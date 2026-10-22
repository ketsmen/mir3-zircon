using System;
using System.IO;

namespace Server.Envir
{
    public static class LogFileMaintenance
    {
        public static void RotateIfNeeded(string path, long maxBytes)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length < maxBytes) return;

            string directory = file.DirectoryName;
            string name = Path.GetFileNameWithoutExtension(file.Name);
            string extension = file.Extension;
            string archivePath = Path.Combine(directory ?? ".", $"{name}.{DateTime.Now:yyyyMMdd-HHmmss}{extension}");

            File.Move(path, archivePath);
        }

        public static void CleanupOldFiles(string path, int retentionDays)
        {
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            DateTime threshold = DateTime.Now.AddDays(-retentionDays);

            foreach (string file in Directory.GetFiles(directory, $"{name}.*{extension}"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < threshold)
                        File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                }
            }
        }
    }
}
