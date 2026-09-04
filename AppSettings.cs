using System.Text.Json;

namespace SrtSuite;

public sealed class AppSettings
{
    public string TxSource { get; set; } = "DeckLink SDI Input";
    public string TxDevice { get; set; } = "DeckLink SDI 4K";
    public string TxVideoInput { get; set; } = "sdi";
    public string TxFormat { get; set; } = "Hi50 (1080i50 - Default)";
    public string TxEncoder { get; set; } = "h264_nvenc (NVIDIA GPU)";
    public string TxBitrate { get; set; } = "6000k";
    public string TxFilePath { get; set; } = "sample_video.mp4";
    public bool TxLoop { get; set; } = true;
    public string TxMode { get; set; } = "Caller (Send to remote)";
    public string TxHost { get; set; } = "127.0.0.1";
    public int TxPort { get; set; } = 9998;
    public int TxLatency { get; set; } = 120;
    public string TxPassphrase { get; set; } = "";
    public string TxStreamId { get; set; } = "";

    public bool RxEnableDeckLink { get; set; } = true;
    public string RxDevice { get; set; } = "DeckLink Duo (1)";
    public string RxFormat { get; set; } = "Hi50 (1080i50 - Default)";
    public string RxMode { get; set; } = "Listener (Listen for incoming)";
    public string RxHost { get; set; } = "0.0.0.0";
    public int RxPort { get; set; } = 9998;
    public int RxLatency { get; set; } = 120;
    public string RxPassphrase { get; set; } = "";
    public string RxStreamId { get; set; } = "";

    public static string SettingsFilePath
    {
        get
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var path = Path.Combine(baseDir, "appsettings.json");
            return path;
        }
    }

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsFilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var path = SettingsFilePath;
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(path, json);
        }
        catch { }
    }
}
