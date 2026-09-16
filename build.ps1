$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compilerPath)) { throw '需要 Windows .NET Framework 4.8。' }
$binDir = Join-Path $projectDir '便携版-0.4.1'
New-Item -ItemType Directory -Path $binDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectDir 'src\拆包助手.exe.config') -Destination $binDir -Force
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectDir 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compilerPath /nologo /utf8output "/resource:$projectDir\src\strings.en.json,strings.en.json" /optimize+ /target:winexe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.Core.dll /reference:System.ServiceProcess.dll "/win32manifest:$projectDir\src\app.manifest" "/out:$binDir\拆包助手.exe" @sources
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Write-Output "已生成：$binDir\拆包助手.exe"
