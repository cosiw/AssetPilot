# 일별 계좌 스냅샷 설계

## 목표

Windows 개인 PC에서 별도 C# Worker를 매시간 실행한다. Worker는 한국 표준시 기준 월요일부터 금요일의 16:00 이상 24:00 미만에만 키움에 등록된 모든 계좌를 조회한다. 계좌별로 해당 날짜의 기록이 없을 때만 계좌 평가 요약과 보유 종목 전체를 SQLite에 저장한다.

WPF 앱은 계속 실행하지 않는다. Windows 작업 스케줄러가 `AssetPilot.SnapshotWorker.exe`를 호출하고, Worker는 작업을 끝내면 종료한다.

## 결정과 범위

- 저장 대상은 Credential Manager에 등록된 모든 계좌다.
- 토요일과 일요일은 저장하지 않는다. 평일 휴장일은 별도 판정하지 않는다.
- 같은 계좌와 날짜의 스냅샷은 한 건만 보관한다.
- 누락된 과거 날짜를 현재 계좌 값으로 채우지 않는다.
- 계좌번호, App Key, Secret Key, 키움 접근 토큰, 실전/모의 환경은 SQLite에 저장하지 않는다.
- 장중 다중 스냅샷과 WPF 이력 화면은 이번 범위에서 제외한다.

`kt00018`의 총자산·예수금·전체 매입금액·평가금액·평가손익·수익률 및 종목별 보유 수량·평균단가·현재가·매입금액·평가금액·평가손익·수익률은 모두 저장한다. `kt00015`의 입금·출금·매수·매도·배당 내역은 거래 원장에 누적 저장한다.

## 구성

```text
Windows 작업 스케줄러 (월~금, 매시간)
    -> AssetPilot.SnapshotWorker.exe
        -> IKiwoomAccountStore: 모든 Credential 조회
        -> IKiwoomPortfolioClient: 계좌별 kt00018 Portfolio 조회
        -> IKiwoomTransactionClient: 계좌별 kt00015 거래 내역 조회
        -> IAccountSnapshotStore: SQLite 스냅샷·거래 원장 저장

WPF 앱
    -> 같은 SQLite DB를 읽어 향후 이력 화면에 사용
```

새 Worker 프로젝트는 `AssetPilot.SnapshotWorker`로 둔다. Worker와 WPF는 `Core`, `Infrastructure`를 공유한다. `SnapshotWorker`는 UI·ViewModel을 참조하지 않는다.

SQLite 파일은 현재 Windows 사용자의 `%LOCALAPPDATA%\\AssetPilot\\assetpilot.db`에 둔다. Credential Manager는 같은 Windows 사용자 문맥에서 접근하므로, 작업 스케줄러도 해당 사용자로 실행한다.

## 계좌 식별자와 Credential 형식

계좌 하나는 기존처럼 Windows Generic Credential 하나와 대응한다.

```text
TargetName:     AssetPilot:Kiwoom:{계좌번호}
UserName:       App Key
CredentialBlob: SecretKey, Environment, AccountId
```

`AccountId`는 계좌 생성 시 한 번 생성하는 무작위 GUID이며 계좌의 안정적인 내부 식별자다. 계좌번호가 아니며 SQLite에는 GUID만 저장한다.

```json
{
  "SecretKey": "...",
  "Environment": "Real",
  "AccountId": "7f583a0e-0000-0000-0000-000000000000"
}
```

기존 Credential Blob에 `AccountId`가 없으면 `WindowsKiwoomAccountStore`가 계좌를 열거하거나 읽을 때 GUID를 한 번 생성하고, 기존 App Key·Secret Key·Environment를 보존한 새 Blob으로 즉시 갱신한다. 이후에는 같은 GUID를 계속 반환한다. 계좌를 삭제하면 Credential과 그 계좌의 미래 저장만 사라지며, 기존 SQLite 이력은 삭제하지 않는다.

`KiwoomAccountProfile`은 `AccountId`, `AccountNumber`, `Environment`를 제공한다. `AccountId`는 비밀값이 아니지만 화면에 표시하거나 로그에 출력하지 않는다.

## SQLite 스키마

금액은 한국 원화의 정수 단위로 `INTEGER`에 저장한다. 수익률은 퍼센트의 1/100 단위인 basis point 정수로 저장한다. 예를 들어 `1.23%`는 `123`, `-0.05%`는 `-5`다. SQLite의 부동소수점·느슨한 `DECIMAL` 표현을 사용하지 않는다.

