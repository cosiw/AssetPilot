# 시장 데이터 수집과 월간 파라미터 추천 명세

## 목표

Windows 작업 스케줄러가 장 마감 후 한 번 실행하는 `AssetPilot.MarketDataWorker`를 만든다. 이 Worker는 첫 대상인 KODEX 200 (`069500`)의 일봉과 5분봉을 키움 REST에서 받아, 백테스트와 실전 신호 계산이 같은 정규화 CSV를 읽도록 만든다.

매월 마지막 거래일에는 코드 기반 워크포워드 평가로 다음 달에 사용할 ATR 단일 진입 전략 파라미터의 **추천안**을 만든다. 추천안은 저장만 하며, 사용자가 명시적으로 승인하기 전에는 활성 설정이 바뀌지 않는다. AI는 결과를 해설하는 보조 기능일 뿐, 데이터 저장·파라미터 선정·적용을 직접 결정하지 않는다.

## 확정된 결정

| 항목 | 결정 |
| --- | --- |
| 1차 범위 | KODEX 200 데이터 수집, 공통 CSV, 백테스트용 로더, 월간 추천안 |
| 시장 데이터 공급자 | 교체 가능한 인터페이스. 첫 어댑터는 키움 REST |
| 적용 방식 | 추천안 생성 후 사용자 승인 필요 |
| 실행 방식 | WebSocket이 아닌 단발 Worker + Windows 작업 스케줄러 |
| 주문 | 범위 밖. 매수 신호와 실제 주문은 만들지 않음 |
| AI 훅 | 범위 밖. 이후 리포트 해설용으로만 추가 가능 |

## 현재 상태 (2026-11-15 확인)

- `src/AssetPilot.Backtest/MarketData/CsvMarketDataLoader.cs:8-18`은 헤더를 건너뛴 일봉·5분봉 CSV를 읽지만, 저장·병합 기능은 없다.
- `src/AssetPilot.Backtest/MarketData/DailyBar.cs`와 `FiveMinuteBar.cs`는 OHLC만 보유한다. 거래량·수집 출처·수집 시각은 보유하지 않는다.
- `src/AssetPilot.Infrastructure/Kiwoom/KiwoomClient.cs:17-217`은 키움 인증, 토큰 캐시, 재시도를 갖고 있지만 계좌 평가·예수금·거래내역 계약만 구현한다.
- `src/AssetPilot.SnapshotWorker/Program.cs:36-55`은 의존성을 등록하고 한 번 실행한 뒤 끝나는 Worker의 선례다.
- `docs/windows-task-scheduler.md:3,20-23`은 기존 계좌 Worker의 실행 계정·재시도·스케줄 규칙을 정의한다.
- `AssetPilot.Backtest.Cli`는 아직 `Hello, World!`뿐이다. 따라서 추천 실행은 이 CLI를 확장하지 않고 새 Worker의 명시적 작업으로 구현한다.

기존 계좌 스냅샷 Worker와 그 스케줄은 변경하지 않는다.

## 아키텍처

```text
Windows Task Scheduler (평일 16:05, 실패 시 10분 간격 3회)
  -> AssetPilot.MarketDataWorker (단발 실행)
      -> IMarketDataProvider
          -> KiwoomMarketDataProvider (REST, 첫 구현)
      -> CsvMarketDataStore
          -> data/raw/kiwoom/069500/...       원본 응답
          -> data/market-data/069500/...      정규화 CSV
      -> 월말일이면 WalkForwardParameterSelector
          -> BacktestRunner + SwingAtrStrategy
          -> data/strategy-state/parameter-decisions.csv (추천안)

백테스트 / 이후 실전 신호 계산
  -> data/market-data/069500/daily.csv 및 5m/{year}.csv만 읽음

사용자 승인 도구 또는 승인 명령
  -> active-parameters.json 갱신
```

`raw`는 API 응답을 감사·재파싱하기 위한 보관본이다. 전략은 절대로 `raw`를 직접 읽지 않는다. `market-data`만 신호와 백테스트의 단일 데이터 원천이다.

## 파일과 CSV 계약

`data/`는 운영 데이터이므로 Git 추적 대상에서 제외한다. `samples/market-data`는 테스트 재현용 고정 입력으로 남기며, Worker가 쓰지 않는다.

```text
data/
  raw/kiwoom/069500/daily/2026-11-15.json
  raw/kiwoom/069500/5m/2026-11-15.json
  market-data/069500/daily.csv
  market-data/069500/5m/2026.csv
  strategy-state/parameter-decisions.csv
  strategy-state/active-parameters.json
  collection-log/2026-11.csv
```

정규화 파일은 UTF-8, 헤더 1행, 불변 문화권 숫자 형식으로 저장한다.

```csv
# daily.csv
date,open,high,low,close,volume,source,updated_at
2026-11-13,51200,51800,51000,51600,1234567,kiwoom,2026-11-15T16:05:00+09:00

# 5m/2026.csv
date,time,open,high,low,close,volume,source,updated_at
2026-11-13,09:00,51200,51300,51150,51250,12345,kiwoom,2026-11-13T09:05:10+09:00
```

