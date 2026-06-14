using DevExpress.XtraEditors;
using DevExpress.XtraGrid.Views.Grid;
using MirDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace Server
{
    public class JsonExporter
    {
        public static void Export<T>(GridView grid) where T : DBObject, new()
        {
            var options = new JsonSerializerOptions
            {
                Converters = { new DBObjectArrayConverter<T>(SMain.Session) },
                WriteIndented = true
            };

            JsonExporter.Export<T>(grid, options);
        }

        public static void Export<T>(GridView gridView, JsonSerializerOptions options) where T : DBObject, new()
        {
            if (!Directory.Exists($"Exports/{typeof(T).Name}"))
            {
                Directory.CreateDirectory($"Exports/{typeof(T).Name}");
            }

            List<T> selectedItems = new();

            var rows = gridView.GetSelectedRows();

            if (rows.Length == 0)
            {
                selectedItems.AddRange(SMain.Session.GetCollection<T>().Binding);
            }
            else
            {
                foreach (var row in rows)
                {
                    T selRow = (T)gridView.GetRow(row);

                    selectedItems.Add(selRow);
                }
            }

            if (XtraMessageBox.Show($"即将导出 {selectedItems.Count} 行，确定继续吗？", "确认", MessageBoxButtons.YesNo) != DialogResult.Yes)
            {
                return;
            }

            var json = JsonSerializer.Serialize<T[]>(selectedItems.ToArray(), options);

            var time = DateTime.UtcNow;
            var now = $"{time.Year:0000}-{time.Month:00}-{time.Day:00} {time.Hour:00}-{time.Minute:00}-{time.Second:00}";

            var path = $"Exports/{typeof(T).Name}/{typeof(T).Name} - {selectedItems.Count} - {now}.json";

            using StreamWriter file = new(path);

            file.WriteLine(json);

            XtraMessageBox.Show($"已将选中的行导出到 '{path}'。", "成功", MessageBoxButtons.OK);
        }
    }
}
