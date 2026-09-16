param([Parameter(Mandatory=$true)][string]$SevenZip, [Parameter(Mandatory=$true)][string]$Rar, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$resourcePath = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\strings.en.json'
$projectDir = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot ('.generated-engines-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectDir 'src') -Filter '*.cs' | Where-Object { $_.Name -notin @('MainForm.cs','ImePasswordBox.cs','StorageForm.cs','SettingsForm.cs','ModeSwitch.cs') } | ForEach-Object FullName)
$testExe = Join-Path $PSScriptRoot 'EngineTests.exe'
& $compiler /nologo /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.ServiceProcess.dll "/resource:$resourcePath,strings.en.json" /utf8output /target:exe /platform:x64 /r:System.Security.dll /r:System.Web.Extensions.dll /r:System.Core.dll "/win32manifest:$projectDir/src/app.manifest" "/out:$testExe" @sources (Join-Path $PSScriptRoot 'EngineTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Engine tests did not compile' }
Copy-Item -LiteralPath (Join-Path $projectDir 'src/拆包助手.exe.config') -Destination ($testExe + '.config') -Force
& $testExe $OutputDirectory $SevenZip $Rar
if ($LASTEXITCODE -ne 0) { throw 'Engine tests failed' }
