using MirDB;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server
{
    public class DBObjectArrayConverter<T> : DBObjectBaseConverter<T[]> where T : DBObject, new()
    {
        public DBObjectArrayConverter(Session session) : base(session) { }

        public override T[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            List<T> collection = new();

            var converterOptions = CreateDBObjectOptions(typeof(T), null, null);

            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException($"Expected StartArray token for '{typeof(T).Name}[]'.");

            bool endArray = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    endArray = true;
                    break;
                }

                if (reader.TokenType != JsonTokenType.StartObject)
                    throw new JsonException($"Expected StartObject token for '{typeof(T).Name}' array item.");

                T obj = JsonSerializer.Deserialize<T>(ref reader, converterOptions);

                collection.Add(obj);
            }

            if (!endArray)
                throw new JsonException($"Unexpected end of JSON while reading '{typeof(T).Name}[]'.");

            return collection.ToArray();
        }

        public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options)
        {
            var converterOptions = CreateDBObjectOptions(typeof(T), null, null);

            writer.WriteStartArray();

            foreach (var obj in value)
            {
                JsonSerializer.Serialize<T>(writer, obj, converterOptions);
            }

            writer.WriteEndArray();
        }
    }

    public class DBObjectConverter<T> : DBObjectBaseConverter<T> where T : DBObject, new()
    {
        private readonly DBBindingList<T> _bindingList;
        private readonly PropertyInfo _parentProperty;

        public DBObjectConverter(Session session, PropertyInfo parentProperty = null, DBBindingList<T> bindingList = null) : base(session)
        {
            Session = session;
            _bindingList = bindingList;
            _parentProperty = parentProperty;
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Expected StartObject token for '{typeof(T).Name}'.");

            T obj = FindObjectOrCreate(ref reader);

            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            bool endObject = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    endObject = true;
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName token.");
                }

                string propertyName = reader.GetString();

                var property = Array.Find(properties, p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

                if (property == null)
                {
                    reader.Skip();
                    continue;
                }

                // read value
                reader.Read();
                if (!CanImportProperty(property))
                {
                    reader.Skip();
                    continue;
                }

                ReadPropertyValue(ref reader, obj, property);
            }

            if (!endObject)
                throw new JsonException($"Unexpected end of JSON while reading '{typeof(T).Name}'.");

            return obj;
        }

        private void ReadPropertyValue(ref Utf8JsonReader reader, T obj, PropertyInfo property)
        {
            if (IsDBBindingListProperty(property))
            {
                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    throw new JsonException($"Expected StartArray token for DBBindingList at '{property.Name}'.");
                }

                Type bindingListType = property.PropertyType.GetGenericArguments().First();

                List<object> collection = new();

                var existingCollection = property.GetValue(obj) as IList;

                var converterOptions = CreateDBObjectOptions(bindingListType, property, existingCollection);

                bool endArray = false;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        endArray = true;
                        break;
                    }

                    if (reader.TokenType != JsonTokenType.StartObject)
                        throw new JsonException($"Expected StartObject token for DBBindingList item at '{property.Name}'.");

                    var childObject = JsonSerializer.Deserialize(ref reader, bindingListType, converterOptions);

                    SetAssociationProperty(property, obj, childObject);

                    collection.Add(childObject);
                }

                if (!endArray)
                    throw new JsonException($"Unexpected end of JSON while reading DBBindingList at '{property.Name}'.");

                if (existingCollection != null)
                {
                    RemoveMissingObjects(existingCollection, collection);

                    foreach (var childObject in collection)
                    {
                        if (!existingCollection.Contains(childObject))
                        {
                            existingCollection.Add(childObject);
                        }
                    }
                }
            }
            else if (typeof(DBObject).IsAssignableFrom(property.PropertyType))
            {
                var converterOptions = CreateDBObjectReferenceOptions(property.PropertyType);

                ImportReferenceResolver.SetContext(obj, property);

                try
                {
                    var childObject = JsonSerializer.Deserialize(ref reader, property.PropertyType, converterOptions);

                    property.SetValue(obj, childObject);
                }
                finally
                {
                    ImportReferenceResolver.ClearContext();
                }
            }
            else if (property.PropertyType.IsEnum)
            {
                if (reader.TokenType != JsonTokenType.String)
                    throw new JsonException($"Expected String token for enum '{property.Name}'.");

                Enum value = ParseDefinedEnum(property.PropertyType, reader.GetString(), property.Name);

                property.SetValue(obj, value);
            }
            else
            {
                var converterOptions = CreateOptions();
                var value = JsonSerializer.Deserialize(ref reader, property.PropertyType, converterOptions);

                property.SetValue(obj, value);
            }
        }

        private void SetAssociationProperty(PropertyInfo parentProperty, T parentObject, object childObject)
        {
            if (!IsDBBindingListProperty(parentProperty))
            {
                return;
            }

            Type bindingListType = parentProperty.PropertyType.GetGenericArguments().First();

            var childProperty = bindingListType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.PropertyType == typeof(T) && IsAssociationProperty(p, parentProperty));

            if (childProperty?.CanWrite == true)
                childProperty.SetValue(childObject, parentObject);
        }

        private static void RemoveMissingObjects(IList existingCollection, IList importedObjects)
        {
            for (int j = existingCollection.Count - 1; j >= 0; j--)
            {
                var obj = (DBObject)existingCollection[j];

                if (!importedObjects.Contains(obj))
                {
                    existingCollection.Remove(obj);
                    obj.Delete();
                }
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();

            PropertyInfo[] properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (PropertyInfo property in properties)
            {
                if (!property.CanRead || HasIgnoreAttribute(property) || IsAssociationProperty(property, _parentProperty))
                {
                    continue;
                }

                writer.WritePropertyName(property.Name);

                WritePropertyValue(ref writer, property, property.GetValue(value));
            }

            writer.WriteEndObject();
        }

        private void WritePropertyValue(ref Utf8JsonWriter writer, PropertyInfo property, object value)
        {
            if (value == null)
            {
                writer.WriteNullValue();
            }
            else if (IsDBBindingListProperty(property))
            {
                Type bindingListType = property.PropertyType.GetGenericArguments().First();

                var converterOptions = CreateDBObjectOptions(bindingListType, property, null);

                writer.WriteStartArray();

                foreach (var obj in (IList)value)
                {
                    JsonSerializer.Serialize(writer, obj, converterOptions);
                }

                writer.WriteEndArray();
            }
            else if (typeof(DBObject).IsAssignableFrom(property.PropertyType))
            {
                var converterOptions = CreateDBObjectReferenceOptions(property.PropertyType);

                JsonSerializer.Serialize(writer, value, property.PropertyType, converterOptions);
            }
            else if (property.PropertyType.IsEnum)
            {
                writer.WriteStringValue(value.ToString());
            }
            else
            {
                var converterOptions = CreateOptions();
                JsonSerializer.Serialize(writer, value, property.PropertyType, converterOptions);
            }
        }

        private T FindObjectOrCreate(ref Utf8JsonReader reader)
        {
            var readerAtStart = reader;

            var identityProperties = GetIdentityProperties(typeof(T)).ToArray();
            var jsonIdentityProperties = identityProperties
                .Where(property => !IsAssociationProperty(property, _parentProperty))
                .ToArray();

            var identityValuesByProperty = new Dictionary<PropertyInfo, string[]>();

            if (identityProperties.Length == 0 || jsonIdentityProperties.Length == 0)
            {
                reader = readerAtStart;

                return Session.GetCollection<T>().CreateNewObject();
            }

            bool endObject = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    endObject = true;
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName token.");
                }

                string propertyName = reader.GetString();

                var property = Array.Find(jsonIdentityProperties, p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

                if (property == null)
                {
                    reader.Skip();
                    continue;
                }

                // read value
                reader.Read();

                if (typeof(DBObject).IsAssignableFrom(property.PropertyType))
                {
                    if (reader.TokenType != JsonTokenType.String)
                        throw new JsonException($"Expected String token for object identity '{property.Name}'.");

                    identityValuesByProperty[property] = SplitIdentityValue(reader.GetString()).ToArray();
                }
                else
                {
                    object value;

                    if (property.PropertyType.IsEnum)
                    {
                        if (reader.TokenType != JsonTokenType.String)
                            throw new JsonException($"Expected String token for enum identity '{property.Name}'.");

                        value = ParseDefinedEnum(property.PropertyType, reader.GetString(), property.Name);
                    }
                    else
                    {
                        var converterOptions = CreateOptions();
                        value = JsonSerializer.Deserialize(ref reader, property.PropertyType, converterOptions);
                    }

                    identityValuesByProperty[property] = new[] { Convert.ToString(value, CultureInfo.InvariantCulture) };
                }

                if (identityValuesByProperty.Count >= jsonIdentityProperties.Length)
                {
                    break;
                }
            }

            if (!endObject && identityValuesByProperty.Count < jsonIdentityProperties.Length)
                throw new JsonException($"Unexpected end of JSON while reading identity for '{typeof(T).Name}'.");

            reader = readerAtStart;

            var identities = new List<string>();
            foreach (PropertyInfo identityProperty in jsonIdentityProperties)
            {
                if (!identityValuesByProperty.TryGetValue(identityProperty, out string[] values))
                    throw new JsonException($"Missing identity property '{identityProperty.Name}' for '{typeof(T).Name}'.");

                identities.AddRange(values);
            }

            if (identities.Count == 0 || identities.Any(string.IsNullOrWhiteSpace))
                throw new JsonException($"Identity value for '{typeof(T).Name}' cannot be empty.");

            var obj = FindObject(identities, _parentProperty);

            if (obj == null)
            {
                obj = Session.GetCollection<T>().CreateNewObject();
            }

            return obj;
        }

        private T FindObject(List<string> identityValue, PropertyInfo parentProperty)
        {
            if (_bindingList != null)
            {
                return FindObjectFromCollection(_bindingList, identityValue, parentProperty);
            }
            else
            {
                return FindObjectFromCollection(Session.GetCollection<T>().Binding, identityValue, parentProperty);
            }
        }

        private T FindObjectFromCollection(IList<T> collection, List<string> identityValues, PropertyInfo parentProperty)
        {
            var identityProperties = GetIdentityProperties(typeof(T));

            return collection.FirstOrDefault(entity =>
            {
                var identities = new List<string>();

                FindAllIdentities(parentProperty, typeof(T), identityProperties, entity, identities);

                return identityValues.SequenceEqual(identities);
            });
        }
    }

    public class DBObjectReferenceConverter<T> : DBObjectBaseConverter<T> where T : DBObject, new()
    {
        public DBObjectReferenceConverter(Session session) : base(session) { }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return default;
            }

            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"Expected String token for '{typeof(T).Name}' reference.");

            var identityValue = reader.GetString();

            if (string.IsNullOrWhiteSpace(identityValue))
            {
                return null;
            }

            List<string> identityValues = SplitIdentityValue(identityValue).ToList();

            var obj = GetObjectFromIdentity(identityValues, false);

            if (obj == null)
            {
                if (ImportReferenceResolver.TryAddMissingReference(typeof(T), identityValues))
                {
                    return null;
                }

                return GetObjectFromIdentity(identityValues);
            }

            return obj;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            var identities = new List<string>();

            FindAllIdentities(null, typeof(T), GetIdentityProperties(typeof(T)), value, identities);

            writer.WriteStringValue(JoinIdentityValues(identities.ToArray()));
        }

        internal T GetObjectFromIdentity(List<string> identityValues, bool throwOnMissing = true)
        {
            var identityProperties = GetIdentityProperties(typeof(T));

            var obj = Session.GetCollection<T>().Binding.FirstOrDefault(item =>
            {
                var identities = new List<string>();

                FindAllIdentities(null, typeof(T), identityProperties, item, identities);

                return identities.SequenceEqual(identityValues);
            });

            if (obj == null && throwOnMissing)
            {
                throw new JsonException($"Object not found for '{typeof(T).Name}' using values '{JoinIdentityValues(identityValues.ToArray())}'.");
            }

            return obj;
        }
    }

    public abstract class DBObjectBaseConverter<T> : JsonConverter<T>
    {
        protected Session Session { get; set; }

        private readonly Dictionary<Type, JsonConverter> _dbObjectReferenceConverterCache = new();
        private readonly Dictionary<Type, JsonConverter> _dbObjectConverterCache = new();

        public DBObjectBaseConverter(Session session)
        {
            Session = session;
        }

        protected void FindAllIdentities(PropertyInfo parentProperty, Type type, IEnumerable<PropertyInfo> identityProperties, object obj, List<string> identities)
        {
            foreach (var property in identityProperties)
            {
                if (property.PropertyType == type)
                {
                    continue; //prevent recursive loop
                }

                if (typeof(DBObject).IsAssignableFrom(property.PropertyType))
                {
                    if (IsAssociationProperty(property, parentProperty))
                    {
                        continue;
                    }

                    var childObject = property.GetValue(obj);

                    if (childObject == null)
                    {
                        // property hasn't been set so we can't compare against it
                        continue;
                    }

                    FindAllIdentities(property, property.PropertyType, GetIdentityProperties(property.PropertyType), childObject, identities);
                }
                else
                {
                    object identity = property.GetValue(obj);

                    identities.Add(Convert.ToString(identity, CultureInfo.InvariantCulture) ?? string.Empty);
                }
            }
        }

        protected IEnumerable<PropertyInfo> GetIdentityProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(prop => Attribute.IsDefined(prop, typeof(IsIdentityAttribute), false));
        }

        protected bool HasIgnoreAttribute(PropertyInfo property)
        {
            return property.GetCustomAttribute<JsonIgnoreAttribute>() != null;
        }

        protected JsonSerializerOptions CreateOptions()
        {
            var converterOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            AddDefaultConverters(converterOptions);

            return converterOptions;
        }

        protected JsonSerializerOptions CreateDBObjectOptions(Type typeToConvert, PropertyInfo parent, object bindingList)
        {
            JsonConverter converter = null;

            if (bindingList == null && parent == null)
            {
                if (_dbObjectConverterCache.TryGetValue(typeToConvert, out JsonConverter value))
                {
                    converter = value;
                }
            }

            if (converter == null)
            {
                Type converterType = typeof(DBObjectConverter<>).MakeGenericType(typeToConvert);
                converter = (JsonConverter)Activator.CreateInstance(converterType, Session, parent, bindingList);

                if (bindingList == null && parent == null)
                {
                    _dbObjectConverterCache.Add(typeToConvert, converter);
                }
            }

            var converterOptions = new JsonSerializerOptions
            {
                Converters = { converter },
                PropertyNameCaseInsensitive = true
            };

            AddDefaultConverters(converterOptions);

            return converterOptions;
        }

        protected JsonSerializerOptions CreateDBObjectReferenceOptions(Type typeToConvert)
        {
            JsonConverter converter;

            if (_dbObjectReferenceConverterCache.TryGetValue(typeToConvert, out JsonConverter value))
            {
                converter = value;
            }
            else
            {
                Type converterType = typeof(DBObjectReferenceConverter<>).MakeGenericType(typeToConvert);
                converter = (JsonConverter)Activator.CreateInstance(converterType, Session);

                _dbObjectReferenceConverterCache.Add(typeToConvert, converter);
            }

            var converterOptions = new JsonSerializerOptions
            {
                Converters = { converter },
                PropertyNameCaseInsensitive = true
            };

            AddDefaultConverters(converterOptions);

            return converterOptions;
        }

        private static void AddDefaultConverters(JsonSerializerOptions options)
        {
            options.Converters.Add(new JsonStringEnumConverter());
        }

        protected bool IsAssociationProperty(PropertyInfo childProperty, PropertyInfo parentProperty)
        {
            if (childProperty == null || parentProperty == null)
            {
                return false;
            }

            var childAssociationValue = GetAssociationValueFromProperty(childProperty);
            var parentAssociationValue = GetAssociationValueFromProperty(parentProperty);

            if (IsDBBindingListProperty(parentProperty))
            {
                Type parentBindingListType = parentProperty.PropertyType.GetGenericArguments().First();

                if (parentAssociationValue == childAssociationValue && parentProperty.DeclaringType == childProperty.PropertyType && parentBindingListType == childProperty.DeclaringType)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetAssociationValueFromProperty(PropertyInfo property)
        {
            if (property == null)
            {
                return null;
            }

            AssociationAttribute associationAttribute = property.GetCustomAttribute<AssociationAttribute>();

            if (associationAttribute == null)
            {
                return null;
            }

            return associationAttribute.Identity;
        }

        protected static bool IsDBBindingListProperty(PropertyInfo property)
        {
            return property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(DBBindingList<>);
        }

        protected static bool CanImportProperty(PropertyInfo property)
        {
            return property.CanWrite || IsDBBindingListProperty(property);
        }

        protected static string[] SplitIdentityValue(string value)
        {
            if (value == null) return [];

            List<string> values = new();
            System.Text.StringBuilder current = new System.Text.StringBuilder();

            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];

                if (ch == '\\' && i + 1 < value.Length && (value[i + 1] == '/' || value[i + 1] == '\\'))
                {
                    current.Append(value[i + 1]);
                    i++;
                    continue;
                }

                if (ch == '/')
                {
                    values.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            values.Add(current.ToString());
            return values.ToArray();
        }

        protected string JoinIdentityValues(string[] values)
        {
            return string.Join('/', values.Select(EscapeIdentityValue));
        }

        private static string EscapeIdentityValue(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("/", "\\/");
        }

        protected static Enum ParseDefinedEnum(Type enumType, string text, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new JsonException($"Enum value for '{propertyName}' cannot be empty.");

            object parsed;
            try
            {
                parsed = Enum.Parse(enumType, text, true);
            }
            catch (Exception ex) when (ex is ArgumentException or OverflowException)
            {
                throw new JsonException($"Enum value '{text}' is not valid for '{enumType.Name}' at '{propertyName}'.", ex);
            }

            if (!IsDefinedEnumValue(enumType, parsed))
                throw new JsonException($"Enum value '{text}' is not valid for '{enumType.Name}' at '{propertyName}'.");

            return (Enum)parsed;
        }

        private static bool IsDefinedEnumValue(Type enumType, object value)
        {
            if (Enum.IsDefined(enumType, value))
                return true;

            if (enumType.GetCustomAttribute<FlagsAttribute>() == null)
                return false;

            try
            {
                ulong raw = GetEnumBits(value, enumType);
                if (raw == 0)
                    return true;

                ulong allowedBits = 0;
                foreach (object definedValue in Enum.GetValues(enumType))
                    allowedBits |= GetEnumBits(definedValue, enumType);

                return (raw & ~allowedBits) == 0;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static ulong GetEnumBits(object value, Type enumType)
        {
            return Type.GetTypeCode(Enum.GetUnderlyingType(enumType)) switch
            {
                TypeCode.SByte => unchecked((ulong)(byte)Convert.ToSByte(value, CultureInfo.InvariantCulture)),
                TypeCode.Byte => Convert.ToByte(value, CultureInfo.InvariantCulture),
                TypeCode.Int16 => unchecked((ulong)(ushort)Convert.ToInt16(value, CultureInfo.InvariantCulture)),
                TypeCode.UInt16 => Convert.ToUInt16(value, CultureInfo.InvariantCulture),
                TypeCode.Int32 => unchecked((ulong)(uint)Convert.ToInt32(value, CultureInfo.InvariantCulture)),
                TypeCode.UInt32 => Convert.ToUInt32(value, CultureInfo.InvariantCulture),
                TypeCode.Int64 => unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture)),
                TypeCode.UInt64 => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
                _ => throw new OverflowException($"Unsupported enum underlying type for '{enumType.Name}'."),
            };
        }
    }
}
