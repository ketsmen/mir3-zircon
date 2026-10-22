using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Library;
using Library.Network;
using C = Library.Network.ClientPackets;
using G = Library.Network.GeneralPackets;
using S = Library.Network.ServerPackets;
using Xunit;

namespace ProtocolTests
{
    public sealed class PacketCapabilityTests
    {
        private static T RoundTrip<T>(T packet, ServerCapabilities writeCapabilities, ServerCapabilities readCapabilities)
            where T : Packet
        {
            byte[] bytes = packet.GetPacketBytes(writeCapabilities);

            Packet received = Packet.ReceivePacket(bytes, out byte[] extra, readCapabilities);

            Assert.Empty(extra);
            return Assert.IsType<T>(received);
        }

        private static string ToHex(Packet packet, ServerCapabilities capabilities)
        {
            return Convert.ToHexString(packet.GetPacketBytes(capabilities));
        }

        private static string ToNamedHexLines(IEnumerable<KeyValuePair<string, Packet>> samples, ServerCapabilities capabilities)
        {
            return string.Join(Environment.NewLine, samples.Select(pair => $"{pair.Key}={ToHex(pair.Value, capabilities)}"));
        }

        private static Stats CreateStats(int value)
        {
            Stats stats = new();
            stats[Stat.MinDC] = value;
            return stats;
        }

        private static StartInformation CreateStartInformation(ServerCapabilities serverCapabilities = ServerCapabilities.Current)
        {
            return new StartInformation
            {
                ProtocolVersion = Globals.ProtocolVersion,
                ServerCapabilities = serverCapabilities,
                Index = 42,
                ObjectID = 77,
                Name = "Tester",
                Caption = "Caption",
                GuildName = "Guild",
                GuildRank = "Rank",
                Class = MirClass.Warrior,
                Gender = MirGender.Male,
                Location = new System.Drawing.Point(10, 20),
                Direction = MirDirection.Up,
                MapIndex = 3,
                InstanceIndex = 4,
                Level = 11,
                TimeOfDayLabel = "Day",
                Friends = new List<ClientFriendInfo>(),
                Items = new List<ClientUserItem>(),
                BeltLinks = new List<ClientBeltLink>(),
                AutoPotionLinks = new List<ClientAutoPotionLink>(),
                Milestones = new List<ClientUserMilestone>(),
                Magics = new List<ClientUserMagic>(),
                Buffs = new List<ClientBuffInfo>(),
                Currencies = new List<ClientUserCurrency>(),
                Quests = new List<ClientUserQuest>(),
                CompanionUnlocks = new List<int>(),
                Companions = new List<ClientUserCompanion>(),
                Heroes = new List<ClientHeroInformation>(),
                StorageSize = 99,
                FiltersClass = "All",
                FiltersRarity = "Any",
                FiltersItemType = "Any",
            };
        }

        [Fact]
        public void ServerItemMoveSkipsHeroIndexWithoutShiftingSuccess()
        {
            S.ItemMove packet = new()
            {
                FromGrid = GridType.HeroInventory,
                ToGrid = GridType.Inventory,
                FromSlot = 1,
                ToSlot = 2,
                MergeItem = true,
                HeroIndex = 123,
                Success = true,
            };

            S.ItemMove legacy = RoundTrip(packet, ServerCapabilities.None, ServerCapabilities.None);
            Assert.Equal(0, legacy.HeroIndex);
            Assert.True(legacy.Success);
            Assert.Equal(GridType.HeroInventory, legacy.FromGrid);
            Assert.Equal(GridType.Inventory, legacy.ToGrid);

            S.ItemMove current = RoundTrip(packet, ServerCapabilities.Current, ServerCapabilities.Current);
            Assert.Equal(123, current.HeroIndex);
            Assert.True(current.Success);
        }

