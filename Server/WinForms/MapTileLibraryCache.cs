using Library;
using Server.Envir;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Server.WinForms
{
    internal sealed class MapTileLibraryCache : IDisposable
    {
        private readonly Dictionary<LibraryFile, MapTileLibrary> _libraries = new Dictionary<LibraryFile, MapTileLibrary>();
        private readonly Dictionary<LibraryFile, string> _missingLibraries = new Dictionary<LibraryFile, string>();
        private string _clientPathConfigSnapshot;
        private string _clientPathSnapshot;

        public int LoadedLibraryCount
        {
            get
            {
                EnsureClientPathCurrent();
                return _libraries.Count;
            }
        }

        public int MissingLibraryCount
        {
            get
            {
                EnsureClientPathCurrent();
                return _missingLibraries.Count;
            }
        }

        public string LastFailure { get; private set; }
        public string ClientPath
        {
            get
            {
                EnsureClientPathCurrent();
                return _clientPathSnapshot;
            }
        }

        public bool TryGetLibrary(LibraryFile file, out MapTileLibrary library)
        {
            EnsureClientPathCurrent();
            library = null;

            if (file == LibraryFile.None) return false;

            if (_libraries.TryGetValue(file, out library))
            {
                if (!string.IsNullOrWhiteSpace(library.LoadFailure))
                {
                    RememberMissing(file, $"图库读取失败: {library.LoadFailure}");
                    _libraries.Remove(file);
                    library.Dispose();
                    library = null;
                    return false;
                }

                return true;
            }

            if (_missingLibraries.ContainsKey(file))
                return false;

            if (!Libraries.LibraryList.TryGetValue(file, out string relativePath))
            {
                RememberMissing(file, "没有图库路径映射");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_clientPathSnapshot))
            {
                RememberMissing(file, string.IsNullOrWhiteSpace(LastFailure) ? "未配置 ClientPath" : LastFailure);
                return false;
            }

            if (!TryResolveLibraryFullPath(file, relativePath, out string fullPath))
                return false;

            if (!File.Exists(fullPath))
            {
                RememberMissing(file, $"图库不存在: {fullPath}");
                return false;
            }

            try
            {
                library = new MapTileLibrary(fullPath);
                _libraries[file] = library;
                return true;
            }
            catch (Exception ex)
            {
                RememberMissing(file, $"图库加载失败: {fullPath} ({ex.Message})");
                return false;
            }
        }

        public string GetStatusText()
        {
            EnsureClientPathCurrent();

            if (string.IsNullOrWhiteSpace(_clientPathSnapshot))
            {
                if (!string.IsNullOrWhiteSpace(LastFailure))
                    return $"显示: 属性图（{LastFailure}）";

                return "显示: 属性图（未配置 ClientPath）";
            }

            if (_libraries.Count == 0 && _missingLibraries.Count == 0)
                return "显示: 真实地图";

            if (_missingLibraries.Count == 0)
                return $"显示: 真实地图（图库 {_libraries.Count}）";

            return $"显示: 真实地图（图库 {_libraries.Count}，缺失 {_missingLibraries.Count}）";
        }

        public bool TryGetLibraryPath(LibraryFile file, out string relativePath, out string fullPath)
        {
            EnsureClientPathCurrent();
            relativePath = null;
            fullPath = null;

            if (!Libraries.LibraryList.TryGetValue(file, out relativePath))
                return false;

            if (string.IsNullOrWhiteSpace(_clientPathSnapshot))
                return true;

            return TryResolveLibraryFullPath(file, relativePath, out fullPath);
        }

        public bool TryGetMissingReason(LibraryFile file, out string reason)
        {
            EnsureClientPathCurrent();
            return _missingLibraries.TryGetValue(file, out reason);
        }

        private void RememberMissing(LibraryFile file, string reason)
        {
            _missingLibraries[file] = reason;
            LastFailure = reason;
        }

        private static string ResolveClientPath(out string failure)
        {
            failure = null;
            if (string.IsNullOrWhiteSpace(Config.ClientPath)) return null;

            try
            {
                string configuredPath = Config.ClientPath.Trim();
                string path = Path.IsPathRooted(configuredPath)
                    ? configuredPath
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configuredPath);

                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
            {
                failure = $"ClientPath 无效: {Config.ClientPath} ({ex.Message})";
                return null;
            }
        }

        private bool TryResolveLibraryFullPath(LibraryFile file, string relativePath, out string fullPath)
        {
            fullPath = null;

            try
            {
                string root = Path.GetFullPath(_clientPathSnapshot);
                if (Path.IsPathRooted(relativePath))
                {
                    RememberMissing(file, $"图库路径必须是相对路径: {relativePath}");
                    return false;
                }

                string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
                if (!IsPathInsideDirectory(root, candidate))
                {
                    RememberMissing(file, $"图库路径不能跳出 ClientPath: {relativePath}");
                    return false;
                }

                fullPath = candidate;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
            {
                RememberMissing(file, $"图库路径无效: {relativePath} ({ex.Message})");
                return false;
            }
        }

        private static bool IsPathInsideDirectory(string rootDirectory, string path)
        {
            string normalizedRoot = Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureClientPathCurrent()
        {
            string configuredClientPath = Config.ClientPath?.Trim() ?? string.Empty;
            string clientPath = ResolveClientPath(out string failure);
            if (string.Equals(_clientPathConfigSnapshot, configuredClientPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_clientPathSnapshot, clientPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(failure))
                    LastFailure = failure;

                return;
            }

            Clear();
            _clientPathConfigSnapshot = configuredClientPath;
            _clientPathSnapshot = clientPath;
            LastFailure = failure;
        }

        public void Dispose()
        {
            Clear();
        }

        private void Clear()
        {
            foreach (MapTileLibrary library in _libraries.Values)
                library.Dispose();

            _libraries.Clear();
            _missingLibraries.Clear();
        }
    }

    internal sealed class MapTileLibrary : IDisposable
    {
        private readonly string _fileName;
        private readonly FileStream _stream;
        private readonly BinaryReader _reader;
        private readonly object _loadLocker = new object();

        private bool _loaded;
        private bool _loading;
        private int _version;
        private MapTileImage[] _images;

        public string LoadFailure { get; private set; }

        public MapTileLibrary(string fileName)
        {
            _fileName = fileName;
            _stream = File.OpenRead(fileName);
            _reader = new BinaryReader(_stream);
        }

        public Size GetSize(int index)
        {
            if (!CheckImage(index)) return Size.Empty;

            return new Size(_images[index].Width, _images[index].Height);
        }

        public bool HasImage(int index)
        {
            return CheckImage(index);
        }

        public Bitmap GetBitmap(int index)
        {
            if (!CheckImage(index)) return null;

            try
            {
                return _images[index].GetBitmap(_reader);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or ExternalException)
            {
                LoadFailure = $"图片 {index} 读取失败: {ex.Message}";
                SEnvir.Log($"地图图库图片读取失败: {_fileName} #{index}\r\n{ex}");
                return null;
            }
        }

        private void ReadLibrary()
        {
            lock (_loadLocker)
            {
                if (_loaded) return;

                if (_loading) return;
                _loading = true;
            }

            try
            {
                byte[] header;

                lock (_reader)
                {
                    _reader.BaseStream.Seek(0, SeekOrigin.Begin);
                    int headerLength = _reader.ReadInt32();
                    long remaining = _reader.BaseStream.Length - _reader.BaseStream.Position;
                    if (headerLength <= 0 || headerLength > remaining)
                        throw new InvalidDataException($"图库头长度无效: {headerLength}。");

                    header = _reader.ReadBytes(headerLength);
                    if (header.Length != headerLength)
                        throw new InvalidDataException($"图库头数据不完整: 需要 {headerLength:#,##0} 字节，实际 {header.Length:#,##0} 字节。");
                }

                using (MemoryStream stream = new MemoryStream(header))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    int value = reader.ReadInt32();

                    int count = value & 0x1FFFFFF;
                    _version = (value >> 25) & 0x7F;

                    if (_version == 0)
                        count = value;

                    long remainingHeaderBytes = reader.BaseStream.Length - reader.BaseStream.Position;
                    if (count < 0 || count > remainingHeaderBytes)
                        throw new InvalidDataException($"图库图片数量无效: {count}。");

                    _images = new MapTileImage[count];

                    for (int i = 0; i < _images.Length; i++)
                    {
                        if (reader.BaseStream.Position >= reader.BaseStream.Length)
                            throw new InvalidDataException($"图库头数据不完整: 缺少图片 {i} 的存在标记。");

                        if (!reader.ReadBoolean()) continue;

                        if (reader.BaseStream.Length - reader.BaseStream.Position < MapTileImage.HeaderSize)
                            throw new InvalidDataException($"图库头数据不完整: 图片 {i} 的头部不足 {MapTileImage.HeaderSize} 字节。");

                        _images[i] = new MapTileImage(reader, _version);
                    }
                }
            }
            catch (Exception ex)
            {
                LoadFailure = ex.Message;
                _images = Array.Empty<MapTileImage>();
                SEnvir.Log($"地图图库读取失败: {_fileName}\r\n{ex}");
            }
            finally
            {
                lock (_loadLocker)
                {
                    _loaded = true;
                    _loading = false;
                    Monitor.PulseAll(_loadLocker);
                }
            }
        }

        private bool CheckImage(int index)
        {
            if (!_loaded)
                ReadLibrary();

            lock (_loadLocker)
            {
                while (_loading && !_loaded)
                    Monitor.Wait(_loadLocker);
            }

            return _loaded && index >= 0 && index < _images.Length && _images[index] != null;
        }

        public void Dispose()
        {
            if (_images != null)
            {
                foreach (MapTileImage image in _images)
                    image?.Dispose();
            }

            _reader?.Dispose();
            _stream?.Dispose();
        }
    }

    internal sealed class MapTileImage : IDisposable
    {
        public const int HeaderSize = 25;
        private const int MaxImagePixels = 16777216;

        private readonly int _position;
        private readonly int _version;
        private Bitmap _bitmap;

        public short Width { get; }
        public short Height { get; }

        private long ImageDataSize
        {
            get
            {
                long width = Pad4(Width);
                long height = Pad4(Height);

                return _version > 0 ? width * height : width * height / 2;
            }
        }

        public MapTileImage(BinaryReader reader, int version)
        {
            _version = version;
            _position = reader.ReadInt32();

            Width = reader.ReadInt16();
            Height = reader.ReadInt16();

            reader.BaseStream.Seek(2 + 2 + 1 + 2 + 2 + 2 + 2 + 2 + 2, SeekOrigin.Current);
        }

        public Bitmap GetBitmap(BinaryReader reader)
        {
            if (_bitmap != null) return _bitmap;
            if (_position == 0 || Width <= 0 || Height <= 0) return null;

            long imageDataSize = ImageDataSize;
            long pixelDataSize = (long)Width * Height * 4;
            if (imageDataSize <= 0 ||
                imageDataSize > int.MaxValue ||
                pixelDataSize > int.MaxValue ||
                (long)Width * Height > MaxImagePixels)
                throw new InvalidDataException($"图库图片尺寸无效: {Width}x{Height}。");

            byte[] buffer;

            lock (reader)
            {
                if (_position < 0 || _position > reader.BaseStream.Length)
                    throw new InvalidDataException($"图库图片位置无效: {_position}。");

                reader.BaseStream.Seek(_position, SeekOrigin.Begin);
                buffer = reader.ReadBytes((int)imageDataSize);
            }

            if (buffer.Length != imageDataSize)
                throw new InvalidDataException($"图库图片数据不完整: 位置 {_position}，需要 {imageDataSize:#,##0} 字节，实际 {buffer.Length:#,##0} 字节。");

            _bitmap = _version > 0
                ? DecodeDxt5(buffer, Width, Height)
                : DecodeDxt1(buffer, Width, Height);

            return _bitmap;
        }

        private static Bitmap DecodeDxt1(byte[] data, int width, int height)
        {
            byte[] pixels = new byte[width * height * 4];
            int paddedWidth = Pad4(width);
            int paddedHeight = Pad4(height);
            int source = 0;

            for (int blockY = 0; blockY < paddedHeight; blockY += 4)
            {
                for (int blockX = 0; blockX < paddedWidth; blockX += 4)
                {
                    if (source + 8 > data.Length)
                        return CreateBitmap(pixels, width, height);

                    ushort colour0 = BitConverter.ToUInt16(data, source);
                    ushort colour1 = BitConverter.ToUInt16(data, source + 2);
                    uint lookup = BitConverter.ToUInt32(data, source + 4);

                    Rgba[] colours = BuildDxt1Palette(colour0, colour1);

                    WriteColourBlock(pixels, width, height, blockX, blockY, colours, lookup, null);
                    source += 8;
                }
            }

            return CreateBitmap(pixels, width, height);
        }

        private static Bitmap DecodeDxt5(byte[] data, int width, int height)
        {
            byte[] pixels = new byte[width * height * 4];
            int paddedWidth = Pad4(width);
            int paddedHeight = Pad4(height);
            int source = 0;

            for (int blockY = 0; blockY < paddedHeight; blockY += 4)
            {
                for (int blockX = 0; blockX < paddedWidth; blockX += 4)
                {
                    if (source + 16 > data.Length)
                        return CreateBitmap(pixels, width, height);

                    byte[] alphaPalette = BuildDxt5AlphaPalette(data[source], data[source + 1]);
                    ulong alphaLookup = 0;
                    for (int i = 0; i < 6; i++)
                        alphaLookup |= (ulong)data[source + 2 + i] << (8 * i);

                    ushort colour0 = BitConverter.ToUInt16(data, source + 8);
                    ushort colour1 = BitConverter.ToUInt16(data, source + 10);
                    uint colourLookup = BitConverter.ToUInt32(data, source + 12);

                    Rgba[] colours = BuildDxt5Palette(colour0, colour1);
                    byte[] alphas = new byte[16];

                    for (int i = 0; i < alphas.Length; i++)
                        alphas[i] = alphaPalette[(alphaLookup >> (3 * i)) & 0x07];

                    WriteColourBlock(pixels, width, height, blockX, blockY, colours, colourLookup, alphas);
                    source += 16;
                }
            }

            return CreateBitmap(pixels, width, height);
        }

        private static Rgba[] BuildDxt1Palette(ushort colour0, ushort colour1)
        {
            Rgba c0 = Decode565(colour0, 255);
            Rgba c1 = Decode565(colour1, 255);

            Rgba[] colours = new Rgba[4];
            colours[0] = c0;
            colours[1] = c1;

            if (colour0 > colour1)
            {
                colours[2] = Lerp(c0, c1, 2, 1, 3, 255);
                colours[3] = Lerp(c0, c1, 1, 2, 3, 255);
            }
            else
            {
                colours[2] = Lerp(c0, c1, 1, 1, 2, 255);
                colours[3] = new Rgba(0, 0, 0, 0);
            }

            return colours;
        }

        private static Rgba[] BuildDxt5Palette(ushort colour0, ushort colour1)
        {
            Rgba c0 = Decode565(colour0, 255);
            Rgba c1 = Decode565(colour1, 255);

            return new[]
            {
                c0,
                c1,
                Lerp(c0, c1, 2, 1, 3, 255),
                Lerp(c0, c1, 1, 2, 3, 255),
            };
        }

        private static byte[] BuildDxt5AlphaPalette(byte alpha0, byte alpha1)
        {
            byte[] values = new byte[8];
            values[0] = alpha0;
            values[1] = alpha1;

            if (alpha0 > alpha1)
            {
                for (int i = 2; i < 8; i++)
                    values[i] = (byte)(((8 - i) * alpha0 + (i - 1) * alpha1) / 7);
            }
            else
            {
                for (int i = 2; i < 6; i++)
                    values[i] = (byte)(((6 - i) * alpha0 + (i - 1) * alpha1) / 5);

                values[6] = 0;
                values[7] = 255;
            }

            return values;
        }

        private static void WriteColourBlock(byte[] pixels, int width, int height, int blockX, int blockY, Rgba[] colours, uint lookup, byte[] alphas)
        {
            for (int y = 0; y < 4; y++)
            {
                int targetY = blockY + y;
                if (targetY >= height) continue;

                for (int x = 0; x < 4; x++)
                {
                    int targetX = blockX + x;
                    if (targetX >= width) continue;

                    int blockIndex = y * 4 + x;
                    int colourIndex = (int)((lookup >> (2 * blockIndex)) & 0x03);
                    Rgba colour = colours[colourIndex];
                    byte alpha = alphas == null ? colour.A : alphas[blockIndex];

                    int target = (targetY * width + targetX) * 4;
                    pixels[target] = colour.B;
                    pixels[target + 1] = colour.G;
                    pixels[target + 2] = colour.R;
                    pixels[target + 3] = alpha;
                }
            }
        }

        private static Bitmap CreateBitmap(byte[] pixels, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            Rectangle rectangle = new Rectangle(0, 0, width, height);
            BitmapData bitmapData = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = bitmapData.Stride;
                int rowSize = width * 4;

                if (stride == rowSize)
                {
                    Marshal.Copy(pixels, 0, bitmapData.Scan0, pixels.Length);
                }
                else
                {
                    for (int y = 0; y < height; y++)
                    {
                        IntPtr target = IntPtr.Add(bitmapData.Scan0, y * stride);
                        Marshal.Copy(pixels, y * rowSize, target, rowSize);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bitmap;
        }

        private static Rgba Decode565(ushort value, byte alpha)
        {
            byte r = (byte)(((value >> 11) & 0x1F) * 255 / 31);
            byte g = (byte)(((value >> 5) & 0x3F) * 255 / 63);
            byte b = (byte)((value & 0x1F) * 255 / 31);

            return new Rgba(r, g, b, alpha);
        }

        private static Rgba Lerp(Rgba first, Rgba second, int firstWeight, int secondWeight, int divisor, byte alpha)
        {
            return new Rgba(
                (byte)((first.R * firstWeight + second.R * secondWeight) / divisor),
                (byte)((first.G * firstWeight + second.G * secondWeight) / divisor),
                (byte)((first.B * firstWeight + second.B * secondWeight) / divisor),
                alpha);
        }

        private static int Pad4(int value)
        {
            return value + (4 - value % 4) % 4;
        }

        public void Dispose()
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }

        private readonly struct Rgba
        {
            public readonly byte R;
            public readonly byte G;
            public readonly byte B;
            public readonly byte A;

            public Rgba(byte r, byte g, byte b, byte a)
            {
                R = r;
                G = g;
                B = b;
                A = a;
            }
        }
    }
}
