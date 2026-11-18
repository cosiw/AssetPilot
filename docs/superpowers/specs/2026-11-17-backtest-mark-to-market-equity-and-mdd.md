# 백테스트 시가평가 자산곡선 및 실제 MDD 명세

## 목적

현재 `BacktestMetrics.MaxDrawdown`은 완료된 매도 체결의 실현손익 누적값만으로 계산한다. 보유 중인 포지션의 하락을 반영하지 않으므로 실제 백테스트 위험을 나타내는 MDD가 아니다.

이 작업은 각 5분봉 종가에서 현금과 보유 포지션을 시가평가한 자산곡선을 생성하고, 그 곡선에서 실제 MDD를 계산·저장·CSV 출력·화면 요약에 연결한다. 매수 신호, 체결 규칙, ATR 청산 규칙은 변경하지 않는다.

## 구현 상태

- 완료: 5분봉 종가 기준 `BacktestEquityCurve`와 실제 MDD 계산
- 완료: `BacktestResult`·`BacktestRun` 전달 및 SQLite `backtest_equity_points` 저장·조회
- 완료: `backtest_equity.csv`, summary CSV MDD, 화면 `MetricsSummary`의 실제 MDD 연결
- 완료: KODEX 200 일봉과 5분봉(26년) 샘플 회귀 테스트

## 범위

### 포함

- 5분봉 종가 기준 시가평가 자산곡선
- 평가 시점별 현금, 수량, 평균진입가, 종가, 포지션 평가액, 총자산, 누적수익률, 고점, 낙폭 금액·비율
- 자산곡선 기준 최대 낙폭(MDD) 금액·비율과 고점·저점 시점
- SQLite 원본 저장과 조회
- `backtest_equity.csv`를 실제 자산곡선으로 교체
- 요약 CSV 및 화면 요약의 MDD를 실제 시가평가 MDD로 교체

### 제외

- ATR 익절·손절·최대 보유기간 조합 실험
- Walk-Forward 검증
- 일별 또는 월별 리밸런싱
- 수수료·세금 체계 변경
- 실거래 계좌 평가금액 연결

## 평가 규칙

### 평가 시점

1. 입력 5분봉을 `Timestamp` 오름차순으로 처리한다.
2. 동일 시점에 실행된 체결은 `Sequence` 오름차순으로 먼저 반영한다.
3. 그 5분봉의 종가로 남은 수량을 평가한다.
4. 평가점은 `bar.Timestamp`이며, 해당 시점의 체결 반영 후 상태를 나타낸다.

이 규칙은 해당 봉의 종료 뒤에만 종가를 사용하므로 미래 가격을 사용하지 않는다. 체결 봉의 고가·저가로 유리한 평가를 하지 않는다.

### 자산 계산

```text
PositionValue = PositionQuantity × Bar.Close
TotalEquity = Cash + PositionValue
CumulativeReturn = (TotalEquity / StartingCash) - 1
PeakEquity = 지금까지 TotalEquity의 최대값
DrawdownAmount = TotalEquity - PeakEquity
DrawdownRate = DrawdownAmount / PeakEquity
```

- 매수 체결: `Cash -= GrossAmount + Fee + Tax`
- 매도 체결: `Cash += GrossAmount - Fee - Tax`
- 보유수량이 0이면 `PositionValue`는 0이다.
- 시작자금이 0 이하이거나 고점자산이 0 이하인 경우 수익률·낙폭률은 0으로 기록한다. 현재 UI 입력 검증은 시작자금을 양수로 제한한다.
- 5분봉이 없는 일자에는 평가점을 인위적으로 추가하지 않는다. 마지막 5분봉 이후의 미실현 가격도 가정하지 않는다.

### MDD 정의

- `MaxDrawdownAmount`: 자산곡선의 `DrawdownAmount` 중 가장 작은 값의 절댓값
- `MaxDrawdownRate`: 자산곡선의 `DrawdownRate` 중 가장 작은 값의 절댓값
- `PeakAt`: 최대 낙폭 구간 직전까지의 최고 총자산 시점
- `TroughAt`: 최대 낙폭이 발생한 시점
- 자산곡선이 비어 있으면 모든 MDD 값은 0이고 시점은 `null`이다.

