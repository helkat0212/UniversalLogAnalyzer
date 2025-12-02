param(
    [string]$OutputDir = ".\publish",
    [string]$Configuration = "Release"
)

$projectPath = ".\HuaweiLogAnalyzer\HuaweiLogAnalyzer.csproj"
$publishPath = $OutputDir

Write-Host "Publishing portable executable..."
Write-Host "Configuration: $Configuration"
Write-Host "Output Directory: $publishPath"

# Clean previous publish
if (Test-Path $publishPath) {
    Write-Host "Cleaning previous publish..."
    Remove-Item -Path $publishPath -Recurse -Force
}

dotnet publish $projectPath --configuration $Configuration --runtime win-x64 --self-contained --output $publishPath -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded

if ($LASTEXITCODE -eq 0) {
    Write-Host "Publish Successful!"
    Write-Host "Output Location: $publishPath"
    $exe = Get-ChildItem -Path $publishPath -Filter "UniversalLogAnalyzer.exe" | Select-Object -First 1
    if ($exe) {
        Write-Host "Portable Executable: $($exe.FullName)"
    }
} else {
    Write-Host "Publish Failed!"
    exit 1
}
