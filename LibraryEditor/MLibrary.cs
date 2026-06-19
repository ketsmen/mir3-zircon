using ManagedSquish;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LibraryEditor
{
    public sealed class MLibrary
    {
        public const int LibVersion = 1;
        public static bool Load = true;
        public string FileName;

        public List<MImage> Images = new List<MImage>();
        public List<int> IndexList = new List<int>();
        public int Count;
        private bool _initialized;

        private BinaryReader _reader;
        private FileStream _stream;

        public MLibrary(string filename)
        {
            FileName = filename;
            try
            {
                Initialize();
            }
            finally
            {
                Close();
            }
        }

        public void Initialize()
        {
            int CurrentVersion;
            _initialized = true;

            if (!File.Exists(FileName))
                return;

            _stream = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            _reader = new BinaryReader(_stream);
            CurrentVersion = _reader.ReadInt32();
            if (CurrentVersion != LibVersion)
            {
                MessageBox.Show("Wrong version, expecting lib version: " + LibVersion.ToString() + " found version: " + CurrentVersion.ToString() + ".", "Failed to open", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                return;
            }
            Count = _reader.ReadInt32();
            ValidateImageCount(Count, _stream.Length, 8);
            Images = new List<MImage>();
            IndexList = new List<int>();

            long imageDataStart = 8L + (long)Count * sizeof(int);
            for (int i = 0; i < Count; i++)
            {
                int offset = _reader.ReadInt32();
                ValidateImageOffset(offset, imageDataStart, _stream.Length, i);
                IndexList.Add(offset);
            }

            for (int i = 0; i < Count; i++)
                Images.Add(null);

            for (int i = 0; i < Count; i++)
                CheckImage(i);
        }

        public void Close()
        {
            _reader?.Dispose();
            _reader = null;

            _stream?.Dispose();
            _stream = null;
        }

        private static void ValidateImageCount(int count, long streamLength, int headerPrefixBytes)
        {
            long imageDataStart = headerPrefixBytes + (long)count * sizeof(int);
            if (count < 0 || imageDataStart > streamLength)
                throw new InvalidDataException($"图库图片数量无效: {count:#,##0}。");
        }

        private static void ValidateImageOffset(int offset, long imageDataStart, long streamLength, int imageIndex)
        {
            if (offset == 0)
                return;

            if (offset < imageDataStart || offset >= streamLength)
                throw new InvalidDataException($"图库图片 {imageIndex} 的数据位置无效: {offset:#,##0}。");
        }

        public void Save()
        {
            Close();

            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);

            Count = Images.Count;
            IndexList.Clear();

            int offSet = 8 + Count * 4;
            for (int i = 0; i < Count; i++)
            {
                IndexList.Add((int)stream.Length + offSet);
                MImage image = Images[i];
                if (image == null)
                    MImage.SaveBlank(writer);
                else
                    image.Save(writer);
            }

            writer.Flush();
            byte[] fBytes = stream.ToArray();
            //  writer.Dispose();

            using (MemoryStream output = new MemoryStream())
            using (BinaryWriter fileWriter = new BinaryWriter(output))
            {
                fileWriter.Write(LibVersion);
                fileWriter.Write(Count);
                for (int i = 0; i < Count; i++)
                    fileWriter.Write(IndexList[i]);

                fileWriter.Write(fBytes);
                fileWriter.Flush();
                LibraryFileWriter.WriteAllBytesAtomically(FileName, output.ToArray());
            }

            Close();
        }

        private void CheckImage(int index)
        {
            if (!_initialized)
                Initialize();

            if (Images == null || index < 0 || index >= Images.Count)
                return;

            if (Images[index] == null)
            {
                if (IndexList[index] == 0)
                    return;

                _stream.Position = IndexList[index];
                Images[index] = new MImage(_reader);
            }

            if (!Load) return;

            MImage mi = Images[index];
            if (!mi.TextureValid)
            {
                _stream.Seek(IndexList[index] + 12, SeekOrigin.Begin);
                mi.CreateTexture(_reader);
            }
        }

        public void ToMLibrary(bool useBlackKeyTransparency = false)
        {
            string fileName = Path.ChangeExtension(FileName, ".Zl");

            using Mir3Library library = Mir3Library.CreateEmpty(fileName, useBlackKeyTransparency);
            library.Version = Mir3Library.LIBRARY_VERSION;

            for (int i = 0; i < Images.Count; i++)
                library.Images.Add(null);

            ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = 8 };

            Parallel.For(0, Images.Count, options, i =>
            {
                MImage image = Images[i];
                if (image == null) return;

                if (image.HasMask)
                    library.Images[i] = new Mir3Library.Mir3Image(CloneBitmap(image.Image), null, CloneBitmap(image.MaskImage), library.Version, library.UseBlackKeyTransparency) { OffSetX = image.X, OffSetY = image.Y, ShadowOffSetX = image.ShadowX, ShadowOffSetY = image.ShadowY };
                else
                    library.Images[i] = new Mir3Library.Mir3Image(CloneBitmap(image.Image), library.Version, library.UseBlackKeyTransparency) { OffSetX = image.X, OffSetY = image.Y, ShadowOffSetX = image.ShadowX, ShadowOffSetY = image.ShadowY };
            });

            library.Save(fileName);

            // Operation finished.
            // System.Windows.Forms.MessageBox.Show("Converted " + fileName + " successfully.",
            //    "Wemade Information",
            //        System.Windows.Forms.MessageBoxButtons.OK,
            //            System.Windows.Forms.MessageBoxIcon.Information,
            //                System.Windows.Forms.MessageBoxDefaultButton.Button1);
        }
        public void MergeToMLibrary(Mir3Library lib, int newImages)
        {
            int offset = lib.Images.Count;
            for (int i = 0; i < Images.Count; i++)
                lib.Images.Add(null);
            //library.Save();

            ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = 8 };

            Parallel.For(0, Images.Count, options, i =>
            {
                MImage image = Images[i];
                if (image == null) return;

                if (image.HasMask)
                    lib.Images[i + offset] = new Mir3Library.Mir3Image(CloneBitmap(image.Image), null, CloneBitmap(image.MaskImage), lib.Version, lib.UseBlackKeyTransparency) { OffSetX = image.X, OffSetY = image.Y, ShadowOffSetX = image.ShadowX, ShadowOffSetY = image.ShadowY };
                else
                    lib.Images[i + offset] = new Mir3Library.Mir3Image(CloneBitmap(image.Image), lib.Version, lib.UseBlackKeyTransparency) { OffSetX = image.X, OffSetY = image.Y, ShadowOffSetX = image.ShadowX, ShadowOffSetY = image.ShadowY };
            });
            lib.AddBlanks(newImages);
        }

        public sealed class MImage
        {
            private const int MaxImagePixels = 16777216;

            public short Width, Height, X, Y, ShadowX, ShadowY;
            public byte Shadow;
            public int Length;
            public byte[] FBytes;
            public bool TextureValid;
            public Bitmap Image, Preview;

            //layer 2:
            public short MaskWidth, MaskHeight, MaskX, MaskY;

            public int MaskLength;
            public byte[] MaskFBytes;
            public Bitmap MaskImage;
            public Boolean HasMask;

            public MImage(BinaryReader reader)
            {
                //read layer 1
                Width = reader.ReadInt16();
                Height = reader.ReadInt16();
                X = reader.ReadInt16();
                Y = reader.ReadInt16();
                ShadowX = reader.ReadInt16();
                ShadowY = reader.ReadInt16();
                Shadow = reader.ReadByte();
                Length = reader.ReadInt32();
                ValidateStoredDimensions(Width, Height, Length, "主图");
                FBytes = ReadExactBytes(reader, Length, "主图");
                //check if there's a second layer and read it
                HasMask = ((Shadow >> 7) == 1) ? true : false;
                if (HasMask)
                {
                    MaskWidth = reader.ReadInt16();
                    MaskHeight = reader.ReadInt16();
                    MaskX = reader.ReadInt16();
                    MaskY = reader.ReadInt16();
                    MaskLength = reader.ReadInt32();
                    ValidateStoredDimensions(MaskWidth, MaskHeight, MaskLength, "遮罩图");
                    MaskFBytes = ReadExactBytes(reader, MaskLength, "遮罩图");
                }
            }

            private Bitmap FixImageSize(Bitmap input)
            {
                int w = input.Width + (4 - input.Width % 4) % 4;
                int h = input.Height + (4 - input.Height % 4) % 4;

                if (input.Width != w || input.Height != h)
                {
                    Bitmap temp = new Bitmap(w, h);
                    using (Graphics g = Graphics.FromImage(temp))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.DrawImage(input, 0, 0);
                        g.Save();
                    }
                    input.Dispose();
                    input = temp;
                }

                return input;
            }

            private unsafe byte[] ConvertBitmapToArray(Bitmap input)
            {
                byte[] output;

                byte[] pixels = new byte[input.Width * input.Height * 4];

                BitmapData data = null;
                try
                {
                    data = input.LockBits(new Rectangle(0, 0, input.Width, input.Height), ImageLockMode.ReadOnly,
                                                     PixelFormat.Format32bppArgb);

                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                }
                finally
                {
                    if (data != null)
                        input.UnlockBits(data);
                }

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    //Reverse Red/Blue
                    byte b = pixels[i];
                    pixels[i] = pixels[i + 2];
                    pixels[i + 2] = b;

                    if (pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0)
                        pixels[i + 3] = 0; //Make Transparent
                }

                int count = Squish.GetStorageRequirements(input.Width, input.Height, SquishFlags.Dxt1);

                output = new byte[count];
                fixed (byte* dest = output)
                fixed (byte* source = pixels)
                {
                    Squish.CompressImage((IntPtr)source, input.Width, input.Height, (IntPtr)dest, SquishFlags.Dxt1);
                }
                return output;
            }

            public void Save(BinaryWriter writer)
            {
                writer.Write(Width);
                writer.Write(Height);
                writer.Write(X);
                writer.Write(Y);
                writer.Write(ShadowX);
                writer.Write(ShadowY);
                writer.Write(HasMask ? (byte)(Shadow | 0x80) : (byte)Shadow);
                byte[] imageBytes = FBytes ?? Array.Empty<byte>();
                writer.Write(imageBytes.Length);
                writer.Write(imageBytes);
                if (HasMask)
                {
                    writer.Write(MaskWidth);
                    writer.Write(MaskHeight);
                    writer.Write(MaskX);
                    writer.Write(MaskY);
                    byte[] maskBytes = MaskFBytes ?? Array.Empty<byte>();
                    writer.Write(maskBytes.Length);
                    writer.Write(maskBytes);
                }
            }

            public static void SaveBlank(BinaryWriter writer)
            {
                writer.Write((short)0);
                writer.Write((short)0);
                writer.Write((short)0);
                writer.Write((short)0);
                writer.Write((short)0);
                writer.Write((short)0);
                writer.Write((byte)0);
                writer.Write(0);
            }

            public unsafe void CreateTexture(BinaryReader reader)
            {
                int w = Width + (4 - Width % 4) % 4;
                int h = Height + (4 - Height % 4) % 4;

                if (w == 0 || h == 0)
                {
                    return;
                }

                Image = new Bitmap(w, h);

                BitmapData data = null;
                bool imageReady = false;
                try
                {
                    data = Image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite,
                                                     PixelFormat.Format32bppArgb);

                    ValidateBufferLength(FBytes, Squish.GetStorageRequirements(w, h, SquishFlags.Dxt1), "主图");
                    fixed (byte* source = FBytes)
                        Squish.DecompressImage(data.Scan0, w, h, (IntPtr)source, SquishFlags.Dxt1);

                    byte* dest = (byte*)data.Scan0;

                    for (int i = 0; i < h * w * 4; i += 4)
                    {
                        //Reverse Red/Blue
                        byte b = dest[i];
                        dest[i] = dest[i + 2];
                        dest[i + 2] = b;
                    }

                    imageReady = true;
                }
                finally
                {
                    try
                    {
                        if (data != null)
                            Image.UnlockBits(data);
                    }
                    finally
                    {
                        if (!imageReady)
                        {
                            Image?.Dispose();
                            Image = null;
                        }
                    }
                }

                if (HasMask)
                {
                    w = MaskWidth + (4 - MaskWidth % 4) % 4;
                    h = MaskHeight + (4 - MaskHeight % 4) % 4;

                    if (w == 0 || h == 0)
                    {
                        return;
                    }
                    MaskImage = new Bitmap(w, h);

                    BitmapData maskData = null;
                    bool maskReady = false;
                    try
                    {
                        maskData = MaskImage.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite,
                                                         PixelFormat.Format32bppArgb);

                        ValidateBufferLength(MaskFBytes, Squish.GetStorageRequirements(w, h, SquishFlags.Dxt1), "遮罩图");
                        fixed (byte* source = MaskFBytes)
                            Squish.DecompressImage(maskData.Scan0, w, h, (IntPtr)source, SquishFlags.Dxt1);

                        byte* dest = (byte*)maskData.Scan0;

                        for (int i = 0; i < h * w * 4; i += 4)
                        {
                            //Reverse Red/Blue
                            byte b = dest[i];
                            dest[i] = dest[i + 2];
                            dest[i + 2] = b;
                        }

                        maskReady = true;
                    }
                    finally
                    {
                        try
                        {
                            if (maskData != null)
                                MaskImage.UnlockBits(maskData);
                        }
                        finally
                        {
                            if (!maskReady)
                            {
                                MaskImage?.Dispose();
                                MaskImage = null;
                            }
                        }
                    }
                }
            }

            private static byte[] ReadExactBytes(BinaryReader reader, int count, string description)
            {
                if (count < 0)
                    throw new InvalidDataException($"{description}长度无效: {count:#,##0} 字节。");
                if (reader.BaseStream.CanSeek && count > reader.BaseStream.Length - reader.BaseStream.Position)
                    throw new InvalidDataException($"{description}数据不完整: 需要 {count:#,##0} 字节，剩余 {Math.Max(0, reader.BaseStream.Length - reader.BaseStream.Position):#,##0} 字节。");

                byte[] bytes = reader.ReadBytes(count);
                if (bytes.Length != count)
                    throw new InvalidDataException($"{description}数据不完整: 需要 {count:#,##0} 字节，实际 {bytes.Length:#,##0} 字节。");

                return bytes;
            }

            private static void ValidateStoredDimensions(short width, short height, int length, string description)
            {
                if (length == 0 && width == 0 && height == 0) return;

                if (width <= 0 ||
                    height <= 0 ||
                    (long)Pad4(width) * Pad4(height) > MaxImagePixels)
                    throw new InvalidDataException($"{description}尺寸无效: {width}x{height}。");
            }

            private static int Pad4(int value)
            {
                return value + (4 - value % 4) % 4;
            }

            private static void ValidateBufferLength(byte[] bytes, int minimumLength, string description)
            {
                int actualLength = bytes?.Length ?? 0;
                if (actualLength < minimumLength)
                    throw new InvalidDataException($"{description}压缩数据长度不足: 至少需要 {minimumLength:#,##0} 字节，实际 {actualLength:#,##0} 字节。");
            }
        }

        private static Bitmap CloneBitmap(Bitmap image)
        {
            return image == null ? null : new Bitmap(image);
        }
    }
}