```sql
CREATE TABLE account_snapshots (
    id INTEGER PRIMARY KEY,
    account_id TEXT NOT NULL,
    snapshot_date TEXT NOT NULL,
    captured_at_utc TEXT NOT NULL,
    cash_balance INTEGER NOT NULL,
    total_asset_amount INTEGER NOT NULL,
    total_purchase_amount INTEGER NOT NULL,
    total_evaluation_amount INTEGER NOT NULL,
    total_profit_loss INTEGER NOT NULL,
    total_profit_rate_bps INTEGER NOT NULL,
    position_count INTEGER NOT NULL,
    source TEXT NOT NULL,
    UNIQUE (account_id, snapshot_date)
);

CREATE TABLE position_snapshots (
    account_snapshot_id INTEGER NOT NULL,
    market TEXT NOT NULL,
    stock_code TEXT NOT NULL,
    stock_name TEXT NOT NULL,
    quantity INTEGER NOT NULL,
    average_purchase_price INTEGER NOT NULL,
    current_price INTEGER NOT NULL,
    purchase_amount INTEGER NOT NULL,
    evaluation_amount INTEGER NOT NULL,
    profit_loss INTEGER NOT NULL,
    profit_rate_bps INTEGER NOT NULL,
    PRIMARY KEY (account_snapshot_id, market, stock_code),
    FOREIGN KEY (account_snapshot_id) REFERENCES account_snapshots(id) ON DELETE CASCADE
);

CREATE INDEX ix_position_snapshots_stock
    ON position_snapshots (market, stock_code, account_snapshot_id);

CREATE TABLE account_transactions (
    id INTEGER PRIMARY KEY,
    account_id TEXT NOT NULL,
    source_transaction_key TEXT NOT NULL,
    transaction_date TEXT NOT NULL,
    occurred_at_utc TEXT NULL,
    transaction_type TEXT NOT NULL,
    broker_transaction_type TEXT NOT NULL,
    stock_code TEXT NULL,
    stock_name TEXT NULL,
    quantity INTEGER NULL,
    unit_price INTEGER NULL,
    cash_amount INTEGER NOT NULL,
    fee_amount INTEGER NOT NULL,
    tax_amount INTEGER NOT NULL,
    captured_at_utc TEXT NOT NULL,
    source TEXT NOT NULL,
    UNIQUE (account_id, source_transaction_key)
);

CREATE INDEX ix_account_transactions_account_date
    ON account_transactions (account_id, transaction_date);
```

`account_snapshots.id`는 한 날짜·한 계좌의 스냅샷을 식별한다. `account_snapshots.account_id`는 어느 Credential 계좌의 기록인지 식별한다. `position_snapshots.account_snapshot_id`는 특정 일자의 특정 계좌 스냅샷에 종목을 연결한다. 종목명도 스냅샷에 저장해 이후 종목명이 바뀌거나 상장폐지돼도 당시 기록을 보존한다. `cash_balance`는 예수금이며, `total_asset_amount`는 키움이 반환한 총자산 원문 값을 보존한다.

`source`에는 초기 값으로 `kiwoom-kt00018`을 저장한다. `market`의 초기 값은 `KRX`다.

`account_transactions`는 입금·출금·매수·매도·배당 같은 현금흐름과 거래 이벤트를 보존한다. `transaction_type`은 앱이 정규화한 `Deposit`, `Withdrawal`, `Buy`, `Sell`, `Dividend` 중 하나이며, 키움의 원래 구분 값은 `broker_transaction_type`에 보존한다. 인식하지 못한 키움 구분 값은 원래 값을 보존한 채 `Other`로 저장한다.

`cash_amount`는 현금 잔고의 변화량이다. 입금·매도·배당은 양수, 출금·매수·수수료·세금은 음수로 저장한다. `fee_amount`와 `tax_amount`는 항상 양수 원 단위로 저장하며, 해당 값이 API에 없으면 `0`이다.

`source_transaction_key`는 키움이 제공하는 거래 고유 번호가 있으면 그 값으로 만든다. 고유 번호가 없는 응답 행은 계좌와 키움 원문 거래 식별 필드를 정규화해 만든 SHA-256 키를 사용한다. 이 키와 `UNIQUE(account_id, source_transaction_key)` 제약으로 매시간 재조회해도 같은 거래는 한 번만 저장한다.

## Worker 실행 흐름

