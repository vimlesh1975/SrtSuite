namespace SrtSuite;

public enum SourceType
{
    DeckLink,
    File,
    ColorBars
}

public enum SrtMode
{
    Caller,
    Listener
}

public record TxSettings(
    SourceType SourceType,
    string DeckLinkDevice,
    string FormatCode,
    string VideoInput,
    string Encoder,
    string Bitrate,
    string? FilePath,
    bool Loop,
    SrtMode Mode,
    string Host,
    int Port,
    int LatencyMs,
    string? Passphrase,
    string? StreamId
);

public record RxSettings(
    SrtMode Mode,
    string Host,
    int Port,
    int LatencyMs,
    string? Passphrase,
    string? StreamId,
    bool EnableDeckLinkPlayout,
    string DeckLinkDevice,
    string FormatCode,
    int AudioDelayMs = 0
);

public record StreamStats(
    string? Fps,
    string? Bitrate,
    string? Time,
    string? Speed
);
