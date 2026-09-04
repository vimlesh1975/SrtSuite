# SRT Broadcast Suite (Native .NET 10 & Blackmagic SDI)

![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue)
![Framework](https://img.shields.io/badge/.NET-10.0--windows-purple)
![Hardware](https://img.shields.io/badge/hardware-Blackmagic%20DeckLink-black)
![Protocol](https://img.shields.io/badge/protocol-SRT%20(libsrt)-green)
![Encoding](https://img.shields.io/badge/encoder-NVIDIA%20NVENC-76B900)

**SRT Broadcast Suite** is a high-performance native C# desktop application built on **.NET 10 Windows Forms (x64)** for professional broadcast contribution, playout, and contribution monitoring. It provides synchronous **Blackmagic DeckLink SDI hardware input and output (1080i50 native)** with ultra-low latency **Secure Reliable Transport (SRT)** transmission.

---

## 🚀 Key Highlights

- **Native Blackmagic DeckLink SDI Playout (1080i50 Default)**:
  - Direct hardware output via official Blackmagic DeckLink SDK (`DeckLinkAPI.Interop`).
  - Native 1080i50 (`bmdModeHD1080i50` / `Hi50`), 1080p50/p59.94/p25, 720p50, PAL 576i support.
  - Instant SDI carrier lock with synchronous standby SMPTE colorbars pattern on idle/disconnect.
- **16-Bit 48kHz Embedded SDI Audio**:
  - Broadcast-compliant 16-bit 48kHz stereo PCM embedded SDI audio (`bmdAudioSampleType16bitInteger`).
  - Synchronous video-pump audio clocking: perfectly eliminates COM threading/apartment marshaling errors (`E_NOINTERFACE`, `E_ACCESSDENIED`) and maintains rock-solid lip-sync.
- **Dual Real-Time Preview Monitors**:
  - **Left Panel (TX)**: Live in-app video monitor displaying captured SDI input or source video file.
  - **Right Panel (RX)**: Live in-app video monitor displaying incoming SRT stream matching SDI playout.
- **Hardware-Accelerated Encoding (NVIDIA NVENC)**:
  - Zero-latency GPU encoding via `h264_nvenc` with low-latency tuning, fixed GOP (`-g 25 -bf 0`), and configurable bitrate.
  - CPU fallback (`libx264` veryfast/zerolatency) for systems without NVIDIA GPUs.
- **Multi-Source Transmitter (TX)**:
  - **DeckLink SDI Input**: Live SDI capture directly from cards (defaults to `DeckLink SDI 4K` or `DeckLink Duo`).
  - **Video File**: File streaming (`.mp4`, `.mkv`, `.ts`, `.mov`, `.avi`) with seamless looping.
  - **SMPTE Color Bars**: Built-in test generator with synchronized 1kHz sine audio tone.
- **Persistent Settings Memory**:
  - Automatically remembers selected DeckLink cards (TX input card, RX output card), video standards, bitrates, ports, and latency across app restarts via `appsettings.json`.
- **Production-Grade SRT Protocol**:
  - Caller (Push) and Listener (Server) connection modes.
  - Configurable latency buffer (20ms to 5000ms), packet loss recovery (`tlpktdrop`), and large socket ring buffers.
  - Optional AES encryption with passphrases and Stream ID routing.

---

## 🏗 System Architecture

```
                       ┌──────────────────────────────────────────────┐
                       │           SRT TRANSMITTER (TX)               │
                       │                                              │
Blackmagic SDI 4K ────►│ FFmpeg Ingest Engine (DeckLink Demuxer)      ├────┐
(or File / Colorbars)  │  ├─ Video: NVENC Low-Latency H.264 (25fps)   │    │
                       │  ├─ Audio: AAC 48kHz Stereo                  │    │
                       │  ├─ Output 1: MPEG-TS over SRT (UDP)         │    │
                       │  └─ Output 2: Pipe to In-App Preview (Left)  │    │
                       └──────────────────────────────────────────────┘    │
                                                                           │
                                                                    SRT Network
                                                              (Caller / Listener)
                                                                           │
                       ┌──────────────────────────────────────────────┐    │
                       │             SRT RECEIVER (RX)                │    │
                       │                                              │    │
                       │ FFmpeg Decoder Engine (libsrt)               │◄───┘
                       │  ├─ In-App Preview Pump (Right)              │
                       │  └─ Raw UYVY422 Video & 16-bit PCM Audio     │
                       │                                              │
                       │ Native .NET DeckLink Playout Engine          │
                       │  ├─ Synchronous DisplayVideoFrameSync        │
                       │  └─ Synchronous 48kHz Embedded SDI Audio     │
                       └──────────────────────┬───────────────────────┘
                                              ▼
                                 Blackmagic DeckLink SDI Out
                                  (1080i50 Clean SDI Feed)
```

---

## 📋 System Requirements

- **Operating System**: Windows 10 or Windows 11 (64-bit).
- **.NET Runtime**: [.NET 10.0 x64 Windows Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
- **DeckLink Drivers**: [Blackmagic Desktop Video](https://www.blackmagicdesign.com/support/family/capture-and-playback) version 12.0 or later.
- **Hardware**: Blackmagic Design DeckLink capture/playback card (`DeckLink SDI 4K`, `DeckLink Duo 2`, `DeckLink 8K Pro`, `DeckLink Studio`, etc.).
- **GPU (Optional but recommended)**: NVIDIA GPU supporting NVENC for hardware encoding.

---

## ⚡ Quick Start

### 1. Running the Pre-built Application
A fully compiled, self-contained Windows x64 build with all FFmpeg and DeckLink binaries is included in `bin\Release\net10.0-windows\win-x64\`.

1. Clone or download the repository (ensure **Git LFS** is installed, see below):
   ```powershell
   git clone https://github.com/vimlesh1975/SrtSuite.git
   cd SrtSuite
   git lfs pull
   ```
2. Run the application:
   ```powershell
   .\bin\Release\net10.0-windows\win-x64\SrtSuite.exe
   ```

### 2. Live SDI In to SDI Out (Local Loopback)
1. **Right Panel (RX)**:
   - Check **Enable DeckLink Hardware SDI Output**.
   - Select your playout card (e.g. `DeckLink Duo (1)`).
   - Set **SRT Mode** to `Listener`, Port `9998`.
   - Click **▶ START RECEIVER**. (The SDI monitor will immediately display standby colorbars).
2. **Left Panel (TX)**:
   - Select **Source Type**: `DeckLink SDI Input`.
   - Select **DeckLink Card**: `DeckLink SDI 4K` (or your input card).
   - Set **Input Port**: `sdi`.
   - Set **SRT Mode**: `Caller`, Target `127.0.0.1:9998`.
   - Click **▶ START TRANSMITTER**.
3. **Result**:
   - Left monitor shows live incoming video feed from DeckLink SDI 4K.
   - Right monitor shows decoded playout video feed.
   - External SDI monitor outputs seamless 1080i50 video with clear 48kHz embedded SDI audio.

---

## 🛠 Building from Source

To compile the project yourself:

```powershell
# Restore and build the Release configuration
dotnet build -c Release
```

The output executable and dependencies will be generated in `bin\Release\net10.0-windows\win-x64\`.

---

## 📦 Git LFS (Large File Storage)

This repository tracks large native executables and media using **Git LFS**:
- `*.exe` (FFmpeg, FFplay, FFprobe, SrtSuite binaries)
- `*.dll` (DeckLink COM interop assemblies)
- `*.mp4` (Sample test video files)
- `*.pdb` (Debugging symbol files)

### Cloning with Git LFS
Make sure you have Git LFS installed prior to cloning:
```bash
git lfs install
git clone https://github.com/vimlesh1975/SrtSuite.git
cd SrtSuite
git lfs pull
```

---

## ⚙️ Configuration (`appsettings.json`)

User configurations are automatically persisted to `appsettings.json` in the application directory:

```json
{
  "TxSource": "DeckLink SDI Input",
  "TxDevice": "DeckLink SDI 4K",
  "TxVideoInput": "sdi",
  "TxFormat": "Hi50 (1080i50 - Default)",
  "TxEncoder": "h264_nvenc (NVIDIA GPU)",
  "TxBitrate": "6000k",
  "TxFilePath": "sample_video.mp4",
  "TxLoop": true,
  "TxMode": "Caller (Send to remote)",
  "TxHost": "127.0.0.1",
  "TxPort": 9998,
  "TxLatency": 120,
  "TxPassphrase": "",
  "TxStreamId": "",
  "RxEnableDeckLink": true,
  "RxDevice": "DeckLink Duo (1)",
  "RxFormat": "Hi50 (1080i50 - Default)",
  "RxMode": "Listener (Listen for incoming)",
  "RxHost": "0.0.0.0",
  "RxPort": 9998,
  "RxLatency": 120,
  "RxPassphrase": "",
  "RxStreamId": ""
}
```

---

## 📄 License

MIT License. Designed for broadcast engineers, live streaming workflows, and production studios.
