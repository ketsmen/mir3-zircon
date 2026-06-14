using MirDB;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;

namespace Server.WinForms
{
    public sealed class MirDbMetadata
    {
        private readonly Session _session;
        private readonly Dictionary<Type, object> _collections = new Dictionary<Type, object>();
        private readonly Dictionary<Type, IList> _bindings = new Dictionary<Type, IList>();

        public MirDbMetadata(Session session)
        {
            _session = session;

            foreach (Type type in session.Assemblies.SelectMany(x => x.GetTypes()).Where(x => x.IsSubclassOf(typeof(DBObject))).OrderBy(x => x.Name))
            {
                object collection = session.GetCollection(type);
                _collections[type] = collection;
                _bindings[type] = (IList)collection.GetType().GetField("Binding").GetValue(collection);
            }
        }

        public IEnumerable<Type> ObjectTypes => _collections.Keys.OrderBy(IsUserObject).ThenBy(x => x.Name);

        public bool IsUserObject(Type type)
        {
            return type.GetCustomAttribute<UserObjectAttribute>() != null;
        }

        public object GetCollection(Type type)
        {
            return _collections[type];
        }

        public IList GetBinding(Type type)
        {
            return _bindings[type];
        }

        public int GetCount(Type type)
        {
            return GetBinding(type).Count;
        }

        public IReadOnlyList<PropertyInfo> GetValueProperties(Type type)
        {
            return type.GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.GetCustomAttribute<IgnorePropertyAttribute>() == null)
                .Where(x => x.GetIndexParameters().Length == 0)
                .Where(x => IsGridValueType(x.PropertyType))
                .OrderBy(x => x.Name == nameof(DBObject.Index) ? 0 : 1)
                .ThenBy(x => x.MetadataToken)
                .ToArray();
        }

        public IReadOnlyList<PropertyInfo> GetChildListProperties(Type type)
        {
            return type.GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.GetCustomAttribute<IgnorePropertyAttribute>() == null)
                .Where(x => x.PropertyType.IsGenericType && x.PropertyType.GetGenericTypeDefinition() == typeof(DBBindingList<>))
                .OrderBy(x => x.MetadataToken)
                .ToArray();
        }

        public string GetDisplayText(object value)
        {
            if (value == null) return string.Empty;

            if (value is DBObject dbObject)
            {
                string identity = GetIdentityText(dbObject);
                return string.IsNullOrWhiteSpace(identity) ? $"#{dbObject.Index}" : $"{identity} (#{dbObject.Index})";
            }

            if (value is Color color) return ColorTranslator.ToHtml(color);
            if (value is Point point) return $"{point.X},{point.Y}";
            if (value is Size size) return $"{size.Width},{size.Height}";
            if (value is Array array) return $"[{array.Length}]";

            return SafeToString(value);
        }

        public IReadOnlyList<ObjectChoice> GetObjectChoices(Type type)
        {
            List<ObjectChoice> choices = new List<ObjectChoice>
            {
                new ObjectChoice(null, "[空]")
            };

            foreach (object item in GetBinding(type))
                choices.Add(new ObjectChoice(item, GetDisplayText(item)));

            return choices;
        }

        public object CreateRootObject(Type type)
        {
            object collection = GetCollection(type);
            return collection.GetType().GetMethod("CreateNewObject").Invoke(collection, null);
        }

        public object InsertRootObjectAfter(Type type, int index)
        {
            MethodInfo method = typeof(Session).GetMethod(nameof(Session.InsertObjectAfter)).MakeGenericMethod(type);
            return method.Invoke(_session, new object[] { index });
        }

        public static bool IsSimpleEditable(Type type)
        {
            Type target = Nullable.GetUnderlyingType(type) ?? type;

            return target == typeof(string) ||
                   target == typeof(char) ||
                   target == typeof(byte) ||
                   target == typeof(sbyte) ||
                   target == typeof(short) ||
                   target == typeof(ushort) ||
                   target == typeof(int) ||
                   target == typeof(uint) ||
                   target == typeof(long) ||
                   target == typeof(ulong) ||
                   target == typeof(float) ||
                   target == typeof(double) ||
                   target == typeof(decimal) ||
                   target == typeof(DateTime) ||
                   target == typeof(TimeSpan) ||
                   target == typeof(Point) ||
                   target == typeof(Size) ||
                   target == typeof(Color);
        }

        public static bool IsGridValueType(Type type)
        {
            Type target = Nullable.GetUnderlyingType(type) ?? type;

            return IsSimpleEditable(target) ||
                   target == typeof(bool) ||
                   target.IsEnum ||
                   target.IsSubclassOf(typeof(DBObject)) ||
                   target.IsArray ||
                   target.FullName == "Library.Stats" ||
                   target == typeof(BitArray);
        }

        private string GetIdentityText(DBObject value)
        {
            PropertyInfo[] identities = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.GetCustomAttribute<IsIdentityAttribute>() != null)
                .ToArray();

            if (identities.Length == 0)
                return SafeObjectName(value);

            List<string> parts = new List<string>();

            foreach (PropertyInfo property in identities)
            {
                object propertyValue;

                try
                {
                    propertyValue = property.GetValue(value);
                }
                catch
                {
                    continue;
                }

                string text = GetDisplayText(propertyValue);
                if (!string.IsNullOrWhiteSpace(text))
                    parts.Add(text);
            }

            return parts.Count == 0 ? SafeObjectName(value) : string.Join(" / ", parts);
        }

        private static string SafeObjectName(DBObject value)
        {
            string text = SafeToString(value);
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            return $"{value.GetType().Name} #{value.Index}";
        }

        private static string SafeToString(object value)
        {
            try
            {
                return value?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public sealed class ObjectChoice
    {
        public ObjectChoice(object value, string display)
        {
            Value = value;
            Display = display;
        }

        public object Value { get; }
        public string Display { get; }
    }
}
