# 백테스트 성과 분석 명세

## 목적

체결 원장과 완료 포지션 사이클을 이용해, 단순 총수익률을 넘어 전략의 손익 구조와 청산 규칙의 적절성을 분석한다. 이 단계는 진입 규칙이나 청산 규칙을 바꾸지 않는다. 분석 결과는 이후 ATR 익절·손절·최대 보유일 실험에서 조합을 평가하는 공통 기준이 된다.

## 현재 구현 상태

현재 `BacktestMetricsCalculator`는 매도 체결의 실현손익을 대상으로 거래 수, 승률, Profit Factor, 평균 수익·손실, 최대 연속 손실, 실현손익 누적 기준 금액 낙폭을 계산한다. `PositionLedger`는 완료 포지션 사이클과 체결 직후 스냅샷을 제공한다.

아직 없는 기능은 CAGR, 청산 사유별 분석, 포지션 사이클 기준 성과, MFE/MAE, 고정 관찰기간의 청산 뒤 가격 움직임, 기간별 성과이다. 현재의 `MaxDrawdown`은 시가평가 자산곡선 기반 MDD가 아니므로 이 단계에서 진짜 MDD로 이름을 바꾸거나 대체하지 않는다. 진짜 MDD는 4번 자산 곡선 단계에서 구현한다.

## 범위

### 포함

- 완료 포지션 사이클을 기준으로 한 성과 분석
- 총수익률, CAGR, 승률, Profit Factor, 평균 거래 수익률, 평균 수익·손실, 최대 연속 손실
- 익절·손절·기간청산별 거래 수, 실현손익, 승률, 평균수익률
- 보유 기간 MFE/MAE
- 고정 관찰기간 MFE/MAE 및 청산 뒤 움직임
- 월별 성과 집계
- SQLite 저장, CSV 내보내기, 화면 요약에 분석 결과를 연결할 수 있는 모델과 계약

### 제외

- 시가평가 기반 자산곡선·MDD·일별 자산 CSV: 4번에서 구현
- ATR 파라미터 조합 실행·추천·자동 적용: 6번에서 구현
- 진입 조건(SMA, ATR 기간, 5분 확인) 변경
- 실제 주문이나 실거래 체결 분석
- MFE/MAE를 이용한 자동 청산 규칙 변경

## 분석 단위

성과 분석의 거래 단위는 `BacktestPositionCycle` 하나다. 하나의 사이클은 첫 매수부터 보유수량이 다시 0이 되는 마지막 매도까지다. 그러므로 나중에 추가 매수나 부분 매도를 지원해도 한 보유 구간의 수익률이 중복 집계되지 않는다.

`ExitReason`이 없는 완료 사이클은 `Other`로 분류한다. 현재 러너가 만드는 Target, Stop, Time은 각각 익절, 손절, 기간 청산에 해당한다.

## 지표 정의

| 지표 | 정의 |
| --- | --- |
| 총수익률 | `총 실현손익 / 시작자금` |
| CAGR | `((종료자산 / 시작자금) ^ (365.25 / 경과일수)) - 1`; 경과일수가 0이면 0 |
| 승률 | 이익 사이클 수 / 완료 사이클 수; 0 수익은 패배도 승리도 아니지만 분모에는 포함 |
| Profit Factor | 총이익 / 절대값(총손실); 손실이 없고 이익이 있으면 `decimal.MaxValue`, 거래가 없거나 이익도 없으면 0 |
| 평균 거래 수익률 | 각 완료 사이클의 `ReturnRate` 평균 |
| 평균 수익·손실 | 양수·음수 사이클을 각각 평균; 해당 표본이 없으면 0 |
| 최대 연속 손실 | 완료 사이클 시간 순서에서 음수 실현손익이 연속된 최대 횟수 |
| 청산 사유별 성과 | 사유별 거래수, 실현손익 합계, 승률, 평균수익률 |
| 월별 성과 | 사이클 종료 월 기준 거래수, 실현손익 합계, 승률, 평균수익률 |

시작자금, 종료자산, 데이터 시작일·종료일은 `BacktestRun`에 기록한다. 종료자산은 이번 단계에서는 `시작자금 + 완료 사이클 실현손익 합계`다. 미청산 포지션의 평가손익은 포함하지 않는다.

