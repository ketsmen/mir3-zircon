using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using Library;

namespace Library.Network
{
    public abstract class Packet
    {
        public const int MaxPacketSize = 4 * 1024 * 1024;
        private static readonly List<Type> Packets;
        private static readonly Dictionary<Type, short> PacketIds;
        private static readonly ConcurrentDictionary<Type, ObjectMetadata> MetadataCache;
        private static readonly Dictionary<Type, Action<object, BinaryWriter>> TypeWrite;
        private static readonly Dictionary<Type, Func<BinaryReader, object>> TypeRead;
        private const string GeneralPacketsNamespace = @"Library.Network.GeneralPackets";
        private const string ClientPacketsNamespace = @"Library.Network.ClientPackets";
        private const string ServerPacketsNamespace = @"Library.Network.ServerPackets";
        private static readonly HashSet<string> ClientFirstSameNamePackets = new HashSet<string>(StringComparer.Ordinal)
        {
            "ItemSplit",
        };

        public static bool IsClient { get; set; }

        public Type PacketType;

        public int Length;
        public bool ObserverPacket = true;

        static Packet()
        {
            Packets = new List<Type>();
            PacketIds = new Dictionary<Type, short>();
            MetadataCache = new ConcurrentDictionary<Type, ObjectMetadata>();

            Type[] list = PacketGeneratedRegistry.GetPacketTypes() ?? Assembly.GetExecutingAssembly().GetTypes();

            foreach (Type type in list)
            {
                if (type.BaseType != typeof(Packet)) continue;
                Packets.Add(type);
            }

            Packets.Sort(ComparePacketTypes);
            for (short i = 0; i < Packets.Count; i++)
                PacketIds[Packets[i]] = i;

            #region Writes

            TypeWrite = new Dictionary<Type, Action<object, BinaryWriter>>
            {
                [typeof(Boolean)] = (v, w) => w.Write((bool)v),
                [typeof(Byte)] = (v, w) => w.Write((Byte)v),
                [typeof(Byte[])] = (v, w) =>
                {
                    w.Write(v != null ? ((Byte[])v).Length : 0);
                    w.Write(v != null ? (Byte[])v : new byte[0]);
                },
                [typeof(Char)] = (v, w) => w.Write((Char)v),
                [typeof(Color)] = (v, w) => w.Write(((Color)v).ToArgb()),
                [typeof(DateTime)] = (v, w) => w.Write(((DateTime)v).ToBinary()),
                [typeof(Decimal)] = (v, w) => w.Write((Decimal)v),
                [typeof(Double)] = (v, w) => w.Write((Double)v),
                [typeof(Int16)] = (v, w) => w.Write((Int16)v),
                [typeof(Int32)] = (v, w) => w.Write((Int32)v),
                [typeof(Int64)] = (v, w) => w.Write((Int64)v),
                [typeof(Point)] = (v, w) =>
                {
                    w.Write(((Point)v).X);
                    w.Write(((Point)v).Y);
                },
                [typeof(SByte)] = (v, w) => w.Write((SByte)v),
                [typeof(Single)] = (v, w) => w.Write((Single)v),
                [typeof(Size)] = (v, w) =>
                {
                    w.Write(((Size)v).Width);
                    w.Write(((Size)v).Height);
                },
                [typeof(String)] = (v, w) => w.Write((String)v ?? string.Empty),
                [typeof(TimeSpan)] = (v, w) => w.Write(((TimeSpan)v).Ticks),
                [typeof(UInt16)] = (v, w) => w.Write((UInt16)v),
                [typeof(UInt32)] = (v, w) => w.Write((UInt32)v),
                [typeof(UInt64)] = (v, w) => w.Write((UInt64)v)
            };

            #endregion

            #region Reads

            TypeRead = new Dictionary<Type, Func<BinaryReader, object>>
            {
                [typeof(Boolean)] = (r) => r.ReadBoolean(),
                [typeof(Byte)] = (r) => r.ReadByte(),
                [typeof(Byte[])] = (r) => r.ReadBytes(r.ReadInt32()),
                [typeof(Char)] = (r) => r.ReadChar(),
                [typeof(Color)] = (r) => Color.FromArgb(r.ReadInt32()),
                [typeof(DateTime)] = (r) => DateTime.FromBinary(r.ReadInt64()),
                [typeof(Decimal)] = (r) => r.ReadDecimal(),
                [typeof(Double)] = (r) => r.ReadDouble(),
                [typeof(Enum)] = (r) => r.ReadInt32(),
                [typeof(Int16)] = (r) => r.ReadInt16(),
                [typeof(Int32)] = (r) => r.ReadInt32(),
                [typeof(Int64)] = (r) => r.ReadInt64(),
                [typeof(Point)] = (r) => new Point(r.ReadInt32(), r.ReadInt32()),
                [typeof(SByte)] = (r) => r.ReadSByte(),
                [typeof(Single)] = (r) => r.ReadSingle(),
                [typeof(Size)] = (r) => new Size(r.ReadInt32(), r.ReadInt32()),
                [typeof(String)] = (r) => r.ReadString(),
                [typeof(TimeSpan)] = (r) => TimeSpan.FromTicks(r.ReadInt64()),
                [typeof(UInt16)] = (r) => r.ReadUInt16(),
                [typeof(UInt32)] = (r) => r.ReadUInt32(),
                [typeof(UInt64)] = (r) => r.ReadUInt64()
            };

            #endregion

        }

