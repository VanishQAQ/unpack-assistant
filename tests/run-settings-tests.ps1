$ErrorActionPreference = 'Stop'
$resourcePath = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\strings.en.json'
$project = Split-Path $PSScriptRoot -Parent
$runtime = Join-Path $PSScriptRoot '.generated-settings'
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
$app = Join-Path $project '便携版-0.4.1\拆包助手.exe'
Copy-Item -LiteralPath $app -Destination $runtime -Force
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach ($name in @('ConfigurationTests','PreferencesUiTests','PasswordPersistenceTests')) {
    & $compiler /nologo /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.ServiceProcess.dll "/resource:$resourcePath,strings.en.json" /target:exe /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Security.dll ('/reference:'+$app) ('/out:'+(Join-Path $runtime ($name+'.exe'))) (Join-Path $PSScriptRoot ($name+'.cs'))
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
}
$fixture = Join-Path $runtime ('fixture-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
& (Join-Path $runtime 'ConfigurationTests.exe') $fixture
if ($LASTEXITCODE -ne 0) { throw 'Atomic write tests failed' }
foreach ($mode in @('write','read')) {
    & (Join-Path $runtime 'PreferencesUiTests.exe') $mode (Join-Path $fixture 'preferences')
    if ($LASTEXITCODE -ne 0) { throw 'Preferences tests failed' }
}
foreach ($mode in @('write','read','removed','empty')) {
    & (Join-Path $runtime 'PasswordPersistenceTests.exe') $mode (Join-Path $fixture 'passwords.dat')
    if ($LASTEXITCODE -ne 0) { throw 'Password tests failed' }
}
