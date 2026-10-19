using MysqlDumpBuilder.Core;

namespace MysqlDumpBuilder.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // 포터블 구성: exe 와 같은 폴더의 options.schema.json / presets.json / settings.json 을 쓴다.
        var baseDir = AppContext.BaseDirectory;
        OptionSchema schema;
        try
        {
            schema = OptionSchema.LoadFromFileOrEmbedded(Path.Combine(baseDir, "options.schema.json"));
        }
        catch (Exception ex)
        {
            MessageBox.Show("options.schema.json 을 읽을 수 없습니다. 내장 스키마로 시작합니다.\n\n" + ex.Message,
                "mysqldump Command Builder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            schema = OptionSchema.LoadEmbedded();
        }

        var settings = AppSettings.Load();
        var presets = new PresetStore(Path.Combine(baseDir, "presets.json"));

        Application.Run(new MainForm(schema, settings, presets));
    }
}
