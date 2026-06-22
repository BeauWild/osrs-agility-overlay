$ErrorActionPreference = "Stop"

$Project = ".\OSRSAgilityOverlay.csproj"
$Output = ".\FinalExe"
$Zip = ".\OSRSAgilityOverlay-win-x64.zip"

Write-Host "Killing old overlay processes..." -ForegroundColor Cyan
Get-Process OSRSAgilityOverlay -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Cleaning old build output..." -ForegroundColor Cyan
Remove-Item $Output -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $Zip -Force -ErrorAction SilentlyContinue

Write-Host "Restoring packages..." -ForegroundColor Cyan
dotnet restore $Project
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

Write-Host "Publishing OSRS Agility Overlay v0.1.0..." -ForegroundColor Cyan
dotnet publish $Project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:EnableCompressionInSingleFile=true `
  -o $Output
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Copy-Item ".\markers.json" "$Output\markers.json" -Force

Write-Host "Creating release ZIP..." -ForegroundColor Cyan
Compress-Archive -Path "$Output\*" -DestinationPath $Zip -Force

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "EXE: $Output\OSRSAgilityOverlay.exe" -ForegroundColor Yellow
Write-Host "ZIP: $Zip" -ForegroundColor Yellow
Write-Host ""
pause
