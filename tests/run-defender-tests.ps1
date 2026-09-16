param([switch]$Live)
$ErrorActionPreference = 'Stop'
$resourcePath = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\strings.en.json'
$project = Split-Path $PSScriptRoot -Parent
$generated = Join-Path $PSScriptRoot '.defender'
New-Item -ItemType Directory -Path $generated -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $project 'src') -Filter '*.cs' | Where-Object Name -notin @('MainForm.cs','ImePasswordBox.cs','StorageForm.cs','SettingsForm.cs','ModeSwitch.cs') | ForEach-Object FullName)
$exe = Join-Path $generated 'DefenderTests.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe') /nologo /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.ServiceProcess.dll "/resource:$resourcePath,strings.en.json" /utf8output /target:exe /platform:x64 /r:System.Security.dll /r:System.Web.Extensions.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/out:$exe" @sources (Join-Path $PSScriptRoot 'DefenderTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
$fixture = Join-Path $generated ('run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if ($Live) { & $exe $fixture --live } else { & $exe $fixture }
if ($LASTEXITCODE -ne 0) { throw 'Scanner tests failed' }