1. 한국 표준시 현재 날짜와 시각을 읽는다.
2. 토·일이거나 시간이 16:00 미만 또는 다음 날 00:00 이상이면 성공으로 종료한다.
3. `IKiwoomAccountStore.GetAllAsync`로 모든 계좌를 읽는다. 계좌가 없으면 성공으로 종료한다.
4. 계좌별로 `(AccountId, 오늘 날짜)`가 `account_snapshots`에 없을 때만 `IKiwoomPortfolioClient.GetPortfolioAsync(accountNumber)`로 `kt00018` `Portfolio`를 조회한다.
5. 새 `Portfolio`가 있으면 계좌 요약 행과 모든 `PortfolioPosition` 행을 하나의 SQLite 트랜잭션으로 저장한다.
6. 모든 계좌에서 `IKiwoomTransactionClient`가 `kt00015`를 조회한다. 마지막 저장 거래일이 있으면 그 날짜부터 오늘까지, 없으면 오늘 하루를 조회하고 모든 연속 페이지를 읽는다.
7. 각 거래 행을 `account_transactions`에 idempotent upsert한다. 같은 거래는 `source_transaction_key` 유니크 제약으로 건너뛴다.
8. 다음 계좌를 계속 처리한다. 한 개 이상 실패하면 실패 내용을 비밀값 없이 기록하고 비정상 종료 코드로 끝낸다.

빈 포트폴리오도 유효한 일별 기록이다. 이 경우 `account_snapshots` 한 행을 저장하고 `position_count`는 `0`, `position_snapshots` 행은 `0`개다.

스냅샷 저장은 DB의 `UNIQUE(account_id, snapshot_date)` 제약으로도 중복을 막는다. 조회 뒤 다른 Worker가 먼저 저장한 경쟁 상황에서는 유니크 제약 위반을 이미 저장된 정상 결과로 처리하고 종료한다. API·DB 오류가 발생한 계좌는 해당 스냅샷의 헤더 행과 종목 행을 모두 롤백한다. 거래 동기화가 실패해도 이미 완성된 스냅샷을 되돌리지는 않으며, 다음 정시 실행이 `kt00015` 동기화를 다시 시도한다.

## Windows 작업 스케줄러

- 트리거: 매주 월요일부터 금요일
- 반복: 16:00 시작, 1시간 간격, 23:00 실행까지
- 실행 대상: 배포된 `AssetPilot.SnapshotWorker.exe`
- 사용자: Credential Manager를 소유한 현재 Windows 사용자
- 조건: 놓친 실행은 가능한 시점에 시작
- 실패 처리: 10분 간격으로 최대 3회 재시도

놓친 작업이 오전처럼 허용 시간 밖에 실행되더라도 Worker가 성공으로 즉시 끝난다. 정상적인 다음 16:00 실행이 그날의 첫 저장 기회가 된다.

## 오류, 보안, 운영

- Worker 로그와 예외 메시지에는 계좌번호, App Key, Secret Key, 접근 토큰을 넣지 않는다.
- 각 계좌는 독립적으로 트랜잭션 처리한다. 한 계좌 실패가 다른 계좌의 저장을 취소하지 않는다.
- 네트워크·키움 API·SQLite 오류는 해당 계좌의 스냅샷을 만들지 않는다. 작업 스케줄러 재시도와 다음 정시 실행이 다시 시도한다.
- SQLite 연결마다 foreign key enforcement를 켠다.
- DB 파일은 사용자 프로필의 LocalAppData 아래에 두며, 클라우드 동기화 폴더나 소스 저장소에 두지 않는다.

## 검증

- 새 계좌 저장 시 Credential Blob에 GUID `AccountId`가 포함되는지 검증한다.
- 기존 Credential에 `AccountId`가 없을 때 기존 비밀·환경을 보존하며 GUID를 한 번만 생성하는지 검증한다.
- 토·일과 허용 시간 밖에는 키움 API와 SQLite 저장을 호출하지 않는지 검증한다.
- 같은 날짜에 이미 저장된 계좌는 API 호출 없이 건너뛰는지 검증한다.
- 여러 계좌에서 각 계좌별 요약 한 행과 모든 종목 행을 저장하는지 검증한다.
- `kt00018`의 예수금과 총자산을 계좌 스냅샷에 저장하는지 검증한다.
- 빈 포트폴리오에서 헤더 한 행과 종목 행 없음이 저장되는지 검증한다.
- 중간 실패 시 해당 계좌의 헤더·종목 행이 함께 롤백되고 다른 계좌 처리는 계속되는지 검증한다.
- `UNIQUE(account_id, snapshot_date)`가 중복 저장을 거부하는지 검증한다.
- `kt00015`의 입금·출금·매수·매도·배당을 정규화해 거래 원장에 저장하는지 검증한다.
- 같은 `kt00015` 거래를 여러 번 읽어도 `source_transaction_key` 기준으로 한 번만 저장되는지 검증한다.
- 거래 동기화 실패가 이미 저장된 일별 스냅샷을 삭제하지 않으며 다음 실행에서 다시 시도되는지 검증한다.
