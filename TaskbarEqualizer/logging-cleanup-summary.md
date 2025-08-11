# Audio Processing Debug Logging Cleanup

## Changes Made

To reduce log file noise and improve performance, the following frequent debug log statements have been suppressed or removed:

### FrequencyAnalyzer.cs
**Removed frequent debug logs that occur for every audio sample processed:**

1. ✅ `"Queued audio samples for processing: {SampleCount} samples"` - Replaced with comment
2. ✅ `"Starting audio sample processing: samples={Count}, fftSize={FftSize}"` - Replaced with comment  
3. ✅ `"Processing {SamplesToProcess} samples"` - Replaced with comment
4. ✅ `"Performing FFT on {Size} samples"` - Removed
5. ✅ `"FFT completed, result length: {Length}"` - Removed  
6. ✅ `"Mapping frequency bands"` - Removed
7. ✅ `"Applying smoothing"` - Removed
8. ✅ `"Firing spectrum event: peak={Peak:F3}, rms={Rms:F3}, bands={Bands}"` - Replaced with comment

**Kept important debug logs:**
- Initialization and configuration logs (occur rarely)
- Error conditions and warnings
- Performance statistics (logged every 1000 samples)
- Processing loop start/end (occur once per session)

### ApplicationOrchestrator.cs
**Suppressed frequent spectrum data logging:**

1. ✅ Removed throttled spectrum data debug logging that occurred during audio processing

**Kept important debug logs:**
- Component initialization
- Settings changes
- Error conditions

### AudioCaptureService.cs
**Left unchanged:**
- WASAPI debug logging already has throttling (once per second)
- Provides useful audio capture diagnostics

## Impact

**Before:** Logs were flooded with messages like:
```
[2025-08-10 18:45:27 DBG] Firing spectrum event: peak=0.003, rms=0.001, bands=16  
[2025-08-10 18:45:27 DBG] Queued audio samples for processing: 2048 samples
[2025-08-10 18:45:27 DBG] Starting audio sample processing: samples=2048, fftSize=2048
```

**After:** 
- Clean, readable logs focused on important events
- Better performance (reduced I/O overhead from excessive logging)
- Important diagnostics still available

## Debugging Audio Issues

If you need detailed audio processing logs for debugging:

1. **Change log level to Trace** in appsettings.json:
   ```json
   {
     "Logging": {
       "LogLevel": {
         "TaskbarEqualizer.Core.Audio": "Trace"
       }
     }
   }
   ```

2. **Temporarily add back debug logs** in specific methods you're investigating

3. **Use performance statistics** - FrequencyAnalyzer still logs processing stats every 1000 samples

## Files Modified

- ✅ `TaskbarEqualizer.Core\Audio\FrequencyAnalyzer.cs`
- ✅ `TaskbarEqualizer.Configuration\Services\ApplicationOrchestrator.cs`

All other audio logging left intact with existing throttling mechanisms.