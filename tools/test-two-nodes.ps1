<#
  한 PC에서 두 인스턴스를 서로 다른 노드(PC-A / PC-B)로 띄워 멀티 PC 공 전달을 눈으로 테스트한다.

  각 인스턴스는 --profile 로 설정 파일이 분리되므로(relay.A.json / relay.B.json,
  settings.A.json / settings.B.json) 서로 간섭하지 않는다.

  사용:
    powershell -ExecutionPolicy Bypass -File tools\test-two-nodes.ps1 -Server wss://slimey-relay.throwme.workers.dev

  테스트 방법:
    A 슬라임을 화면 오른쪽으로 세게 던지면 → 사라지고 → B 슬라임이 왼쪽에서 들어온다.
    (두 인스턴스가 같은 화면을 공유하므로, "오른쪽으로 나가 왼쪽에서 재등장"하는 형태로 보인다.)

  종료: 각 슬라임 우클릭 → 종료, 또는  Get-Process Slimey | Stop-Process
#>
param(
    [Parameter(Mandatory = $true)][string]$Server,
    [string]$Room = "TEST-1",
    [string]$Secret = "pw123",
    [string]$NodeA = "PC-A",
    [string]$NodeB = "PC-B",
    [string]$Exe = ""
)

$ErrorActionPreference = "Stop"

if (-not $Exe) {
    $root = Split-Path -Parent $PSScriptRoot
    $Exe = Join-Path $root "src\Slimey\bin\Release\net8.0-windows\win-x64\publish\Slimey.exe"
}
if (-not (Test-Path $Exe)) {
    Write-Error "Slimey.exe 를 찾을 수 없습니다: $Exe`n먼저 publish 하세요:`n  dotnet publish src/Slimey/Slimey.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true"
}

Write-Output "exe    : $Exe"
Write-Output "server : $Server"
Write-Output "room   : $Room"
Write-Output ""

# 각 노드의 좌·우 모두 상대에게 연결 → 어느 쪽으로 던져도 넘어간다(닫힌 고리).
#   오른쪽으로 나가면 상대의 왼쪽으로 들어가고, 왼쪽으로 나가면 상대의 오른쪽으로 들어간다.
#   (한쪽만 연결하면 공을 받은 노드는 반대 방향만 열려 "번갈아" 동작한다.)
$argsA = @(
    "--profile=A", "--node=$NodeA", "--server=$Server", "--room=$Room", "--secret=$Secret",
    "--link=Right:${NodeB}:Left", "--link=Left:${NodeB}:Right"
)
$argsB = @(
    "--profile=B", "--node=$NodeB", "--server=$Server", "--room=$Room", "--secret=$Secret",
    "--link=Right:${NodeA}:Left", "--link=Left:${NodeA}:Right"
)

Write-Output "[1/2] $NodeA 시작 (방 생성·공 소유)..."
Start-Process -FilePath $Exe -ArgumentList $argsA | Out-Null
Start-Sleep -Seconds 3   # A 가 먼저 방을 만들고 공을 소유하도록

Write-Output "[2/2] $NodeB 시작..."
Start-Process -FilePath $Exe -ArgumentList $argsB | Out-Null
Start-Sleep -Seconds 2

Write-Output ""
Write-Output "실행된 Slimey 프로세스:"
Get-Process Slimey -ErrorAction SilentlyContinue | Select-Object Id, StartTime | Format-Table -AutoSize | Out-String | Write-Output

Write-Output @"
--- 확인 방법 ---
1) 슬라임 우클릭 → 설정 → '멀티 PC' 탭에서 상태가 '연결됨 ✓' 인지 확인
   (두 인스턴스 중 공을 가진 쪽만 화면에 보입니다 — 처음엔 $NodeA)
2) 보이는 슬라임을 화면 '오른쪽 끝 밖으로' 세게 던지기
   → 사라진 뒤 상대 인스턴스가 화면 '왼쪽'에서 공을 받아 등장
3) 이제 좌/우 아무 방향으로 던져도 계속 넘어갑니다(양방향 연결).
   왼쪽으로 던지면 상대의 오른쪽에서, 오른쪽으로 던지면 상대의 왼쪽에서 등장.

종료: Get-Process Slimey | Stop-Process
로그: %LOCALAPPDATA%\Slimey\slimey.log
"@
