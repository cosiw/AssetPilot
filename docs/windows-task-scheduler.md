# 계좌 스냅샷 작업 스케줄러 설정

`AssetPilot.SnapshotWorker`는 실행할 때마다 한 번만 동작하고 종료한다. KST 기준 평일 16:00~24:00 외에는 API를 호출하지 않고 성공 코드(`0`)로 종료한다.

## 시장 데이터 Worker

`AssetPilot.MarketDataWorker`는 KODEX 200의 일봉과 5분봉을 수집하는 별도 단발 작업이다. 계좌 스냅샷 작업과 분리해 등록한다.

- 트리거: 월요일~금요일 `16:05` 시작, 실패 시 `10분` 간격으로 최대 `3회` 다시 시작
- 동작: 배포된 `AssetPilot.MarketDataWorker.exe` 또는 `dotnet AssetPilot.MarketDataWorker.dll`
- 실행 계정: 키움 Credential Manager를 소유한 현재 Windows 사용자
- 데이터 위치: 기본 `%LOCALAPPDATA%\\AssetPilot\\data`; 다른 위치가 필요하면 `AssetPilot__MarketDataRoot` 환경 변수 지정
- 동시 실행: 이미 실행 중이면 새 인스턴스를 시작하지 않음

## 배포

PowerShell에서 다음 명령을 실행한다.

```powershell
dotnet publish .\src\AssetPilot.SnapshotWorker\AssetPilot.SnapshotWorker.csproj -c Release -r win-x64 --self-contained false -o .\publish\SnapshotWorker
```

SQLite 파일은 `%LOCALAPPDATA%\AssetPilot\assetpilot.db`에 만들어진다. Kiwoom 계좌 Credential Manager 항목을 만든 동일한 Windows 사용자로 Worker를 실행해야 한다.

## 작업 만들기

작업 스케줄러에서 **작업 만들기**를 선택한다.

- 일반: Credential Manager를 보유한 Windows 사용자로 설정하고, **사용자가 로그온한 경우에만 실행**을 선택한다.
- 트리거: 매주, 월요일~금요일, 시작 시간 `16:00`. 고급 설정에서 `1시간마다 반복`, 기간 `8시간`으로 설정한다.
- 동작: 프로그램/스크립트에 `dotnet.exe`, 인수에 `AssetPilot.SnapshotWorker.dll`, 시작 위치에 배포 폴더를 지정한다. self-contained로 배포했다면 프로그램/스크립트에 `AssetPilot.SnapshotWorker.exe`를 지정한다.
- 설정: **요청 시 작업 실행 허용**을 켠다. 이미 실행 중인 경우에는 새 인스턴스를 시작하지 않도록 설정한다.
- 실패 시: `10분` 간격으로 `3회` 다시 시작하도록 설정한다.

Worker는 한 번만 실행되므로 작업 스케줄러가 반복 실행을 담당한다. 휴장일은 별도로 판별하지 않으며, 주말만 실행 대상에서 제외한다. 로그와 출력에는 계좌번호, 앱 키, 비밀키 같은 Credential 값이 기록되지 않는다.
