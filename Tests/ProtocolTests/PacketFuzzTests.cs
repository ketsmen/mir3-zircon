using System;
using System.IO;
using System.Linq;
using Library;
using Library.Network;
using G = Library.Network.GeneralPackets;
using Xunit;

namespace ProtocolTests
{
    public sealed class PacketFuzzTests
    {
        [Fact]
        public void PacketFrameReaderHandlesDeterministicRandomInputWithoutStateLeak()
        {
            Random random = new(12345);

            for (int i = 0; i < 250; i++)
            {
                byte[] bytes = new byte[random.Next(0, 256)];
                random.NextBytes(bytes);
                PacketFrameReader reader = new();

                try
                {
                    reader.Append(bytes, 0, bytes.Length);
                    while (reader.TryReadPacket(ServerCapabilities.Current, out _))
                    {
                    }

                    Assert.InRange(reader.BufferedBytes, 0, Math.Max(bytes.Length, 4));
                }
                catch (Exception ex) when (IsControlledReadFailure(ex))
                {
                    Assert.True(reader.BufferedBytes >= 0);
                }
            }
        }

        [Fact]
        public void PacketFrameReaderReadsValidPacketAcrossAllSplitPoints()
        {
            byte[] packet = new G.Connected { Capabilities = ServerCapabilities.Current }.GetPacketBytes();

            for (int split = 0; split < packet.Length; split++)
            {
                PacketFrameReader reader = new();
                reader.Append(packet, 0, split);
                Assert.False(reader.TryReadPacket(ServerCapabilities.Current, out Packet partial));
                Assert.Null(partial);

                reader.Append(packet, split, packet.Length - split);
                Assert.True(reader.TryReadPacket(ServerCapabilities.Current, out Packet complete));
                Assert.IsType<G.Connected>(complete);
                Assert.Equal(0, reader.BufferedBytes);
            }
        }

        [Fact]
        public void MutatedValidPacketsFailInControlledWays()
        {
            Random random = new(54321);
            byte[] packet = new G.Connected { Capabilities = ServerCapabilities.Current }.GetPacketBytes();

            for (int i = 0; i < 100; i++)
            {
                byte[] mutated = packet.ToArray();
                int mutations = random.Next(1, 5);

                for (int j = 0; j < mutations; j++)
                    mutated[random.Next(mutated.Length)] = (byte)random.Next(0, 256);

                try
                {
                    _ = Packet.ReceivePacket(mutated, out _);
                }
                catch (Exception ex) when (IsControlledReadFailure(ex))
                {
                }
            }
        }

        private static bool IsControlledReadFailure(Exception ex)
        {
            return ex is InvalidDataException ||
                   ex is EndOfStreamException ||
                   ex is IOException ||
                   ex is ArgumentException ||
                   ex is NotSupportedException;
        }
    }
}
