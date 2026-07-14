# Publish UEVietTranslator.App thành 1 file .exe self-contained cho Windows
# (không cần cài .NET runtime trên máy chạy) — xem docs/DECISIONS.md#adr-015.
#
# Chạy từ PowerShell, tại thư mục gốc repo:
#   .\scripts\publish-windows.ps1
#
# Output: publish\UEVietTranslator.App.exe (~50MB, single file).

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $repoRoot "publish"

dotnet publish (Join-Path $repoRoot "src\UEVietTranslator.App") `
    -c Release `
    -r win-x64 `
    -p:DebugType=None `
    -o $outputDir

Write-Host ""
Write-Host "Xong. File .exe: $outputDir\UEVietTranslator.App.exe"
Write-Host "Lưu ý: repak/retoc (dùng ở bước Repack) là 2 CLI ngoài, KHÔNG được đóng gói kèm exe này — xem docs/DECISIONS.md#adr-012."
