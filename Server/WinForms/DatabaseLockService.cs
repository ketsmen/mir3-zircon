using MirDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Server.WinForms
{
    public sealed class DatabaseLockService : IDisposable
    {
        private readonly string _databaseRoot;
        private readonly List<DatabaseWriteLock> _writeLocks = new List<DatabaseWriteLock>();

        public DatabaseLockService(string databaseRoot = @".\Database\")
        {
            _databaseRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, databaseRoot));
        }

        public bool HasWriteLocks => _writeLocks.Count > 0;

        public bool AcquireWriteLocks(out string reason)
        {
            ReleaseWriteLocks();

            foreach (string path in GetExistingDatabasePaths())
            {
                try
                {
                    _writeLocks.Add(new DatabaseWriteLock(path, File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)));
                }
                catch (Exception ex)
                {
                    ReleaseWriteLocks();
                    reason = $"{path} 正在被占用，编辑器将以只读模式打开。\r\n{ex.Message}";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        public void ReleaseWriteLocks()
        {
            foreach (DatabaseWriteLock writeLock in _writeLocks.ToArray())
            {
                try
                {
                    writeLock.Stream.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to release database write lock '{writeLock.Path}': {ex}");
                }
            }

            _writeLocks.Clear();
        }

        public bool CanWrite(out string reason)
        {
            foreach (string path in GetExistingDatabasePaths())
            {
                try
                {
                    using FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                catch (Exception ex)
                {
                    reason = $"{path} 正在被占用，编辑器将以只读模式打开。\r\n{ex.Message}";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        public void Backup()
        {
            string backupRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backup", "Manual");
            Directory.CreateDirectory(backupRoot);

            string stamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ss fff");

            foreach (string path in GetDatabasePaths())
            {
                if (!File.Exists(path)) continue;

                string target = GetUniqueBackupPath(backupRoot, $"{Path.GetFileNameWithoutExtension(path)} {stamp}");
                DatabaseWriteLock writeLock = _writeLocks.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
                if (writeLock != null)
                    CopyLockedFile(writeLock.Stream, target);
                else
                    WinFormsFileWriter.CopyFileAtomically(path, target, false);
            }
        }

        private static void CopyLockedFile(FileStream source, string target)
        {
            long position = source.CanSeek ? source.Position : 0;

            try
            {
                if (source.CanSeek)
                    source.Position = 0;

                WinFormsFileWriter.CopyStreamAtomically(source, target, false);
            }
            finally
            {
                if (source.CanSeek)
                    source.Position = position;
            }
        }

        private static string GetUniqueBackupPath(string directory, string baseName)
        {
            string target = Path.Combine(directory, baseName + Session.Extension);
            if (!File.Exists(target)) return target;

            for (int i = 2; ; i++)
            {
                target = Path.Combine(directory, $"{baseName} ({i}){Session.Extension}");
                if (!File.Exists(target)) return target;
            }
        }

        public void Dispose()
        {
            ReleaseWriteLocks();
        }

        private IEnumerable<string> GetExistingDatabasePaths()
        {
            return GetDatabasePaths().Where(File.Exists);
        }

        private IEnumerable<string> GetDatabasePaths()
        {
            yield return Path.Combine(_databaseRoot, "System" + Session.Extension);
            yield return Path.Combine(_databaseRoot, "Users" + Session.Extension);
        }

        private sealed class DatabaseWriteLock
        {
            public DatabaseWriteLock(string path, FileStream stream)
            {
                Path = path;
                Stream = stream;
            }

            public string Path { get; }
            public FileStream Stream { get; }
        }
    }
}
