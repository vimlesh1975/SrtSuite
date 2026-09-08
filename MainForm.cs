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
    private CheckBox _chkDarkMode = null!;
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

    private int _heightWithLogs = 720;
    private readonly object _logLock = new();

    public MainForm()
    {
        _appSettings = AppSettings.Load();
        InitializeComponentCustom();

        _ffmpegPath = ResolveFfmpegPath();
        bool ffmpegFound = File.Exists(_ffmpegPath);
        _lblFfmpegStatus.Text = ffmpegFound ? "FFmpeg: Ready" : "WARNING: FFmpeg binary not found!";
        _lblFfmpegStatus.ForeColor = ffmpegFound ? Color.FromArgb(52, 211, 153) : Color.FromArgb(239, 68, 68);
        var tip = new ToolTip();
        tip.SetToolTip(_lblFfmpegStatus, _ffmpegPath);

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

        // Theme (Dark Mode CheckBox)
        bool isDark = !_appSettings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase);
        _chkDarkMode.Checked = isDark;
        ApplyTheme(isDark ? "Dark" : "Light");
    }

    private static void SelectComboItem(ComboBox cbo, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || cbo.Items.Count == 0) return;
        for (int i = 0; i < cbo.Items.Count; i++)
        {
            var item = cbo.Items[i]?.ToString();
            if (item == null) continue;
            if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase) ||
                item.StartsWith(value, StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith(item, StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith(item.Split(' ')[0], StringComparison.OrdinalIgnoreCase) ||
                item.StartsWith(value.Split(' ')[0], StringComparison.OrdinalIgnoreCase))
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
            _appSettings.Theme = _chkDarkMode.Checked ? "Dark" : "Light";

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
                _lblTxStats.Text = $"FPS: {stats.Fps ?? "-"} | {stats.Bitrate ?? "-"} | {stats.Time ?? "-"}";
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
                    _lblTxStats.Text = "FPS: - | Bitrate: - | Time: -";
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
                if (!string.IsNullOrEmpty(stats.Speed) && stats.Speed.Contains("Listening", StringComparison.OrdinalIgnoreCase))
                {
                    _lblRxStatusBadge.Text = "● LISTENING (AWAITING CALLER)";
                    _lblRxStatusBadge.ForeColor = Color.FromArgb(245, 158, 11);
                    _lblRxStats.Text = "FPS: - | Bitrate: - | Time: - (Awaiting caller)";
                }
                else if (!string.IsNullOrEmpty(stats.Speed) && stats.Speed.Contains("Reconnecting", StringComparison.OrdinalIgnoreCase))
                {
                    _lblRxStatusBadge.Text = "● RECONNECTING...";
                    _lblRxStatusBadge.ForeColor = Color.FromArgb(245, 158, 11);
                    _lblRxStats.Text = "FPS: - | Bitrate: - | Time: - (Reconnecting...)";
                }
                else
                {
                    _lblRxStats.Text = $"FPS: {stats.Fps ?? "-"} | {stats.Bitrate ?? "-"} | {stats.Time ?? "-"}";
                }
            });
        };

        _rxEngine.OnStatusChanged += running =>
        {
            if (IsDisposed || Disposing) return;
            BeginInvoke(() =>
            {
                _btnRxStart.Enabled = !running;
                _btnRxStop.Enabled = running;
                if (running)
                {
                    bool isListener = _cboRxMode.SelectedIndex == 0;
                    _lblRxStatusBadge.Text = isListener ? "● LISTENING (AWAITING CALLER)" : "● CONNECTING...";
                    _lblRxStatusBadge.ForeColor = Color.FromArgb(245, 158, 11);
                }
                else
                {
                    _lblRxStatusBadge.Text = "○ IDLE";
                    _lblRxStatusBadge.ForeColor = Color.FromArgb(156, 163, 175);
                    _lblRxStats.Text = "FPS: - | Bitrate: - | Time: -";
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
                if (_lblRxStatusBadge.Text != "● PLAYING (SDI)")
                {
                    _lblRxStatusBadge.Text = "● PLAYING (SDI)";
                    _lblRxStatusBadge.ForeColor = Color.FromArgb(59, 130, 246);
                }
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
        if (_chkShowLogs != null && _chkShowLogs.Checked && Height >= 680)
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
            _pnlTxSettings.Dock = DockStyle.Top;
            _pnlTxSettings.Height = 432;
            _pnlRxSettings.Dock = DockStyle.Top;
            _pnlRxSettings.Height = 432;

            MinimumSize = new Size(800, 680);
            Height = Math.Max(_heightWithLogs, 720);
        }
        else
        {
            if (Height >= 680)
            {
                _heightWithLogs = Height;
            }

            _pnlTxSettings.Dock = DockStyle.Fill;
            _pnlRxSettings.Dock = DockStyle.Fill;

            MinimumSize = new Size(800, 560);
            ClientSize = new Size(ClientSize.Width, 524);
        }
    }

    private void ApplyTheme(string theme)
    {
        bool isLight = theme.Equals("Light", StringComparison.OrdinalIgnoreCase);

        Color formBg = isLight ? Color.FromArgb(238, 242, 246) : Color.FromArgb(20, 22, 26);
        Color formFg = isLight ? Color.FromArgb(17, 24, 39) : Color.FromArgb(240, 243, 246);
        Color panelBg = isLight ? Color.FromArgb(255, 255, 255) : Color.FromArgb(28, 31, 38);
        Color inputBg = isLight ? Color.FromArgb(243, 244, 246) : Color.FromArgb(40, 44, 52);
        Color inputFg = isLight ? Color.FromArgb(17, 24, 39) : Color.White;
        Color btnSecBg = isLight ? Color.FromArgb(229, 231, 235) : Color.FromArgb(44, 49, 60);
        Color btnSecFg = isLight ? Color.FromArgb(17, 24, 39) : Color.White;
        Color mutedLabel = isLight ? Color.FromArgb(75, 85, 99) : Color.FromArgb(156, 163, 175);
        Color logBg = isLight ? Color.FromArgb(249, 250, 251) : Color.FromArgb(15, 17, 21);
        Color txLogFg = isLight ? Color.FromArgb(6, 95, 70) : Color.FromArgb(167, 243, 208);
        Color rxLogFg = isLight ? Color.FromArgb(30, 64, 175) : Color.FromArgb(186, 230, 253);

        BackColor = formBg;
        ForeColor = formFg;
        _pnlHeader.BackColor = panelBg;
        _lblHeaderTitle.ForeColor = isLight ? Color.FromArgb(17, 24, 39) : Color.White;
        _lblHeaderSubtitle.ForeColor = mutedLabel;
        _lblFfmpegStatus.ForeColor = mutedLabel;
        _btnRefreshCards.BackColor = btnSecBg;
        _btnRefreshCards.ForeColor = btnSecFg;
        _chkShowLogs.ForeColor = mutedLabel;
        _chkDarkMode.ForeColor = mutedLabel;

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
        ClientSize = new Size(840, 524);
        MinimumSize = new Size(800, 560);
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(20, 22, 26);
        ForeColor = Color.FromArgb(240, 243, 246);
        Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        StartPosition = FormStartPosition.CenterScreen;

        // Header Panel (Height 36px)
        _pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Color.FromArgb(28, 31, 38),
            Padding = new Padding(10, 4, 10, 4)
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

        _chkDarkMode = new CheckBox
        {
            Text = "Dark Mode",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(209, 213, 219),
            Checked = true,
            AutoSize = true,
            Cursor = Cursors.Hand,
            Margin = new Padding(8, 3, 4, 0)
        };
        _chkDarkMode.CheckedChanged += (_, _) =>
        {
            var selectedTheme = _chkDarkMode.Checked ? "Dark" : "Light";
            _appSettings.Theme = selectedTheme;
            _appSettings.Save();
            ApplyTheme(selectedTheme);
        };

        _lblFfmpegStatus = new Label
        {
            Text = "FFmpeg: Initializing...",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(107, 114, 128),
            AutoSize = true,
            Margin = new Padding(0, 5, 8, 0)
        };

        flowHeaderRight.Controls.AddRange(new Control[] { _btnRefreshCards, _chkShowLogs, _chkDarkMode, _lblFfmpegStatus });

        _pnlHeader.Controls.AddRange(new Control[] { flowHeaderLeft, flowHeaderRight });

        // Main 2-Column Split
        _tblMain = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(6, 2, 6, 4),
            BackColor = Color.FromArgb(20, 22, 26)
        };
        _tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        _tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var pnlTx = BuildTransmitterPanel();
        var pnlRx = BuildReceiverPanel();

        _tblMain.Controls.Add(pnlTx, 0, 0);
        _tblMain.Controls.Add(pnlRx, 1, 0);

        Controls.Add(_tblMain);
        Controls.Add(_pnlHeader);
        _pnlHeader.SendToBack();
        _tblMain.BringToFront();
    }

    private Panel BuildTransmitterPanel()
    {
        _pnlTx = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(28, 31, 38),
            Padding = new Padding(6, 2, 6, 4),
            Margin = new Padding(3)
        };

        // Top Fixed Settings Area (Height 432px: Title 26 + Video 212 + Controls 184 + margins)
        _pnlTxSettings = new Panel
        {
            Dock = DockStyle.Top,
            Height = 432,
            BackColor = Color.FromArgb(28, 31, 38)
        };

        // Title Row
        var pnlTitle = new Panel
        {
            Dock = DockStyle.Top,
            Height = 26,
            BackColor = Color.FromArgb(28, 31, 38)
        };
        _lblTxTitle = new Label
        {
            Text = "📡 SRT TRANSMITTER (TX) — INPUT FEED",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(52, 211, 153),
            AutoSize = true,
            Location = new Point(2, 4)
        };
        _lblTxStatusBadge = new Label
        {
            Text = "○ IDLE",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 163, 175),
            AutoSize = true,
            Location = new Point(280, 5)
        };
        pnlTitle.Controls.AddRange(new Control[] { _lblTxTitle, _lblTxStatusBadge });

        // Video Preview Monitor (Above Settings - 372 x 210)
        _picTxPreview = new PictureBox
        {
            Size = new Size(372, 210),
            Location = new Point(4, 28),
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle
        };

        // TX Controls Container (Below Video - Width 372, Height 184)
        _pnlTxControls = new Panel
        {
            Size = new Size(372, 184),
            Location = new Point(4, 240),
            BackColor = Color.Transparent
        };

        int y = 2;
        // Row 1: Source & Video Standard
        _pnlTxControls.Controls.Add(CreateFieldLabel("Source:", 0, y + 2));
        _cboTxSource = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(54, y),
            Width = 138,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboTxSource.Items.AddRange(new object[] { "DeckLink SDI Input", "Video File", "SMPTE Color Bars (Test)" });
        _cboTxSource.SelectedIndex = 0;
        _pnlTxControls.Controls.Add(_cboTxSource);

        _pnlTxControls.Controls.Add(CreateFieldLabel("Standard:", 198, y + 2));
        _cboTxFormat = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(260, y),
            Width = 102,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboTxFormat.Items.AddRange(new object[]
        {
            "Hi50 (1080i50)",
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
            FlatStyle = FlatStyle.Flat,
            Location = new Point(54, y),
            Width = 138,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlTxControls.Controls.Add(_cboTxDevice);

        _pnlTxControls.Controls.Add(CreateFieldLabel("Input:", 198, y + 2));
        _cboTxVideoInput = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(260, y),
            Width = 102,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboTxVideoInput.Items.AddRange(new object[] { "sdi", "hdmi", "optical_sdi", "component", "composite" });
        _cboTxVideoInput.SelectedIndex = 0;
        _pnlTxControls.Controls.Add(_cboTxVideoInput);
        y += 25;

        // Row 3: Video File & Browse & Loop
        _pnlTxControls.Controls.Add(CreateFieldLabel("File:", 0, y + 2));
        _txtTxFilePath = new TextBox
        {
            Location = new Point(54, y),
            Width = 146,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White,
            Text = @"sample_video.mp4"
        };
        _btnTxBrowse = new Button
        {
            Text = "Browse...",
            Location = new Point(204, y - 1),
            Width = 54,
            Height = 24,
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
            Location = new Point(264, y + 1),
            Width = 60,
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
            FlatStyle = FlatStyle.Flat,
            Location = new Point(54, y),
            Width = 138,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboTxEncoder.Items.AddRange(new object[] { "h264_nvenc (NVIDIA)", "libx264 (CPU)" });
        _cboTxEncoder.SelectedIndex = 0;
        _pnlTxControls.Controls.Add(_cboTxEncoder);

        _pnlTxControls.Controls.Add(CreateFieldLabel("Bitrate:", 198, y + 2));
        _txtTxBitrate = new TextBox
        {
            Location = new Point(260, y),
            Width = 102,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White,
            Text = "6000k"
        };
        _pnlTxControls.Controls.Add(_txtTxBitrate);
        y += 25;

        // Row 5: SRT Connection Mode & Host/Port
        _pnlTxControls.Controls.Add(CreateFieldLabel("Mode:", 0, y + 2));
        _cboTxMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(54, y),
            Width = 138,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboTxMode.Items.AddRange(new object[] { "Caller (Send)", "Listener (Wait)" });
        _cboTxMode.SelectedIndex = 0;
        _pnlTxControls.Controls.Add(_cboTxMode);

        _pnlTxControls.Controls.Add(CreateFieldLabel("Host:Port:", 198, y + 2));
        _txtTxHost = new TextBox
        {
            Location = new Point(260, y),
            Width = 50,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White,
            Text = "127.0.0.1"
        };
        _numTxPort = new NumericUpDown
        {
            Location = new Point(312, y),
            Width = 50,
            Minimum = 1024,
            Maximum = 65535,
            Value = 5000,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlTxControls.Controls.AddRange(new Control[] { _txtTxHost, _numTxPort });
        y += 25;

        // Row 6: Latency & Key & StreamID
        _pnlTxControls.Controls.Add(CreateFieldLabel("Latency:", 0, y + 2));
        _numTxLatency = new NumericUpDown
        {
            Location = new Point(46, y),
            Width = 44,
            Minimum = 20,
            Maximum = 5000,
            Value = 120,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlTxControls.Controls.Add(_numTxLatency);
        _pnlTxControls.Controls.Add(CreateFieldLabel("ms", 92, y + 2));

        _pnlTxControls.Controls.Add(CreateFieldLabel("Key:", 116, y + 2));
        _txtTxPassphrase = new TextBox
        {
            Location = new Point(144, y),
            Width = 58,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlTxControls.Controls.Add(_txtTxPassphrase);

        _pnlTxControls.Controls.Add(CreateFieldLabel("ID:", 208, y + 2));
        _txtTxStreamId = new TextBox
        {
            Location = new Point(232, y),
            Width = 130,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlTxControls.Controls.Add(_txtTxStreamId);
        y += 26;

        // Row 7: Action Buttons & Stats
        _btnTxStart = new Button
        {
            Text = "▶ START TX",
            Location = new Point(0, y),
            Width = 84,
            Height = 26,
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
            Location = new Point(88, y),
            Width = 48,
            Height = 26,
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
            Text = "FPS: - | Bitrate: - | Time: -",
            Font = new Font("Consolas", 7.5f),
            ForeColor = Color.FromArgb(110, 231, 183),
            AutoSize = true,
            Location = new Point(140, y + 5)
        };

        _pnlTxControls.Controls.AddRange(new Control[] { _btnTxStart, _btnTxStop, _lblTxStats });

        _pnlTxSettings.Controls.Add(_pnlTxControls);
        _pnlTxSettings.Controls.Add(_picTxPreview);
        _pnlTxSettings.Controls.Add(pnlTitle);

        _pnlTxSettings.Resize += (_, _) =>
        {
            int offsetX = Math.Max(4, (_pnlTxSettings.ClientSize.Width - 372) / 2);
            _picTxPreview.Left = offsetX;
            _pnlTxControls.Left = offsetX;
            _lblTxStatusBadge.Left = Math.Max(220, _pnlTxSettings.ClientSize.Width - _lblTxStatusBadge.Width - 8);
        };

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

        _pnlTx.Controls.Add(_pnlTxLogContainer);
        _pnlTx.Controls.Add(_pnlTxSettings);
        _pnlTxSettings.SendToBack();
        _pnlTxLogContainer.BringToFront();

        return _pnlTx;
    }

    private Panel BuildReceiverPanel()
    {
        _pnlRx = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(28, 31, 38),
            Padding = new Padding(6, 2, 6, 4),
            Margin = new Padding(3)
        };

        // Top Fixed Settings Area (Height 432px: Title 26 + Video 212 + Controls 184 + margins)
        _pnlRxSettings = new Panel
        {
            Dock = DockStyle.Top,
            Height = 432,
            BackColor = Color.FromArgb(28, 31, 38)
        };

        // Title Row
        var pnlTitle = new Panel
        {
            Dock = DockStyle.Top,
            Height = 26,
            BackColor = Color.FromArgb(28, 31, 38)
        };
        _lblRxTitle = new Label
        {
            Text = "📺 SRT RECEIVER (RX) — SDI PLAYOUT",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(96, 165, 250),
            AutoSize = true,
            Location = new Point(2, 4)
        };
        _lblRxStatusBadge = new Label
        {
            Text = "○ IDLE",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 163, 175),
            AutoSize = true,
            Location = new Point(275, 5)
        };
        pnlTitle.Controls.AddRange(new Control[] { _lblRxTitle, _lblRxStatusBadge });

        // Video Preview Monitor (Above Settings - 372 x 210)
        _picRxPreview = new PictureBox
        {
            Size = new Size(372, 210),
            Location = new Point(4, 28),
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle
        };

        // RX Controls Container (Below Video - Width 372, Height 184)
        _pnlRxControls = new Panel
        {
            Size = new Size(372, 184),
            Location = new Point(4, 240),
            BackColor = Color.Transparent
        };

        int y = 2;
        // Row 1: DeckLink SDI Playout Settings & Audio Delay
        _chkRxEnableDeckLink = new CheckBox
        {
            Text = "DeckLink SDI Playout",
            Location = new Point(0, y + 1),
            Width = 175,
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(147, 197, 253),
            Checked = true
        };
        _pnlRxControls.Controls.Add(_chkRxEnableDeckLink);

        _pnlRxControls.Controls.Add(CreateFieldLabel("Sync:", 184, y + 2));
        _numRxAudioDelay = new NumericUpDown
        {
            Location = new Point(220, y),
            Width = 56,
            Minimum = -1000,
            Maximum = 1000,
            Value = 0,
            Increment = 10,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlRxControls.Controls.Add(_numRxAudioDelay);
        _pnlRxControls.Controls.Add(CreateFieldLabel("ms", 280, y + 2));
        y += 25;

        // Row 2: DeckLink Card & SDI Standard
        _pnlRxControls.Controls.Add(CreateFieldLabel("Card:", 0, y + 2));
        _cboRxDevice = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(54, y),
            Width = 138,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlRxControls.Controls.Add(_cboRxDevice);

        _pnlRxControls.Controls.Add(CreateFieldLabel("Standard:", 198, y + 2));
        _cboRxFormat = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(260, y),
            Width = 102,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboRxFormat.Items.AddRange(new object[]
        {
            "Hi50 (1080i50)",
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
        _pnlRxControls.Controls.Add(CreateFieldLabel("Mode:", 0, y + 2));
        _cboRxMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(54, y),
            Width = 138,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboRxMode.Items.AddRange(new object[] { "Listener (Wait)", "Caller (Connect)" });
        _cboRxMode.SelectedIndex = 0;
        _pnlRxControls.Controls.Add(_cboRxMode);

        _pnlRxControls.Controls.Add(CreateFieldLabel("Host:Port:", 198, y + 2));
        _txtRxHost = new TextBox
        {
            Location = new Point(260, y),
            Width = 50,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White,
            Text = "0.0.0.0"
        };
        _numRxPort = new NumericUpDown
        {
            Location = new Point(312, y),
            Width = 50,
            Minimum = 1024,
            Maximum = 65535,
            Value = 5000,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlRxControls.Controls.AddRange(new Control[] { _txtRxHost, _numRxPort });
        y += 25;

        // Row 4: Latency & Key & StreamID
        _pnlRxControls.Controls.Add(CreateFieldLabel("Latency:", 0, y + 2));
        _numRxLatency = new NumericUpDown
        {
            Location = new Point(46, y),
            Width = 44,
            Minimum = 20,
            Maximum = 5000,
            Value = 120,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlRxControls.Controls.Add(_numRxLatency);
        _pnlRxControls.Controls.Add(CreateFieldLabel("ms", 92, y + 2));

        _pnlRxControls.Controls.Add(CreateFieldLabel("Key:", 116, y + 2));
        _txtRxPassphrase = new TextBox
        {
            Location = new Point(144, y),
            Width = 58,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlRxControls.Controls.Add(_txtRxPassphrase);

        _pnlRxControls.Controls.Add(CreateFieldLabel("ID:", 208, y + 2));
        _txtRxStreamId = new TextBox
        {
            Location = new Point(232, y),
            Width = 130,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _pnlRxControls.Controls.Add(_txtRxStreamId);
        y = 153; // Aligned with TX Row 7

        // Row 5: Action Buttons & Stats
        _btnRxStart = new Button
        {
            Text = "▶ START RX",
            Location = new Point(0, y),
            Width = 84,
            Height = 26,
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
            Location = new Point(88, y),
            Width = 48,
            Height = 26,
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
            Text = "FPS: - | Bitrate: - | Time: -",
            Font = new Font("Consolas", 7.5f),
            ForeColor = Color.FromArgb(147, 197, 253),
            AutoSize = true,
            Location = new Point(140, y + 5)
        };

        _pnlRxControls.Controls.AddRange(new Control[] { _btnRxStart, _btnRxStop, _lblRxStats });

        _pnlRxSettings.Controls.Add(_pnlRxControls);
        _pnlRxSettings.Controls.Add(_picRxPreview);
        _pnlRxSettings.Controls.Add(pnlTitle);

        _pnlRxSettings.Resize += (_, _) =>
        {
            int offsetX = Math.Max(4, (_pnlRxSettings.ClientSize.Width - 372) / 2);
            _picRxPreview.Left = offsetX;
            _pnlRxControls.Left = offsetX;
            _lblRxStatusBadge.Left = Math.Max(220, _pnlRxSettings.ClientSize.Width - _lblRxStatusBadge.Width - 8);
        };

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

        _pnlRx.Controls.Add(_pnlRxLogContainer);
        _pnlRx.Controls.Add(_pnlRxSettings);
        _pnlRxSettings.SendToBack();
        _pnlRxLogContainer.BringToFront();

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
