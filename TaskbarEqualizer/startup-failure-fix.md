# Startup Failure Fix - Frequency Bands Constraint Mismatch

## Issue Identified

**Root Cause**: Constraint mismatch between ApplicationSettings and FrequencyAnalyzer

**Error**: `System.ArgumentException: Frequency bands must be between 8 and 32 (Parameter 'frequencyBands')`

## Problem Details

1. **ApplicationSettings.cs** (line 206): Allows FrequencyBands range 4-64
   ```csharp
   set => SetProperty(ref _frequencyBands, Math.Max(4, Math.Min(64, value)));
   ```

2. **FrequencyAnalyzer.cs** (lines 175-176): Required FrequencyBands range 8-32
   ```csharp
   if (frequencyBands < 8 || frequencyBands > 32)
       throw new ArgumentException("Frequency bands must be between 8 and 32", nameof(frequencyBands));
   ```

3. **Current Settings**: FrequencyBands = 4 (visible in log line 84)
   ```
   [2025-08-10 22:42:33 INF] TaskbarEqualizer.Main.SpectrumAnalyzerWindow: Resizing spectrum array from 16 to 4
   ```

## Fix Applied

Updated FrequencyAnalyzer.cs to accept the same range as ApplicationSettings:

### ConfigureAsync method (line 175-176)
```csharp
// BEFORE:
if (frequencyBands < 8 || frequencyBands > 32)
    throw new ArgumentException("Frequency bands must be between 8 and 32", nameof(frequencyBands));

// AFTER:
if (frequencyBands < 4 || frequencyBands > 64)
    throw new ArgumentException("Frequency bands must be between 4 and 64", nameof(frequencyBands));
```

### UpdateFrequencyBandsAsync method (line 317-318)
```csharp
// BEFORE:
if (frequencyBands < 8 || frequencyBands > 32)
    throw new ArgumentOutOfRangeException(nameof(frequencyBands));

// AFTER:
if (frequencyBands < 4 || frequencyBands > 64)
    throw new ArgumentOutOfRangeException(nameof(frequencyBands));
```

## Technical Justification

- **4 bands**: Minimum useful for basic visualization (bass, low-mid, high-mid, treble)
- **64 bands**: Maximum reasonable for detailed spectrum analysis without excessive overhead
- **Consistency**: Both ApplicationSettings and FrequencyAnalyzer now use the same constraints
- **Backwards Compatibility**: Existing settings with 4-7 bands will now work correctly

## Expected Result

The application should now start successfully with any FrequencyBands value between 4-64, including the current setting of 4.

## Files Modified

- ✅ `TaskbarEqualizer.Core\Audio\FrequencyAnalyzer.cs` (2 validation points updated)

## Test Status

Ready for testing - the application should now start without the ArgumentException.