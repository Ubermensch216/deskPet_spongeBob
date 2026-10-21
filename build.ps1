# 비키니 친구들 빌드 스크립트
# .NET SDK 의 Roslyn(csc.dll)로 .NET Framework 4.8 대상 단일 EXE 를 만든다. (설치 없이 EXE 한 개로 실행)
param([string]$Out = "$PSScriptRoot\output\bikini_friends.exe")
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dotnet = (Get-Command dotnet).Source
$csc = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\dotnet\sdk\*\Roslyn\bincore\csc.dll", 'C:\Program Files\dotnet\sdk\*\Roslyn\bincore\csc.dll' -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $csc) { throw 'Roslyn csc.dll 을 찾지 못했어요. .NET SDK 를 설치해 주세요.' }
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs = 'mscorlib', 'System', 'System.Core', 'System.Drawing', 'System.Windows.Forms', 'System.Xml', 'System.Xml.Linq'
New-Item -ItemType Directory -Force "$root\output", "$root\obj" | Out-Null

function Invoke-Csc([string]$target, [string]$icon) {
    $rsp = New-Object System.Collections.Generic.List[string]
    $rsp.Add('/noconfig'); $rsp.Add('/nostdlib+'); $rsp.Add('/target:winexe'); $rsp.Add('/optimize+'); $rsp.Add('/platform:anycpu')
    $rsp.Add('/nowarn:CS0649,CS0169,CS0414,CS0219'); $rsp.Add('/utf8output'); $rsp.Add('/langversion:latest')
    $rsp.Add('/out:"' + $target + '"'); $rsp.Add('/win32manifest:"' + "$root\assets\app.manifest" + '"')
    if ($icon -and (Test-Path $icon)) { $rsp.Add('/win32icon:"' + $icon + '"') }
    foreach ($r in $refs) { $rsp.Add('/r:"' + "$fw\$r.dll" + '"') }
    Get-ChildItem "$root\src\*.cs" | ForEach-Object { $rsp.Add('"' + $_.FullName + '"') }
    $rspPath = "$root\obj\build.rsp"
    [IO.File]::WriteAllLines($rspPath, $rsp, (New-Object Text.UTF8Encoding($true)))
    & $dotnet $csc.FullName "@$rspPath"
    if ($LASTEXITCODE -ne 0) { throw "빌드 실패 (exit $LASTEXITCODE)" }
}

# 아이콘이 없으면: 아이콘 없이 한 번 빌드해 실행 파일이 그린 얼굴로 .ico 를 만든 뒤, 다시 빌드한다.
$icon = "$root\assets\bikini_friends.ico"
if (-not (Test-Path $icon)) {
    $tmp = "$root\obj\icon-maker.exe"
    Invoke-Csc $tmp $null
    Start-Process -FilePath $tmp -ArgumentList '--make-icon', ('"' + $icon + '"') -Wait
    if (-not (Test-Path $icon)) { throw '아이콘 생성 실패' }
}
Invoke-Csc $Out $icon
Write-Host ("빌드 완료: {0} ({1:N0} bytes)" -f $Out, (Get-Item $Out).Length)
