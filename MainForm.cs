using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using DeckLinkAPI;

namespace SrtSuite;

public sealed partial class MainForm : Form
{
    private readonly SrtTransmitterEngine _txEngine;
    private readonly SrtReceiverEngine _rxEngine;
    private readonly string _ffmpegPath;

    // Persistent settings
    private AppSettings _appSettings = null!;

    // UI Controls - Header
    private Panel _pnlHeader = null!;
    private Label _lblHeaderTitle = null!;
    private Label _lblHeaderSubtitle = null!;
    private Button _btnRefreshCards = null!;
    private Label _lblFfmpegStatus = null!;
    private CheckBox _chkShowLogs = null!;
    private Label _lblTheme = null!;
    private ComboBox _cboTheme = null!;
    private TableLayoutPanel _tblMain = null!;

    // UI Controls - Transmitter (TX)
    private Panel _pnlTx = null!;
    private Panel _pnlTxSettings = null!;
    private Panel _pnlTxControls = null!;
    private Label _lblTxTitle = null!;
    private PictureBox _picTxPreview = null!;
    private ComboBox _cboTxSource = null!;
    private ComboBox _cboTxDevice = null!;
    private ComboBox _cboTxVideoInput = null!;
    private ComboBox _cboTxFormat = null!;
    private TextBox _txtTxFilePath = null!;
    private Button _btnTxBrowse = null!;
    private CheckBox _chkTxLoop = null!;
    private ComboBox _cboTxEncoder = null!;
    private TextBox _txtTxBitrate = null!;
    private ComboBox _cboTxMode = null!;
    private TextBox _txtTxHost = null!;
    private NumericUpDown _numTxPort = null!;
    private NumericUpDown _numTxLatency = null!;
    private TextBox _txtTxPassphrase = null!;
    private TextBox _txtTxStreamId = null!;
    private Button _btnTxStart = null!;
    private Button _btnTxStop = null!;
    private Label _lblTxStatusBadge = null!;
    private Label _lblTxStats = null!;
    private Panel _pnlTxLogContainer = null!;
    private RichTextBox _rtbTxLog = null!;
    private Button _btnTxCopyLog = null!;
    private Button _btnTxClearLog = null!;

    // UI Controls - Receiver (RX)
    private Panel _pnlRx = null!;
    private Panel _pnlRxSettings = null!;
    private Panel _pnlRxControls = null!;
    private Label _lblRxTitle = null!;
    private PictureBox _picRxPreview = null!;
    private CheckBox _chkRxEnableDeckLink = null!;
    private ComboBox _cboRxDevice = null!;
    private ComboBox _cboRxFormat = null!;
    private ComboBox _cboRxMode = null!;
    private TextBox _txtRxHost = null!;
    private NumericUpDown _numRxPort = null!;
    private NumericUpDown _numRxLatency = null!;
    private TextBox _txtRxPassphrase = null!;
    private TextBox _txtRxStreamId = null!;
    private NumericUpDown _numRxAudioDelay = null!;
    private Button _btnRxStart = null!;
    private Button _btnRxStop = null!;
    private Label _lblRxStatusBadge = null!;
    private Label _lblRxStats = null!;
    private Panel _pnlRxLogContainer = null!;
    private RichTextBox _rtbRxLog = null!;
    private Button _btnRxCopyLog = null!;
    private Button _btnRxClearLog = null!;

    private int _heightWithLogs = 500;
    private readonly object _logLock = new();

    public MainForm()
    {
        _appSettings = AppSettings.Load();
        InitializeComponentCustom();

        _ffmpegPath = ResolveFfmpegPath();
        _lblFfmpegStatus.Text = File.Exists(_ffmpegPath) 
            ? $"FFmpeg: {_ffmpegPath}" 
            : "WARNING: FFmpeg binary not found!";

        _txEngine = new SrtTransmitterEngine(_ffmpegPath);
        _rxEngine = new SrtReceiverEngine(_ffmpegPath);

        ApplySettingsToUi();
        WireEvents();
        PopulateDeckLinkDevices();
    }

    private void ApplySettingsToUi()
    {
        // TX Settings
        SelectComboItem(_cboTxSource, _appSettings.TxSource);
        SelectComboItem(_cboTxVideoInput, _appSettings.TxVideoInput);
        SelectComboItem(_cboTxFormat, _appSettings.TxFormat);
        SelectComboItem(_cboTxEncoder, _appSettings.TxEncoder);
        _txtTxBitrate.Text = _appSettings.TxBitrate;
        _txtTxFilePath.Text = _appSettings.TxFilePath;
        _chkTxLoop.Checked = _appSettings.TxLoop;
        SelectComboItem(_cboTxMode, _appSettings.TxMode);
        _txtTxHost.Text = _appSettings.TxHost;
        _numTxPort.Value = Math.Clamp(_appSettings.TxPort, _numTxPort.Minimum, _numTxPort.Maximum);
        _numTxLatency.Value = Math.Clamp(_appSettings.TxLatency, _numTxLatency.Minimum, _numTxLatency.Maximum);
        _txtTxPassphrase.Text = _appSettings.TxPassphrase;
        _txtTxStreamId.Text = _appSettings.TxStreamId;

        // RX Settings
        _chkRxEnableDeckLink.Checked = _appSettings.RxEnableDeckLink;
        SelectComboItem(_cboRxFormat, _appSettings.RxFormat);
        SelectComboItem(_cboRxMode, _appSettings.RxMode);
        _txtRxHost.Text = _appSettings.RxHost;
        _numRxPort.Value = Math.Clamp(_appSettings.RxPort, _numRxPort.Minimum, _numRxPort.Maximum);
        _numRxLatency.Value = Math.Clamp(_appSettings.RxLatency, _numRxLatency.Minimum, _numRxLatency.Maximum);
        _txtRxPassphrase.Text = _appSettings.RxPassphrase;
        _txtRxStreamId.Text = _appSettings.RxStreamId;
        _numRxAudioDelay.Value = Math.Clamp(_appSettings.RxAudioDelayMs, _numRxAudioDelay.Minimum, _numRxAudioDelay.Maximum);

        // Log Visibility
        _chkShowLogs.Checked = _appSettings.ShowLogs;
        UpdateLogVisibility(_appSettings.ShowLogs);

        // Theme
        SelectComboItem(_cboTheme, _appSettings.Theme);
        ApplyTheme(_cboTheme.SelectedItem?.ToString() ?? "Dark");
    }

