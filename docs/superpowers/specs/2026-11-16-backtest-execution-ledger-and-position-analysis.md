# 백테스트 체결 원장 및 포지션 분석 명세

## 목적

AssetPilot 백테스트는 매수와 매도 체결을 원본 행으로 보존한다. 추가 매수와 분할 매도를 지원할 수 있도록, 체결을 하나의 거래로 덮어쓰거나 합치지 않는다.

체결 원장을 바탕으로 보유 수량, 평균 매입가, 매도별 실현손익, 현금 및 포지션 사이클 분석값을 파생한다. 이 구조는 현재 단일 진입 전략과 이후의 추가 매수·분할 매도 전략에 공통으로 사용한다.

## 확정된 범위

### 포함

- 백테스트 실행별 매수·매도 체결 원장 저장
- 체결 순서에 따른 보유 수량과 가중평균 매입가 계산
- 각 매도 체결의 실현손익 계산
- 체결 직후의 포지션·현금 상태 기록
- 포지션이 `0주 → 보유 → 0주`가 되는 구간의 분석용 요약
- SQLite 원본 저장 및 CSV 내보내기 계약

### 제외

- 실제 주문, 부분 체결 조회, 주문 정정·취소
- 세금·수수료의 실제 증권사 규칙 구현
- FIFO/선입선출 로트별 손익 계산
- 추가 매수·분할 매도 매매 신호 자체의 구현
- MFE/MAE 계산 로직 구현. 이 명세의 포지션 사이클 구조를 기반으로 후속 분석 단위에서 구현한다.

비용 필드는 보관하되, 비용 모델이 도입되기 전 초기값은 0이다.

## 현재 상태

| 구성 요소 | 현재 동작 | 부족한 점 |
| --- | --- | --- |
| `BacktestExecution` | 매수·매도 시각, 가격, 수량, 매도 실현손익·사유를 기록 | 체결 ID, 종목, 비용, 체결 후 보유 상태가 없음 |
| `BacktestRunner` | 한 개 포지션의 진입·청산을 5분봉으로 재현 | 체결 원장을 기반으로 한 포지션 원장·사이클을 생성하지 않음 |
| `BacktestResult` | 체결과 진입 거부 진단을 반환 | 포지션 스냅샷·사이클을 반환하지 않음 |
| `SqliteBacktestRunStore` | 실행, 체결, 진단을 SQLite에 저장 | 포지션 스냅샷·사이클 테이블이 없음 |
| CSV 출력 | 없음 | 체결 원장 및 자산 변화 외부 분석 불가 |

관련 코드:

- `src/AssetPilot.Backtest/Execution/BacktestExecution.cs`
- `src/AssetPilot.Backtest/Execution/BacktestRunner.cs`
- `src/AssetPilot.Backtest/Execution/BacktestResult.cs`
- `src/AssetPilot.Backtest/Storage/BacktestRun.cs`
- `src/AssetPilot.Infrastructure/Backtests/SqliteBacktestRunStore.cs`

## 용어

| 용어 | 정의 |
| --- | --- |
| 체결(Execution) | 한 번의 매수 또는 매도. 원본 기록이며 절대 합치지 않는다. |
| 포지션(Position) | 특정 시점의 보유 수량, 평균 매입가, 현금 영향으로 구성된 상태. |
| 포지션 스냅샷(Position Snapshot) | 각 체결 직후의 포지션 상태. |
| 포지션 사이클(Position Cycle) | 보유 수량이 0에서 양수가 된 첫 매수부터, 다시 0이 된 마지막 매도까지의 분석용 구간. |
| 실현손익(Realized PnL) | 매도 시점에 확정된 손익. 평균 매입가 기준으로 계산한다. |

## 데이터 모델

### 체결 원장

```csharp
public sealed record BacktestExecution(
    Guid ExecutionId,
    DateTime ExecutedAt,
    ExecutionSide Side,
    decimal Price,
    decimal Quantity,
    decimal GrossAmount,
    decimal Fee,
    decimal Tax,
    decimal? RealizedProfit = null,
    ExitReason? Reason = null);
```

- `GrossAmount = Price * Quantity`
- `Fee`와 `Tax`는 0 이상이다.
- `RealizedProfit`은 매도 체결에만 존재한다.
- `Reason`은 전략 청산 매도에만 존재한다. 수동·향후 확장 사유도 enum으로 추가 가능하게 한다.
- 종목 코드와 전략 버전은 실행 단위인 `BacktestRun`에 이미 귀속되므로, 초기 체결 레코드에서는 중복하지 않는다. CSV 내보내기에서 `BacktestRun`과 조인하여 출력한다.

### 포지션 스냅샷

