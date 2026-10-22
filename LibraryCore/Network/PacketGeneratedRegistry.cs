using System;
using System.Collections.Generic;

namespace Library.Network
{
    internal static partial class PacketGeneratedRegistry
    {
        public static Type[]? GetPacketTypes()
        {
            List<Type> types = new List<Type>();
            AddGeneratedPacketTypes(types);
            return types.Count == 0 ? null : types.ToArray();
        }

        static partial void AddGeneratedPacketTypes(List<Type> types);
    }
}
