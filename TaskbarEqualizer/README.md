# TaskbarEqualizer

A lightweight Windows 11 taskbar graphic equalizer that provides real-time audio visualization in the system tray.

## Features

- 🎵 Real-time audio visualization in Windows 11 taskbar
- ⚡ High-performance FFT analysis with <50ms latency
- 🎨 Windows 11 Fluent Design compliance
- 🔧 Configurable frequency bands (8-32 bands)
- 💾 Lightweight footprint (<30MB RAM, <3% CPU)
- 🚀 Auto-start with Windows support

## Technology Stack

- **Framework**: C# WPF (.NET 8)
- **Audio Processing**: NAudio with WASAPI loopback
- **FFT Analysis**: FftSharp
- **System Integration**: Win32 Shell_NotifyIcon API

## Development Status

🚧 **In Development** - This project is currently being built following a structured 4-phase approach.

### Current Phase: Phase 1 - Core Infrastructure
- [ ] WPF project setup with .NET 8
- [ ] NAudio integration for audio capture
- [ ] FftSharp integration for frequency analysis
- [ ] Basic system tray implementation
- [ ] Performance benchmarking framework

## Getting Started

### Prerequisites

- Windows 11 (21H2 or later)
- .NET 8 SDK
- Visual Studio 2022 (recommended)

### Building from Source

```bash
git clone https://github.com/yourusername/TaskbarEqualizer.git
cd TaskbarEqualizer
dotnet restore
dotnet build
```

### Running

```bash
dotnet run --project src/TaskbarEqualizer
```

## Project Structure

```
TaskbarEqualizer/
├── src/
│   ├── TaskbarEqualizer/          # Main WPF application
│   ├── TaskbarEqualizer.Core/     # Core audio processing
│   └── TaskbarEqualizer.Tests/    # Unit tests
├── docs/                          # Documentation
├── assets/                        # Images and resources
└── installer/                     # MSI installer project
```

## Performance Targets

| Metric | Target | Critical Threshold |
|--------|--------|-------------------|
| Audio Latency | <50ms | <100ms |
| CPU Usage | <3% | <5% |
| Memory Usage | <30MB | <50MB |
| Frame Rate | 60 FPS | 30 FPS |

## Contributing

This is a personal project, but suggestions and feedback are welcome! Please open an issue to discuss any changes.

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Roadmap

- **Phase 1**: Core infrastructure (Weeks 1-2)
- **Phase 2**: Visualization engine (Weeks 3-4)
- **Phase 3**: User experience & polish (Weeks 5-6)
- **Phase 4**: Testing & deployment (Weeks 7-8)

## Architecture Overview

The application follows a modular architecture with separated concerns:

- **Audio Capture Service**: WASAPI loopback capture
- **FFT Analysis Engine**: Real-time frequency spectrum analysis
- **System Tray Visualizer**: Dynamic icon generation
- **Configuration Manager**: Settings and preferences

For detailed technical documentation, see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).