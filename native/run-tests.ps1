$ErrorActionPreference = 'Stop'
$vcvars = 'C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat'
$testDir = Join-Path $PSScriptRoot 'obj\tests'
New-Item -ItemType Directory -Force -Path $testDir | Out-Null
$command = '"' + $vcvars + '" && cd /d "' + (Resolve-Path $PSScriptRoot).Path + '" && cl /nologo /MT /EHsc /O1 /std:c++17 /DUNICODE /D_UNICODE /D_WIN32_WINNT=0x0601 /DWINVER=0x0601 /Foobj\tests\ tests.cpp /link /SUBSYSTEM:CONSOLE /OUT:obj\tests\tests.exe && obj\tests\tests.exe'
cmd.exe /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw "Native tests failed with exit code $LASTEXITCODE" }
