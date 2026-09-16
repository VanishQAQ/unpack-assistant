param([string]$OutputDirectory, [switch]$CompileOnly)
$ErrorActionPreference = 'Stop'
$resourcePath = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\strings.en.json'
$projectDir = Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot ('运行-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectDir 'src') -Filter '*.cs' | Where-Object { $_.Name -notin @('MainForm.cs','ImePasswordBox.cs','StorageForm.cs','SettingsForm.cs','ModeSwitch.cs') } | ForEach-Object FullName)
$testExe = Join-Path $PSScriptRoot 'Tests.exe'
& $compilerPath /nologo /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.ServiceProcess.dll "/resource:$resourcePath,strings.en.json" /utf8output /optimize+ /target:exe /platform:x64 /reference:System.Security.dll /reference:System.Web.Extensions.dll /reference:System.Core.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/win32manifest:$projectDir\src\app.manifest" "/out:$testExe" @sources (Join-Path $PSScriptRoot 'Tests.cs')
if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
Copy-Item -LiteralPath (Join-Path $projectDir 'src\拆包助手.exe.config') -Destination "$testExe.config" -Force
if ($CompileOnly) { return }
& $testExe $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw '测试未全部通过。' }