## 도메인 모델

```csharp
public sealed record BacktestEquityPoint(
    DateTime DateTime,
    decimal Cash,
    decimal PositionQuantity,
    decimal AverageEntryPrice,
    decimal MarkPrice,
    decimal PositionValue,
    decimal TotalEquity,
    decimal CumulativeReturn,
    decimal PeakEquity,
    decimal DrawdownAmount,
    decimal DrawdownRate);

public sealed record BacktestEquityCurve(
    IReadOnlyList<BacktestEquityPoint> Points,
    decimal MaxDrawdownAmount,
    decimal MaxDrawdownRate,
    DateTime? PeakAt,
    DateTime? TroughAt);
```

`BacktestEquityCurveCalculator`는 시작자금, 순서가 보장된 체결 목록, 5분봉 목록을 입력으로 받아 순수하게 `BacktestEquityCurve`를 반환한다. 체결 생성, 매수·매도 판단, 전략 파라미터 변경에는 관여하지 않는다.

`BacktestResult`와 `BacktestRun`은 선택적 `EquityCurve`를 보유한다. 기존 생성자 호출 호환성을 위해 마지막 선택 매개변수로 추가한다.

## 저장 및 CSV

SQLite에 `backtest_equity_points` 테이블을 추가한다.

```sql
CREATE TABLE IF NOT EXISTS backtest_equity_points (
    run_id TEXT NOT NULL,
    recorded_at TEXT NOT NULL,
    cash TEXT NOT NULL,
    position_quantity TEXT NOT NULL,
    average_entry_price TEXT NOT NULL,
    mark_price TEXT NOT NULL,
    position_value TEXT NOT NULL,
    total_equity TEXT NOT NULL,
    cumulative_return TEXT NOT NULL,
    peak_equity TEXT NOT NULL,
    drawdown_amount TEXT NOT NULL,
    drawdown_rate TEXT NOT NULL,
    PRIMARY KEY (run_id, recorded_at)
);
```

`backtest_equity.csv`는 다음 스키마로 실제 자산곡선을 출력한다.

```text
BacktestId,DateTime,Cash,PositionQuantity,AverageEntryPrice,MarkPrice,PositionValue,TotalEquity,CumulativeReturn,PeakEquity,DrawdownAmount,DrawdownRate
```

`backtest_summary.csv`의 `MDD`에는 `MaxDrawdownRate`를 기록한다. 기존 position snapshot은 SQLite의 `backtest_position_snapshots`에 계속 보존하지만, 자산곡선 CSV로 출력하지 않는다.

화면의 `MetricsSummary`는 실제 MDD 비율을 표시하고, 기존 실현손익 기준 `BacktestMetrics.MaxDrawdown`은 호환성을 위해 남기되 새 요약에는 사용하지 않는다.

## 허용 기준

1. 보유 중 가격이 하락하면 매도 전에도 `TotalEquity`와 낙폭이 변한다.
2. 동일 5분봉의 체결은 `Sequence` 순서로 반영하고, 종가 평가는 그 뒤에 한 번만 발생한다.
3. 체결이 없는 5분봉도 자산곡선 평가점으로 저장된다.
4. 최대 낙폭은 체결 손익 합계가 아니라 모든 자산곡선 점에서 계산된다.
5. 저장 후 조회한 자산곡선과 MDD 값은 저장 전 값과 동일하다.
6. `backtest_equity.csv`와 summary CSV의 MDD는 백테스트를 다시 실행하지 않고 저장된 실행 결과에서 생성된다.
7. 기존 DB는 초기화 시 오류 없이 업그레이드되고 기존 실행 기록을 유지한다.
8. 기존 진입·청산 체결 테스트 결과는 변경되지 않는다.

## 후속 범위

이 작업이 끝나면 제한적인 ATR 청산 파라미터 실험에서 실제 MDD를 CAGR·PF·거래 수와 함께 평가 기준으로 사용한다.