```csharp
public sealed record BacktestPositionSnapshot(
    Guid ExecutionId,
    DateTime RecordedAt,
    decimal Cash,
    decimal Quantity,
    decimal AverageEntryPrice,
    decimal PositionCost,
    decimal RealizedProfitTotal);
```

- 체결마다 정확히 하나를 만든다.
- 보유 수량이 0이면 `AverageEntryPrice`와 `PositionCost`는 0이다.
- `Cash`는 해당 체결 금액과 비용을 반영한 뒤의 현금이다.
- 초기 범위에서는 체결 직후 스냅샷만 기록한다. 장중 평가자산은 별도 자산 곡선 단위에서 기록한다.

### 포지션 사이클

```csharp
public sealed record BacktestPositionCycle(
    Guid CycleId,
    Guid OpeningExecutionId,
    Guid ClosingExecutionId,
    DateTime OpenedAt,
    DateTime ClosedAt,
    decimal BoughtQuantity,
    decimal SoldQuantity,
    decimal AverageEntryPrice,
    decimal AverageExitPrice,
    decimal RealizedProfit,
    decimal ReturnRate,
    ExitReason? ClosingReason);
```

- 첫 매수로 수량이 0에서 양수가 될 때 새 사이클을 연다.
- 추가 매수와 부분 매도는 같은 사이클에 속한다.
- 마지막 매도로 수량이 0이 될 때 사이클을 닫는다.
- `ClosingReason`은 전량 청산을 만든 마지막 매도의 사유다.
- 열린 포지션은 사이클로 저장하지 않는다. 백테스트가 끝난 뒤에도 수량이 남으면 스냅샷만 남긴다.

## 포지션 계산 규칙

### 매수

```text
새 평균 매입가 =
  (기존 수량 × 기존 평균 매입가 + 매수 수량 × 매수가 + 매수 비용)
  ÷ (기존 수량 + 매수 수량)

새 현금 = 기존 현금 - 매수금액 - 매수 수수료 - 매수 세금
```

### 매도

```text
실현손익 =
  (매도가 - 기존 평균 매입가) × 매도 수량
  - 매도 수수료 - 매도 세금

새 현금 = 기존 현금 + 매도금액 - 매도 수수료 - 매도 세금
새 수량 = 기존 수량 - 매도 수량
```

- 매도 수량은 보유 수량을 초과하면 안 된다. 초과 시 백테스트 실행을 실패시킨다.
- 부분 매도 뒤 평균 매입가는 유지한다.
- 전량 매도 뒤 수량·평균 매입가·포지션 원가는 0으로 초기화한다.
- 동일 시각의 체결은 백테스트 엔진이 생성한 순서를 사용한다. SQLite 조회에는 `execution_sequence`을 보관해 순서를 재현한다.

## 처리 흐름

```text
BacktestRunner
  → BacktestExecution 목록 생성
  → PositionLedger가 체결 순서대로 처리
      → BacktestPositionSnapshot 생성
      → 수량이 0이 되면 BacktestPositionCycle 생성
  → BacktestResult에 세 목록 반환
  → SqliteBacktestRunStore가 단일 트랜잭션으로 저장
  → CSV Exporter가 SQLite 실행 ID 기준으로 출력
```

`PositionLedger`는 `BacktestRunner`와 분리된 순수 계산 클래스로 둔다. 추가 매수와 분할 매도는 Runner의 체결 생성 규칙을 확장하기만 하면 동일한 Ledger가 처리한다.

## SQLite 스키마

기존 `backtest_executions` 테이블은 마이그레이션 대신 새 설치와 기존 DB 모두를 지원하도록 누락 열을 추가한다. SQLite의 `ALTER TABLE ADD COLUMN`만 사용한다.

```sql
CREATE TABLE IF NOT EXISTS backtest_position_snapshots (
    run_id TEXT NOT NULL,
    execution_id TEXT NOT NULL,
    execution_sequence INTEGER NOT NULL,
    recorded_at TEXT NOT NULL,
    cash TEXT NOT NULL,
    quantity TEXT NOT NULL,
    average_entry_price TEXT NOT NULL,
    position_cost TEXT NOT NULL,
    realized_profit_total TEXT NOT NULL,
    PRIMARY KEY (run_id, execution_id),
    FOREIGN KEY (run_id) REFERENCES backtest_runs(id)
);

CREATE TABLE IF NOT EXISTS backtest_position_cycles (
    run_id TEXT NOT NULL,
    cycle_id TEXT NOT NULL,
    opening_execution_id TEXT NOT NULL,
    closing_execution_id TEXT NOT NULL,
    opened_at TEXT NOT NULL,
    closed_at TEXT NOT NULL,
    bought_quantity TEXT NOT NULL,
    sold_quantity TEXT NOT NULL,
    average_entry_price TEXT NOT NULL,
    average_exit_price TEXT NOT NULL,
    realized_profit TEXT NOT NULL,
    return_rate TEXT NOT NULL,
    closing_reason INTEGER NULL,
    PRIMARY KEY (run_id, cycle_id),
    FOREIGN KEY (run_id) REFERENCES backtest_runs(id)
);
```

