using ManagedSquish;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LibraryEditor
{
    public class WTLLibrary
    {
        private const int HeaderSize = 32;
        private const int OldImageHeaderSize = 16;
        private const int NewImageHeaderSize = 20;

        private readonly string _fileName;

        public WTLImage[] Images;

        private BinaryReader _bReader;
        private int _count;
        private long _dataStart;
        private long _dataEnd;
        private FileStream _fStream;
        private int[] _indexList;
        private bool _initialized;

        WTLLibrary shadowLibrary;

        public bool IsNewVersion { get; private set; }
        public bool ShadowFile { get; private set; }

        public WTLLibrary(string filename, bool shadowFile = false)
        {
            ShadowFile = shadowFile;

            _fileName = Path.ChangeExtension(filename, null);
            try
            {
                Initialize();
            }
            catch
            {
                Close();
                throw;
            }
        }

        public void Initialize()
        {
            _initialized = true;
            if (!File.Exists(_fileName + ".wtl")) return;
            _fStream = new FileStream(_fileName + ".wtl", FileMode.Open, FileAccess.Read, FileShare.Read);
            _bReader = new BinaryReader(_fStream);
            LoadImageInfo();

            for (int i = 0; i < _count; i++)
            {
                CheckMImage(i);
            }

            if (!ShadowFile)
            {
                string fname = _fileName + ".wtl";
                string shadowPath = fname.Replace(".wtl", "_S.wtl");

                shadowLibrary = null;
                if (File.Exists(shadowPath))
                    shadowLibrary = new WTLLibrary(shadowPath, true);
                else
                {
                    shadowPath = fname.Replace("Mon-", "MonS-");
                    if (shadowPath != fname && File.Exists(shadowPath))
                        shadowLibrary = new WTLLibrary(shadowPath, true);
                }
            }
        }

        private void LoadImageInfo()
        {
            if (_fStream.Length < HeaderSize)
                throw new InvalidDataException($"WTL图库头不完整: 需要 {HeaderSize:#,##0} 字节，实际 {_fStream.Length:#,##0} 字节。");

            _fStream.Seek(2, SeekOrigin.Begin);
            var version = System.Text.Encoding.UTF8.GetString(ReadExactBytes(_bReader, 20, "WTL版本头")).TrimEnd('\0');
            IsNewVersion = version == "ILIB v2.0-WEMADE";

            _fStream.Seek(28, SeekOrigin.Begin);
            _count = _bReader.ReadInt32();
            ValidateImageCount(_count, _fStream.Length);
            _indexList = new int[_count];
            Images = new WTLImage[_count];

            long indexTableStart;
            if (IsNewVersion)
            {
                indexTableStart = _fStream.Length - (long)_count * sizeof(int);
                _dataStart = HeaderSize;
                _dataEnd = indexTableStart;
                _bReader.BaseStream.Seek(indexTableStart, SeekOrigin.Begin);
            }
            else
            {
                indexTableStart = HeaderSize;
                _dataStart = HeaderSize + (long)_count * sizeof(int);
                _dataEnd = _fStream.Length;
            }

            for (int i = 0; i < _count; i++)
            {
                int offset = _bReader.ReadInt32();
                ValidateImageOffset(offset, i);
                _indexList[i] = offset;
            }
        }

        public void Close()
        {
            _bReader?.Dispose();
            _bReader = null;

            _fStream?.Dispose();
            _fStream = null;

            shadowLibrary?.Close();
            shadowLibrary = null;
        }

        public void CheckMImage(int index)
        {
            if (!_initialized)
                Initialize();

            if (index < 0 || index >= Images.Length) return;
            if (_indexList[index] == 0)
            {
                Images[index] = new WTLImage(index, IsNewVersion);
                return;
            }
            WTLImage image = Images[index];

            if (image == null)
            {
                _fStream.Seek(_indexList[index], SeekOrigin.Begin);
                image = new WTLImage(index, IsNewVersion, _bReader);
                Images[index] = image;
                image.CreateTexture(_bReader, _dataEnd);
            }
        }

        public void ToMLibrary(bool useBlackKeyTransparency = false)
        {
            string fileName = Path.ChangeExtension(_fileName, ".Zl");

            using Mir3Library library = Mir3Library.CreateEmpty(fileName, useBlackKeyTransparency);
            library.Version = Mir3Library.LIBRARY_VERSION;

            for (int i = 0; i < Images.Length; i++)
                library.Images.Add(null);

            ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = 8 };

            Parallel.For(0, Images.Length, options, i =>
            {
                WTLImage image = Images[i];
                WTLImage shadowimage = shadowLibrary != null && i < shadowLibrary.Images.Length ? shadowLibrary.Images[i] : null;

                library.Images[i] = CreateMir3Image(image, shadowimage, library.Version, library.UseBlackKeyTransparency);
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
            for (int i = 0; i < Images.Length; i++)
                lib.Images.Add(null);

            ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = 8 };

            Parallel.For(0, Images.Length, options, i =>
            {
                WTLImage image = Images[i];
                WTLImage shadowimage = shadowLibrary != null && i < shadowLibrary.Images.Length ? shadowLibrary.Images[i] : null;

                lib.Images[i + offset] = CreateMir3Image(image, shadowimage, lib.Version, lib.UseBlackKeyTransparency);
            });
            lib.AddBlanks(newImages);
        }

        private static Bitmap CloneBitmap(Bitmap image)
        {
            return image == null ? null : new Bitmap(image);
        }

        private static Mir3Library.Mir3Image CreateMir3Image(WTLImage image, WTLImage shadowimage, int version, bool useBlackKeyTransparency)
        {
            if (image == null)
                return new Mir3Library.Mir3Image((Bitmap)null, version, useBlackKeyTransparency);

            if (shadowimage?.Image != null)
                return new Mir3Library.Mir3Image(CloneBitmap(image.Image), CloneBitmap(shadowimage.Image), CloneBitmap(image.MaskImage), version, useBlackKeyTransparency) { OffSetX = image.X, OffSetY = image.Y, ShadowOffSetX = shadowimage.X, ShadowOffSetY = shadowimage.Y, ShadowType = shadowimage.Shadow };

            return new Mir3Library.Mir3Image(CloneBitmap(image.Image), null, CloneBitmap(image.MaskImage), version, useBlackKeyTransparency) { OffSetX = image.X, OffSetY = image.Y, ShadowOffSetX = image.ShadowX, ShadowOffSetY = image.ShadowY, ShadowType = image.Shadow };
        }

        private void ValidateImageCount(int count, long streamLength)
        {
            long indexBytes = (long)count * sizeof(int);
            if (count < 0 || indexBytes > streamLength - HeaderSize)
                throw new InvalidDataException($"WTL图库图片数量无效: {count:#,##0}。");
        }

        private void ValidateImageOffset(int offset, int imageIndex)
        {
            if (offset == 0)
                return;

            int imageHeaderSize = IsNewVersion ? NewImageHeaderSize : OldImageHeaderSize;
            if (offset < _dataStart || offset > _dataEnd - imageHeaderSize)
                throw new InvalidDataException($"WTL图库图片 {imageIndex} 的数据位置无效: {offset:#,##0}。");
        }

        private static byte[] ReadExactBytes(BinaryReader reader, int count, string description)
        {
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count)
                throw new InvalidDataException($"{description}数据不完整: 需要 {count:#,##0} 字节，实际 {bytes.Length:#,##0} 字节。");

            return bytes;
        }
    }

    public class WTLImage
    {
        public readonly short Width, Height, X, Y, ShadowX, ShadowY;
        public readonly int Length;

        public int DataOffset { get; }

        public readonly byte Shadow;

        public int Index { get; }
        public bool IsNewVersion { get; }
        public byte ImageTextureType { get; }
        public byte MaskTextureType { get; }

        private byte[] _fBytes;
        public Bitmap Image;

        public readonly Boolean HasMask;
        public short MaskWidth, MaskHeight, MaskX, MaskY;
        public int MaskLength;
        private byte[] _MaskfBytes;
        public Bitmap MaskImage;

        public WTLImage(int index, bool isNewVersion)
        {
            Index = index;
            IsNewVersion = isNewVersion;
            _fBytes = new byte[0];
            _MaskfBytes = new byte[0];
        }

        public WTLImage(int index, bool isNewVersion, BinaryReader bReader)
        {
            Index = index;
            IsNewVersion = isNewVersion;
            Width = bReader.ReadInt16();
            Height = bReader.ReadInt16();
            X = bReader.ReadInt16();
            Y = bReader.ReadInt16();
            ShadowX = bReader.ReadInt16();
            ShadowY = bReader.ReadInt16();

            if (IsNewVersion)
            {
                var imageU1 = bReader.ReadByte();
                ImageTextureType = bReader.ReadByte();
                var maskU1 = bReader.ReadByte();
                MaskTextureType = bReader.ReadByte();

                HasMask = MaskTextureType > 0 && MaskTextureType != 128;
                Length = AlignLength(bReader.ReadInt32());
                DataOffset = (int)bReader.BaseStream.Position;
            }
            else
            {
                Length = bReader.ReadByte() | bReader.ReadByte() << 8 | bReader.ReadByte() << 16;
                Shadow = bReader.ReadByte();
                HasMask = ((Shadow >> 7) == 1) ? true : false;
            }
        }

        public unsafe void CreateTexture(BinaryReader bReader, long dataLimit)
        {
            ValidateDataRange(bReader, Length, dataLimit, "WTL主图");
            Image = ReadImage(bReader, Length, Width, Height, ImageTextureType);
            if (HasMask)
            {
                if (IsNewVersion)
                {
                    MaskWidth = Width;
                    MaskHeight = Height;
                    MaskX = X;
                    MaskY = Y;
                    ValidateBytesAvailable(bReader, sizeof(int), dataLimit, "WTL遮罩头");
                    MaskLength = bReader.ReadInt32();
                }
                else
                {
                    ValidateBytesAvailable(bReader, 16, dataLimit, "WTL遮罩头");
                    MaskWidth = bReader.ReadInt16();
                    MaskHeight = bReader.ReadInt16();
                    MaskX = bReader.ReadInt16();
                    MaskY = bReader.ReadInt16();
                    bReader.ReadInt16();//mask shadow x
                    bReader.ReadInt16();//mask shadow y
                    MaskLength = bReader.ReadByte() | bReader.ReadByte() << 8 | bReader.ReadByte() << 16;
                    bReader.ReadByte(); //mask shadow
                }

                ValidateDataRange(bReader, MaskLength, dataLimit, "WTL遮罩图");
                MaskImage = ReadImage(bReader, MaskLength, MaskWidth, MaskHeight, MaskTextureType);
            }
        }

        private unsafe Bitmap DecompressV1Texture(BinaryReader bReader, int imageLength, short outputWidth, short outputHeight)
        {
            if (outputWidth < 4 || outputHeight <= 0)
            {
                bReader.BaseStream.Seek(imageLength, SeekOrigin.Current);
                return null;
            }

            const int size = 8;
            int offset = 0, blockOffSet = 0;
            List<byte> countList = new List<byte>();
            int tWidth = 2;

            while (tWidth < outputWidth)
                tWidth *= 2;

            _fBytes = bReader.ReadBytes(imageLength);

            Bitmap output = new Bitmap(outputWidth, outputHeight);
            if (_fBytes.Length != imageLength)
            {
                output.Dispose();
                return null;
            }

            BitmapData data = null;
            bool disposeOutput = true;

            try
            {
                data = output.LockBits(new Rectangle(0, 0, outputWidth, outputHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                byte* pixels = (byte*)data.Scan0;
                int cap = outputWidth * outputHeight * 4;
                int currentx = 0;

                while (blockOffSet < imageLength)
                {
                    if (blockOffSet + 8 > imageLength)
                        return null;

                    countList.Clear();
                    for (int i = 0; i < 8; i++)
                        countList.Add(_fBytes[blockOffSet++]);

                    for (int i = 0; i < countList.Count; i++)
                    {
                        int count = countList[i];

                        if (i % 2 == 0)
                        {
                            if (currentx >= tWidth)
                                currentx -= tWidth;

                            for (int off = 0; off < count; off++)
                            {
                                if (currentx < outputWidth)
                                    offset++;

                                currentx += 4;

                                if (currentx >= tWidth)
                                    currentx -= tWidth;
                            }
                            continue;
                        }

                        for (int c = 0; c < count; c++)
                        {
                            if (blockOffSet >= _fBytes.Length)
                                break;

                            byte[] newPixels = new byte[64];
                            byte[] block = new byte[size];

                            if (blockOffSet + size > _fBytes.Length)
                                return null;

                            Array.Copy(_fBytes, blockOffSet, block, 0, size);
                            blockOffSet += size;
                            DecompressBlock(newPixels, block);

                            int pixelOffSet = 0;
                            byte[] sourcePixel = new byte[4];

                            for (int py = 0; py < 4; py++)
                            {
                                for (int px = 0; px < 4; px++)
                                {
                                    int blockx = offset % (outputWidth / 4);
                                    int blocky = offset / (outputWidth / 4);

                                    int x = blockx * 4;
                                    int y = blocky * 4;

                                    int destPixel = ((y + py) * outputWidth) * 4 + (x + px) * 4;

                                    Array.Copy(newPixels, pixelOffSet, sourcePixel, 0, 4);
                                    pixelOffSet += 4;

                                    if (destPixel + 4 > cap)
                                        break;
                                    for (int pc = 0; pc < 4; pc++)
                                        pixels[destPixel + pc] = sourcePixel[pc];
                                }
                            }
                            offset++;
                            if (currentx >= outputWidth)
                                currentx -= outputWidth;
                            currentx += 4;
                        }
                    }
                }

                disposeOutput = false;
                return output;
            }
            finally
            {
                try
                {
                    if (data != null)
                        output.UnlockBits(data);
                }
                finally
                {
                    if (disposeOutput)
                        output.Dispose();
                }
            }
        }


        private unsafe Bitmap DecompressV2Texture(BinaryReader bReader, int imageLength, short outputWidth, short outputHeight, byte textureType)
        {
            if (outputWidth <= 0 || outputHeight <= 0)
            {
                bReader.BaseStream.Seek(imageLength, SeekOrigin.Current);
                return null;
            }

            var buffer = bReader.ReadBytes(imageLength);
            if (buffer.Length != imageLength)
                return null;

            int w = outputWidth + (4 - outputWidth % 4) % 4;
            int a = 1;
            while (true)
            {
                a *= 2;
                if (a >= w)
                {
                    w = a;
                    break;
                }
            }
            int h = outputHeight + (4 - outputHeight % 4) % 4;
            int e = w * h / 2;

            bool isRawArgb = textureType >= 128;
            // Type 129 stores raw ARGB pixels after zlib decompression; only 128 is already raw.
            bool isCompressed = textureType != 128;
            SquishFlags type;
            switch (textureType)
            {
                case 0:
                case 1:
                    type = SquishFlags.Dxt1;
                    break;
                case 3:
                    type = SquishFlags.Dxt3;
                    break;
                case 5:
                    type = SquishFlags.Dxt5;
                    break;
                case 128:
                case 129:
                    type = SquishFlags.Dxt1;
                    break;
                default:
                    throw new InvalidDataException($"不支持的 WTL 贴图类型: {textureType}。");
            }

            var decompressedBuffer = isCompressed ? Ionic.Zlib.DeflateStream.UncompressBuffer(buffer) : buffer;

            using Bitmap bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            if (isRawArgb)
            {
                if (decompressedBuffer.Length < w * h * 4)
                    return null;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        var width = w * 4;

                        int b = decompressedBuffer[(y * width) + (x * 4)];
                        int g = decompressedBuffer[(y * width) + (x * 4) + 1];
                        int r = decompressedBuffer[(y * width) + (x * 4) + 2];
                        int al = decompressedBuffer[(y * width) + (x * 4) + 3];

                        bitmap.SetPixel(x, y, Color.FromArgb(al, r, g, b));
                    }
                }
            }
            else
            {
                BitmapData data = null;

                try
                {
                    data = bitmap.LockBits(
                                    new Rectangle(0, 0, w, h),
                                    ImageLockMode.WriteOnly,
                                    PixelFormat.Format32bppArgb
                                );

                    int requiredBytes = Squish.GetStorageRequirements(w, h, type);
                    if (decompressedBuffer.Length < requiredBytes)
                        return null;

                    fixed (byte* source = decompressedBuffer)
                        Squish.DecompressImage(data.Scan0, w, h, (IntPtr)source, type);

                    byte* dest = (byte*)data.Scan0;

                    for (int i = 0; i < w * h * 4; i += 4)
                    {
                        //Reverse Red/Blue
                        byte b = dest[i];
                        dest[i] = dest[i + 2];
                        dest[i + 2] = b;
                    }

                }
                finally
                {
                    if (data != null)
                        bitmap.UnlockBits(data);
                }
            }

            Rectangle cloneRect = new Rectangle(0, 0, outputWidth, outputHeight);
            PixelFormat format = bitmap.PixelFormat;
            Bitmap cloneBitmap = bitmap.Clone(cloneRect, format);

            return cloneBitmap;
        }

        public unsafe Bitmap ReadImage(BinaryReader bReader, int imageLength, short outputWidth, short outputHeight, byte textureType)
        {
            if (imageLength <= 0)
                return null;

            return IsNewVersion
                ? DecompressV2Texture(bReader, imageLength, outputWidth, outputHeight, textureType)
                : DecompressV1Texture(bReader, imageLength, outputWidth, outputHeight);
        }

        private static int AlignLength(int length)
        {
            if (length < 0 || length > int.MaxValue - 3)
                throw new InvalidDataException($"WTL图像长度无效: {length:#,##0} 字节。");

            int remainder = length % 4;
            return remainder == 0 ? length : length + 4 - remainder;
        }

        private static void ValidateDataRange(BinaryReader reader, int length, long dataLimit, string description)
        {
            if (length < 0)
                throw new InvalidDataException($"{description}长度无效: {length:#,##0} 字节。");

            ValidateBytesAvailable(reader, length, dataLimit, description);
        }

        private static void ValidateBytesAvailable(BinaryReader reader, int length, long dataLimit, string description)
        {
            long position = reader.BaseStream.Position;
            if (position > dataLimit || length > dataLimit - position)
                throw new InvalidDataException($"{description}数据越界: 位置 {position:#,##0}，长度 {length:#,##0}，数据上限 {dataLimit:#,##0}。");
        }

        private static void DecompressBlock(IList<byte> newPixels, byte[] block)
        {
            byte[] colours = new byte[8];
            Array.Copy(block, 0, colours, 0, 8);

            byte[] codes = new byte[16];

            int a = Unpack(block, 0, codes, 0);
            int b = Unpack(block, 2, codes, 4);

            for (int i = 0; i < 3; i++)
            {
                int c = codes[i];
                int d = codes[4 + i];

                if (a <= b)
                {
                    codes[8 + i] = (byte)((c + d) / 2);
                    codes[12 + i] = 0;
                }
                else
                {
                    codes[8 + i] = (byte)((2 * c + d) / 3);
                    codes[12 + i] = (byte)((c + 2 * d) / 3);
                }
            }

            codes[8 + 3] = 255;
            codes[12 + 3] = (a <= b) ? (byte)0 : (byte)255;
            for (int i = 0; i < 4; i++)
            {
                if ((codes[i * 4] == 0) && (codes[(i * 4) + 1] == 0) && (codes[(i * 4) + 2] == 0) && (codes[(i * 4) + 3] == 255))
                { //dont ever use pure black cause that gives transparency issues
                    codes[i * 4] = 1;
                    codes[(i * 4) + 1] = 1;
                    codes[(i * 4) + 2] = 1;
                }
            }

            byte[] indices = new byte[16];
            for (int i = 0; i < 4; i++)
            {
                byte packed = block[4 + i];

                indices[0 + i * 4] = (byte)(packed & 0x3);
                indices[1 + i * 4] = (byte)((packed >> 2) & 0x3);
                indices[2 + i * 4] = (byte)((packed >> 4) & 0x3);
                indices[3 + i * 4] = (byte)((packed >> 6) & 0x3);
            }

            for (int i = 0; i < 16; i++)
            {
                byte offset = (byte)(4 * indices[i]);
                for (int j = 0; j < 4; j++)
                    newPixels[4 * i + j] = codes[offset + j];
            }
        }

        private static int Unpack(IList<byte> packed, int srcOffset, IList<byte> colour, int dstOffSet)
        {
            int value = packed[0 + srcOffset] | (packed[1 + srcOffset] << 8);
            // get components in the stored range
            byte red = (byte)((value >> 11) & 0x1F);
            byte green = (byte)((value >> 5) & 0x3F);
            byte blue = (byte)(value & 0x1F);

            // Scale up to 24 Bit
            colour[2 + dstOffSet] = (byte)((red << 3) | (red >> 2));
            colour[1 + dstOffSet] = (byte)((green << 2) | (green >> 4));
            colour[0 + dstOffSet] = (byte)((blue << 3) | (blue >> 2));
            colour[3 + dstOffSet] = 255;
            //*/
            return value;
        }
    }
}