        private static int ComparePacketTypes(Type x1, Type x2)
        {
            bool x1General = string.Compare(x1.Namespace, GeneralPacketsNamespace, StringComparison.Ordinal) == 0;
            bool x2General = string.Compare(x2.Namespace, GeneralPacketsNamespace, StringComparison.Ordinal) == 0;

            if (x1General || x2General)
            {
                if (x1General && x2General)
                    return String.Compare(x1.Name, x2.Name, StringComparison.Ordinal);

                return x1General ? -1 : 1; //We want General Packets shifted To the top.
            }

            int nameCompare = String.Compare(x1.Name, x2.Name, StringComparison.Ordinal);
            if (nameCompare != 0) return nameCompare;

            int namespaceCompare = String.Compare(x1.Namespace, x2.Namespace, StringComparison.Ordinal);
            if (namespaceCompare == 0) return 0;

            // Same-name client/server packets used to compare as equal; keep known legacy ties stable.
            if (ClientFirstSameNamePackets.Contains(x1.Name))
            {
                if (string.Compare(x1.Namespace, ClientPacketsNamespace, StringComparison.Ordinal) == 0)
                    return -1;

                if (string.Compare(x2.Namespace, ClientPacketsNamespace, StringComparison.Ordinal) == 0)
                    return 1;
            }

            if (string.Compare(x1.Namespace, ServerPacketsNamespace, StringComparison.Ordinal) == 0)
                return -1;

            if (string.Compare(x2.Namespace, ServerPacketsNamespace, StringComparison.Ordinal) == 0)
                return 1;

            return namespaceCompare;
        }

