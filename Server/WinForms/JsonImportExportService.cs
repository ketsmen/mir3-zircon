using MirDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            string json = JsonSerializer.Serialize(array, array.GetType(), CreateOptions(itemType, true));
            File.WriteAllText(dialog.FileName, json);

            MessageBox.Show(owner, $"已导出 {selected.Length} 行。", "导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public int Import(Type itemType, IWin32Window owner)
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                RestoreDirectory = true
            };

            if (dialog.ShowDialog(owner) != DialogResult.OK) return 0;

            ImportReferenceResolver.SetDeferredResolution(true);

            string json = File.ReadAllText(dialog.FileName);
            object result = JsonSerializer.Deserialize(json, itemType.MakeArrayType(), CreateOptions(itemType, false));
            int imported = result is Array array ? array.Length : 0;

            (int resolved, int remaining) = ImportReferenceResolver.ResolvePendingReferences(_session);

            MessageBox.Show(owner, $"已导入 {imported} 行。\r\n已解析 {resolved} 个延迟引用，剩余 {remaining} 个。", "导入", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return imported;
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
