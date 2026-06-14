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
        private readonly List<FileStream> _writeLocks = new List<FileStream>();

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
                    _writeLocks.Add(File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
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
            foreach (FileStream stream in _writeLocks)
                stream.Dispose();

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

            string stamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ss");

            foreach (string path in GetDatabasePaths())
            {
                if (!File.Exists(path)) continue;

                string target = Path.Combine(backupRoot, $"{Path.GetFileNameWithoutExtension(path)} {stamp}{Session.Extension}");
                File.Copy(path, target, true);
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
    }
}