        [Fact]
        public void ConnectedHandshakeAllowsStartInformationCapabilities()
        {
            G.Connected serverHello = new() { Capabilities = ServerCapabilities.Current };
            G.Connected clientServerHello = RoundTrip(serverHello, ServerCapabilities.Current, ServerCapabilities.None);
            Assert.Equal(ServerCapabilities.Current, clientServerHello.Capabilities);

            G.Connected clientHello = new() { Capabilities = ServerCapabilities.Current };
            G.Connected serverClientHello = RoundTrip(clientHello, ServerCapabilities.Current, ServerCapabilities.Current);
            Assert.Equal(ServerCapabilities.Current, serverClientHello.Capabilities);

            S.StartGame packet = new()
            {
                Result = StartGameResult.Success,
                StartInformation = CreateStartInformation(),
            };

            S.StartGame received = RoundTrip(packet, serverClientHello.Capabilities, clientServerHello.Capabilities);
            Assert.Equal(StartGameResult.Success, received.Result);
            Assert.NotNull(received.StartInformation);
            Assert.Equal(ServerCapabilities.Current, received.StartInformation.ServerCapabilities);
            Assert.Equal(42, received.StartInformation.Index);
            Assert.Equal("Tester", received.StartInformation.Name);
        }

        [Fact]
        public void LegacyHandshakeKeepsStartInformationLayout()
        {
            S.StartGame packet = new()
            {
                Result = StartGameResult.Success,
                StartInformation = CreateStartInformation(),
            };

            S.StartGame legacy = RoundTrip(packet, ServerCapabilities.None, ServerCapabilities.None);
            Assert.NotNull(legacy.StartInformation);
            Assert.Equal(ServerCapabilities.None, legacy.StartInformation.ServerCapabilities);
            Assert.Equal(42, legacy.StartInformation.Index);
            Assert.Equal("Tester", legacy.StartInformation.Name);
            Assert.Equal(3, legacy.StartInformation.MapIndex);
            Assert.Equal(99, legacy.StartInformation.StorageSize);
        }

        [Fact]
        public void StartObserverKeepsItemsAfterStartInformationWhenCapabilitiesAreMissing()
        {
            S.StartObserver packet = new()
            {
                StartInformation = CreateStartInformation(),
                Items = new List<ClientUserItem>(),
            };

            S.StartObserver legacy = RoundTrip(packet, ServerCapabilities.None, ServerCapabilities.None);
            Assert.NotNull(legacy.StartInformation);
            Assert.Equal(ServerCapabilities.None, legacy.StartInformation.ServerCapabilities);
            Assert.Equal(42, legacy.StartInformation.Index);
            Assert.Equal(99, legacy.StartInformation.StorageSize);
            Assert.NotNull(legacy.Items);
            Assert.Empty(legacy.Items);
        }

        [Fact]
        public void ServerItemMoveLegacyBytesStayFixed()
        {
            S.ItemMove packet = new()
            {
                FromGrid = GridType.HeroInventory,
                ToGrid = GridType.Inventory,
                FromSlot = 1,
                ToSlot = 2,
                MergeItem = true,
                HeroIndex = 123,
                Success = true,
            };

            Assert.Equal("18000000A4002F0000000100000001000000020000000101", ToHex(packet, ServerCapabilities.None));
        }

        [Fact]
        public void ClientItemMoveLegacyBytesStayFixed()
        {
            C.ItemMove packet = new()
            {
                FromGrid = GridType.HeroInventory,
                ToGrid = GridType.Inventory,
                FromSlot = 4,
                ToSlot = 5,
                MergeItem = true,
                HeroIndex = 321,
            };

            Assert.Equal("17000000A5002F00000001000000040000000500000001", ToHex(packet, ServerCapabilities.None));
        }

