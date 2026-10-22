using System;
using System.IO;

namespace Library.Network
{
    public sealed class PacketFrameReader
    {
        private byte[] _buffer = Array.Empty<byte>();
        private int _length;

        public int BufferedBytes => _length;

        public void Append(byte[] bytes, int offset, int count)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (offset < 0 || count < 0 || offset + count > bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0) return;

            EnsureCapacity(_length + count);
            Buffer.BlockCopy(bytes, offset, _buffer, _length, count);
            _length += count;
        }

        public bool TryReadPacket(ServerCapabilities capabilities, out Packet packet)
        {
            packet = null;

            if (_length < 4) return false;

            int frameLength = _buffer[3] << 24 | _buffer[2] << 16 | _buffer[1] << 8 | _buffer[0];

            if (frameLength < 6)
                throw new InvalidDataException($"Packet length {frameLength} is too small.");

            if (frameLength > Packet.MaxPacketSize)
                throw new InvalidDataException($"Packet length {frameLength} exceeds the maximum allowed size {Packet.MaxPacketSize}.");

            if (frameLength > _length) return false;

            byte[] frame = new byte[frameLength];
            Buffer.BlockCopy(_buffer, 0, frame, 0, frameLength);
            Consume(frameLength);

            packet = Packet.ReceivePacket(frame, out byte[] extra, capabilities);
            if (extra.Length != 0)
                throw new InvalidDataException("Packet frame reader produced an unexpected trailing payload.");

            return true;
        }

        public void Clear()
        {
            _length = 0;
        }

        private void EnsureCapacity(int capacity)
        {
            if (_buffer.Length >= capacity) return;

            int newSize = _buffer.Length == 0 ? 8 * 1024 : _buffer.Length;
            while (newSize < capacity)
                newSize *= 2;

            if (newSize > Packet.MaxPacketSize)
                newSize = Packet.MaxPacketSize;

            if (capacity > newSize)
                throw new InvalidDataException($"Buffered packet data exceeds the maximum allowed size {Packet.MaxPacketSize}.");

            byte[] next = new byte[newSize];
            if (_length > 0)
                Buffer.BlockCopy(_buffer, 0, next, 0, _length);
            _buffer = next;
        }

        private void Consume(int count)
        {
            int remaining = _length - count;
            if (remaining > 0)
                Buffer.BlockCopy(_buffer, count, _buffer, 0, remaining);

            _length = remaining;
        }
    }
}
