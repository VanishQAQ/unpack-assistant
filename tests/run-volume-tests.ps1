param([string]$RarSample)
$ErrorActionPreference = 'Stop'
$resourcePath = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\strings.en.json'
$project = Split-Path $PSScriptRoot -Parent
$output = Join-Path $PSScriptRoot ('分卷测试-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $project 'src') -Filter '*.cs' | Where-Object Name -notin @('MainForm.cs','ImePasswordBox.cs','StorageForm.cs','SettingsForm.cs','ModeSwitch.cs') | ForEach-Object FullName)
$exe = Join-Path $output 'VolumeTests.exe'
& $compiler /nologo /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.ServiceProcess.dll "/resource:$resourcePath,strings.en.json" /target:exe /platform:x64 /reference:System.Security.dll /reference:System.Web.Extensions.dll /reference:System.Core.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll ('/out:'+$exe) @sources (Join-Path $PSScriptRoot 'VolumeTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Volume test compilation failed' }
if ($RarSample) { & $exe $output $RarSample } else { & $exe $output }
if ($LASTEXITCODE -ne 0) { throw 'Volume tests failed' }
