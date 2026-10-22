using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Library;
using Server.Envir;

string outputPath = args.Length >= 2 && string.Equals(args[0], "--output", StringComparison.OrdinalIgnoreCase)
    ? args[1]
    : Path.Combine(Environment.CurrentDirectory, "server-config.schema.json");

List<ConfigEntry> entries = new();
string? section = null;

foreach (PropertyInfo property in typeof(Config).GetProperties(BindingFlags.Public | BindingFlags.Static))
{
    ConfigSection? sectionAttribute = property.GetCustomAttribute<ConfigSection>();
    if (sectionAttribute != null)
        section = sectionAttribute.Section;

    if (section == null) continue;
    if (property.GetCustomAttribute<ConfigPropertyIgnore>() != null) continue;

    object? value = property.GetValue(null);
    entries.Add(new ConfigEntry(
        section,
        property.Name,
        GetFriendlyTypeName(property.PropertyType),
        IsSensitive(property.Name),
        FormatValue(value)));
}

ConfigSchema schema = new("Server.ini", entries);
JsonSerializerOptions options = new() { WriteIndented = true };
string json = JsonSerializer.Serialize(schema, options);

string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
if (!string.IsNullOrEmpty(directory))
    Directory.CreateDirectory(directory);

File.WriteAllText(outputPath, json, new UTF8Encoding(false));
Console.WriteLine(Path.GetFullPath(outputPath));

static bool IsSensitive(string name)
{
    return name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Key", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Hash", StringComparison.OrdinalIgnoreCase);
}

static string GetFriendlyTypeName(Type type)
{
    if (type == typeof(string)) return "string";
    if (type == typeof(bool)) return "bool";
    if (type == typeof(byte)) return "byte";
    if (type == typeof(ushort)) return "ushort";
    if (type == typeof(int)) return "int";
    if (type == typeof(long)) return "long";
    if (type == typeof(decimal)) return "decimal";
    if (type == typeof(DateTime)) return "datetime";
    if (type == typeof(TimeSpan)) return "timespan";
    if (type.IsEnum) return type.Name;
    if (type == typeof(byte[])) return "bytes";

    return type.Name;
}

static string? FormatValue(object? value)
{
    return value switch
    {
        null => null,
        byte[] bytes => Convert.ToBase64String(bytes),
        DateTime dateTime => dateTime.ToString("O"),
        TimeSpan timeSpan => timeSpan.ToString(),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
    };
}

public sealed record ConfigSchema(string ConfigFile, IReadOnlyList<ConfigEntry> Entries);
public sealed record ConfigEntry(string Section, string Name, string Type, bool Sensitive, string? DefaultValue);