        [Fact]
        public void ServerHeroItemSyncLegacyBytesStayFixed()
        {
            List<KeyValuePair<string, Packet>> samples = new()
            {
                new("ItemSort", new S.ItemSort
                {
                    Grid = GridType.HeroInventory,
                    Items = new List<ClientUserItem>(),
                    HeroIndex = 101,
                    Success = true,
                }),
                new("ItemSplit", new S.ItemSplit
                {
                    Grid = GridType.HeroInventory,
                    Slot = 2,
                    Count = 3,
                    NewSlot = 4,
                    HeroIndex = 102,
                    Success = true,
                }),
                new("ItemLock", new S.ItemLock
                {
                    Grid = GridType.HeroEquipment,
                    Slot = 5,
                    HeroIndex = 103,
                    Locked = true,
                }),
                new("ItemChanged", new S.ItemChanged
                {
                    Link = new CellLinkInfo { GridType = GridType.HeroInventory, Slot = 6, Count = 7 },
                    HeroIndex = 104,
                    Success = true,
                }),
                new("ItemStatsChanged", new S.ItemStatsChanged
                {
                    GridType = GridType.HeroEquipment,
                    Slot = 8,
                    HeroIndex = 105,
                    NewStats = CreateStats(9),
                }),
                new("ItemStatsRefreshed", new S.ItemStatsRefreshed
                {
                    GridType = GridType.HeroEquipment,
                    Slot = 10,
                    HeroIndex = 106,
                    NewStats = CreateStats(11),
                }),
                new("ItemDurability", new S.ItemDurability
                {
                    GridType = GridType.HeroEquipment,
                    Slot = 12,
                    HeroIndex = 107,
                    CurrentDurability = 13,
                }),
                new("ItemExperience", new S.ItemExperience
                {
                    Target = new CellLinkInfo { GridType = GridType.HeroEquipment, Slot = 14, Count = 15 },
                    HeroIndex = 108,
                    Experience = 16.5M,
                    Level = 17,
                    Flags = UserItemFlags.Locked,
                }),
                new("ItemsChanged", new S.ItemsChanged
                {
                    Links = new List<CellLinkInfo>
                    {
                        new() { GridType = GridType.HeroInventory, Slot = 18, Count = 19 },
                    },
                    HeroIndex = 109,
                    Success = true,
                }),
                new("NPCRepair", new S.NPCRepair
                {
                    Links = new List<CellLinkInfo>
                    {
                        new() { GridType = GridType.HeroEquipment, Slot = 20, Count = 21 },
                    },
                    HeroIndex = 110,
                    Special = true,
                    Success = true,
                    SpecialRepairDelay = TimeSpan.FromSeconds(22),
                }),
                new("ItemAcessoryRefined", new S.ItemAcessoryRefined
                {
                    GridType = GridType.HeroEquipment,
                    Slot = 23,
                    HeroIndex = 111,
                    NewStats = CreateStats(24),
                }),
            };

            string expected = string.Join(Environment.NewLine,
                "ItemSort=10000000A6002F000000010000000001",
                "ItemSplit=1B000000A9002F0000000200000003000000000000000400000001",
                "ItemLock=0F000000A200300000000500000001",
                "ItemChanged=180000009C00012F00000006000000070000000000000001",
                "ItemStatsChanged=1C000000AA0030000000080000000101010000000800000009000000",
                "ItemStatsRefreshed=1C000000AB00300000000A000000010101000000080000000B000000",
                "ItemDurability=12000000A000300000000C0000000D000000",
                "ItemExperience=2F000000A10001300000000E0000000F00000000000000A50000000000000000000000000001001100000001000000",
                "ItemsChanged=1D000000AE000101000000012F00000012000000130000000000000001",
                "NPCRepair=26000000070101010000000130000000140000001500000000000000010100EF1C0D00000000",
                "ItemAcessoryRefined=1C0000009B0030000000170000000101010000000800000018000000");

            Assert.Equal(expected, ToNamedHexLines(samples, ServerCapabilities.None));
        }

        [Fact]
        public void ServerItemStatsChangedSkipsHeroIndexWithoutShiftingStats()
        {
            Stats stats = CreateStats(7);

            S.ItemStatsChanged packet = new()
            {
                GridType = GridType.HeroEquipment,
                Slot = 3,
                HeroIndex = 456,
                NewStats = stats,
            };

            S.ItemStatsChanged legacy = RoundTrip(packet, ServerCapabilities.None, ServerCapabilities.None);
            Assert.Equal(0, legacy.HeroIndex);
            Assert.NotNull(legacy.NewStats);
            Assert.Equal(7, legacy.NewStats[Stat.MinDC]);

            S.ItemStatsChanged current = RoundTrip(packet, ServerCapabilities.Current, ServerCapabilities.Current);
            Assert.Equal(456, current.HeroIndex);
            Assert.Equal(7, current.NewStats[Stat.MinDC]);
        }

