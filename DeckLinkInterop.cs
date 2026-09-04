using System.Runtime.InteropServices;
using DeckLinkAPI;

namespace SrtSuite;

public record DeckLinkDeviceItem(string Name, string ModelName, IDeckLink Device);

public static class DeckLinkInterop
{
    public static List<DeckLinkDeviceItem> EnumerateDevices()
    {
        var list = new List<DeckLinkDeviceItem>();
        try
        {
            var iterator = new CDeckLinkIteratorClass();
            while (true)
            {
                try
                {
                    iterator.Next(out var deckLink);
                    if (deckLink is null) break;

                    deckLink.GetDisplayName(out var displayName);
                    deckLink.GetModelName(out var modelName);
                    list.Add(new DeckLinkDeviceItem(displayName, modelName, deckLink));
                }
                catch (COMException)
                {
                    break;
                }
            }
            Marshal.ReleaseComObject(iterator);
        }
        catch (Exception)
        {
            // DeckLink driver not installed or COM initialization failed
        }
        return list;
    }

    public static IDeckLink? FindDevice(string requestedName)
    {
        var iterator = new CDeckLinkIteratorClass();
        try
        {
            while (true)
            {
                IDeckLink deckLink;
                try
                {
                    iterator.Next(out deckLink);
                }
                catch (COMException)
                {
                    break;
                }

                if (deckLink is null) break;

                deckLink.GetDisplayName(out var displayName);
                deckLink.GetModelName(out var modelName);

                if (string.Equals(displayName, requestedName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(modelName, requestedName, StringComparison.OrdinalIgnoreCase) ||
                    displayName.Contains(requestedName, StringComparison.OrdinalIgnoreCase))
                {
                    return deckLink;
                }

                Marshal.ReleaseComObject(deckLink);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(iterator);
        }
        return null;
    }

    public static _BMDDisplayMode ResolveDisplayMode(string formatCode, out int width, out int height, out double frameRate)
    {
        switch (formatCode)
        {
            case "Hi50":
                width = 1920; height = 1080; frameRate = 25.0;
                return _BMDDisplayMode.bmdModeHD1080i50;
            case "Hp50":
                width = 1920; height = 1080; frameRate = 50.0;
                return _BMDDisplayMode.bmdModeHD1080p50;
            case "Hp25":
                width = 1920; height = 1080; frameRate = 25.0;
                return _BMDDisplayMode.bmdModeHD1080p25;
            case "Hi59":
                width = 1920; height = 1080; frameRate = 30000.0 / 1001.0;
                return _BMDDisplayMode.bmdModeHD1080i5994;
            case "Hp59":
                width = 1920; height = 1080; frameRate = 60000.0 / 1001.0;
                return _BMDDisplayMode.bmdModeHD1080p5994;
            case "Hi60":
                width = 1920; height = 1080; frameRate = 30.0;
                return _BMDDisplayMode.bmdModeHD1080i6000;
            case "Hp60":
                width = 1920; height = 1080; frameRate = 60.0;
                return _BMDDisplayMode.bmdModeHD1080p6000;
            case "hp50":
                width = 1280; height = 720; frameRate = 50.0;
                return _BMDDisplayMode.bmdModeHD720p50;
            case "hp59":
                width = 1280; height = 720; frameRate = 60000.0 / 1001.0;
                return _BMDDisplayMode.bmdModeHD720p5994;
            case "pal":
                width = 720; height = 576; frameRate = 25.0;
                return _BMDDisplayMode.bmdModePAL;
            case "ntsc":
                width = 720; height = 486; frameRate = 30000.0 / 1001.0;
                return _BMDDisplayMode.bmdModeNTSC;
            default:
                width = 1920; height = 1080; frameRate = 25.0;
                return _BMDDisplayMode.bmdModeHD1080i50;
        }
    }
}