- 일봉의 고유 키는 `(symbol, date)`, 5분봉의 고유 키는 `(symbol, timestamp)`다.
- 같은 키를 다시 받으면 새 행을 추가하지 않고 최신 API 값을 사용해 대체한다.
- 저장 전 `high >= max(open, close, low)`, `low <= min(open, close, high)`, 가격·거래량의 음수 금지, 5분 경계 시각을 검사한다.
- 정렬·중복 제거를 끝낸 전체 결과를 임시 파일에 쓴 뒤 원자적으로 교체한다. 실패하면 기존 정규화 파일은 보존한다.
- 실행마다 대상일과 직전 9거래일, 총 10거래일을 다시 동기화한다. 누락·정정분을 보완하면서도 전체 이력을 매번 다시 받지 않는다.

기존 `CsvMarketDataLoader`는 이 헤더 기반 표준 형식을 지원하도록 바꾼다. 기존 샘플의 레거시 열 순서는 테스트 호환을 위해 별도 호환 파서로 유지한다.

## 코드 계약

### 공급자 포트

`src/AssetPilot.Backtest/MarketData/IMarketDataProvider.cs`:

```csharp
public interface IMarketDataProvider
{
    string Source { get; }
    Task<IReadOnlyList<DailyMarketBar>> GetDailyAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IntradayMarketBar>> GetFiveMinuteAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
```

`DailyMarketBar`는 날짜·OHLC·거래량을, `IntradayMarketBar`는 KST 타임스탬프·OHLC·거래량을 가진 불변 record다. 기존 전략용 `DailyBar`와 `FiveMinuteBar`는 이 표준 모델에서 거래량 등을 제외해 변환한다.

`src/AssetPilot.Infrastructure/Kiwoom/KiwoomMarketDataProvider.cs`는 `IMarketDataProvider`를 구현한다. 인증, 토큰 캐시, HTTP 오류 재시도 정책은 기존 `KiwoomClient`의 검증된 구현을 재사용한다. 키움 분봉·일봉 API의 연속조회 페이지를 모두 따라가며, API 제한을 넘지 않도록 요청 간격을 둔다.

### 저장소와 Worker

`CsvMarketDataStore`는 다음 메서드를 제공한다.

```csharp
Task MergeDailyAsync(string symbol, IEnumerable<DailyMarketBar> bars, CancellationToken ct = default);
Task MergeFiveMinuteAsync(string symbol, IEnumerable<IntradayMarketBar> bars, CancellationToken ct = default);
Task<MarketDataSnapshot> LoadAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);
```

`MarketDataRunner.RunAsync`는 KST 평일 16:05 이상일 때만 동작한다. 아직 장중이거나 주말이면 API 호출 없이 성공으로 종료한다. API·검증·저장 중 하나라도 실패하면 실패 코드로 종료하여 작업 스케줄러의 재시도를 사용한다. 성공 로그에는 종목, 일봉/5분봉 수, 병합·대체 수, 최신 봉 시각만 기록하며 키·토큰·원문 응답은 기록하지 않는다.

`AssetPilot.MarketDataWorker`는 새 실행 프로젝트로 만들고 `SnapshotWorker`와 동일하게 한 번 실행하고 종료한다. 기존 계좌 Worker에 시장 데이터 책임을 추가하지 않는다.

## 월간 추천 계약

추천은 매월 마지막 거래일의 데이터 저장이 성공한 뒤 수행한다. 데이터가 부족하면 실패가 아니라 `InsufficientHistory` 상태의 추천 이력을 남기고 활성 설정을 건드리지 않는다.

초기 기준은 다음과 같다.

| 항목 | 값 |
| --- | --- |
| 학습 구간 | 평가 기준일 직전 3년 |
| 검증 구간 | 학습 직후 6개월 |
| 적용 시작일 | 다음 거래월 첫 거래일 |
| 최소 완료 거래 수 | 20 |
| 후보 성능 순서 | 검증 수익률 내림차순, Profit Factor 내림차순, MDD 오름차순 |
| 동점 | 더 단순한 기본값 근접 조합 우선 |

후보군은 코드의 상수로 버전 관리한다. 첫 버전은 아래처럼 제한해 과최적화를 방지한다.

```text
SMA: 20, 40, 60, 80, 120
SMA 기울기 비교일: 5, 10, 20
최소 SMA 이격률: 0, 1, 2
단기 SMA: 5, 10, 20
ATR 기간: 3, 5, 14
매수 밴드: 0.25, 0.5 ATR
목표: 2, 3, 4 ATR
손절: 1, 1.5 ATR
최대 보유: 5, 10 거래일
```

각 후보는 기존 `BacktestRunner`의 동일 체결 규칙으로 평가한다. 수수료·세금·슬리피지는 현 백테스트 엔진이 아직 반영하지 않으므로 추천 결과와 승인 화면에 반드시 `비용 미반영`을 표시한다. 이 제한은 1차에서 숨기거나 추정하지 않는다.