        [Fact]
        public void HeroInformationInspectFallsBackToCurrentHeroShape()
        {
            S.HeroInformation packet = new()
            {
                Hero = new ClientUserHero
                {
                    Index = 789,
                    Name = "Hero",
                    Class = MirClass.Warrior,
                    Gender = MirGender.Male,
                    Level = 12,
                    Stats = new Stats(),
                    Items = new List<ClientUserItem>(),
                    Magics = new List<ClientUserMagic>(),
                },
                Inspect = true,
            };

            S.HeroInformation legacy = RoundTrip(packet, ServerCapabilities.None, ServerCapabilities.None);
            Assert.NotNull(legacy.Hero);
            Assert.Equal(789, legacy.Hero.Index);
            Assert.False(legacy.Inspect);

            S.HeroInformation current = RoundTrip(packet, ServerCapabilities.Current, ServerCapabilities.Current);
            Assert.NotNull(current.Hero);
            Assert.Equal(789, current.Hero.Index);
            Assert.True(current.Inspect);
        }

        [Fact]
        public void HeroGradePromotePreviewFieldsAreCapabilityGated()
        {
            DateTime eventEnd = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
            S.HeroInformation packet = new()
            {
                Hero = new ClientUserHero
                {
                    Index = 789,
                    Name = "Hero",
                    Class = MirClass.Warrior,
                    Gender = MirGender.Male,
                    Level = 12,
                    Grade = 1,
                    GradeStatBonusRate = 5,
                    HeroGradePromoteFailureCount = 2,
                    HeroGradePromoteEventCostRate = 80,
                    HeroGradePromoteEventSuccessBonus = 10,
                    HeroGradePromoteEventEnd = eventEnd,
                    Experience = 123.5M,
                    MaxExperience = 999M,
                    CurrentHP = 77,
                    CurrentMP = 66,
                    Stats = new Stats(),
                    Items = new List<ClientUserItem>(),
                    Magics = new List<ClientUserMagic>(),
                },
            };

            S.HeroInformation legacy = RoundTrip(packet, ServerCapabilities.None, ServerCapabilities.None);
            Assert.NotNull(legacy.Hero);
            Assert.Equal(1, legacy.Hero.Grade);
            Assert.Equal(0, legacy.Hero.GradeStatBonusRate);
            Assert.Equal(0, legacy.Hero.HeroGradePromoteFailureCount);
            Assert.Equal(100, legacy.Hero.HeroGradePromoteEventCostRate);
            Assert.Equal(0, legacy.Hero.HeroGradePromoteEventSuccessBonus);
            Assert.Equal(DateTime.MinValue, legacy.Hero.HeroGradePromoteEventEnd);
            Assert.Equal(123.5M, legacy.Hero.Experience);
            Assert.Equal(999M, legacy.Hero.MaxExperience);
            Assert.Equal(77, legacy.Hero.CurrentHP);
            Assert.Equal(66, legacy.Hero.CurrentMP);

            S.HeroInformation current = RoundTrip(packet, ServerCapabilities.Current, ServerCapabilities.Current);
            Assert.NotNull(current.Hero);
            Assert.Equal(5, current.Hero.GradeStatBonusRate);
            Assert.Equal(2, current.Hero.HeroGradePromoteFailureCount);
            Assert.Equal(80, current.Hero.HeroGradePromoteEventCostRate);
            Assert.Equal(10, current.Hero.HeroGradePromoteEventSuccessBonus);
            Assert.Equal(eventEnd, current.Hero.HeroGradePromoteEventEnd);
            Assert.Equal(123.5M, current.Hero.Experience);
        }

