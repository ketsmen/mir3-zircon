using System;
using System.Collections.Generic;
using System.Diagnostics;
using Library;
using Library.Network;
using S = Library.Network.ServerPackets;

int iterations = 20000;
for (int i = 0; i < args.Length - 1; i++)
{
    if (string.Equals(args[i], "--iterations", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(args[i + 1], out int parsed) &&
        parsed > 0)
    {
        iterations = parsed;
    }
}

S.ItemMove itemMove = new()
{
    FromGrid = GridType.HeroInventory,
    ToGrid = GridType.Inventory,
    FromSlot = 1,
    ToSlot = 2,
    MergeItem = true,
    HeroIndex = 123,
    Success = true,
};

S.StartGame startGame = new()
{
    Result = StartGameResult.Success,
    StartInformation = new StartInformation
    {
        ProtocolVersion = Globals.ProtocolVersion,
        ServerCapabilities = ServerCapabilities.Current,
        Index = 1,
        ObjectID = 2,
        Name = "Bench",
        Caption = "Bench",
        GuildName = string.Empty,
        GuildRank = string.Empty,
        Class = MirClass.Warrior,
        Gender = MirGender.Male,
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
        FiltersClass = "All",
        FiltersRarity = "Any",
        FiltersItemType = "Any",
    },
};

Run("ItemMove", itemMove, iterations);
Run("StartGame", startGame, iterations);

static void Run(string name, Packet packet, int iterations)
{
    byte[] bytes = packet.GetPacketBytes(ServerCapabilities.Current);
    PacketFrameReader reader = new();

    Stopwatch write = Stopwatch.StartNew();
    for (int i = 0; i < iterations; i++)
        packet.GetPacketBytes(ServerCapabilities.Current);
    write.Stop();

    Stopwatch read = Stopwatch.StartNew();
    for (int i = 0; i < iterations; i++)
    {
        reader.Append(bytes, 0, bytes.Length);
        if (!reader.TryReadPacket(ServerCapabilities.Current, out Packet received) || received == null)
            throw new InvalidOperationException("Packet frame reader failed during benchmark.");
    }
    read.Stop();

    Console.WriteLine($"{name}: size={bytes.Length} bytes, iterations={iterations}, write={write.ElapsedMilliseconds}ms, read={read.ElapsedMilliseconds}ms");
}