## MFE/MAE 정의

MFE(Maximum Favorable Excursion)는 진입 기준으로 가장 유리했던 가격 움직임이고, MAE(Maximum Adverse Excursion)는 가장 불리했던 가격 움직임이다. 모든 값은 원 단위 금액과 진입가 대비 수익률을 함께 기록한다.

### 보유 기간 기준

- 기준 가격: 포지션 사이클의 `AverageEntryPrice`
- 관찰 시작: 첫 매수 체결이 난 5분봉의 **다음** 5분봉
- 관찰 종료: 마지막 매도 체결이 난 5분봉의 **직전** 5분봉
- 보정: 첫 매수 가격과 마지막 매도 가격을 관찰 후보에 포함한다.
- MFE 가격: 후보 중 최고가. `MFE = 최고가 - 평균진입가`
- MAE 가격: 후보 중 최저가. `MAE = 최저가 - 평균진입가`
- 수익률: 금액을 평균진입가로 나눈 값

진입·청산이 같은 5분봉 안에서 일어난 경우 OHLC만으로 체결 전후 순서를 알 수 없으므로, 그 봉의 고가·저가는 관찰에 포함하지 않는다. 이 규칙은 미래 정보 또는 유리한 체결 순서 가정을 막는다.

### 고정 관찰기간 기준

- 기간: 기본값 5 거래일. 설정 객체 `BacktestAnalysisOptions.FixedObservationTradingDays`로 기록한다.
- 기준 가격과 시작 규칙은 보유 기간 기준과 같다.
- 종료: 진입일을 포함하여 거래일 5일째의 마지막 이용 가능한 5분봉. 데이터가 더 짧으면 마지막 이용 가능한 봉에서 끝낸다.
- 거래를 이미 청산했어도 관찰을 계속한다.

이 값은 “손절 뒤 반등했는가”, “익절 뒤 계속 올랐는가”를 판단하는 데 쓴다. 청산 시점 뒤 구간만 별도로 계산한 `PostExitMfeRate`, `PostExitMaeRate`도 같이 기록한다. 이는 마지막 매도가 있는 사이클에만 존재한다.

## 데이터 모델

```csharp
public sealed record BacktestAnalysisOptions(int FixedObservationTradingDays = 5);

public sealed record BacktestExcursion(
    Guid CycleId,
    decimal HoldingMfeAmount,
    decimal HoldingMfeRate,
    decimal HoldingMaeAmount,
    decimal HoldingMaeRate,
    decimal FixedMfeAmount,
    decimal FixedMfeRate,
    decimal FixedMaeAmount,
    decimal FixedMaeRate,
    decimal? PostExitMfeRate,
    decimal? PostExitMaeRate,
    int FixedObservationTradingDays);

public sealed record ExitReasonPerformance(
    ExitReason? Reason,
    int TradeCount,
    decimal TotalProfit,
    decimal WinRate,
    decimal AverageReturn);

public sealed record MonthlyPerformance(
    DateOnly Month,
    int TradeCount,
    decimal TotalProfit,
    decimal WinRate,
    decimal AverageReturn);

public sealed record BacktestPerformanceReport(
    int CompletedTrades,
    decimal TotalProfit,
    decimal TotalReturn,
    decimal Cagr,
    decimal WinRate,
    decimal ProfitFactor,
    decimal AverageReturn,
    decimal AverageWin,
    decimal AverageLoss,
    int MaxConsecutiveLosses,
    IReadOnlyList<ExitReasonPerformance> ExitReasonPerformances,
    IReadOnlyList<MonthlyPerformance> MonthlyPerformances,
    IReadOnlyList<BacktestExcursion> Excursions);
```

`BacktestResult`은 실행 중 계산된 `Excursions`를 보유한다. `BacktestRun`은 데이터 시작일, 종료일, 분석 옵션, 성과 보고서와 excursion 목록을 저장한다. 기존 `BacktestMetrics`와 UI는 호환성을 위해 남기며, 새 보고서가 충분히 검증된 뒤 별도 작업에서 교체한다.

## 처리 흐름

