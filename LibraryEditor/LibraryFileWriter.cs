using System;
using System.Collections.Generic;
using System.IO;

namespace LibraryEditor
{
    internal static class LibraryFileWriter
    {
        public static void WriteAllBytesAtomically(string path, byte[] data)
        {
            WriteFileAtomically(path, tempPath => File.WriteAllBytes(tempPath, data));
        }

        public static void WriteAllLinesAtomically(string path, IEnumerable<string> lines)
        {
            WriteFileAtomically(path, tempPath => File.WriteAllLines(tempPath, lines));
        }

        public static void WriteFileAtomically(string path, Action<string> writeTempFile)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("保存路径不能为空。", nameof(path));
            if (writeTempFile == null)
                throw new ArgumentNullException(nameof(writeTempFile));

            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("无法解析图库保存目录。");

            Directory.CreateDirectory(directory);
            string tempPath = Path.Combine(directory, $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                writeTempFile(tempPath);

                if (File.Exists(fullPath))
                    File.Replace(tempPath, fullPath, null);
                else
                    File.Move(tempPath, fullPath);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }

                throw;
            }
        }
    }
}