`backtest_executions`에는 다음 열을 추가한다.

```sql
execution_id TEXT NULL,
execution_sequence INTEGER NULL,
gross_amount TEXT NULL,
fee TEXT NULL,
tax TEXT NULL
```

기존 실행 데이터는 새 열이 `NULL`이어도 읽을 수 있어야 한다. 새로 저장하는 실행은 모든 열을 채운다.

## CSV 계약

SQLite가 원본이며 CSV는 실행 ID를 지정해 다시 생성할 수 있는 출력물이다. 숫자는 invariant culture, 날짜는 ISO 8601, UTF-8 헤더 포함 형식을 사용한다.

### `backtest_summary.csv`

한 실행당 한 행. 기존 요약 성과와 전략 버전·파라미터를 포함한다.

### `backtest_executions.csv`

체결 원장이다. 매수와 매도는 반드시 별도 행이다.

```csv
BacktestId,ExecutionId,ExecutionSequence,Symbol,DateTime,Side,Price,Quantity,GrossAmount,Fee,Tax,RealizedPnL,ExitReason,StrategyVersion
```

### `backtest_equity.csv`

우선은 체결 직후의 포지션 스냅샷을 출력한다. 장중·일별 평가자산은 후속 자산 곡선 단위에서 확장한다.

```csv
BacktestId,ExecutionId,DateTime,Cash,Quantity,AverageEntryPrice,PositionCost,RealizedProfitTotal
```

### `backtest_position_cycles.csv`

분석용 선택 출력이다. 체결 원장을 대체하지 않는다.

```csv
BacktestId,CycleId,OpenedAt,ClosedAt,BoughtQuantity,SoldQuantity,AverageEntryPrice,AverageExitPrice,RealizedPnL,ReturnRate,ClosingReason
```

## 수용 기준

1. 매수·매도 체결은 별도 행으로 SQLite와 `backtest_executions.csv`에 저장된다.
2. 100주 매수, 50주 추가 매수, 80주 매도, 70주 매도 시 각 체결 뒤 수량은 100, 150, 70, 0으로 기록된다.
3. 추가 매수 뒤 평균 매입가는 가중평균 공식으로 계산된다.
4. 부분 매도 뒤 평균 매입가는 유지되고, 매도 수량에 대한 실현손익만 증가한다.
5. 전량 매도 뒤 평균 매입가와 포지션 원가는 0으로 초기화된다.
6. 한 사이클 내의 추가 매수·부분 매도는 하나의 포지션 사이클에 포함된다.
7. 마지막 매도로 수량이 0이 되면 하나의 닫힌 포지션 사이클이 생성된다.
8. 백테스트 종료 시 수량이 남으면 닫힌 사이클은 생성하지 않는다.
9. 보유 수량보다 큰 매도는 예외로 실패하며, 잘못된 스냅샷·사이클은 저장하지 않는다.
10. 기존 `SqliteBacktestRunStoreTests`와 `BacktestRunnerTests`를 포함한 전체 백테스트 테스트가 통과한다.

## 테스트 계획

| 계층 | 검증 |
| --- | --- |
| 단위 | 추가 매수 가중평균, 부분 매도 손익, 전량 매도 초기화, 초과 매도 거부 |
| 단위 | 체결 4건이 스냅샷 4건과 사이클 1건을 만드는지 |
| 저장소 통합 | 실행·체결·스냅샷·사이클 저장 후 동일 순서와 값으로 조회되는지 |
| CSV 통합 | SQLite 실행 하나를 지정해 체결·자산·사이클 CSV가 동일 값으로 생성되는지 |
| 회귀 | 기존 단일 진입·단일 청산 백테스트의 손익과 청산 사유가 변하지 않는지 |

## 구현 순서

1. 순수 `PositionLedger`와 체결·스냅샷·사이클 모델, 단위 테스트
2. `BacktestResult`와 `BacktestRunner` 연결, 기존 단일 포지션 회귀 테스트
3. SQLite 스키마 확장·저장·조회 테스트
4. CSV Exporter와 CSV 통합 테스트
5. 후속 분석 단위에서 MFE/MAE와 평가자산 곡선 추가

## 롤백

- 새 테이블은 기존 실행 결과를 변경하지 않는다.
- 저장 오류 시 기존 `backtest_runs`와 관련 체결·스냅샷·사이클 삽입 전체를 동일 SQLite 트랜잭션에서 롤백한다.
- 기능을 되돌릴 때 새 CSV 출력과 Ledger 호출만 제거하면 기존 백테스트 엔진의 단일 진입·청산 동작은 유지된다.