```text
5분봉 + 완료 포지션 사이클
  → BacktestExcursionCalculator
  → BacktestExcursion 목록
  → BacktestPerformanceCalculator
  → BacktestPerformanceReport
  → BacktestResult / BacktestRun / SQLite
  → summary, cycles CSV 확장 및 화면 요약
```

`BacktestExcursionCalculator`는 순수 계산 객체다. 러너가 종료된 뒤 입력 5분봉과 완료 사이클만 전달한다. 체결 생성 규칙에는 관여하지 않는다. `BacktestPerformanceCalculator`는 완료 사이클, 분석 기간, 시작자금을 입력으로 받으므로 SQLite에서 읽은 실행 결과에도 같은 계산을 재현할 수 있다.

## SQLite와 CSV

SQLite에는 기존 실행 데이터를 지우지 않는 추가형 테이블을 사용한다.

```sql
CREATE TABLE IF NOT EXISTS backtest_excursions (
    run_id TEXT NOT NULL,
    cycle_id TEXT NOT NULL,
    holding_mfe_amount TEXT NOT NULL,
    holding_mfe_rate TEXT NOT NULL,
    holding_mae_amount TEXT NOT NULL,
    holding_mae_rate TEXT NOT NULL,
    fixed_mfe_amount TEXT NOT NULL,
    fixed_mfe_rate TEXT NOT NULL,
    fixed_mae_amount TEXT NOT NULL,
    fixed_mae_rate TEXT NOT NULL,
    post_exit_mfe_rate TEXT NULL,
    post_exit_mae_rate TEXT NULL,
    fixed_observation_trading_days INTEGER NOT NULL,
    PRIMARY KEY (run_id, cycle_id)
);
```

`backtest_runs`에는 `data_start_date`, `data_end_date`, `analysis_options_json`을 nullable 열로 추가한다. 이전 실행은 분석기간을 알 수 없으므로 CAGR은 0으로 계산하고, 새 분석 데이터를 만들 때만 채운다.

CSV 변경:

- `backtest_summary.csv`의 CAGR, WinRate, TradeCount, ProfitFactor, AverageReturn을 성과 보고서 값으로 채운다.
- `backtest_position_cycles.csv`에 청산 사유와 해당 사이클의 보유기간·고정기간 MFE/MAE 열을 추가한다.
- `backtest_exit_reason_performance.csv`, `backtest_monthly_performance.csv`를 추가한다.
- 모든 숫자는 invariant culture, 날짜는 ISO 8601, UTF-8 BOM을 유지한다.

## 수용 기준

1. 부분매도를 포함한 완료 사이클은 한 번만 성과에 집계된다.
2. 보유기간 MFE/MAE는 진입·청산 동일 봉의 미확정 고저가를 사용하지 않는다.
3. 고정 관찰기간은 청산 후에도 최대 5 거래일까지 가격을 관찰한다.
4. Target·Stop·Time 각각의 거래수와 손익을 조회할 수 있다.
5. 기간과 시작자금이 있는 실행은 CAGR을 재현할 수 있다.
6. 기존 SQLite DB와 기존 백테스트 실행은 계속 읽힌다.
7. CSV를 다시 만들 때 백테스트를 재실행하지 않는다.
8. 기존 진입·청산 체결 테스트 결과는 변하지 않는다.

## 구현 완료 (2026-11-17)

- 완료 포지션 사이클 기준 성과 보고서와 청산 사유별·월별 성과 계산을 구현했다.
- 진입·청산 봉 OHLC를 배제하고 실제 체결가를 포함하는 MFE/MAE 계산을 구현했다.
- 분석 기간, 관찰 옵션, excursion을 SQLite에 저장하고 조회 시 성과 보고서를 재현한다.
- 기존 CSV를 확장하고 청산 사유별·월별 CSV를 추가했다.
- 제공된 KODEX 200 샘플 데이터에서 완료 포지션 사이클과 excursion의 1:1 연결을 회귀 테스트로 검증했다.

시가평가 자산곡선과 실제 MDD, ATR 청산 파라미터 실험은 이 명세의 후속 범위로 남는다.

## 후속 범위

4번에서 5분봉 또는 일봉 시가평가 자산곡선과 실제 MDD를 구현한다. 6번에서 이 성과 보고서를 사용해 ATR 청산값 실험을 비교하되, 자동 전략 변경은 하지 않는다.