    private static void SelectComboItem(ComboBox cbo, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || cbo.Items.Count == 0) return;
        for (int i = 0; i < cbo.Items.Count; i++)
        {
            var item = cbo.Items[i]?.ToString();
            if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase) ||
                (item != null && item.StartsWith(value, StringComparison.OrdinalIgnoreCase)))
            {
                cbo.SelectedIndex = i;
                return;
            }
        }
    }

    private void SaveUiToSettings()
    {
        try
        {
            _appSettings.TxSource = _cboTxSource.SelectedItem?.ToString() ?? _appSettings.TxSource;
            _appSettings.TxDevice = _cboTxDevice.SelectedItem?.ToString() ?? _appSettings.TxDevice;
            _appSettings.TxVideoInput = _cboTxVideoInput.SelectedItem?.ToString() ?? _appSettings.TxVideoInput;
            _appSettings.TxFormat = _cboTxFormat.SelectedItem?.ToString() ?? _appSettings.TxFormat;
            _appSettings.TxEncoder = _cboTxEncoder.SelectedItem?.ToString() ?? _appSettings.TxEncoder;
            _appSettings.TxBitrate = _txtTxBitrate.Text.Trim();
            _appSettings.TxFilePath = _txtTxFilePath.Text.Trim();
            _appSettings.TxLoop = _chkTxLoop.Checked;
            _appSettings.TxMode = _cboTxMode.SelectedItem?.ToString() ?? _appSettings.TxMode;
            _appSettings.TxHost = _txtTxHost.Text.Trim();
            _appSettings.TxPort = (int)_numTxPort.Value;
            _appSettings.TxLatency = (int)_numTxLatency.Value;
            _appSettings.TxPassphrase = _txtTxPassphrase.Text.Trim();
            _appSettings.TxStreamId = _txtTxStreamId.Text.Trim();

            _appSettings.RxEnableDeckLink = _chkRxEnableDeckLink.Checked;
            _appSettings.RxDevice = _cboRxDevice.SelectedItem?.ToString() ?? _appSettings.RxDevice;
            _appSettings.RxFormat = _cboRxFormat.SelectedItem?.ToString() ?? _appSettings.RxFormat;
            _appSettings.RxMode = _cboRxMode.SelectedItem?.ToString() ?? _appSettings.RxMode;
            _appSettings.RxHost = _txtRxHost.Text.Trim();
            _appSettings.RxPort = (int)_numRxPort.Value;
            _appSettings.RxLatency = (int)_numRxLatency.Value;
            _appSettings.RxPassphrase = _txtRxPassphrase.Text.Trim();
            _appSettings.RxStreamId = _txtRxStreamId.Text.Trim();
            _appSettings.RxAudioDelayMs = (int)_numRxAudioDelay.Value;
            _appSettings.ShowLogs = _chkShowLogs.Checked;
            _appSettings.Theme = _cboTheme.SelectedItem?.ToString() ?? _appSettings.Theme;

            _appSettings.Save();
        }
        catch { }
    }

    public static string ResolveFfmpegPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg", "ffmpeg.exe"),
            @"c:\Users\vimlesh\Documents\vimlesh\srt\bin\Release\net10.0-windows\win-x64\ffmpeg.exe",
            @"c:\Users\vimlesh\Documents\vimlesh\srt\ffmpeg\ffmpeg.exe",
            @"c:\Users\vimlesh\Documents\vimlesh\srt\bin\ffmpeg.exe"
        };

        foreach (var p in candidates)
        {
            if (File.Exists(p)) return p;
        }

        return "ffmpeg.exe";
    }

    private void PopulateDeckLinkDevices()
    {
        var devices = DeckLinkInterop.EnumerateDevices();
        _cboTxDevice.Items.Clear();
        _cboRxDevice.Items.Clear();

        if (devices.Count == 0)
        {
            _cboTxDevice.Items.Add("No DeckLink hardware detected");
            _cboRxDevice.Items.Add("No DeckLink hardware detected");
            _cboTxDevice.SelectedIndex = 0;
            _cboRxDevice.SelectedIndex = 0;
            return;
        }

        int txIndex = -1;
        int rxIndex = -1;

        for (int i = 0; i < devices.Count; i++)
        {
            var dev = devices[i];
            _cboTxDevice.Items.Add(dev.Name);
            _cboRxDevice.Items.Add(dev.Name);

            // Match TX device from saved settings first
            if (txIndex < 0 && !string.IsNullOrEmpty(_appSettings.TxDevice) &&
                dev.Name.Equals(_appSettings.TxDevice, StringComparison.OrdinalIgnoreCase))
            {
                txIndex = i;
            }

            // Match RX device from saved settings
            if (rxIndex < 0 && !string.IsNullOrEmpty(_appSettings.RxDevice) &&
                dev.Name.Equals(_appSettings.RxDevice, StringComparison.OrdinalIgnoreCase))
            {
                rxIndex = i;
            }
        }

        // If TX device not matched yet, default to DeckLink SDI 4K or any 4K card
        if (txIndex < 0)
        {
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i].Name.Contains("SDI 4K", StringComparison.OrdinalIgnoreCase) ||
                    devices[i].Name.Contains("4K", StringComparison.OrdinalIgnoreCase))
                {
                    txIndex = i;
                    break;
                }
            }
        }

        _cboTxDevice.SelectedIndex = txIndex >= 0 ? txIndex : 0;
        _cboRxDevice.SelectedIndex = rxIndex >= 0 ? rxIndex : 0;
    }

    private void WireEvents()
    {
        // Transmitter events
        _txEngine.OnLog += msg =>
        {
            WriteFileLog("tx.log", msg);
            if (IsDisposed || Disposing) return;
            BeginInvoke(() => AppendLog(_rtbTxLog, msg));
        };

        _txEngine.OnStats += stats =>
        {
            if (IsDisposed || Disposing) return;
            BeginInvoke(() =>
            {
                _lblTxStats.Text = $"FPS: {stats.Fps ?? "-"} | Bitrate: {stats.Bitrate ?? "-"} | Time: {stats.Time ?? "-"} | Speed: {stats.Speed ?? "-"}";
            });
        };

        _txEngine.OnStatusChanged += running =>
        {
            if (IsDisposed || Disposing) return;
            BeginInvoke(() =>
            {
                _btnTxStart.Enabled = !running;
                _btnTxStop.Enabled = running;
                _lblTxStatusBadge.Text = running ? "● TRANSMITTING" : "○ IDLE";
                _lblTxStatusBadge.ForeColor = running ? Color.FromArgb(16, 185, 129) : Color.FromArgb(156, 163, 175);
                if (!running)
                {
                    _lblTxStats.Text = "FPS: - | Bitrate: - | Time: - | Speed: -";
                    var old = _picTxPreview.Image;
                    _picTxPreview.Image = null;
                    old?.Dispose();
                }
            });
        };

        _txEngine.OnPreviewFrame += bmp =>
        {
            if (IsDisposed || Disposing)
            {
                bmp.Dispose();
                return;
            }

            BeginInvoke(() =>
            {
                var old = _picTxPreview.Image;
                _picTxPreview.Image = bmp;
                old?.Dispose();
            });
        };

        // Receiver events
        _rxEngine.OnLog += msg =>
        {
            WriteFileLog("rx.log", msg);
            if (IsDisposed || Disposing) return;
            BeginInvoke(() => AppendLog(_rtbRxLog, msg));
        };

        _rxEngine.OnStats += stats =>
        {
            if (IsDisposed || Disposing) return;
            BeginInvoke(() =>
            {
                _lblRxStats.Text = $"FPS: {stats.Fps ?? "-"} | Bitrate: {stats.Bitrate ?? "-"} | Time: {stats.Time ?? "-"} | Speed: {stats.Speed ?? "-"}";
            });
        };

        _rxEngine.OnStatusChanged += running =>
        {
            if (IsDisposed || Disposing) return;
            BeginInvoke(() =>
            {
                _btnRxStart.Enabled = !running;
                _btnRxStop.Enabled = running;
                _lblRxStatusBadge.Text = running ? "● PLAYING (1080i50 SDI)" : "○ IDLE";
                _lblRxStatusBadge.ForeColor = running ? Color.FromArgb(59, 130, 246) : Color.FromArgb(156, 163, 175);
                if (!running)
                {
                    _lblRxStats.Text = "FPS: - | Bitrate: - | Time: - | Speed: -";
                    var old = _picRxPreview.Image;
                    _picRxPreview.Image = null;
                    old?.Dispose();
                }
            });
        };

        _rxEngine.OnPreviewFrame += bmp =>
        {
            if (IsDisposed || Disposing)
            {
                bmp.Dispose();
                return;
            }

            BeginInvoke(() =>
            {
                var old = _picRxPreview.Image;
                _picRxPreview.Image = bmp;
                old?.Dispose();
            });
        };

        _numRxAudioDelay.ValueChanged += (_, _) =>
        {
            _rxEngine.AudioDelayMs = (int)_numRxAudioDelay.Value;
        };

        // Remember selections immediately on change
        _cboTxDevice.SelectedIndexChanged += (_, _) =>
        {
            if (_cboTxDevice.SelectedItem is string name && !name.StartsWith("No DeckLink"))
            {
                _appSettings.TxDevice = name;
                _appSettings.Save();
            }
        };

        _cboRxDevice.SelectedIndexChanged += (_, _) =>
        {
            if (_cboRxDevice.SelectedItem is string name && !name.StartsWith("No DeckLink"))
            {
                _appSettings.RxDevice = name;
                _appSettings.Save();
            }
        };

        _cboTxSource.SelectedIndexChanged += (_, _) =>
        {
            _appSettings.TxSource = _cboTxSource.SelectedItem?.ToString() ?? _appSettings.TxSource;
            _appSettings.Save();
        };

        _cboTxVideoInput.SelectedIndexChanged += (_, _) =>
        {
            _appSettings.TxVideoInput = _cboTxVideoInput.SelectedItem?.ToString() ?? _appSettings.TxVideoInput;
            _appSettings.Save();
        };
    }

    private void WriteFileLog(string filename, string msg)
    {
        try
        {
            lock (_logLock)
            {
                File.AppendAllText(filename, msg);
            }
        }
        catch { }
    }

    private static void AppendLog(RichTextBox rtb, string msg)
    {
        if (rtb.TextLength > 100_000)
        {
            rtb.Select(0, 30_000);
            rtb.SelectedText = "";
        }
        rtb.AppendText(msg);
        rtb.Select(rtb.TextLength, 0);
        rtb.ScrollToCaret();
    }

    private void OnTxStartClicked(object? sender, EventArgs e)
    {
        SourceType source = _cboTxSource.SelectedIndex switch
        {
            0 => SourceType.DeckLink,
            1 => SourceType.File,
            _ => SourceType.ColorBars
        };

        SrtMode mode = _cboTxMode.SelectedIndex == 1 ? SrtMode.Listener : SrtMode.Caller;

        var formatCode = _cboTxFormat.SelectedItem?.ToString()?.Split(' ')[0] ?? "Hi50";

        var settings = new TxSettings(
            SourceType: source,
            DeckLinkDevice: _cboTxDevice.SelectedItem?.ToString() ?? "DeckLink Duo (1)",
            FormatCode: formatCode,
            VideoInput: _cboTxVideoInput.SelectedItem?.ToString() ?? "sdi",
            Encoder: _cboTxEncoder.SelectedIndex == 0 ? "h264_nvenc" : "libx264",
            Bitrate: _txtTxBitrate.Text.Trim(),
            FilePath: _txtTxFilePath.Text.Trim(),
            Loop: _chkTxLoop.Checked,
            Mode: mode,
            Host: _txtTxHost.Text.Trim(),
            Port: (int)_numTxPort.Value,
            LatencyMs: (int)_numTxLatency.Value,
            Passphrase: string.IsNullOrWhiteSpace(_txtTxPassphrase.Text) ? null : _txtTxPassphrase.Text.Trim(),
            StreamId: string.IsNullOrWhiteSpace(_txtTxStreamId.Text) ? null : _txtTxStreamId.Text.Trim()
        );

        _txEngine.Start(settings);
    }

    private void OnTxStopClicked(object? sender, EventArgs e)
    {
        _txEngine.Stop();
    }

    private void OnRxStartClicked(object? sender, EventArgs e)
    {
        SrtMode mode = _cboRxMode.SelectedIndex == 1 ? SrtMode.Caller : SrtMode.Listener;
        var formatCode = _cboRxFormat.SelectedItem?.ToString()?.Split(' ')[0] ?? "Hi50";

        var settings = new RxSettings(
            Mode: mode,
            Host: _txtRxHost.Text.Trim(),
            Port: (int)_numRxPort.Value,
            LatencyMs: (int)_numRxLatency.Value,
            Passphrase: string.IsNullOrWhiteSpace(_txtRxPassphrase.Text) ? null : _txtRxPassphrase.Text.Trim(),
            StreamId: string.IsNullOrWhiteSpace(_txtRxStreamId.Text) ? null : _txtRxStreamId.Text.Trim(),
            EnableDeckLinkPlayout: _chkRxEnableDeckLink.Checked,
            DeckLinkDevice: _cboRxDevice.SelectedItem?.ToString() ?? "DeckLink Duo (1)",
            FormatCode: formatCode,
            AudioDelayMs: (int)_numRxAudioDelay.Value
        );

        _rxEngine.Start(settings);
    }

    private void OnRxStopClicked(object? sender, EventArgs e)
    {
        _rxEngine.Stop();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveUiToSettings();
        _txEngine.Dispose();
        _rxEngine.Dispose();
        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_chkShowLogs != null && _chkShowLogs.Checked && Height >= 480)
        {
            _heightWithLogs = Height;
        }
    }

    private void UpdateLogVisibility(bool show)
    {
        if (_pnlTxLogContainer == null || _pnlRxLogContainer == null) return;

        _pnlTxLogContainer.Visible = show;
        _pnlRxLogContainer.Visible = show;

        if (show)
        {
            MinimumSize = new Size(960, 480);
            Height = Math.Max(Height, _heightWithLogs);
        }
        else
        {
            if (Height > 360)
            {
                _heightWithLogs = Height;
            }
            MinimumSize = new Size(960, 305);
            Height = 310;
        }
    }

    private void ApplyTheme(string theme)
    {
        bool isLight = theme.Equals("Light", StringComparison.OrdinalIgnoreCase);
        bool isMidnight = theme.Equals("Midnight", StringComparison.OrdinalIgnoreCase);

        Color formBg = isLight ? Color.FromArgb(238, 242, 246) : (isMidnight ? Color.FromArgb(8, 9, 11) : Color.FromArgb(20, 22, 26));
        Color formFg = isLight ? Color.FromArgb(17, 24, 39) : (isMidnight ? Color.FromArgb(245, 245, 245) : Color.FromArgb(240, 243, 246));
        Color panelBg = isLight ? Color.FromArgb(255, 255, 255) : (isMidnight ? Color.FromArgb(15, 16, 20) : Color.FromArgb(28, 31, 38));
        Color inputBg = isLight ? Color.FromArgb(243, 244, 246) : (isMidnight ? Color.FromArgb(24, 25, 30) : Color.FromArgb(40, 44, 52));
        Color inputFg = isLight ? Color.FromArgb(17, 24, 39) : Color.White;
        Color btnSecBg = isLight ? Color.FromArgb(229, 231, 235) : (isMidnight ? Color.FromArgb(32, 34, 40) : Color.FromArgb(44, 49, 60));
        Color btnSecFg = isLight ? Color.FromArgb(17, 24, 39) : Color.White;
        Color mutedLabel = isLight ? Color.FromArgb(75, 85, 99) : (isMidnight ? Color.FromArgb(140, 145, 155) : Color.FromArgb(156, 163, 175));
        Color logBg = isLight ? Color.FromArgb(249, 250, 251) : (isMidnight ? Color.FromArgb(5, 5, 7) : Color.FromArgb(15, 17, 21));
        Color txLogFg = isLight ? Color.FromArgb(6, 95, 70) : (isMidnight ? Color.FromArgb(110, 231, 183) : Color.FromArgb(167, 243, 208));
        Color rxLogFg = isLight ? Color.FromArgb(30, 64, 175) : (isMidnight ? Color.FromArgb(147, 197, 253) : Color.FromArgb(186, 230, 253));

        BackColor = formBg;
        ForeColor = formFg;
        _pnlHeader.BackColor = panelBg;
        _lblHeaderTitle.ForeColor = isLight ? Color.FromArgb(17, 24, 39) : Color.White;
        _lblHeaderSubtitle.ForeColor = mutedLabel;
        _lblFfmpegStatus.ForeColor = mutedLabel;
        _btnRefreshCards.BackColor = btnSecBg;
        _btnRefreshCards.ForeColor = btnSecFg;
        _chkShowLogs.ForeColor = mutedLabel;
        _lblTheme.ForeColor = mutedLabel;
        _cboTheme.BackColor = inputBg;
        _cboTheme.ForeColor = inputFg;

        _tblMain.BackColor = formBg;
        _pnlTx.BackColor = panelBg;
        _pnlRx.BackColor = panelBg;

        _pnlTxSettings.BackColor = panelBg;
        _pnlRxSettings.BackColor = panelBg;
        _pnlTxControls.BackColor = panelBg;
        _pnlRxControls.BackColor = panelBg;

        _lblTxTitle.ForeColor = isLight ? Color.FromArgb(5, 150, 105) : Color.FromArgb(52, 211, 153);
        _lblRxTitle.ForeColor = isLight ? Color.FromArgb(37, 99, 235) : Color.FromArgb(96, 165, 250);

        UpdateControlColors(this, panelBg, inputBg, inputFg, btnSecBg, btnSecFg, mutedLabel);

        _picTxPreview.BackColor = Color.Black;
        _picRxPreview.BackColor = Color.Black;
        _rtbTxLog.BackColor = logBg;
        _rtbTxLog.ForeColor = txLogFg;
        _rtbRxLog.BackColor = logBg;
        _rtbRxLog.ForeColor = rxLogFg;
        _btnTxStart.BackColor = Color.FromArgb(16, 185, 129);
        _btnTxStart.ForeColor = Color.White;
        _btnTxStop.BackColor = Color.FromArgb(239, 68, 68);
        _btnTxStop.ForeColor = Color.White;
        _btnRxStart.BackColor = Color.FromArgb(59, 130, 246);
        _btnRxStart.ForeColor = Color.White;
        _btnRxStop.BackColor = Color.FromArgb(239, 68, 68);
        _btnRxStop.ForeColor = Color.White;
        _lblTxStats.ForeColor = isLight ? Color.FromArgb(5, 150, 105) : Color.FromArgb(110, 231, 183);
        _lblRxStats.ForeColor = isLight ? Color.FromArgb(37, 99, 235) : Color.FromArgb(147, 197, 253);
        _chkRxEnableDeckLink.ForeColor = isLight ? Color.FromArgb(30, 64, 175) : Color.FromArgb(147, 197, 253);
        _chkTxLoop.ForeColor = mutedLabel;
    }

    private static void UpdateControlColors(Control parent, Color panelBg, Color inputBg, Color inputFg, Color btnBg, Color btnFg, Color labelFg)
    {
        foreach (Control c in parent.Controls)
        {
            if (c is TextBox or NumericUpDown or ComboBox)
            {
                c.BackColor = inputBg;
                c.ForeColor = inputFg;
                if (c is ComboBox cb)
                {
                    cb.FlatStyle = FlatStyle.Flat;
                }
            }
            else if (c is Button b)
            {
                if (b.Text.Contains("START") || b.Text.Contains("STOP")) continue;
                b.BackColor = btnBg;
                b.ForeColor = btnFg;
            }
            else if (c is CheckBox chk)
            {
                if (chk.Text.Contains("DeckLink")) continue;
                chk.ForeColor = labelFg;
            }
            else if (c is Label lbl)
            {
                if (!lbl.Text.Contains("SRT") && !lbl.Text.Contains("FPS") && !lbl.Text.Contains("IDLE") && !lbl.Text.Contains("●") && !lbl.Text.Contains("○"))
                {
                    lbl.ForeColor = labelFg;
                }
            }

            if (c.HasChildren && c is not RichTextBox)
            {
                UpdateControlColors(c, panelBg, inputBg, inputFg, btnBg, btnFg, labelFg);
            }
        }
    }

    #region GUI Layout Builder
    private void InitializeComponentCustom()
    {
        Text = "SRT Broadcast Suite — Native Blackmagic SDI Playout & NVENC Streaming";
        Size = new Size(1220, 310);
        MinimumSize = new Size(960, 305);
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(20, 22, 26);
        ForeColor = Color.FromArgb(240, 243, 246);
        Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        StartPosition = FormStartPosition.CenterScreen;

        // Header Panel (Height 38px)
        _pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 38,
            BackColor = Color.FromArgb(28, 31, 38),
            Padding = new Padding(10, 5, 10, 5)
        };

        var flowHeaderLeft = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        _lblHeaderTitle = new Label
        {
            Text = "SRT BROADCAST SUITE",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Margin = new Padding(0, 3, 6, 0)
        };

        _lblHeaderSubtitle = new Label
        {
            Text = "• Blackmagic SDI 1080i50 & NVENC",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(156, 163, 175),
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 0)
        };

        flowHeaderLeft.Controls.AddRange(new Control[] { _lblHeaderTitle, _lblHeaderSubtitle });

        var flowHeaderRight = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        _btnRefreshCards = new Button
        {
            Text = "🔄 Refresh Cards",
            Size = new Size(120, 24),
            BackColor = Color.FromArgb(44, 49, 60),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8f),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Margin = new Padding(6, 1, 0, 0)
        };
        _btnRefreshCards.FlatAppearance.BorderSize = 0;
        _btnRefreshCards.Click += (_, _) => PopulateDeckLinkDevices();

        _chkShowLogs = new CheckBox
        {
            Text = "Show Logs",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(209, 213, 219),
            Checked = false,
            AutoSize = true,
            Cursor = Cursors.Hand,
            Margin = new Padding(8, 3, 4, 0)
        };
        _chkShowLogs.CheckedChanged += (_, _) =>
        {
            UpdateLogVisibility(_chkShowLogs.Checked);
            SaveUiToSettings();
        };

        _cboTheme = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(88, 24),
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8f),
            Margin = new Padding(4, 1, 6, 0)
        };
        _cboTheme.Items.AddRange(new object[] { "Dark", "Midnight", "Light" });
        _cboTheme.SelectedIndex = 0;
        _cboTheme.SelectedIndexChanged += (_, _) =>
        {
            var selectedTheme = _cboTheme.SelectedItem?.ToString() ?? "Dark";
            _appSettings.Theme = selectedTheme;
            _appSettings.Save();
            ApplyTheme(selectedTheme);
        };

        _lblTheme = new Label
        {
            Text = "Theme:",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(156, 163, 175),
            AutoSize = true,
            Margin = new Padding(0, 5, 2, 0)
        };

        _lblFfmpegStatus = new Label
        {
            Text = "FFmpeg: Initializing...",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(107, 114, 128),
            AutoSize = true,
            Margin = new Padding(0, 5, 6, 0)
        };

        flowHeaderRight.Controls.AddRange(new Control[] { _btnRefreshCards, _chkShowLogs, _cboTheme, _lblTheme, _lblFfmpegStatus });

        _pnlHeader.Controls.AddRange(new Control[] { flowHeaderLeft, flowHeaderRight });

        // Main 2-Column Split
        _tblMain = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(6, 4, 6, 6),
            BackColor = Color.FromArgb(20, 22, 26)
        };
        _tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var pnlTx = BuildTransmitterPanel();
        var pnlRx = BuildReceiverPanel();

        _tblMain.Controls.Add(pnlTx, 0, 0);
        _tblMain.Controls.Add(pnlRx, 1, 0);

        Controls.Add(_pnlHeader);
        Controls.Add(_tblMain);
        _pnlHeader.BringToFront();
        _tblMain.SendToBack();
    }

    private Panel BuildTransmitterPanel()
    {
        _pnlTx = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(28, 31, 38),
            Padding = new Padding(6, 4, 6, 4),
            Margin = new Padding(3)
        };

        // Top Fixed Settings Area (Height 212px)
        _pnlTxSettings = new Panel
        {
            Dock = DockStyle.Top,
            Height = 212,
            BackColor = Color.FromArgb(28, 31, 38)
        };

        // Title Row
        var pnlTitle = new Panel
        {
            Dock = DockStyle.Top,
            Height = 22,
            BackColor = Color.Transparent
        };
        _lblTxTitle = new Label
        {
            Text = "📡 SRT TRANSMITTER (TX)",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(52, 211, 153),
            AutoSize = true,
            Location = new Point(0, 2)
        };
        _lblTxStatusBadge = new Label
        {
            Text = "○ IDLE",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 163, 175),
            AutoSize = true,
            Location = new Point(190, 4)
        };
        pnlTitle.Controls.AddRange(new Control[] { _lblTxTitle, _lblTxStatusBadge });
        _pnlTxSettings.Controls.Add(pnlTitle);

        // Body area below title
        var pnlBody = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };

        // Left Side: TX Controls Container (Width 354)
        _pnlTxControls = new Panel
        {
            Dock = DockStyle.Left,
            Width = 354,
            BackColor = Color.Transparent
        };

        int y = 0;
        // Row 1: Source & Video Standard
        _pnlTxControls.Controls.Add(CreateFieldLabel("Source:", 0, y + 2));
        _cboTxSource = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(72, y),
            Width = 118,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboTxSource.Items.AddRange(new object[] { "DeckLink SDI Input", "Video File", "SMPTE Color Bars (Test)" });
        _cboTxSource.SelectedIndex = 0;
        _pnlTxControls.Controls.Add(_cboTxSource);

        _pnlTxControls.Controls.Add(CreateFieldLabel("Standard:", 196, y + 2));
        _cboTxFormat = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(256, y),
            Width = 94,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboTxFormat.Items.AddRange(new object[]
        {
            "Hi50 (1080i50 - Default)",
            "Hp50 (1080p50)",
            "Hp25 (1080p25)",
            "Hi59 (1080i59.94)",
            "Hp59 (1080p59.94)",
            "hp50 (720p50)",
            "pal (576i50)"
        });
        _cboTxFormat.SelectedIndex = 0;
        _pnlTxControls.Controls.Add(_cboTxFormat);
        y += 25;

        // Row 2: DeckLink Card & Input Port
        _pnlTxControls.Controls.Add(CreateFieldLabel("Card:", 0, y + 2));
        _cboTxDevice = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(72, y),
            Width = 118,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlTxControls.Controls.Add(_cboTxDevice);

        _pnlTxControls.Controls.Add(CreateFieldLabel("Input:", 196, y + 2));
        _cboTxVideoInput = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(256, y),
            Width = 94,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboTxVideoInput.Items.AddRange(new object[] { "sdi", "hdmi", "optical_sdi", "component", "composite" });
        _cboTxVideoInput.SelectedIndex = 0;
        _pnlTxControls.Controls.Add(_cboTxVideoInput);
        y += 25;

        // Row 3: Video File & Browse & Loop
        _pnlTxControls.Controls.Add(CreateFieldLabel("Video File:", 0, y + 2));
        _txtTxFilePath = new TextBox
        {
            Location = new Point(72, y),
            Width = 142,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White,
            Text = @"sample_video.mp4"
        };
        _btnTxBrowse = new Button
        {
            Text = "Browse...",
            Location = new Point(218, y - 1),
            Width = 55,
            Height = 23,
            BackColor = Color.FromArgb(44, 49, 60),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8f),
            FlatStyle = FlatStyle.Flat
        };
        _btnTxBrowse.FlatAppearance.BorderSize = 0;
        _btnTxBrowse.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = "Video Files|*.mp4;*.mkv;*.mov;*.ts;*.avi|All Files|*.*" };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                _txtTxFilePath.Text = ofd.FileName;
            }
        };

        _chkTxLoop = new CheckBox
        {
            Text = "Loop",
            Location = new Point(278, y + 1),
            Width = 65,
            AutoSize = true,
            ForeColor = Color.FromArgb(209, 213, 219),
            Checked = true
        };
        _pnlTxControls.Controls.AddRange(new Control[] { _txtTxFilePath, _btnTxBrowse, _chkTxLoop });
        y += 25;

        // Row 4: Encoder & Bitrate
        _pnlTxControls.Controls.Add(CreateFieldLabel("Encoder:", 0, y + 2));
        _cboTxEncoder = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(72, y),
            Width = 118,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboTxEncoder.Items.AddRange(new object[] { "h264_nvenc (NVIDIA GPU)", "libx264 (CPU)" });
        _cboTxEncoder.SelectedIndex = 0;
        _pnlTxControls.Controls.Add(_cboTxEncoder);

        _pnlTxControls.Controls.Add(CreateFieldLabel("Bitrate:", 196, y + 2));
        _txtTxBitrate = new TextBox
        {
            Location = new Point(256, y),
            Width = 94,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White,
            Text = "6000k"
        };
        _pnlTxControls.Controls.Add(_txtTxBitrate);
        y += 25;

        // Row 5: SRT Connection Mode & Host/Port
        _pnlTxControls.Controls.Add(CreateFieldLabel("SRT Mode:", 0, y + 2));
        _cboTxMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(72, y),
            Width = 118,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboTxMode.Items.AddRange(new object[] { "Caller (Send to remote)", "Listener (Wait for RX)" });
        _cboTxMode.SelectedIndex = 0;
        _pnlTxControls.Controls.Add(_cboTxMode);

        _pnlTxControls.Controls.Add(CreateFieldLabel("Host:Port:", 196, y + 2));
        _txtTxHost = new TextBox
        {
            Location = new Point(256, y),
            Width = 52,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White,
            Text = "127.0.0.1"
        };
        _numTxPort = new NumericUpDown
        {
            Location = new Point(310, y),
            Width = 40,
            Minimum = 1024,
            Maximum = 65535,
            Value = 9998,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlTxControls.Controls.AddRange(new Control[] { _txtTxHost, _numTxPort });
        y += 25;

        // Row 6: Latency & Key & StreamID
        _pnlTxControls.Controls.Add(CreateFieldLabel("Latency:", 0, y + 2));
        _numTxLatency = new NumericUpDown
        {
            Location = new Point(50, y),
            Width = 46,
            Minimum = 20,
            Maximum = 5000,
            Value = 120,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlTxControls.Controls.Add(_numTxLatency);

        _pnlTxControls.Controls.Add(CreateFieldLabel("Key:", 100, y + 2));
        _txtTxPassphrase = new TextBox
        {
            Location = new Point(128, y),
            Width = 62,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlTxControls.Controls.Add(_txtTxPassphrase);

        _pnlTxControls.Controls.Add(CreateFieldLabel("Stream ID:", 194, y + 2));
        _txtTxStreamId = new TextBox
        {
            Location = new Point(256, y),
            Width = 94,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlTxControls.Controls.Add(_txtTxStreamId);
        y += 28;

        // Row 7: Action Buttons & Stats
        _btnTxStart = new Button
        {
            Text = "▶ START TX",
            Location = new Point(0, y),
            Width = 88,
            Height = 25,
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnTxStart.FlatAppearance.BorderSize = 0;
        _btnTxStart.Click += OnTxStartClicked;

        _btnTxStop = new Button
        {
            Text = "⏹ STOP",
            Location = new Point(92, y),
            Width = 50,
            Height = 25,
            BackColor = Color.FromArgb(239, 68, 68),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Enabled = false,
            Cursor = Cursors.Hand
        };
        _btnTxStop.FlatAppearance.BorderSize = 0;
        _btnTxStop.Click += OnTxStopClicked;

        _lblTxStats = new Label
        {
            Text = "FPS: - | Bitrate: - | Time: - | Speed: -",
            Font = new Font("Consolas", 7.5f),
            ForeColor = Color.FromArgb(110, 231, 183),
            AutoSize = true,
            Location = new Point(148, y + 5)
        };

        _pnlTxControls.Controls.AddRange(new Control[] { _btnTxStart, _btnTxStop, _lblTxStats });

        // Right Side: TX Preview Monitor (Input Video to right of its settings)
        _picTxPreview = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(6, 0, 2, 0)
        };

        pnlBody.Controls.Add(_pnlTxControls);
        pnlBody.Controls.Add(_picTxPreview);
        _pnlTxControls.BringToFront();
        _picTxPreview.SendToBack();

        _pnlTxSettings.Controls.Add(pnlTitle);
        _pnlTxSettings.Controls.Add(pnlBody);
        pnlTitle.BringToFront();
        pnlBody.SendToBack();

        // Log Console Area - Container docked Fill
        _pnlTxLogContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(28, 31, 38),
            Padding = new Padding(0, 4, 0, 0)
        };

        var pnlLogHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 22,
            BackColor = Color.FromArgb(28, 31, 38),
            Padding = new Padding(2, 0, 2, 0)
        };
        var lblLogTitle = new Label
        {
            Text = "Transmitter Log Output:",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 163, 175),
            Dock = DockStyle.Left,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var flowLogBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        _btnTxClearLog = new Button
        {
            Text = "🗑 Clear",
            Size = new Size(50, 20),
            BackColor = Color.FromArgb(44, 49, 60),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 7.5f),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(2, 0, 0, 0)
        };
        _btnTxClearLog.FlatAppearance.BorderSize = 0;
        _btnTxClearLog.Click += (_, _) => _rtbTxLog.Clear();

        _btnTxCopyLog = new Button
        {
            Text = "📋 Copy",
            Size = new Size(55, 20),
            BackColor = Color.FromArgb(44, 49, 60),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 7.5f),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(2, 0, 0, 0)
        };
        _btnTxCopyLog.FlatAppearance.BorderSize = 0;
        _btnTxCopyLog.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_rtbTxLog.Text))
            {
                Clipboard.SetText(_rtbTxLog.Text);
                MessageBox.Show(this, "Transmitter log copied to clipboard!", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

        flowLogBtns.Controls.AddRange(new Control[] { _btnTxClearLog, _btnTxCopyLog });
        pnlLogHeader.Controls.AddRange(new Control[] { lblLogTitle, flowLogBtns });
        _pnlTxLogContainer.Controls.Add(pnlLogHeader);

        _rtbTxLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 17, 21),
            ForeColor = Color.FromArgb(167, 243, 208),
            Font = new Font("Consolas", 8f),
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Margin = new Padding(0, 2, 0, 0)
        };
        _pnlTxLogContainer.Controls.Add(_rtbTxLog);

        pnlLogHeader.BringToFront();
        _rtbTxLog.BringToFront();

        _pnlTx.Controls.Add(_pnlTxSettings);
        _pnlTx.Controls.Add(_pnlTxLogContainer);
        _pnlTxSettings.BringToFront();
        _pnlTxLogContainer.SendToBack();

        return _pnlTx;
    }

    private Panel BuildReceiverPanel()
    {
        _pnlRx = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(28, 31, 38),
            Padding = new Padding(6, 4, 6, 4),
            Margin = new Padding(3)
        };

        // Top Fixed Settings Area (Height 212px)
        _pnlRxSettings = new Panel
        {
            Dock = DockStyle.Top,
            Height = 212,
            BackColor = Color.FromArgb(28, 31, 38)
        };

        // Title Row
        var pnlTitle = new Panel
        {
            Dock = DockStyle.Top,
            Height = 22,
            BackColor = Color.Transparent
        };
        _lblRxTitle = new Label
        {
            Text = "📺 SRT RECEIVER & SDI PLAYOUT (RX)",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(96, 165, 250),
            AutoSize = true,
            Location = new Point(0, 2)
        };
        _lblRxStatusBadge = new Label
        {
            Text = "○ IDLE",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 163, 175),
            AutoSize = true,
            Location = new Point(255, 4)
        };
        pnlTitle.Controls.AddRange(new Control[] { _lblRxTitle, _lblRxStatusBadge });
        _pnlRxSettings.Controls.Add(pnlTitle);

        // Body area below title
        var pnlBody = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };

        // Right Side: RX Controls Container (Width 354)
        _pnlRxControls = new Panel
        {
            Dock = DockStyle.Right,
            Width = 354,
            BackColor = Color.Transparent
        };

        int y = 0;
        // Row 1: DeckLink SDI Playout Settings & Audio Delay
        _chkRxEnableDeckLink = new CheckBox
        {
            Text = "DeckLink SDI Playout",
            Location = new Point(0, y + 1),
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(147, 197, 253),
            Checked = true
        };
        _pnlRxControls.Controls.Add(_chkRxEnableDeckLink);

        _pnlRxControls.Controls.Add(CreateFieldLabel("Audio Sync:", 186, y + 2));
        _numRxAudioDelay = new NumericUpDown
        {
            Location = new Point(256, y),
            Width = 60,
            Minimum = -1000,
            Maximum = 1000,
            Value = 0,
            Increment = 10,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlRxControls.Controls.Add(_numRxAudioDelay);
        _pnlRxControls.Controls.Add(CreateFieldLabel("ms", 320, y + 2));
        y += 25;

        // Row 2: DeckLink Card & SDI Standard
        _pnlRxControls.Controls.Add(CreateFieldLabel("Card:", 0, y + 2));
        _cboRxDevice = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(72, y),
            Width = 118,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlRxControls.Controls.Add(_cboRxDevice);

        _pnlRxControls.Controls.Add(CreateFieldLabel("Standard:", 196, y + 2));
        _cboRxFormat = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(256, y),
            Width = 94,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboRxFormat.Items.AddRange(new object[]
        {
            "Hi50 (1080i50 - Default)",
            "Hp50 (1080p50)",
            "Hp25 (1080p25)",
            "Hi59 (1080i59.94)",
            "Hp59 (1080p59.94)",
            "hp50 (720p50)",
            "pal (576i50)"
        });
        _cboRxFormat.SelectedIndex = 0;
        _pnlRxControls.Controls.Add(_cboRxFormat);
        y += 25;

        // Row 3: SRT Connection Mode & Host/Port
        _pnlRxControls.Controls.Add(CreateFieldLabel("SRT Mode:", 0, y + 2));
        _cboRxMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(72, y),
            Width = 118,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboRxMode.Items.AddRange(new object[] { "Listener (Listen for incoming)", "Caller (Connect to remote)" });
        _cboRxMode.SelectedIndex = 0;
        _pnlRxControls.Controls.Add(_cboRxMode);

        _pnlRxControls.Controls.Add(CreateFieldLabel("Host:Port:", 196, y + 2));
        _txtRxHost = new TextBox
        {
            Location = new Point(256, y),
            Width = 52,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White,
            Text = "0.0.0.0"
        };
        _numRxPort = new NumericUpDown
        {
            Location = new Point(310, y),
            Width = 40,
            Minimum = 1024,
            Maximum = 65535,
            Value = 9998,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlRxControls.Controls.AddRange(new Control[] { _txtRxHost, _numRxPort });
        y += 25;

        // Row 4: Latency & Key & StreamID
        _pnlRxControls.Controls.Add(CreateFieldLabel("Latency:", 0, y + 2));
        _numRxLatency = new NumericUpDown
        {
            Location = new Point(50, y),
            Width = 46,
            Minimum = 20,
            Maximum = 5000,
            Value = 120,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlRxControls.Controls.Add(_numRxLatency);

        _pnlRxControls.Controls.Add(CreateFieldLabel("Key:", 100, y + 2));
        _txtRxPassphrase = new TextBox
        {
            Location = new Point(128, y),
            Width = 62,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlRxControls.Controls.Add(_txtRxPassphrase);

        _pnlRxControls.Controls.Add(CreateFieldLabel("Stream ID:", 194, y + 2));
        _txtRxStreamId = new TextBox
        {
            Location = new Point(256, y),
            Width = 94,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlRxControls.Controls.Add(_txtRxStreamId);
        y = 154; // Aligned horizontally with TX Action row

        // Row 5: Action Buttons & Stats
        _btnRxStart = new Button
        {
            Text = "▶ START RX",
            Location = new Point(0, y),
            Width = 88,
            Height = 25,
            BackColor = Color.FromArgb(59, 130, 246),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnRxStart.FlatAppearance.BorderSize = 0;
        _btnRxStart.Click += OnRxStartClicked;

        _btnRxStop = new Button
        {
            Text = "⏹ STOP",
            Location = new Point(92, y),
            Width = 50,
            Height = 25,
            BackColor = Color.FromArgb(239, 68, 68),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Enabled = false,
            Cursor = Cursors.Hand
        };
        _btnRxStop.FlatAppearance.BorderSize = 0;
        _btnRxStop.Click += OnRxStopClicked;

        _lblRxStats = new Label
        {
            Text = "FPS: - | Bitrate: - | Time: - | Speed: -",
            Font = new Font("Consolas", 7.5f),
            ForeColor = Color.FromArgb(147, 197, 253),
            AutoSize = true,
            Location = new Point(148, y + 5)
        };

        _pnlRxControls.Controls.AddRange(new Control[] { _btnRxStart, _btnRxStop, _lblRxStats });

        // Left Side: Output Video Preview (Fills remainder)
        _picRxPreview = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(2, 0, 6, 0)
        };

        pnlBody.Controls.Add(_pnlRxControls);
        pnlBody.Controls.Add(_picRxPreview);
        _pnlRxControls.BringToFront();
        _picRxPreview.SendToBack();

        _pnlRxSettings.Controls.Add(pnlTitle);
        _pnlRxSettings.Controls.Add(pnlBody);
        pnlTitle.BringToFront();
        pnlBody.SendToBack();

        // Log Console Area - Container docked Fill
        _pnlRxLogContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(28, 31, 38),
            Padding = new Padding(0, 4, 0, 0)
        };

        var pnlLogHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 22,
            BackColor = Color.FromArgb(28, 31, 38),
            Padding = new Padding(2, 0, 2, 0)
        };
        var lblLogTitle = new Label
        {
            Text = "Receiver Log Output:",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 163, 175),
            Dock = DockStyle.Left,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var flowLogBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        _btnRxClearLog = new Button
        {
            Text = "🗑 Clear",
            Size = new Size(50, 20),
            BackColor = Color.FromArgb(44, 49, 60),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 7.5f),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(2, 0, 0, 0)
        };
        _btnRxClearLog.FlatAppearance.BorderSize = 0;
        _btnRxClearLog.Click += (_, _) => _rtbRxLog.Clear();

        _btnRxCopyLog = new Button
        {
            Text = "📋 Copy",
            Size = new Size(55, 20),
            BackColor = Color.FromArgb(44, 49, 60),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 7.5f),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(2, 0, 0, 0)
        };
        _btnRxCopyLog.FlatAppearance.BorderSize = 0;
        _btnRxCopyLog.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_rtbRxLog.Text))
            {
                Clipboard.SetText(_rtbRxLog.Text);
                MessageBox.Show(this, "Receiver log copied to clipboard!", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

        flowLogBtns.Controls.AddRange(new Control[] { _btnRxClearLog, _btnRxCopyLog });
        pnlLogHeader.Controls.AddRange(new Control[] { lblLogTitle, flowLogBtns });
        _pnlRxLogContainer.Controls.Add(pnlLogHeader);

        _rtbRxLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 17, 21),
            ForeColor = Color.FromArgb(186, 230, 253),
            Font = new Font("Consolas", 8f),
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Margin = new Padding(0, 2, 0, 0)
        };
        _pnlRxLogContainer.Controls.Add(_rtbRxLog);

        pnlLogHeader.BringToFront();
        _rtbRxLog.BringToFront();

        _pnlRx.Controls.Add(_pnlRxSettings);
        _pnlRx.Controls.Add(_pnlRxLogContainer);
        _pnlRxSettings.BringToFront();
        _pnlRxLogContainer.SendToBack();

        return _pnlRx;
    }

    private static Label CreateFieldLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(156, 163, 175)
        };
    }
    #endregion
}
