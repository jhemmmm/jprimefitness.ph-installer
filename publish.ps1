# Builds the single-file, self-contained JPrimePanel.exe into .\dist\
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "jprimefitness.ph-installer\jprimefitness.ph-installer.csproj"
$out = Join-Path $PSScriptRoot "dist"
dotnet publish $proj -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
Get-ChildItem $out -Filter *.exe | ForEach-Object { "{0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB) }
