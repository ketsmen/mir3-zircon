using MirDB;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Server.WinForms
{
    public sealed class ValidationService
    {
        private readonly MirDbMetadata _metadata;

        public ValidationService(MirDbMetadata metadata)
        {
            _metadata = metadata;
        }

        public List<string> Validate()
        {
            List<string> messages = new List<string>();

            foreach (Type type in _metadata.ObjectTypes)
            {
                ValidateIdentityDuplicates(type, messages);
                ValidateReferences(type, messages);
            }

            return messages;
        }

        private void ValidateIdentityDuplicates(Type type, List<string> messages)
        {
            PropertyInfo[] identities = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.GetCustomAttribute<IsIdentityAttribute>() != null)
                .ToArray();

            if (identities.Length == 0) return;

            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object item in _metadata.GetBinding(type))
            {
                string key = string.Join("|", identities.Select(x => SafeGetText(x, item)));
                if (string.IsNullOrWhiteSpace(key.Replace("|", string.Empty))) continue;

                if (!keys.Add(key))
                    messages.Add($"{type.Name} 存在重复身份字段: {key}");
            }
        }

        private void ValidateReferences(Type type, List<string> messages)
        {
            foreach (object item in _metadata.GetBinding(type))
            {
                if (item is not DBObject owner) continue;

                foreach (PropertyInfo property in _metadata.GetValueProperties(type))
                {
                    if (!property.PropertyType.IsSubclassOf(typeof(DBObject))) continue;

                    object value;
                    try
                    {
                        value = property.GetValue(owner);
                    }
                    catch
                    {
                        continue;
                    }

                    if (value == null) continue;

                    IList targetList = _metadata.GetBinding(property.PropertyType);
                    if (!targetList.Contains(value))
                        messages.Add($"{type.Name} #{owner.Index} 的 {property.Name} 引用了不存在的 {property.PropertyType.Name}。");
                }
            }
        }

        private static string SafeGetText(PropertyInfo property, object item)
        {
            try
            {
                return property.GetValue(item)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
