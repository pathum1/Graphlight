# Architecture Documentation

## Overview

TaskbarEqualizer is designed as a modular, high-performance Windows 11 application that provides real-time audio visualization in the system tray.

## System Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    System Tray Host                     │
│                  (WPF Application)                      │
├─────────────────────────────────────────────────────────┤
│  ┌─────────────────┐  ┌─────────────────┐             │
│  │  Audio Capture  │  │  Visualization  │             │
│  │   (NAudio)      │  │    Engine       │             │
│  │                 │  │                 │             │
│  │ - WASAPI Loop   │  │ - Icon Render   │             │
│  │ - Buffer Mgmt   │  │ - Animation     │             │
│  │ - Device Monitor│  │ - Theme Support │             │
│  └─────────────────┘  └─────────────────┘             │
│           │                      │                     │
│  ┌─────────────────┐  ┌─────────────────┐             │
│  │ FFT Processing  │  │  Render Engine  │             │
│  │  (FftSharp)     │  │    (GDI+)       │             │
│  │                 │  │                 │             │
│  │ - Real-time FFT │  │ - Dynamic Icons │             │
│  │ - Freq Analysis │  │ - Smooth Anim   │             │
│  │ - Smoothing     │  │ - Color Themes  │             │
│  └─────────────────┘  └─────────────────┘             │
├─────────────────────────────────────────────────────────┤
│                 Configuration Manager                   │
│              (Settings & Preferences)                   │
└─────────────────────────────────────────────────────────┘
```

## Component Details

### 1. Audio Capture Service
**Responsibility**: Capture system audio using WASAPI loopback

**Key Features**:
- Low-latency audio capture (<50ms)
- Automatic device detection and switching
- Configurable buffer sizes (1024-4096 samples)
- Thread-safe circular buffer implementation

**Implementation**:
```csharp
public class AudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture _capture;
    private readonly CircularBuffer<float> _audioBuffer;
    private readonly AudioConfiguration _config;
    
    public event EventHandler<AudioDataEventArgs> AudioDataAvailable;
    
    public void StartCapture();
    public void StopCapture();
    public void SwitchDevice(MMDevice device);
}
```

### 2. FFT Analysis Engine
**Responsibility**: Convert audio samples to frequency spectrum

**Key Features**:
- Real-time FFT processing using FftSharp
- Configurable frequency bands (8-32)
- Logarithmic frequency scaling
- Exponential smoothing for visual stability

**Implementation**:
```csharp
public class FrequencyAnalyzer
{
    private readonly FftSharp.Windows.Hanning _window;
    private readonly int _fftSize = 1024;
    private readonly double[] _smoothingBuffer;
    
    public double[] ProcessAudioBuffer(float[] samples);
    public void ConfigureFrequencyBands(int bandCount);
    private double[] ApplyLogarithmicScaling(Complex[] fftResult);
}
```

### 3. System Tray Visualizer
**Responsibility**: Render equalizer visualization in taskbar

**Key Features**:
- Dynamic icon generation (16x16, 32x32, 48x48)
- Windows 11 Fluent Design compliance
- Smooth animations with 60 FPS target
- Dark/Light theme adaptation

**Implementation**:
```csharp
public class TaskbarEqualizer : IDisposable
{
    private NotifyIcon _notifyIcon;
    private EqualizerRenderer _renderer;
    private ContextMenuStrip _contextMenu;
    
    public void UpdateVisualization(double[] frequencyData);
    public void ApplyTheme(ThemeMode theme);
    private Icon GenerateEqualizerIcon(double[] bands);
}
```

### 4. Configuration Manager
**Responsibility**: Manage user settings and preferences

**Key Features**:
- JSON-based configuration persistence
- Real-time setting updates
- Default configuration management
- Auto-start registry integration

## Threading Model

### Main UI Thread
- WPF application host
- System tray management
- User interaction handling
- Configuration updates

### Audio Capture Thread
- WASAPI audio capture
- Buffer management
- Device monitoring
- Low-latency audio processing

### FFT Processing Thread
- Real-time frequency analysis
- Data smoothing and filtering
- Performance optimization
- Thread-safe data exchange

### Render Thread
- Icon generation and caching
- Animation frame updates
- GDI+ rendering operations
- Memory management

## Performance Considerations

### Memory Management
- Object pooling for frequent allocations
- Circular buffers for audio data
- Icon caching to reduce GDI+ overhead
- Automatic garbage collection optimization

### CPU Optimization
- Efficient FFT algorithms (FftSharp)
- Adaptive quality based on system load
- Vectorized operations where possible
- Minimal lock contention

### Real-time Constraints
- Audio buffer management with overflow protection
- Frame rate limiting to prevent excessive CPU usage
- Adaptive refresh rates based on audio activity
- Graceful degradation under high system load

## Error Handling Strategy

### Audio System Failures
- Automatic device re-detection
- Fallback to default audio device
- Graceful handling of driver crashes
- User notification for critical failures

### Performance Degradation
- Automatic quality reduction under load
- Frame rate adaptation
- Memory usage monitoring
- CPU usage throttling

### System Integration Issues
- Windows theme change detection
- Display scaling adaptation
- Multi-monitor support
- UAC and permissions handling

## Security Considerations

### Audio Privacy
- No audio data storage or transmission
- Local processing only
- No network communication
- Minimal permission requirements

### System Integration
- Code signing for Windows Defender compatibility
- Minimal system API usage
- No elevated privileges required
- Registry access limited to auto-start

## Extensibility Points

### Plugin Architecture (Future)
- Custom visualization plugins
- Audio effect processors
- Theme providers
- Device-specific optimizations

### Configuration Extensions
- Advanced audio processing options
- Custom frequency band configurations
- Performance tuning parameters
- Multi-monitor display options