        [Fact]
        public void HeroGradePromoteRequestHeroIndexIsCapabilityGatedTailField()
        {
            C.ZZHeroGradePromoteRequest packet = new()
            {
                RequestConfirmation = true,
                Confirm = true,
                HeroIndex = 321,
            };

            C.ZZHeroGradePromoteRequest legacy = RoundTrip(packet, ServerCapabilities.None, ServerCapabilities.Current);
            Assert.True(legacy.RequestConfirmation);
            Assert.True(legacy.Confirm);
            Assert.Equal(0, legacy.HeroIndex);

            C.ZZHeroGradePromoteRequest current = RoundTrip(packet, ServerCapabilities.Current, ServerCapabilities.Current);
            Assert.True(current.RequestConfirmation);
            Assert.True(current.Confirm);
            Assert.Equal(321, current.HeroIndex);
        }

        [Fact]
        public void HeroGradePromotePreviewHeroIndexIsCapabilityGatedTailField()
        {
            S.ZZHeroGradePromotePreview packet = new()
            {
                Preview = new ClientHeroGradePromotePreview
                {
                    Enabled = true,
                    CanPromote = true,
                    HeroName = "Hero",
                    CurrentGrade = 1,
                    NextGrade = 2,
                    GrowthText = "growth",
                },
                ConfirmRequested = true,
                HeroIndex = 654,
            };

            S.ZZHeroGradePromotePreview legacy = RoundTrip(packet, ServerCapabilities.None, ServerCapabilities.Current);
            Assert.NotNull(legacy.Preview);
            Assert.Equal("Hero", legacy.Preview.HeroName);
            Assert.True(legacy.ConfirmRequested);
            Assert.Equal(0, legacy.HeroIndex);

            S.ZZHeroGradePromotePreview current = RoundTrip(packet, ServerCapabilities.Current, ServerCapabilities.Current);
            Assert.NotNull(current.Preview);
            Assert.Equal("Hero", current.Preview.HeroName);
            Assert.True(current.ConfirmRequested);
            Assert.Equal(654, current.HeroIndex);
        }

        [Fact]
        public void ClientItemMoveAllowsLegacyServerToReadMissingHeroIndex()
        {
            C.ItemMove packet = new()
            {
                FromGrid = GridType.HeroInventory,
                ToGrid = GridType.Inventory,
                FromSlot = 4,
                ToSlot = 5,
                MergeItem = true,
                HeroIndex = 321,
            };

            C.ItemMove legacy = RoundTrip(packet, ServerCapabilities.None, ServerCapabilities.Current);
            Assert.Equal(0, legacy.HeroIndex);
            Assert.True(legacy.MergeItem);

            C.ItemMove current = RoundTrip(packet, ServerCapabilities.Current, ServerCapabilities.Current);
            Assert.Equal(321, current.HeroIndex);
            Assert.True(current.MergeItem);
        }

        [Fact]
        public void ClientNpcRepairAllowsLegacyServerToReadMissingHeroIndex()
        {
            C.NPCRepair packet = new()
            {
                Links = new List<CellLinkInfo>
                {
                    new() { GridType = GridType.HeroEquipment, Slot = 6, Count = 1 },
                },
                Special = true,
                GuildFunds = true,
                HeroIndex = 654,
            };

            C.NPCRepair legacy = RoundTrip(packet, ServerCapabilities.None, ServerCapabilities.Current);
            Assert.Equal(0, legacy.HeroIndex);
            Assert.True(legacy.Special);
            Assert.True(legacy.GuildFunds);
            Assert.Single(legacy.Links);

            C.NPCRepair current = RoundTrip(packet, ServerCapabilities.Current, ServerCapabilities.Current);
            Assert.Equal(654, current.HeroIndex);
            Assert.True(current.Special);
            Assert.True(current.GuildFunds);
        }

