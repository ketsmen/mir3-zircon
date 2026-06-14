using DevExpress.XtraEditors;
using MirDB;
using Server.Envir;
using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace Server
{
    public class JsonImporter
    {
        public static void Import<T>(bool allowDeferredReferences = true) where T : DBObject, new()
        {
            var options = new JsonSerializerOptions
            {
                Converters = { new DBObjectArrayConverter<T>(SMain.Session) },
                PropertyNameCaseInsensitive = true
            };

            JsonImporter.Import<T>(options, allowDeferredReferences);
        }

        public static void Import<T>(JsonSerializerOptions options, bool allowDeferredReferences = true)
        {
            if (!Directory.Exists($"Exports/{typeof(T).Name}"))
            {
                Directory.CreateDirectory($"Exports/{typeof(T).Name}");
            }

            ImportReferenceResolver.SetDeferredResolution(allowDeferredReferences);
            SMain.Session.Save(true);

            XtraOpenFileDialog dlg = new()
            {
                InitialDirectory = $"{Directory.GetCurrentDirectory()}\\Exports\\{typeof(T).Name}\\",
                Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                RestoreDirectory = true
            };

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                var file = File.ReadAllText(dlg.FileName);

                try
                {
                    var rows = JsonSerializer.Deserialize<T[]>(file, options);

                    var (resolved, remaining) = ImportReferenceResolver.ResolvePendingReferences(SMain.Session);

                    string message = $"已成功导入 {rows.Length} 行。";

                    if (allowDeferredReferences)
                    {
                        message += $"\n已解析 {resolved} 个延迟引用。";

                        if (remaining > 0)
                        {
                            message += $"\n仍有 {remaining} 个引用待处理，将在下次导入时重试。";
                        }
                    }

                    XtraMessageBox.Show(message, "成功", MessageBoxButtons.OK);
                }
                catch (Exception ex)
                {
                    SEnvir.Log(ex.Message);

                    XtraMessageBox.Show($"未能导入全部行。\r\n\r\n{ex.Message}", "失败", MessageBoxButtons.OK);
                }
            }
        }
    }
}