        public static Packet ReceivePacket(byte[] rawBytes, out byte[] extra, ServerCapabilities capabilities = ServerCapabilities.Current)
        {
            extra = rawBytes;

            Packet p = null;

            if (rawBytes.Length < 4) return null; //4Bytes: Packet Size |

            int length = rawBytes[3] << 24 | rawBytes[2] << 16 | rawBytes[1] << 8 | rawBytes[0];

            if (length > MaxPacketSize)
                throw new InvalidDataException($"Packet length {length} exceeds the maximum allowed size {MaxPacketSize}.");

            if (length < 6)
            {
                extra = rawBytes.Length > length && length > 0 ? rawBytes[length..] : Array.Empty<byte>();
                return null;
            }

            if (length > rawBytes.Length) return null;

            int extraLength = rawBytes.Length - length;
            extra = extraLength == 0 ? Array.Empty<byte>() : new byte[extraLength];
            if (extraLength > 0)
                Buffer.BlockCopy(rawBytes, length, extra, 0, extraLength);


            using (MemoryStream stream = new MemoryStream(rawBytes))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                stream.Seek(4, SeekOrigin.Begin);

                short id = reader.ReadInt16();
                if (id >= 0 && id < Packets.Count)
                {
                    p = (Packet)Activator.CreateInstance(Packets[id]);
                    p.PacketType = Packets[id];
                    ReadObject(reader, p, capabilities);
                }
            }

            if (p == null) return null;

            p.Length = length;

            return p;
        }
        public byte[] GetPacketBytes(ServerCapabilities capabilities = ServerCapabilities.Current)
        {
            byte[] packet;

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(PacketIds[GetType()]);
                WriteObject(writer, this, capabilities);
                packet = stream.ToArray();
            }

            Length = packet.Length;

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(packet.Length + 4); //| 4Bytes: Packet Size | Data... |
                writer.Write(packet);

