using ManagedSquish;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace LibraryEditor
{
    public sealed class MLibraryv0
    {
        public static bool Load = true;
        public string FileName;

        public List<MImage> Images = new List<MImage>();
        public List<int> IndexList = new List<int>();
        public int Count;
        private bool _initialized;

        private BinaryReader _reader;
        private FileStream _stream;

        public MLibraryv0(string filename)
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
            _initialized = true;

            if (!File.Exists(FileName))
                return;

            _stream = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            _reader = new BinaryReader(_stream);

            Count = _reader.ReadInt32();
            ValidateImageCount(Count, _stream.Length, 4);
            Images = new List<MImage>();
            IndexList = new List<int>();

            long imageDataStart = 4L + (long)Count * sizeof(int);
            for (int i = 0; i < Count; i++)
            {
                int offset = _reader.ReadInt32();
                ValidateImageOffset(offset, imageDataStart, _stream.Length, i);
                IndexList.Add(offset);
            }

            for (int i = 0; i < Count; i++)
                Images.Add(null);
            Load = false;//dont need to process all images when converting
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

            int offSet = 4 + Count * 4;
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

        public Point GetOffSet(int index)
        {
            if (!_initialized)
                Initialize();

            if (Images == null || index < 0 || index >= Images.Count)
                return Point.Empty;

            if (Images[index] == null)
            {
                if (IndexList[index] == 0)
                    return Point.Empty;

                _stream.Seek(IndexList[index], SeekOrigin.Begin);
                Images[index] = new MImage(_reader);
            }

            return new Point(Images[index].X, Images[index].Y);
        }

        public Size GetSize(int index)
        {
            if (!_initialized)
                Initialize();
            if (Images == null || index < 0 || index >= Images.Count)
                return Size.Empty;

            if (Images[index] == null)
            {
                if (IndexList[index] == 0)
                    return Size.Empty;

                _stream.Seek(IndexList[index], SeekOrigin.Begin);
                Images[index] = new MImage(_reader);
            }

            return new Size(Images[index].Width, Images[index].Height);
        }

        public MImage GetMImage(int index)
        {
            if (index < 0 || index >= Images.Count)
                return null;

            return Images[index];
        }

        public Bitmap GetPreview(int index)
        {
            if (index < 0 || index >= Images.Count)
                return new Bitmap(1, 1);

            MImage image = Images[index];

            if (image == null || image.Image == null)
                return new Bitmap(1, 1);

            if (image.Preview == null)
                image.CreatePreview();

            return image.Preview;
        }

        public void AddImage(Bitmap image, short x, short y)
        {
            MImage mImage = new MImage(image) { X = x, Y = y };

            Count++;
            Images.Add(mImage);
        }

        public void InsertImage(int index, Bitmap image, short x, short y)
        {
            MImage mImage = new MImage(image) { X = x, Y = y };

            Count++;
            Images.Insert(index, mImage);
        }

        public void RemoveImage(int index)
        {
            if (Images == null || Count <= 1)
            {
                Count = 0;
                Images = new List<MImage>();
                return;
            }
            Count--;

            Images.RemoveAt(index);
        }

        public static bool CompareBytes(byte[] a, byte[] b)
        {
            if (a == b) return true;

            if (a == null || b == null || a.Length != b.Length) return false;

            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;

            return true;
        }

        public void RemoveBlanks(bool safe = false)
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                if (Images[i].FBytes == null || Images[i].FBytes.Length <= 8)
                {
                    if (!safe)
                        RemoveImage(i);
                    else if (Images[i].X == 0 && Images[i].Y == 0)
                        RemoveImage(i);
                }
            }
        }

        public sealed class MImage
        {
            private const int MaxImagePixels = 16777216;

            public short Width, Height, X, Y;
            public int Length;
            public bool TextureValid;
            public Bitmap Image, Preview;
            public byte[] FBytes;

            public MImage(BinaryReader reader)
            {
                Width = reader.ReadInt16();
                Height = reader.ReadInt16();
                X = reader.ReadInt16();
                Y = reader.ReadInt16();
                Length = reader.ReadInt32();
                ValidateStoredDimensions(Width, Height, Length, "主图");

                FBytes = ReadExactBytes(reader, Length, "主图");
            }

            public unsafe MImage(Bitmap image)
            {
                if (image == null)
                {
                    FBytes = new byte[0];
                    return;
                }

                Width = (short)image.Width;
                Height = (short)image.Height;

                int w = image.Width + (4 - image.Width % 4) % 4;
                int h = image.Height + (4 - image.Height % 4) % 4;

                if (image.Width != w || image.Height != h)
                {
                    Bitmap temp = new Bitmap(w, h);
                    using (Graphics g = Graphics.FromImage(temp))
                    {
                        g.Clear(Color.Transparent);
                        g.DrawImage(image, 0, 0);
                        g.Save();
                    }
                    image.Dispose();
                    image = temp;
                }

                Image = image;

                byte[] pixels = new byte[image.Width * image.Height * 4];
                BitmapData data = null;

                try
                {
                    data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly,
                                                     PixelFormat.Format32bppArgb);

                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                }
                finally
                {
                    if (data != null)
                        image.UnlockBits(data);
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

                int count = Squish.GetStorageRequirements(image.Width, image.Height, SquishFlags.Dxt1);

                FBytes = new byte[count];
                fixed (byte* dest = FBytes)
                fixed (byte* source = pixels)
                {
                    Squish.CompressImage((IntPtr)source, image.Width, image.Height, (IntPtr)dest, SquishFlags.Dxt1);
                }
            }

            public void Save(BinaryWriter writer)
            {
                writer.Write(Width);
                writer.Write(Height);
                writer.Write(X);
                writer.Write(Y);

                byte[] imageBytes = FBytes ?? Array.Empty<byte>();
                writer.Write(imageBytes.Length);
                writer.Write(imageBytes);
            }

            public static void SaveBlank(BinaryWriter writer)
            {
                writer.Write((short)0);
                writer.Write((short)0);
                writer.Write((short)0);
                writer.Write((short)0);
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

                Bitmap bitmap = new Bitmap(w, h);
                BitmapData data = null;
                bool imageReady = false;

                try
                {
                    data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite,
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

                    Image?.Dispose();
                    Image = bitmap;
                    bitmap = null;
                    imageReady = true;
                }
                finally
                {
                    if (data != null)
                        (bitmap ?? Image)?.UnlockBits(data);

                    if (!imageReady)
                        bitmap?.Dispose();
                }
            }

            private static void ValidateBufferLength(byte[] buffer, int expectedLength, string description)
            {
                if (buffer == null || buffer.Length < expectedLength)
                {
                    int actualLength = buffer?.Length ?? 0;
                    throw new InvalidDataException($"{description}数据不完整: 需要至少 {expectedLength:#,##0} 字节，实际 {actualLength:#,##0} 字节。");
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

            public void CreatePreview()
            {
                if (Image == null)
                {
                    Preview = new Bitmap(1, 1);
                    return;
                }

                Preview = new Bitmap(64, 64);

                using (Graphics g = Graphics.FromImage(Preview))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);
                    int w = Math.Min((int)Width, 64);
                    int h = Math.Min((int)Height, 64);

                    g.DrawImage(Image, new Rectangle((64 - w) / 2, (64 - h) / 2, w, h), new Rectangle(0, 0, Width, Height), GraphicsUnit.Pixel);
                    g.Save();
                }
            }
        }
    }
}
