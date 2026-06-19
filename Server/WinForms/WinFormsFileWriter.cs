using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Server.WinForms
{
    internal static class WinFormsFileWriter
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void WriteAllTextAtomically(string path, string text)
        {
            WriteAllTextAtomically(path, text, Utf8NoBom);
        }

        public static void WriteAllTextAtomically(string path, string text, Encoding encoding)
        {
            string fullPath = Path.GetFullPath(path);
            string tempPath = null;

            try
            {
                tempPath = GetTempWritePath(fullPath);
                File.WriteAllText(tempPath, text, encoding);
                ReplaceOrMoveTempFile(tempPath, fullPath);
                tempPath = null;
            }
            catch
            {
                TryDeleteTempFile(tempPath);
                throw;
            }
        }

        public static void WriteAllLinesAtomically(string path, IEnumerable<string> lines)
        {
            string fullPath = Path.GetFullPath(path);
            string tempPath = null;

            try
            {
                tempPath = GetTempWritePath(fullPath);
                File.WriteAllLines(tempPath, lines, Utf8NoBom);
                ReplaceOrMoveTempFile(tempPath, fullPath);
                tempPath = null;
            }
            catch
            {
                TryDeleteTempFile(tempPath);
                throw;
            }
        }

        public static void CopyFileAtomically(string sourcePath, string destinationPath, bool overwrite)
        {
            string fullPath = Path.GetFullPath(destinationPath);
            string tempPath = null;

            try
            {
                tempPath = GetTempWritePath(fullPath);
                File.Copy(sourcePath, tempPath, false);
                MoveTempFile(tempPath, fullPath, overwrite);
                tempPath = null;
            }
            catch
            {
                TryDeleteTempFile(tempPath);
                throw;
            }
        }

        public static void CopyStreamAtomically(Stream source, string destinationPath, bool overwrite)
        {
            string fullPath = Path.GetFullPath(destinationPath);
            string tempPath = null;

            try
            {
                tempPath = GetTempWritePath(fullPath);
                using (FileStream destination = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    source.CopyTo(destination);

                MoveTempFile(tempPath, fullPath, overwrite);
                tempPath = null;
            }
            catch
            {
                TryDeleteTempFile(tempPath);
                throw;
            }
        }

        private static string GetTempWritePath(string fullPath)
        {
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
            return Path.Combine(tempDirectory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        }

        private static void MoveTempFile(string tempPath, string destinationPath, bool overwrite)
        {
            if (!overwrite)
            {
                File.Move(tempPath, destinationPath);
                return;
            }

            ReplaceOrMoveTempFile(tempPath, destinationPath);
        }

        private static void ReplaceOrMoveTempFile(string tempPath, string destinationPath)
        {
            if (File.Exists(destinationPath))
                File.Replace(tempPath, destinationPath, null);
            else
                File.Move(tempPath, destinationPath);
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
    }
}
