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
    private Label _lblHeaderTitle = null!;
    private Label _lblHeaderSubtitle = null!;
    private Button _btnRefreshCards = null!;
    private Label _lblFfmpegStatus = null!;

    // UI Controls - Transmitter (TX)
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
    private RichTextBox _rtbTxLog = null!;
    private Button _btnTxCopyLog = null!;
    private Button _btnTxClearLog = null!;

    // UI Controls - Receiver (RX)
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
    private RichTextBox _rtbRxLog = null!;
    private Button _btnRxCopyLog = null!;
    private Button _btnRxClearLog = null!;

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

    #region GUI Layout Builder
    private void InitializeComponentCustom()
    {
        Text = "SRT Broadcast Suite — Native Blackmagic SDI Playout & NVENC Streaming";
        Size = new Size(1400, 920);
        MinimumSize = new Size(1180, 800);
        BackColor = Color.FromArgb(20, 22, 26);
        ForeColor = Color.FromArgb(240, 243, 246);
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        StartPosition = FormStartPosition.CenterScreen;

        // Header Panel
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 65,
            BackColor = Color.FromArgb(28, 31, 38),
            Padding = new Padding(18, 10, 18, 10)
        };

        _lblHeaderTitle = new Label
        {
            Text = "SRT BROADCAST SUITE",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(16, 10)
        };

        _lblHeaderSubtitle = new Label
        {
            Text = "Blackmagic DeckLink SDI Playout Engine (Default 1080i50 Hi50) • Synchronous Frame Timing • NVENC Low-Latency",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(156, 163, 175),
            AutoSize = true,
            Location = new Point(18, 36)
        };

        _btnRefreshCards = new Button
        {
            Text = "🔄 Refresh DeckLink Cards",
            Size = new Size(190, 36),
            Location = new Point(Width - 230, 14),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(44, 49, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnRefreshCards.FlatAppearance.BorderSize = 0;
        _btnRefreshCards.Click += (_, _) => PopulateDeckLinkDevices();

        _lblFfmpegStatus = new Label
        {
            Text = "FFmpeg: Initializing...",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(107, 114, 128),
            AutoSize = true,
            Location = new Point(Width - 620, 24),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        pnlHeader.Controls.AddRange(new Control[] { _lblHeaderTitle, _lblHeaderSubtitle, _lblFfmpegStatus, _btnRefreshCards });

        // Main 2-Column Split
        var tblMain = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12),
            BackColor = Color.FromArgb(20, 22, 26)
        };
        tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        var pnlTx = BuildTransmitterPanel();
        var pnlRx = BuildReceiverPanel();

        tblMain.Controls.Add(pnlTx, 0, 0);
        tblMain.Controls.Add(pnlRx, 1, 0);

        Controls.Add(tblMain);
        Controls.Add(pnlHeader);
    }

    private Panel BuildTransmitterPanel()
    {
        var pnl = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(28, 31, 38),
            Padding = new Padding(14),
            Margin = new Padding(6),
            AutoScroll = true
        };

        int y = 10;

        // Title Row
        var lblTitle = new Label
        {
            Text = "📡 SRT TRANSMITTER (TX)",
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(52, 211, 153),
            AutoSize = true,
            Location = new Point(10, y)
        };

        _lblTxStatusBadge = new Label
        {
            Text = "○ IDLE",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 163, 175),
            AutoSize = true,
            Location = new Point(230, y + 2)
        };
        pnl.Controls.AddRange(new Control[] { lblTitle, _lblTxStatusBadge });
        y += 32;

        // Preview PictureBox (TX Monitor)
        _picTxPreview = new PictureBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 200),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle
        };
        pnl.Controls.Add(_picTxPreview);
        y += 208;

        // Source Selection
        pnl.Controls.Add(CreateFieldLabel("Source Type:", 10, y));
        _cboTxSource = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(120, y - 3),
            Width = 190,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboTxSource.Items.AddRange(new object[] { "DeckLink SDI Input", "Video File", "SMPTE Color Bars (Test)" });
        _cboTxSource.SelectedIndex = 0;
        pnl.Controls.Add(_cboTxSource);

        pnl.Controls.Add(CreateFieldLabel("Video Standard:", 330, y));
        _cboTxFormat = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(440, y - 3),
            Width = 170,
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
        pnl.Controls.Add(_cboTxFormat);
        y += 32;

        // DeckLink Card & Port
        pnl.Controls.Add(CreateFieldLabel("DeckLink Card:", 10, y));
        _cboTxDevice = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(120, y - 3),
            Width = 190,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        pnl.Controls.Add(_cboTxDevice);

        pnl.Controls.Add(CreateFieldLabel("Input Port:", 330, y));
        _cboTxVideoInput = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(440, y - 3),
            Width = 170,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboTxVideoInput.Items.AddRange(new object[] { "sdi", "hdmi", "optical_sdi", "component", "composite" });
        _cboTxVideoInput.SelectedIndex = 0;
        pnl.Controls.Add(_cboTxVideoInput);
        y += 32;

        // File Path & Loop
        pnl.Controls.Add(CreateFieldLabel("Video File:", 10, y));
        _txtTxFilePath = new TextBox
        {
            Location = new Point(120, y - 3),
            Width = 260,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White,
            Text = @"sample_video.mp4"
        };
        _btnTxBrowse = new Button
        {
            Text = "Browse...",
            Location = new Point(390, y - 5),
            Width = 75,
            Height = 28,
            BackColor = Color.FromArgb(44, 49, 60),
            ForeColor = Color.White,
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
            Text = "Loop Video",
            Location = new Point(480, y - 3),
            AutoSize = true,
            ForeColor = Color.FromArgb(209, 213, 219),
            Checked = true
        };
        pnl.Controls.AddRange(new Control[] { _txtTxFilePath, _btnTxBrowse, _chkTxLoop });
        y += 32;

        // Encoder & Bitrate
        pnl.Controls.Add(CreateFieldLabel("Encoder:", 10, y));
        _cboTxEncoder = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(120, y - 3),
            Width = 190,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboTxEncoder.Items.AddRange(new object[] { "h264_nvenc (NVIDIA GPU)", "libx264 (CPU)" });
        _cboTxEncoder.SelectedIndex = 0;
        pnl.Controls.Add(_cboTxEncoder);

        pnl.Controls.Add(CreateFieldLabel("Bitrate:", 330, y));
        _txtTxBitrate = new TextBox
        {
            Location = new Point(440, y - 3),
            Width = 170,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White,
            Text = "6000k"
        };
        pnl.Controls.Add(_txtTxBitrate);
        y += 32;

        // SRT Connection Mode & Host/Port
        pnl.Controls.Add(CreateFieldLabel("SRT Mode:", 10, y));
        _cboTxMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(120, y - 3),
            Width = 190,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboTxMode.Items.AddRange(new object[] { "Caller (Send to remote)", "Listener (Wait for RX)" });
        _cboTxMode.SelectedIndex = 0;
        pnl.Controls.Add(_cboTxMode);

        pnl.Controls.Add(CreateFieldLabel("Target Host:Port:", 330, y));
        _txtTxHost = new TextBox
        {
            Location = new Point(440, y - 3),
            Width = 100,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White,
            Text = "127.0.0.1"
        };
        _numTxPort = new NumericUpDown
        {
            Location = new Point(545, y - 3),
            Width = 65,
            Minimum = 1024,
            Maximum = 65535,
            Value = 9998,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        pnl.Controls.AddRange(new Control[] { _txtTxHost, _numTxPort });
        y += 32;

        // Latency & Passphrase
        pnl.Controls.Add(CreateFieldLabel("Latency (ms):", 10, y));
        _numTxLatency = new NumericUpDown
        {
            Location = new Point(120, y - 3),
            Width = 100,
            Minimum = 20,
            Maximum = 5000,
            Value = 120,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        pnl.Controls.Add(_numTxLatency);

        pnl.Controls.Add(CreateFieldLabel("Passphrase:", 330, y));
        _txtTxPassphrase = new TextBox
        {
            Location = new Point(440, y - 3),
            Width = 170,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        pnl.Controls.Add(_txtTxPassphrase);
        y += 32;

        // StreamID
        pnl.Controls.Add(CreateFieldLabel("Stream ID:", 10, y));
        _txtTxStreamId = new TextBox
        {
            Location = new Point(120, y - 3),
            Width = 190,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        pnl.Controls.Add(_txtTxStreamId);
        y += 40;

        // Action Buttons
        _btnTxStart = new Button
        {
            Text = "▶ START TRANSMITTER",
            Location = new Point(10, y),
            Width = 220,
            Height = 38,
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnTxStart.FlatAppearance.BorderSize = 0;
        _btnTxStart.Click += OnTxStartClicked;

        _btnTxStop = new Button
        {
            Text = "⏹ STOP",
            Location = new Point(240, y),
            Width = 120,
            Height = 38,
            BackColor = Color.FromArgb(239, 68, 68),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Enabled = false,
            Cursor = Cursors.Hand
        };
        _btnTxStop.FlatAppearance.BorderSize = 0;
        _btnTxStop.Click += OnTxStopClicked;

        pnl.Controls.AddRange(new Control[] { _btnTxStart, _btnTxStop });
        y += 46;

        // Stats Row
        _lblTxStats = new Label
        {
            Text = "FPS: - | Bitrate: - | Time: - | Speed: -",
            Font = new Font("Consolas", 9f),
            ForeColor = Color.FromArgb(110, 231, 183),
            AutoSize = true,
            Location = new Point(10, y)
        };
        pnl.Controls.Add(_lblTxStats);
        y += 24;

        // Log Console Area
        var pnlLogHeader = new Panel
        {
            Location = new Point(10, y),
            Size = new Size(600, 26),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var lblLogTitle = new Label
        {
            Text = "Transmitter Log Output:",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 163, 175),
            AutoSize = true,
            Location = new Point(0, 4)
        };
        _btnTxCopyLog = new Button
        {
            Text = "📋 Copy Log",
            Size = new Size(90, 24),
            Location = new Point(410, 1),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(44, 49, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
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

        _btnTxClearLog = new Button
        {
            Text = "🗑 Clear",
            Size = new Size(70, 24),
            Location = new Point(510, 1),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(44, 49, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnTxClearLog.FlatAppearance.BorderSize = 0;
        _btnTxClearLog.Click += (_, _) => _rtbTxLog.Clear();

        pnlLogHeader.Controls.AddRange(new Control[] { lblLogTitle, _btnTxCopyLog, _btnTxClearLog });
        pnl.Controls.Add(pnlLogHeader);
        y += 28;

        _rtbTxLog = new RichTextBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 300),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.FromArgb(15, 17, 21),
            ForeColor = Color.FromArgb(167, 243, 208),
            Font = new Font("Consolas", 8.5f),
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        pnl.Controls.Add(_rtbTxLog);

        return pnl;
    }

    private Panel BuildReceiverPanel()
    {
        var pnl = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(28, 31, 38),
            Padding = new Padding(14),
            Margin = new Padding(6),
            AutoScroll = true
        };

        int y = 10;

        // Title Row
        var lblTitle = new Label
        {
            Text = "📺 SRT RECEIVER & SDI PLAYOUT (RX)",
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(96, 165, 250),
            AutoSize = true,
            Location = new Point(10, y)
        };

        _lblRxStatusBadge = new Label
        {
            Text = "○ IDLE",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 163, 175),
            AutoSize = true,
            Location = new Point(290, y + 2)
        };
        pnl.Controls.AddRange(new Control[] { lblTitle, _lblRxStatusBadge });
        y += 32;

        // Preview PictureBox (Monitor)
        _picRxPreview = new PictureBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 200),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle
        };
        pnl.Controls.Add(_picRxPreview);
        y += 208;

        // DeckLink Playout Settings
        _chkRxEnableDeckLink = new CheckBox
        {
            Text = "Enable DeckLink Hardware SDI Output (No Freezes, 1080i50 Native)",
            Location = new Point(10, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(147, 197, 253),
            Checked = true
        };
        pnl.Controls.Add(_chkRxEnableDeckLink);
        y += 28;

        pnl.Controls.Add(CreateFieldLabel("DeckLink Card:", 10, y));
        _cboRxDevice = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(120, y - 3),
            Width = 190,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        pnl.Controls.Add(_cboRxDevice);

        pnl.Controls.Add(CreateFieldLabel("SDI Standard:", 330, y));
        _cboRxFormat = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(440, y - 3),
            Width = 170,
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
        pnl.Controls.Add(_cboRxFormat);
        y += 32;

        // SRT Connection Mode & Host/Port
        pnl.Controls.Add(CreateFieldLabel("SRT Mode:", 10, y));
        _cboRxMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(120, y - 3),
            Width = 190,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        _cboRxMode.Items.AddRange(new object[] { "Listener (Listen for incoming)", "Caller (Connect to remote)" });
        _cboRxMode.SelectedIndex = 0;
        pnl.Controls.Add(_cboRxMode);

        pnl.Controls.Add(CreateFieldLabel("Listen Host:Port:", 330, y));
        _txtRxHost = new TextBox
        {
            Location = new Point(440, y - 3),
            Width = 100,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White,
            Text = "0.0.0.0"
        };
        _numRxPort = new NumericUpDown
        {
            Location = new Point(545, y - 3),
            Width = 65,
            Minimum = 1024,
            Maximum = 65535,
            Value = 9998,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        pnl.Controls.AddRange(new Control[] { _txtRxHost, _numRxPort });
        y += 32;

        // Latency & Passphrase
        pnl.Controls.Add(CreateFieldLabel("Latency (ms):", 10, y));
        _numRxLatency = new NumericUpDown
        {
            Location = new Point(120, y - 3),
            Width = 100,
            Minimum = 20,
            Maximum = 5000,
            Value = 120,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        pnl.Controls.Add(_numRxLatency);

        pnl.Controls.Add(CreateFieldLabel("Passphrase:", 330, y));
        _txtRxPassphrase = new TextBox
        {
            Location = new Point(440, y - 3),
            Width = 170,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };
        pnl.Controls.Add(_txtRxPassphrase);
        y += 32;

        // StreamID & Lip-Sync Audio Delay
        pnl.Controls.Add(CreateFieldLabel("Stream ID:", 10, y));
        _txtRxStreamId = new TextBox
        {
            Location = new Point(120, y - 3),
            Width = 190,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };

        pnl.Controls.Add(CreateFieldLabel("Audio Sync (ms):", 330, y));
        _numRxAudioDelay = new NumericUpDown
        {
            Location = new Point(440, y - 3),
            Width = 170,
            Minimum = -1000,
            Maximum = 1000,
            Value = 0,
            Increment = 10,
            BackColor = Color.FromArgb(40, 44, 52),
            ForeColor = Color.White
        };

        pnl.Controls.AddRange(new Control[] { _txtRxStreamId, _numRxAudioDelay });
        y += 38;

        // Action Buttons
        _btnRxStart = new Button
        {
            Text = "▶ START RECEIVER",
            Location = new Point(10, y),
            Width = 220,
            Height = 38,
            BackColor = Color.FromArgb(59, 130, 246),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnRxStart.FlatAppearance.BorderSize = 0;
        _btnRxStart.Click += OnRxStartClicked;

        _btnRxStop = new Button
        {
            Text = "⏹ STOP",
            Location = new Point(240, y),
            Width = 120,
            Height = 38,
            BackColor = Color.FromArgb(239, 68, 68),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Enabled = false,
            Cursor = Cursors.Hand
        };
        _btnRxStop.FlatAppearance.BorderSize = 0;
        _btnRxStop.Click += OnRxStopClicked;

        pnl.Controls.AddRange(new Control[] { _btnRxStart, _btnRxStop });
        y += 46;

        // Stats Row
        _lblRxStats = new Label
        {
            Text = "FPS: - | Bitrate: - | Time: - | Speed: -",
            Font = new Font("Consolas", 9f),
            ForeColor = Color.FromArgb(147, 197, 253),
            AutoSize = true,
            Location = new Point(10, y)
        };
        pnl.Controls.Add(_lblRxStats);
        y += 24;

        // Log Console Area
        var pnlLogHeader = new Panel
        {
            Location = new Point(10, y),
            Size = new Size(600, 26),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var lblLogTitle = new Label
        {
            Text = "Receiver Log Output:",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(156, 163, 175),
            AutoSize = true,
            Location = new Point(0, 4)
        };
        _btnRxCopyLog = new Button
        {
            Text = "📋 Copy Log",
            Size = new Size(90, 24),
            Location = new Point(410, 1),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(44, 49, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
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

        _btnRxClearLog = new Button
        {
            Text = "🗑 Clear",
            Size = new Size(70, 24),
            Location = new Point(510, 1),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(44, 49, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnRxClearLog.FlatAppearance.BorderSize = 0;
        _btnRxClearLog.Click += (_, _) => _rtbRxLog.Clear();

        pnlLogHeader.Controls.AddRange(new Control[] { lblLogTitle, _btnRxCopyLog, _btnRxClearLog });
        pnl.Controls.Add(pnlLogHeader);
        y += 28;

        _rtbRxLog = new RichTextBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 170),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.FromArgb(15, 17, 21),
            ForeColor = Color.FromArgb(186, 230, 253),
            Font = new Font("Consolas", 8.5f),
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Both
        };
        pnl.Controls.Add(_rtbRxLog);

        return pnl;
    }

    private static Label CreateFieldLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(156, 163, 175)
        };
    }
    #endregion
}
