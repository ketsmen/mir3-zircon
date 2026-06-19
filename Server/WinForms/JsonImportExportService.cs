using MirDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace Server.WinForms
{
    public sealed class JsonImportExportService
    {
        private readonly Session _session;

        public JsonImportExportService(Session session)
        {
            _session = session;
        }

        public void Export(Type itemType, IEnumerable<object> items, IWin32Window owner)
        {
            object[] selected = items?.ToArray() ?? Array.Empty<object>();
            if (selected.Length == 0)
            {
                MessageBox.Show(owner, "没有可导出的行。", "导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                FileName = $"{itemType.Name} - {selected.Length} - {DateTime.UtcNow:yyyy-MM-dd HH-mm-ss}.json",
                RestoreDirectory = true
            };

            if (dialog.ShowDialog(owner) != DialogResult.OK) return;

            Array array = Array.CreateInstance(itemType, selected.Length);
            for (int i = 0; i < selected.Length; i++)
                array.SetValue(selected[i], i);

            try
            {
                string json = JsonSerializer.Serialize(array, array.GetType(), CreateOptions(itemType, true));
                WinFormsFileWriter.WriteAllTextAtomically(dialog.FileName, json);

                MessageBox.Show(owner, $"已导出 {selected.Length} 行。", "导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
            {
                MessageBox.Show(owner, ex.ToString(), "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public int Import(Type itemType, IWin32Window owner)
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                RestoreDirectory = true
            };

            if (dialog.ShowDialog(owner) != DialogResult.OK) return 0;

            string json;
            try
            {
                json = File.ReadAllText(dialog.FileName);
                ValidateImportJson(json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                MessageBox.Show(owner, ex.ToString(), "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 0;
            }

            try
            {
                ImportReferenceResolver.SetDeferredResolution(true);

                object result = JsonSerializer.Deserialize(json, itemType.MakeArrayType(), CreateOptions(itemType, false));
                int imported = result is Array array ? array.Length : 0;

                (int resolved, int remaining) = ImportReferenceResolver.ResolvePendingReferences(_session);
                if (remaining > 0)
                {
                    MessageBox.Show(owner, BuildUnresolvedReferenceMessage(imported, resolved, remaining), "导入存在未解析引用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return imported;
                }

                MessageBox.Show(owner, $"已导入 {imported} 行。\r\n已解析 {resolved} 个延迟引用，剩余 {remaining} 个。", "导入", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return imported;
            }
            catch (Exception ex) when (IsPartialImportException(ex))
            {
                throw new InvalidDataException("JSON 导入过程中断，可能已经创建或修改了部分内存对象。", ex);
            }
            finally
            {
                ImportReferenceResolver.SetDeferredResolution(false);
            }
        }

        private static void ValidateImportJson(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("导入 JSON 必须是对象数组。");

            int index = 0;
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"导入 JSON 第 {index + 1} 项必须是对象。");

                index++;
            }
        }

        private static string BuildUnresolvedReferenceMessage(int imported, int resolved, int remaining)
        {
            List<string> examples = ImportReferenceResolver.GetPendingReferenceDescriptions(10).ToList();

            string message = $"已导入 {imported} 行并解析 {resolved} 个延迟引用，但仍有 {remaining} 个引用无法解析。导入内容可能已部分写入内存，请修正 JSON 中的引用后重试。";

            if (examples.Count == 0)
                return message;

            List<string> lines = new List<string>
            {
                message,
                string.Empty,
                "未解析引用示例:"
            };

            lines.AddRange(examples.Select(x => "- " + x));

            if (ImportReferenceResolver.PendingReferenceCount > examples.Count)
                lines.Add($"- 另有 {ImportReferenceResolver.PendingReferenceCount - examples.Count} 个未显示。");

            return string.Join("\r\n", lines);
        }

        private static bool IsPartialImportException(Exception ex)
        {
            return ex is JsonException or InvalidDataException or NotSupportedException or ArgumentException or InvalidOperationException or TargetInvocationException;
        }

        private JsonSerializerOptions CreateOptions(Type itemType, bool writeIndented)
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = writeIndented
            };

            Type converterType = typeof(DBObjectArrayConverter<>).MakeGenericType(itemType);
            options.Converters.Add((JsonConverter)Activator.CreateInstance(converterType, _session));

            return options;
        }
    }
}