                return stream.ToArray();
            }
        }

        private static ObjectMetadata GetMetadata(Type type)
        {
            return MetadataCache.GetOrAdd(type, static value => new ObjectMetadata(value));
        }

        private static bool HasCapability(PacketProperty property, ServerCapabilities capabilities)
        {
            return property.Capability?.IsSupported(capabilities) ?? true;
        }

        private static bool HasOptionalData(BinaryReader reader, PacketProperty property)
        {
            if (!property.Optional) return true;

            // Optional properties are only safe for tail fields because the reader cannot
            // distinguish "missing" from "next field starts here" in the middle of a packet.
            return reader.BaseStream.Position < reader.BaseStream.Length;
        }

        private static void WriteObject(BinaryWriter writer, object ob, ServerCapabilities capabilities)
        {
            PacketProperty[] properties = GetMetadata(ob.GetType()).Properties;

            foreach (PacketProperty item in properties)
            {
                if (!HasCapability(item, capabilities)) continue;

                Action<object, BinaryWriter> writeAction;
                if (!TypeWrite.TryGetValue(item.PropertyType, out writeAction))
                {

                    if (item.PropertyType.IsClass)
                    {
                        object value = item.GetValue(ob);
                        writer.Write(value != null);
                        if (value == null) continue;
                    }

                    if (item.PropertyType.IsEnum)
                        TypeWrite[item.PropertyType.GetEnumUnderlyingType()](item.GetValue(ob), writer);
                    else if (item.PropertyType.IsGenericType)
                    {
                        if (item.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            IList list = (IList)item.GetValue(ob);

                            writer.Write(list.Count);
                            Type genType = item.PropertyType.GetGenericArguments()[0];

                            if (!TypeWrite.TryGetValue(genType, out writeAction))
                            {
                                if (genType.IsEnum)
                                {
                                    genType = genType.GetEnumUnderlyingType();
                                    foreach (object x in list)
                                        TypeWrite[genType](x, writer);
                                }
                                else
                                {
                                    foreach (object x in list)
                                    {
                                        writer.Write(x != null);

                                        if (x == null) continue;

                                        WriteObject(writer, x, capabilities);
                                    }
                                }
                            }
                            else
                            {
                                foreach (object x in list)
                                    writeAction(x, writer);
                            }
                        }
                        else if (item.PropertyType.GetGenericTypeDefinition() == typeof(SortedDictionary<,>) || item.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        {
                            IDictionary dictionary = (IDictionary)item.GetValue(ob);

                            writer.Write(dictionary.Count);

                            Type genKey = item.PropertyType.GetGenericArguments()[0];
                            Type genValue = item.PropertyType.GetGenericArguments()[1];

                            Action<object, BinaryWriter> keyAction = null;
                            Action<object, BinaryWriter> valueAction = null;


                            if (!TypeWrite.TryGetValue(genKey, out keyAction))
                            {
                                if (genKey.IsEnum)
                                    keyAction = TypeWrite[genKey.GetEnumUnderlyingType()];
                            }

                            if (!TypeWrite.TryGetValue(genValue, out valueAction))
                            {
                                if (genValue.IsEnum)
                                    valueAction = TypeWrite[genValue.GetEnumUnderlyingType()];
                            }

                            foreach (object key in dictionary.Keys)
                            {
                                if (keyAction == null)
                                    WriteObject(writer, key, capabilities); //Not allowed to have Null Key so no point checking.
                                else
                                    keyAction(key, writer);

                                if (valueAction == null)
                                {
                                    writer.Write(dictionary[key] != null);
                                    if (dictionary[key] == null) continue;

                                    WriteObject(writer, dictionary[key], capabilities);
                                }
                                else
                                    valueAction(dictionary[key], writer);
                            }
                        }
                    }
                    else if (item.PropertyType.IsClass)
                        WriteObject(writer, item.GetValue(ob), capabilities);
                    else
                        throw new NotImplementedException($"Not Implemented Exception: WirteObject: Type:{item.PropertyType}.");

                    continue;
                }

                writeAction(item.GetValue(ob), writer);
            }
        }
        private static void ReadObject(BinaryReader reader, object ob, ServerCapabilities capabilities)
        {
            ObjectMetadata metadata = GetMetadata(ob.GetType());

            foreach (PacketProperty item in metadata.Properties)
            {
                if (!HasCapability(item, capabilities)) continue;
                if (!HasOptionalData(reader, item)) continue;

                Func<BinaryReader, object> readAction;
                if (!TypeRead.TryGetValue(item.PropertyType, out readAction))
                {
                    if (item.PropertyType.IsClass)
                    {
                        if (!reader.ReadBoolean()) continue;
                        item.SetValue(ob, Activator.CreateInstance(item.PropertyType));
                    }

                    if (item.PropertyType.IsEnum)
                        item.SetValue(ob, TypeRead[item.PropertyType.GetEnumUnderlyingType()](reader));
                    else if (item.PropertyType.IsGenericType)
                    {
                        if (item.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            IList list = (IList)item.GetValue(ob);

                            int count = reader.ReadInt32();
                            Type genType = item.PropertyType.GetGenericArguments()[0];
                            if (!TypeRead.TryGetValue(genType, out readAction))
                            {
                                if (genType.IsEnum)
                                {
                                    genType = genType.GetEnumUnderlyingType();

                                    for (int i = 0; i < count; i++)
                                        list.Add(TypeRead[genType](reader));
                                }
                                else
                                {
                                    for (int i = 0; i < count; i++)
                                    {
                                        if (!reader.ReadBoolean()) continue;

                                        object value = Activator.CreateInstance(genType);
                                        list.Add(value);
                                        ReadObject(reader, value, capabilities);
                                    }
                                }
                            }
                            else
                            {
                                for (int i = 0; i < count; i++)
                                    list.Add(readAction(reader));
                            }
                        }
                        else if (item.PropertyType.GetGenericTypeDefinition() == typeof(SortedDictionary<,>) || item.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        {
                            IDictionary dictionary = (IDictionary)item.GetValue(ob);

                            int count = reader.ReadInt32();

                            Type genKey = item.PropertyType.GetGenericArguments()[0];
                            Type genValue = item.PropertyType.GetGenericArguments()[1];

                            Func<BinaryReader, object> keyAction = null;
                            Func<BinaryReader, object> valueAction = null;

                            if (!TypeRead.TryGetValue(genKey, out keyAction))
                            {
                                if (genKey.IsEnum)
                                    keyAction = TypeRead[genKey.GetEnumUnderlyingType()];
                            }

                            if (!TypeRead.TryGetValue(genValue, out valueAction))
                            {
                                if (genValue.IsEnum)
                                    valueAction = TypeRead[genValue.GetEnumUnderlyingType()];
                            }

                            for (int i = 0; i < count; i++)
                            {
                                object key;
                                object value;

                                if (keyAction == null)
                                {
                                    key = Activator.CreateInstance(genKey); //Never Null as it's a key

                                    ReadObject(reader, key, capabilities);
                                }
                                else
                                    key = keyAction(reader);

                                if (valueAction == null)
                                {
                                    if (!reader.ReadBoolean())
                                    {
                                        value = null;
                                    }
                                    else
                                    {
                                        value = Activator.CreateInstance(genValue);

                                        ReadObject(reader, value, capabilities);
                                    }
                                }
                                else
                                    value = valueAction(reader);

                                dictionary[key] = value;
                            }
                        }
                    }
                    else if (item.PropertyType.IsClass)
                        ReadObject(reader, item.GetValue(ob), capabilities);
                    else
                        throw new NotImplementedException($"Not Implemented Exception: ReadObject: Type: {item.PropertyType}.");

                    continue;
                }

                item.SetValue(ob, readAction(reader));
            }

            foreach (MethodInfo item in metadata.CompleteMethods)
            {
                item.Invoke(ob, null);
            }
        }

        private sealed class ObjectMetadata
        {
            public PacketProperty[] Properties { get; }
            public MethodInfo[] CompleteMethods { get; }

            public ObjectMetadata(Type type)
            {
                PropertyInfo[] properties = type.GetProperties();
                List<PacketProperty> packetProperties = new List<PacketProperty>(properties.Length);

                foreach (PropertyInfo property in properties)
                {
                    if (property.GetCustomAttribute<IgnorePropertyPacket>() != null) continue;

                    packetProperties.Add(new PacketProperty(property));
                }

                Properties = packetProperties.ToArray();

                MethodInfo[] methods = type.GetMethods();
                List<MethodInfo> completeMethods = new List<MethodInfo>();

                foreach (MethodInfo method in methods)
                {
                    if (method.GetCustomAttribute<CompleteObject>() == null) continue;

                    completeMethods.Add(method);
                }

                CompleteMethods = completeMethods.ToArray();
            }
        }

        private sealed class PacketProperty
        {
            public PropertyInfo Property { get; }
            public Type PropertyType { get; }
            public PacketCapability Capability { get; }
            public bool Optional { get; }

            public PacketProperty(PropertyInfo property)
            {
                Property = property;
                PropertyType = property.PropertyType;
                Capability = property.GetCustomAttribute<PacketCapability>();
                Optional = property.GetCustomAttribute<OptionalPacketProperty>() != null;
            }

            public object GetValue(object ob)
            {
                return Property.GetValue(ob);
            }

            public void SetValue(object ob, object value)
            {
                Property.SetValue(ob, value);
            }
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    internal class IgnorePropertyPacket : Attribute
    {

    }

    [AttributeUsage(AttributeTargets.Property)]
    internal class OptionalPacketProperty : Attribute
    {

    }

    [AttributeUsage(AttributeTargets.Property)]
    internal class PacketCapability : Attribute
    {
        public ServerCapabilities Capabilities { get; }
        public bool RequireAll { get; set; } = true;

        // Use this for fields that would shift later packet data when an older peer
        // does not know about the field.
        public PacketCapability(ServerCapabilities capabilities)
        {
            Capabilities = capabilities;
        }

        public bool IsSupported(ServerCapabilities capabilities)
        {
            if (Capabilities == ServerCapabilities.None) return true;

            return RequireAll
                ? (capabilities & Capabilities) == Capabilities
                : (capabilities & Capabilities) != ServerCapabilities.None;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal class CompleteObject : Attribute
    {

    }
}