        [Fact]
        public void HeroIndexPacketPropertiesDeclareCapabilityMetadata()
        {
            Assembly assembly = typeof(Packet).Assembly;
            Type[] packetRoots =
            {
                typeof(C.ItemMove),
                typeof(S.ItemMove),
            };

            string[] packetNamespaces = packetRoots.Select(x => x.Namespace!).Distinct().ToArray();

            List<string> missing = assembly.GetTypes()
                .Where(type => type.BaseType == typeof(Packet))
                .Where(type => packetNamespaces.Contains(type.Namespace))
                .SelectMany(type => type.GetProperties().Where(property => property.Name == "HeroIndex"))
                .Where(property => property.CustomAttributes.All(attribute => attribute.AttributeType.Name != "PacketCapability"))
                .Select(property => $"{property.DeclaringType?.FullName}.{property.Name}")
                .OrderBy(value => value)
                .ToList();

            Assert.Empty(missing);
        }

        [Fact]
        public void ReceivePacketKeepsIncompletePacketBytes()
        {
            byte[] packet = new G.Connected { Capabilities = ServerCapabilities.Current }.GetPacketBytes();
            byte[] partial = packet.Take(packet.Length - 1).ToArray();

            Packet received = Packet.ReceivePacket(partial, out byte[] extra);

            Assert.Null(received);
            Assert.Equal(partial, extra);
        }

        [Fact]
        public void ReceivePacketIgnoresTooShortPacketLength()
        {
            byte[] packet = { 4, 0, 0, 0 };

            Packet received = Packet.ReceivePacket(packet, out byte[] extra);

            Assert.Null(received);
            Assert.Empty(extra);
        }

        [Fact]
        public void ReceivePacketIgnoresUnknownPacketId()
        {
            byte[] packet = { 6, 0, 0, 0, 255, 127 };

            Packet received = Packet.ReceivePacket(packet, out byte[] extra);

            Assert.Null(received);
            Assert.Empty(extra);
        }

        [Fact]
        public void ReceivePacketRejectsOversizedPacketLength()
        {
            byte[] packet =
            {
                (byte)((Packet.MaxPacketSize + 1) & 0xFF),
                (byte)(((Packet.MaxPacketSize + 1) >> 8) & 0xFF),
                (byte)(((Packet.MaxPacketSize + 1) >> 16) & 0xFF),
                (byte)(((Packet.MaxPacketSize + 1) >> 24) & 0xFF),
            };

            Assert.Throws<System.IO.InvalidDataException>(() => Packet.ReceivePacket(packet, out _));
        }

        [Fact]
        public void PacketFrameReaderKeepsPartialFramesUntilComplete()
        {
            byte[] packet = new G.Connected { Capabilities = ServerCapabilities.Current }.GetPacketBytes();
            PacketFrameReader reader = new();

            reader.Append(packet, 0, packet.Length - 1);
            Assert.False(reader.TryReadPacket(ServerCapabilities.Current, out Packet partial));
            Assert.Null(partial);
            Assert.Equal(packet.Length - 1, reader.BufferedBytes);

            reader.Append(packet, packet.Length - 1, 1);
            Assert.True(reader.TryReadPacket(ServerCapabilities.Current, out Packet complete));
            G.Connected connected = Assert.IsType<G.Connected>(complete);
            Assert.Equal(ServerCapabilities.Current, connected.Capabilities);
            Assert.Equal(0, reader.BufferedBytes);
        }

        [Fact]
        public void PacketFrameReaderReadsMultipleFrames()
        {
            byte[] first = new G.Connected { Capabilities = ServerCapabilities.Current }.GetPacketBytes();
            byte[] second = new G.Disconnect { Reason = DisconnectReason.ServerClosing }.GetPacketBytes();
            byte[] combined = first.Concat(second).ToArray();
            PacketFrameReader reader = new();

            reader.Append(combined, 0, combined.Length);

            Assert.True(reader.TryReadPacket(ServerCapabilities.Current, out Packet firstPacket));
            Assert.IsType<G.Connected>(firstPacket);

            Assert.True(reader.TryReadPacket(ServerCapabilities.Current, out Packet secondPacket));
            G.Disconnect disconnect = Assert.IsType<G.Disconnect>(secondPacket);
            Assert.Equal(DisconnectReason.ServerClosing, disconnect.Reason);

            Assert.False(reader.TryReadPacket(ServerCapabilities.Current, out Packet empty));
            Assert.Null(empty);
        }
    }
}