`parameter-decisions.csv`에는 추천과 승인 상태를 모두 추가한다.

```csv
decision_id,decision_date,effective_from,symbol,status,options_json,train_from,train_to,validation_from,validation_to,completed_trades,validation_return_pct,profit_factor,max_drawdown_pct,reason,approved_at,approved_by
2026-11-30-069500,2026-11-30,2026-12-01,069500,PendingApproval,"{...}",2023-05-30,2026-05-29,2026-05-30,2026-11-30,25,17.88,3.21,4.13,"Top ranked valid candidate",,
```

`active-parameters.json`은 승인된 가장 최근 한 건만 가진다. 승인 명령은 `PendingApproval` 상태, `effective_from`이 미래 또는 당일, 대상 종목 일치를 검증한 뒤에만 이 파일을 원자적으로 바꾼다. 거절은 행을 삭제하지 않고 `Rejected`로 전환한다.

## 구현 순서

1. 표준 시장 데이터 record, 공급자 포트, CSV 읽기·쓰기·병합·검증 테스트를 추가한다.
2. 키움 REST 시장 데이터 어댑터를 구현하고 HTTP 요청·연속조회·오류 재시도를 계약 테스트로 검증한다.
3. 단발 `MarketDataWorker`와 KST 장마감 실행 규칙, 원본 보관, 수집 로그를 추가한다.
4. 워크포워드 후보 평가와 추천 이력 저장을 추가한다.
5. 승인 명령 또는 WPF 승인 화면을 추가해 `active-parameters.json`만 갱신하게 한다.
6. 작업 스케줄러 문서와 운영 점검 절차를 작성한다.

## 수용 기준

1. Worker가 평일 16:05 전 또는 주말에 실행되면 키움 API를 호출하지 않고 종료 코드 0으로 끝난다.
2. 허용 시간에 KODEX 200 일봉·5분봉을 저장하고, 5분봉은 해당 연도 파일 하나에 KST 시각 오름차순으로 저장된다.
3. 같은 수집을 두 번 실행해도 일봉 날짜와 5분봉 타임스탬프는 각각 한 번씩만 존재한다.
4. 잘못된 OHLC 또는 잘못된 5분 경계 데이터가 오면 정규화 CSV는 변경되지 않고 Worker는 실패한다.
5. 원자적 파일 교체 중 오류가 나도 기존 정규화 CSV는 읽을 수 있다.
6. 정규화 CSV를 읽은 백테스트는 기존 `DailyBar`·`FiveMinuteBar` 입력과 같은 시간 순서로 전략을 실행한다.
7. 충분한 이력이 있을 때 마지막 거래일의 추천은 `PendingApproval` 행 하나를 남기고 `active-parameters.json`을 변경하지 않는다.
8. 3년 학습 + 6개월 검증 또는 완료 거래 20건을 만족하지 못하면 `InsufficientHistory` 행을 남기고 추천값을 만들지 않는다.
9. 승인된 추천만 활성 설정으로 기록되며, 거절·미승인 추천은 실전 신호가 읽지 않는다.
10. 모든 새 단위·통합 테스트와 기존 전체 테스트가 통과한다.

## 테스트 계획

| 계층 | 검증 내용 |
| --- | --- |
| 단위 | CSV 행 파싱, OHLC 검증, 연도 파일 분할, 중복 교체, 원자 교체, 시간 조건 |
| 단위 | 키움 요청 DTO, 페이지 연속조회, HTTP 재시도, 5분 주기 요청 |
| 통합 | 임시 디렉터리에서 원본→정규화 병합→로더→백테스트 |
| 통합 | 3년 이상 합성 데이터의 추천, 이력 부족, 승인·거절 상태 전이 |
| 회귀 | 현재 `AssetPilot.Backtest.Tests`와 `AssetPilot.Core.Tests` 전체 |

## 롤백

Worker 스케줄을 비활성화하면 이후 파일은 갱신되지 않으며 기존 백테스트는 마지막 정상 CSV를 계속 사용한다. 잘못된 정규화 데이터는 해당 원본 JSON을 기준으로 재생성한다. 잘못 승인된 활성 설정은 이전 승인 행을 다시 승인해 `active-parameters.json`을 되돌린다. 이 기능은 주문을 만들지 않으므로 금전적 되돌림 절차는 없다.

## 예상 작업량

| 작업 | 예상 |
| --- | --- |
| 시장 데이터 계약·CSV 저장·테스트 | 1일 |
| 키움 REST 어댑터·페이지 처리·테스트 | 1일 |
| Worker·스케줄·운영 문서 | 0.5일 |
| 워크포워드·이력·승인 흐름·테스트 | 1.5일 |
| 합계 | 약 4일 |

## 범위 밖

- 실제 주문 제출, 체결 확인, 부분 체결 및 위험 한도
- WebSocket 연결과 실시간 분봉 생성
- 다종목·다공급자 실제 어댑터 구현
- AI가 값을 직접 변경하거나 주문을 실행하는 흐름
- 수수료·세금·슬리피지 모델. 이는 다음 백테스트 정확도 작업으로 분리한다.
