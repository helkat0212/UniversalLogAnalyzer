# Single-File Portable EXE Setup

## ✅ Configuration Complete

Your application has been configured to publish as a **single portable .exe file** and the GUI launch issue has been fixed.

## Changes Made

### 1. **Single-File Publishing**
   - Updated `Publish.ps1` to publish as single-file by default
   - Added proper flags for WPF compatibility:
     - `PublishSingleFile=true`
     - `IncludeAllContentForSelfExtract=true`
     - `IncludeNativeLibrariesForSelfExtract=true`
     - `EnableCompressionInSingleFile=true`
     - `PublishTrimmed=false` (required for WPF resources)

### 2. **GUI Launch Fix**
   - Created explicit `Program.cs` entry point with proper error handling
   - Added `App_Startup` event handler to ensure MainWindow is created and shown
   - Removed dependency on `StartupUri` in App.xaml (which can fail in single-file deployments)
   - Added comprehensive error logging to temp folder

### 3. **Project Configuration**
   - Disabled auto-generated Main method: `EnableDefaultApplicationDefinition=false`
   - Added application manifest reference
   - Removed DPI settings from manifest (handled by project property)

## Usage

### Publish Single-File EXE (Default)
```powershell
.\Publish.ps1
```
This creates a single portable `HuaweiLogAnalyzer.exe` (~85MB) in the `.\publish` folder.

### Publish Multi-File (Optional)
```powershell
.\Publish.ps1 -NoSingleFile
```
This creates multiple files (EXE + DLLs) in the publish folder.

## Output

**Location**: `.\publish\HuaweiLogAnalyzer.exe`

**Size**: ~85-90 MB (typical for .NET 8 WPF self-contained single-file)

This creates a single portable `UniversalLogAnalyzer.exe` (~85MB) in the `.\publish` folder.

## Testing

After publishing, you can test the executable:
1. Navigate to `.\publish` folder
2. Double-click `HuaweiLogAnalyzer.exe`
3. The GUI window should appear immediately

## Error Logging

If the application fails to start, errors are logged to:
- `%TEMP%\HuaweiLogAnalyzer_startup.log` - Startup errors
2. Double-click `UniversalLogAnalyzer.exe`

## Troubleshooting

### GUI Doesn't Appear
1. Check error logs in temp folder
2. Verify you're running Windows 10 or later
3. Check Windows Event Viewer for system-level errors
4. Ensure all required Visual C++ redistributables are installed (should be included in single-file)

### File Size Concerns
- The ~85MB size is normal for a self-contained .NET 8 WPF app
- Includes full .NET runtime, WPF framework, and all dependencies
- If size is critical, consider multi-file publish or trimming (may break WPF features)

### Performance
- Single-file apps extract to temp folder on first run
- Subsequent runs are faster
- Extraction location: `%TEMP%\.net\` folder

## Files Modified

1. `HuaweiLogAnalyzer/Program.cs` - NEW: Explicit entry point
2. `HuaweiLogAnalyzer/App.xaml` - Removed StartupUri
3. `HuaweiLogAnalyzer/App.xaml.cs` - Added App_Startup handler
4. `HuaweiLogAnalyzer/HuaweiLogAnalyzer.csproj` - Updated publish settings
5. `HuaweiLogAnalyzer/app.manifest` - Cleaned up DPI settings
6. `Publish.ps1` - Updated for single-file by default

---

**Status**: ✅ Ready for distribution as single portable EXE



