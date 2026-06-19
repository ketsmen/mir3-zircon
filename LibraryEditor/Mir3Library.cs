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

    public enum ImageType
    {
        Image,
        Shadow,
        Overlay,
    }

    public sealed class Mir3Library : IDisposable
    {
        /// <summary>
        /// V0 - Default version - Dxt1 Images
        /// V1 - First version - Dxt5 Images
        /// </summary>
        public const int LIBRARY_VERSION = 1;

        public int Version;

        public string FileName;
        public string _fileName;

        private FileStream _fStream;
        private BinaryReader _bReader;

        public List<Mir3Image> Images;

        public bool UseBlackKeyTransparency { get; }

        public Mir3Library(string fileName, bool useBlackKeyTransparency = false)
            : this(fileName, useBlackKeyTransparency, loadExisting: true)
        {
        }

        public static Mir3Library CreateEmpty(string fileName, bool useBlackKeyTransparency = false)
        {
            return new Mir3Library(fileName, useBlackKeyTransparency, loadExisting: false);
        }

        private Mir3Library(string fileName, bool useBlackKeyTransparency, bool loadExisting)
        {
            FileName = fileName;
            _fileName = fileName;
            UseBlackKeyTransparency = useBlackKeyTransparency;
            Images = new List<Mir3Image>();
            if (!loadExisting || !File.Exists(fileName))
                return;

            try
            {
                _fStream = File.OpenRead(fileName);
                _bReader = new BinaryReader(_fStream);

                ReadLibrary();
                Close();
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        public void ReadLibrary()
        {
            if (_bReader == null)
                return;

            int headerLength = _bReader.ReadInt32();
            if (headerLength < sizeof(int))
                throw new InvalidDataException($"图库头长度无效: {headerLength:#,##0} 字节。");

            long remainingBytes = _bReader.BaseStream.Length - _bReader.BaseStream.Position;
            if (headerLength > remainingBytes)
                throw new InvalidDataException($"图库头数据不完整: 需要 {headerLength:#,##0} 字节，剩余 {remainingBytes:#,##0} 字节。");

            byte[] headerBytes = ReadExactBytes(_bReader, headerLength, "图库头");
            using (MemoryStream mstream = new MemoryStream(headerBytes))
            using (BinaryReader reader = new BinaryReader(mstream))
            {
                int value = reader.ReadInt32();

                int count = value & 0x1FFFFFF;
                Version = (value >> 25) & 0x7F;

                if (Version == 0)
                {
                    count = value;
                }

                long remainingHeaderBytes = reader.BaseStream.Length - reader.BaseStream.Position;
                if (count < 0 || count > remainingHeaderBytes)
                    throw new InvalidDataException($"图库图片数量无效: {count:#,##0}。");

                for (int i = 0; i < count; i++)
                    Images.Add(null);

                for (int i = 0; i < Images.Count; i++)
                {
                    if (reader.BaseStream.Position >= reader.BaseStream.Length)
                        throw new InvalidDataException($"图库头数据不完整: 缺少图片 {i} 的存在标记。");

                    if (!reader.ReadBoolean()) continue;

                    if (reader.BaseStream.Length - reader.BaseStream.Position < Mir3Image.HeaderSize)
                        throw new InvalidDataException($"图库头数据不完整: 图片 {i} 的头部不足 {Mir3Image.HeaderSize} 字节。");

                    Images[i] = new Mir3Image(reader, Version);
                }
            }

            for (int i = 0; i < Images.Count; i++)
            {
                if (Images[i] == null) continue;

                CreateImage(i, ImageType.Image);
                CreateImage(i, ImageType.Shadow);
                CreateImage(i, ImageType.Overlay);
            }
        }
        public void Close()
        {
            if (_fStream != null)
            {
                _fStream.Dispose();
                _fStream = null;
            }
            if (_bReader != null)
            {
                _bReader.Dispose();
                _bReader = null;
            }
        }

        public void Dispose()
        {
            Close();

            if (Images != null)
            {
                foreach (Mir3Image image in Images)
                    image?.Dispose();

                Images.Clear();
            }

            GC.SuppressFinalize(this);
        }
        public Mir3Image CreateImage(int index, ImageType type)
        {
            if (!CheckImage(index)) return null;

            Mir3Image image = Images[index];
            Bitmap bmp;

            switch (type)
            {
                case ImageType.Image:
                    if (!image.ImageValid) image.CreateImage(_bReader);
                    bmp = image.Image;
                    break;
                case ImageType.Shadow:
                    if (!image.ShadowValid) image.CreateShadow(_bReader);
                    bmp = image.ShadowImage;
                    break;
                case ImageType.Overlay:
                    if (!image.OverlayValid) image.CreateOverlay(_bReader);
                    bmp = image.OverlayImage;
                    break;
                default:
                    return null;
            }

            if (bmp == null) return null;

            return image;
        }

        private bool CheckImage(int index)
        {
            return index >= 0 && index < Images.Count && Images[index] != null;
        }

        public Mir3Image GetImage(int index)
        {
            if (index < 0 || index >= Images.Count)
                return null;

            return Images[index];
        }

        public Bitmap GetPreview(int index, ImageType type)
        {
            if (index < 0 || index >= Images.Count)
                return new Bitmap(1, 1);

            Mir3Image image = Images[index];

            switch (type)
            {
                case ImageType.Image:
                    if (image == null || image.Image == null)
                        return new Bitmap(1, 1);

                    if (image.Preview == null)
                        image.CreatePreview();

                    return image.Preview;
                case ImageType.Shadow:
                    if (image == null || image.ShadowImage == null)
                        return new Bitmap(1, 1);

                    if (image.ShadowPreview == null)
                        image.CreateShadowPreview();

                    return image.ShadowPreview;
                case ImageType.Overlay:
                    if (image == null || image.OverlayImage == null)
                        return new Bitmap(1, 1);

                    if (image.OverlayPreview == null)
                        image.CreateOverlayPreview();

                    return image.OverlayPreview;
            }

            return new Bitmap(1, 1);
        }

        public void RemoveBlanks(bool safe = false)
        {
            for (int i = Images.Count - 1; i >= 0; i--)
            {
                Mir3Image image = Images[i];
                if (image == null)
                {
                    RemoveImage(i);
                    continue;
                }

                if (IsBlankImage(image, requireZeroOffset: safe))
                {
                    RemoveImage(i);
                }
            }
        }

        private static bool IsBlankImage(Mir3Image image, bool requireZeroOffset)
        {
            if (image == null) return true;

            bool blank = image.FBytes == null ||
                         image.FBytes.Length == 0 ||
                         (image.Width <= 1 && image.Height <= 1);
            if (!blank) return false;

            return !requireZeroOffset || (image.OffSetX == 0 && image.OffSetY == 0);
        }

        public void RemoveImage(int index)
        {
            if (Images == null)
                return;
            if (index < 0 || index >= Images.Count)
                return;

            if (Images.Count <= 1)
            {
                foreach (Mir3Image image in Images)
                    image?.Dispose();

                Images = new List<Mir3Image>();
                return;
            }

            Mir3Image removed = Images[index];
            Images.RemoveAt(index);
            removed?.Dispose();
        }
        public void AddBlanks(int newImages)
        {
            if (newImages <= 0)
                return;

            int cap = newImages - (Images.Count % newImages);
            if (cap != newImages)
                AddBlankImages(cap);
        }

        public void AddBlankImages(int count)
        {
            if (Images == null)
                throw new InvalidOperationException("Library image list is not initialized.");
            if (count <= 0)
                return;

            for (int i = 0; i < count; i++)
                Images.Add(null);
        }

        public void InsertBlankImages(int index, int count)
        {
            if (Images == null)
                throw new InvalidOperationException("Library image list is not initialized.");
            if (index < 0 || index > Images.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (count <= 0)
                return;

            for (int i = 0; i < count; i++)
                Images.Insert(index, null);
        }
        public void AddImage(Bitmap image, short x, short y)
        {
            if (Images == null)
                throw new InvalidOperationException("Library image list is not initialized.");

            Mir3Image mImage = CreateOwnedImage(image, x, y);
            Images.Add(mImage);
        }

        public void ReplaceImage(int Index, Bitmap image, short x, short y)
        {
            if (Images == null)
                throw new InvalidOperationException("Library image list is not initialized.");
            if (Index < 0 || Index >= Images.Count)
                throw new ArgumentOutOfRangeException(nameof(Index));

            Mir3Image mImage = CreateOwnedImage(image, x, y);

            Mir3Image oldImage = Images[Index];
            Images[Index] = mImage;
            oldImage?.Dispose();
        }

        public void InsertImage(int index, Bitmap image, short x, short y)
        {
            if (Images == null)
                throw new InvalidOperationException("Library image list is not initialized.");
            if (index < 0 || index > Images.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            Mir3Image mImage = CreateOwnedImage(image, x, y);

            Images.Insert(index, mImage);
        }

        private Mir3Image CreateOwnedImage(Bitmap image, short x, short y)
        {
            Mir3Image mImage = null;

            try
            {
                mImage = new Mir3Image(image, Version, UseBlackKeyTransparency) { OffSetX = x, OffSetY = y };
                image = null;
                return mImage;
            }
            catch
            {
                mImage?.Dispose();
                image?.Dispose();
                throw;
            }
        }

        public void Save(string path)
        {
            //|Header Size|Count|T|Header|F|F|T|Header|F|T|Header...|Image|Image|Im...

            int headerSize = 4 + Images.Count;

            foreach (Mir3Image image in Images)
            {
                if (!HasImageBytes(image)) continue;

                headerSize += Mir3Image.HeaderSize;
            }

            int position = headerSize + 4;

            foreach (Mir3Image image in Images)
            {
                if (!HasImageBytes(image)) continue;

                image.Position = position;

                position += image.DataSize;
            }


            using (MemoryStream buffer = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(buffer))
            {
                writer.Write(headerSize);

                writer.Write((Version & 0x7F) << 25 | (Images.Count & 0x1FFFFFF));

                foreach (Mir3Image image in Images)
                {
                    writer.Write(HasImageBytes(image));

                    if (!HasImageBytes(image)) continue;

                    image.SaveHeader(writer);
                }

                foreach (Mir3Image image in Images)
                {
                    if (!HasImageBytes(image)) continue;

                    if (image.FBytes != null)
                        writer.Write(image.FBytes);

                    if (image.ShadowFBytes != null)
                        writer.Write(image.ShadowFBytes);

                    if (image.OverlayFBytes != null)
                        writer.Write(image.OverlayFBytes);
                }

                LibraryFileWriter.WriteAllBytesAtomically(path, buffer.ToArray());
            }

            FileName = path;
            _fileName = path;
        }

        private static bool HasImageBytes(Mir3Image image)
        {
            return image?.FBytes?.Length > 0;
        }

        private static byte[] ReadExactBytes(BinaryReader reader, int count, string description)
        {
            if (count < 0)
                throw new InvalidDataException($"{description}长度无效: {count:#,##0} 字节。");

            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count)
                throw new InvalidDataException($"{description}数据不完整: 需要 {count:#,##0} 字节，实际 {bytes.Length:#,##0} 字节。");

            return bytes;
        }

        public sealed class Mir3Image : IDisposable
        {
            public const int HeaderSize = 25;
            private const int MaxImagePixels = 16777216;

            public int Version;

            public int DataSize => (FBytes?.Length ?? 0) + (ShadowFBytes?.Length ?? 0) + (OverlayFBytes?.Length ?? 0);

            public int Position;

            #region Texture

            public short Width;
            public short Height;
            public short OffSetX;
            public short OffSetY;
            public byte ShadowType;
            public Bitmap Image, Preview;
            public bool ImageValid { get; private set; }
            public unsafe byte* ImageData;
            public int ImageDataSize
            {
                get
                {
                    return GetImageDataSize(Width, Height, Version, "主图");
                }
            }
            public byte[] FBytes;
            #endregion

            #region Shadow
            public short ShadowWidth;
            public short ShadowHeight;

            public short ShadowOffSetX;
            public short ShadowOffSetY;

            public Bitmap ShadowImage, ShadowPreview;
            public bool ShadowValid { get; private set; }
            public unsafe byte* ShadowData;
            public byte[] ShadowFBytes;
            public int ShadowDataSize
            {
                get
                {
                    return GetImageDataSize(ShadowWidth, ShadowHeight, Version, "阴影图");
                }
            }
            #endregion

            #region Overlay
            public short OverlayWidth;
            public short OverlayHeight;

            public Bitmap OverlayImage, OverlayPreview;
            public bool OverlayValid { get; private set; }
            public unsafe byte* OverlayData;
            public byte[] OverlayFBytes;
            public int OverlayDataSize
            {
                get
                {
                    return GetImageDataSize(OverlayWidth, OverlayHeight, Version, "叠加图");
                }
            }
            #endregion


            public DateTime ExpireTime;

            public Mir3Image(int version)
            {
                Version = version;
            }

            public Mir3Image(BinaryReader reader, int version)
            {
                Version = version;

                Position = reader.ReadInt32();

                Width = reader.ReadInt16();
                Height = reader.ReadInt16();
                OffSetX = reader.ReadInt16();
                OffSetY = reader.ReadInt16();

                ShadowType = reader.ReadByte();
                ShadowWidth = reader.ReadInt16();
                ShadowHeight = reader.ReadInt16();
                ShadowOffSetX = reader.ReadInt16();
                ShadowOffSetY = reader.ReadInt16();

                OverlayWidth = reader.ReadInt16();
                OverlayHeight = reader.ReadInt16();

                ValidateRequiredStoredDimensions(Width, Height, "主图");
                ValidateOptionalStoredDimensions(ShadowWidth, ShadowHeight, "阴影图");
                ValidateOptionalStoredDimensions(OverlayWidth, OverlayHeight, "叠加图");
            }

            private static int Pad4(int value)
            {
                return value + (4 - value % 4) % 4;
            }

            private static int GetImageDataSize(short width, short height, int version, string description)
            {
                if (width <= 0 || height <= 0)
                    return 0;

                long paddedWidth = Pad4(width);
                long paddedHeight = Pad4(height);
                long pixels = paddedWidth * paddedHeight;
                if (pixels > MaxImagePixels)
                    throw new InvalidDataException($"{description}尺寸过大: {width}x{height}。");

                long dataSize = version > 0 ? pixels : pixels / 2;
                if (dataSize > int.MaxValue)
                    throw new InvalidDataException($"{description}数据长度过大: {dataSize:#,##0} 字节。");

                return (int)dataSize;
            }

            private static void ValidateBitmapDimensions(Bitmap bitmap, string description)
            {
                if (bitmap == null) return;

                if (bitmap.Width <= 0 ||
                    bitmap.Height <= 0 ||
                    bitmap.Width > short.MaxValue ||
                    bitmap.Height > short.MaxValue ||
                    (long)Pad4(bitmap.Width) * Pad4(bitmap.Height) > MaxImagePixels)
                    throw new InvalidDataException($"{description}尺寸无效: {bitmap.Width}x{bitmap.Height}。");
            }

            private static void ValidateRequiredStoredDimensions(short width, short height, string description)
            {
                if (width <= 0 ||
                    height <= 0 ||
                    (long)Pad4(width) * Pad4(height) > MaxImagePixels)
                    throw new InvalidDataException($"{description}尺寸无效: {width}x{height}。");
            }

            private static void ValidateOptionalStoredDimensions(short width, short height, string description)
            {
                if (width == 0 && height == 0) return;

                ValidateRequiredStoredDimensions(width, height, description);
            }

            private static byte[] PreparePixels(Bitmap bitmap, bool useBlackKeyTransparency)
            {
                byte[] pixels = new byte[bitmap.Width * bitmap.Height * 4];
                BitmapData data = null;

                try
                {
                    data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly,
                                                     PixelFormat.Format32bppArgb);

                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                }
                finally
                {
                    if (data != null)
                        bitmap.UnlockBits(data);
                }

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    //Reverse Red/Blue
                    byte b = pixels[i];
                    pixels[i] = pixels[i + 2];
                    pixels[i + 2] = b;

                    if (useBlackKeyTransparency && pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0)
                        pixels[i + 3] = 0; //Make Transparent
                }

                return pixels;
            }


            public unsafe Mir3Image(Bitmap image, int version, bool useBlackKeyTransparency = false)
            {
                Version = version;

                if (image == null)
                {
                    FBytes = new byte[0];
                    return;
                }

                ValidateBitmapDimensions(image, "主图");
                Position = -1;
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

                byte[] pixels = PreparePixels(image, useBlackKeyTransparency);

                int count = Squish.GetStorageRequirements(image.Width, image.Height, DxtFlags);

                FBytes = new byte[count];
                fixed (byte* dest = FBytes)
                fixed (byte* source = pixels)
                {
                    Squish.CompressImage((IntPtr)source, image.Width, image.Height, (IntPtr)dest, DxtFlags);
                }
            }

            public unsafe Mir3Image(Bitmap image, Bitmap shadow, Bitmap overlay, int version, bool useBlackKeyTransparency = false)
            {
                Version = version;

                if (image == null)
                {
                    shadow?.Dispose();
                    overlay?.Dispose();
                    FBytes = new byte[0];
                    return;
                }

                ValidateBitmapDimensions(image, "主图");
                ValidateBitmapDimensions(shadow, "阴影图");
                ValidateBitmapDimensions(overlay, "叠加图");
                Position = -1;
                Width = (short)image.Width;
                Height = (short)image.Height;

                int w = image.Width + (4 - image.Width % 4) % 4;
                int h = image.Height + (4 - image.Height % 4) % 4;

                if (image.Width != w || image.Height != h)
                {
                    Bitmap temp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
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

                byte[] pixels = PreparePixels(image, useBlackKeyTransparency);

                int count = Squish.GetStorageRequirements(image.Width, image.Height, DxtFlags);

                FBytes = new byte[count];
                fixed (byte* dest = FBytes)
                fixed (byte* source = pixels)
                {
                    Squish.CompressImage((IntPtr)source, image.Width, image.Height, (IntPtr)dest, DxtFlags);
                }

                //Shadow
                if (shadow != null)
                {
                    ShadowWidth = (short)shadow.Width;
                    ShadowHeight = (short)shadow.Height;

                    w = shadow.Width + (4 - shadow.Width % 4) % 4;
                    h = shadow.Height + (4 - shadow.Height % 4) % 4;

                    if (shadow.Width != w || shadow.Height != h)
                    {
                        Bitmap temp = new Bitmap(w, h);
                        using (Graphics g = Graphics.FromImage(temp))
                        {
                            g.Clear(Color.Transparent);
                            g.DrawImage(shadow, 0, 0);
                            g.Save();
                        }
                        shadow.Dispose();
                        shadow = temp;
                    }

                    ShadowImage = shadow;

                    pixels = PreparePixels(shadow, useBlackKeyTransparency);

                    count = Squish.GetStorageRequirements(shadow.Width, shadow.Height, DxtFlags);

                    ShadowFBytes = new byte[count];
                    fixed (byte* dest = ShadowFBytes)
                    fixed (byte* source = pixels)
                    {
                        Squish.CompressImage((IntPtr)source, shadow.Width, shadow.Height, (IntPtr)dest, DxtFlags);
                    }
                }

                //Overlay
                if (overlay != null)
                {
                    OverlayWidth = (short)overlay.Width;
                    OverlayHeight = (short)overlay.Height;

                    w = overlay.Width + (4 - overlay.Width % 4) % 4;
                    h = overlay.Height + (4 - overlay.Height % 4) % 4;

                    if (overlay.Width != w || overlay.Height != h)
                    {
                        Bitmap temp = new Bitmap(w, h);
                        using (Graphics g = Graphics.FromImage(temp))
                        {
                            g.Clear(Color.Transparent);
                            g.DrawImage(overlay, 0, 0);
                            g.Save();
                        }
                        overlay.Dispose();
                        overlay = temp;
                    }

                    OverlayImage = overlay;

                    pixels = PreparePixels(overlay, useBlackKeyTransparency);

                    count = Squish.GetStorageRequirements(overlay.Width, overlay.Height, DxtFlags);

                    OverlayFBytes = new byte[count];
                    fixed (byte* dest = OverlayFBytes)
                    fixed (byte* source = pixels)
                    {
                        Squish.CompressImage((IntPtr)source, overlay.Width, overlay.Height, (IntPtr)dest, DxtFlags);
                    }
                }
            }

            public unsafe void CreateImage(BinaryReader reader)
            {
                if (Position == 0) return;

                int w = Width + (4 - Width % 4) % 4;
                int h = Height + (4 - Height % 4) % 4;

                if (w == 0 || h == 0) return;

                Bitmap bitmap = new Bitmap(w, h);
                BitmapData data = null;

                try
                {
                    data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

                    reader.BaseStream.Seek(Position, SeekOrigin.Begin);
                    FBytes = ReadExactBytes(reader, ImageDataSize, "主图");

                    fixed (byte* source = FBytes)
                        Squish.DecompressImage(data.Scan0, w, h, (IntPtr)source, DxtFlags);

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
                    ImageValid = true;
                }
                finally
                {
                    if (data != null)
                        (bitmap ?? Image)?.UnlockBits(data);

                    bitmap?.Dispose();
                }
            }
            public unsafe void CreateShadow(BinaryReader reader)
            {
                if (Position == 0) return;

                if (!ImageValid)
                    CreateImage(reader);

                int w = ShadowWidth + (4 - ShadowWidth % 4) % 4;
                int h = ShadowHeight + (4 - ShadowHeight % 4) % 4;

                if (w == 0 || h == 0) return;

                Bitmap bitmap = new Bitmap(w, h);
                BitmapData data = null;

                try
                {
                    data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

                    reader.BaseStream.Seek(Position + ImageDataSize, SeekOrigin.Begin);
                    ShadowFBytes = ReadExactBytes(reader, ShadowDataSize, "阴影图");

                    fixed (byte* source = ShadowFBytes)
                        Squish.DecompressImage(data.Scan0, w, h, (IntPtr)source, DxtFlags);

                    byte* dest = (byte*)data.Scan0;

                    for (int i = 0; i < h * w * 4; i += 4)
                    {
                        //Reverse Red/Blue
                        byte b = dest[i];
                        dest[i] = dest[i + 2];
                        dest[i + 2] = b;
                    }

                    ShadowImage?.Dispose();
                    ShadowImage = bitmap;
                    bitmap = null;
                    ShadowValid = true;
                }
                finally
                {
                    if (data != null)
                        (bitmap ?? ShadowImage)?.UnlockBits(data);

                    bitmap?.Dispose();
                }
            }
            public unsafe void CreateOverlay(BinaryReader reader)
            {
                if (Position == 0) return;

                if (!ImageValid)
                    CreateImage(reader);

                int w = OverlayWidth + (4 - OverlayWidth % 4) % 4;
                int h = OverlayHeight + (4 - OverlayHeight % 4) % 4;

                if (w == 0 || h == 0) return;

                Bitmap bitmap = new Bitmap(w, h);
                BitmapData data = null;

                try
                {
                    data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

                    reader.BaseStream.Seek(Position + ImageDataSize + ShadowDataSize, SeekOrigin.Begin);
                    OverlayFBytes = ReadExactBytes(reader, OverlayDataSize, "叠加图");

                    fixed (byte* source = OverlayFBytes)
                        Squish.DecompressImage(data.Scan0, w, h, (IntPtr)source, DxtFlags);

                    byte* dest = (byte*)data.Scan0;

                    for (int i = 0; i < h * w * 4; i += 4)
                    {
                        //Reverse Red/Blue
                        byte b = dest[i];
                        dest[i] = dest[i + 2];
                        dest[i + 2] = b;
                    }

                    OverlayImage?.Dispose();
                    OverlayImage = bitmap;
                    bitmap = null;
                    OverlayValid = true;
                }
                finally
                {
                    if (data != null)
                        (bitmap ?? OverlayImage)?.UnlockBits(data);

                    bitmap?.Dispose();
                }
            }

            private static byte[] ReadExactBytes(BinaryReader reader, int count, string description)
            {
                if (count < 0)
                    throw new InvalidDataException($"{description}长度无效: {count:#,##0} 字节。");

                byte[] bytes = reader.ReadBytes(count);
                if (bytes.Length != count)
                    throw new InvalidDataException($"{description}数据不完整: 需要 {count:#,##0} 字节，实际 {bytes.Length:#,##0} 字节。");

                return bytes;
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
                    g.InterpolationMode = InterpolationMode.Low;//HighQualityBicubic
                    g.Clear(Color.Transparent);
                    int w = Math.Min((int)Width, 64);
                    int h = Math.Min((int)Height, 64);
                    g.DrawImage(Image, new Rectangle((64 - w) / 2, (64 - h) / 2, w, h), new Rectangle(0, 0, Width, Height), GraphicsUnit.Pixel);

                    g.Save();
                }
            }

            public void CreateShadowPreview()
            {
                if (ShadowImage == null)
                {
                    ShadowPreview = new Bitmap(1, 1);
                    return;
                }

                ShadowPreview = new Bitmap(64, 64);

                using (Graphics g = Graphics.FromImage(ShadowPreview))
                {
                    g.InterpolationMode = InterpolationMode.Low;//HighQualityBicubic
                    g.Clear(Color.Transparent);
                    int w = Math.Min((int)ShadowWidth, 64);
                    int h = Math.Min((int)ShadowHeight, 64);
                    g.DrawImage(ShadowImage, new Rectangle((64 - w) / 2, (64 - h) / 2, w, h), new Rectangle(0, 0, ShadowWidth, ShadowHeight), GraphicsUnit.Pixel);

                    g.Save();
                }
            }

            public void CreateOverlayPreview()
            {
                if (OverlayImage == null)
                {
                    OverlayPreview = new Bitmap(1, 1);
                    return;
                }

                OverlayPreview = new Bitmap(64, 64);

                using (Graphics g = Graphics.FromImage(OverlayPreview))
                {
                    g.InterpolationMode = InterpolationMode.Low;//HighQualityBicubic
                    g.Clear(Color.Transparent);
                    int w = Math.Min((int)OverlayWidth, 64);
                    int h = Math.Min((int)OverlayHeight, 64);
                    g.DrawImage(OverlayImage, new Rectangle((64 - w) / 2, (64 - h) / 2, w, h), new Rectangle(0, 0, OverlayWidth, OverlayHeight), GraphicsUnit.Pixel);

                    g.Save();
                }
            }

            public void SaveHeader(BinaryWriter writer)
            {
                writer.Write(Position);

                writer.Write(Width);
                writer.Write(Height);
                writer.Write(OffSetX);
                writer.Write(OffSetY);

                writer.Write(ShadowType);
                writer.Write(ShadowWidth);
                writer.Write(ShadowHeight);
                writer.Write(ShadowOffSetX);
                writer.Write(ShadowOffSetY);

                writer.Write(OverlayWidth);
                writer.Write(OverlayHeight);
            }

            private SquishFlags DxtFlags
            {
                get
                {
                    return Version switch
                    {
                        0 => SquishFlags.Dxt1,
                        _ => SquishFlags.Dxt5,
                    };
                }
            }

            #region IDisposable Support

            public bool IsDisposed { get; private set; }

            public void Dispose(bool disposing)
            {
                if (IsDisposed) return;

                if (disposing)
                {
                    Image?.Dispose();
                    Image = null;
                    Preview?.Dispose();
                    Preview = null;

                    ShadowImage?.Dispose();
                    ShadowImage = null;
                    ShadowPreview?.Dispose();
                    ShadowPreview = null;

                    OverlayImage?.Dispose();
                    OverlayImage = null;
                    OverlayPreview?.Dispose();
                    OverlayPreview = null;

                    Position = 0;

                    Width = 0;
                    Height = 0;
                    OffSetX = 0;
                    OffSetY = 0;

                    ShadowWidth = 0;
                    ShadowHeight = 0;
                    ShadowOffSetX = 0;
                    ShadowOffSetY = 0;

                    OverlayWidth = 0;
                    OverlayHeight = 0;
                }

                IsDisposed = true;
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
            ~Mir3Image()
            {
                Dispose(false);
            }

            #endregion
        }
    }
}
