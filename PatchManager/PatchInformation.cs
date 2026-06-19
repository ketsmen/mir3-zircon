using System.IO;
using System.Security.Cryptography;

namespace PatchManager
{
    public sealed class PatchInformation
    {
        public string FileName { get; set; }

        public string UploadFileName { get; set; }
        public string PatchFileName { get; set; }
        public long CompressedLength { get; set; }
        public byte[] CheckSum { get; set; }

        public PatchInformation()//FileInfo
        {
        }

        public PatchInformation(string fileName)
        {
            string cleanClientRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(Config.CleanClient));
            string fullPath = Path.GetFullPath(fileName);

            if (!fullPath.StartsWith(cleanClientRoot, System.StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Patch file is outside clean client root: {fileName}");

            FileName = fullPath.Substring(cleanClientRoot.Length);

            using (MD5 md5 = MD5.Create())
            {
                using (FileStream stream = File.OpenRead(fileName))
                    CheckSum = md5.ComputeHash(stream);
            }
        }
        public PatchInformation(BinaryReader reader)
        {
            FileName = reader.ReadString();
            CompressedLength = reader.ReadInt64();

            CheckSum = reader.ReadBytes(reader.ReadInt32());
        }
        public void Save(BinaryWriter writer)
        {
            writer.Write(FileName);
            writer.Write(CompressedLength);
            writer.Write(CheckSum.Length);
            writer.Write(CheckSum);
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                return path;

            return path + Path.DirectorySeparatorChar;
        }
    }
}
