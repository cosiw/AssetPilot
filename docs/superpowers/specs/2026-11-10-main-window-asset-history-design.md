# 메인 화면 자산 기록 및 예수금 설계

## 목적

기존 메인 창의 실시간 보유 현황 아래에서, Windows 작업 스케줄러가 저장한 계좌 스냅샷의 자산 변화와 자산 구성을 확인한다. 사용자는 현재 평가금액·예수금·보유 종목을 계속 조회할 수 있고, 별도 화면으로 이동하지 않고도 기간별 총자산 추이를 본다.

## 확정한 사용자 경험

메인 화면은 다음 순서를 따른다.

1. 계좌 선택과 현재가 새로고침
2. 현재 보유 현황 요약: 총자산, 평가금액, 예수금, 매입금액, 평가손익, 수익률
3. 현재 보유 종목 표
4. 자산 기록: 기간 선택, 기준일 선택, 총자산·예수금 추이, 국내주식·예수금 구성

계좌 선택과 새로고침은 화면 전체의 현재 보유 현황에 적용한다. `1주`, `1개월`, `3개월`, `전체`와 기준일은 자산 기록 영역에만 적용한다. 기간 변경은 차트의 조회 범위를 바꾸고, 기준일은 해당 범위 안에서 자산 구성의 기준 스냅샷을 바꾼다. 기본 기준일은 선택 기간의 가장 최근 스냅샷이다.

현재 보유 현황은 키움 API를 다시 호출해 표시한다. 자산 기록은 로컬 SQLite 스냅샷만 읽으며, 기간 또는 기준일을 바꿀 때 키움 API를 호출하지 않는다.

## 데이터 원칙

`kt00018`은 종목별 평가와 추정예탁자산을 제공하고, `KiwoomClient.GetPortfolioAsync`는 이 API만 호출한다. 예수금은 별도 `kt00001 예수금상세현황요청`의 `entr` 값을 사용하며, `KiwoomClient.GetCashBalanceAsync`가 이 API만 호출한다. `AccountSnapshotRunner`가 두 결과를 조합해 스냅샷을 저장한다.

`total_asset_amount`는 키움이 돌려준 `kt00018.prsm_dpst_aset_amt`를 그대로 보존한다. 화면의 자산 구성은 사용자가 보유한 국내주식과 예수금만 표시한다.

```
국내주식 금액 = total_evaluation_amount
예수금       = cash_balance (kt00001.entr)
구성 비율    = 각 금액 / (국내주식 금액 + 예수금)
```

분모가 0이면 구성 그래프 대신 빈 상태를 표시한다. 키움 총자산과 두 금액의 합이 다르더라도 두 값을 억지로 총자산에 맞추지 않는다.

## 과거 데이터와 정확성

현재 DB에는 예수금 컬럼이 있지만, 기존 수집기는 `0`을 저장했다. 실제 예수금 0원과 구별하려면 스냅샷에 `cash_balance_captured` 플래그를 추가한다.

- 기존 행은 마이그레이션 기본값 `0`(예수금 미수집)이다.
- `kt00001`을 성공적으로 조회한 새 스냅샷은 `1`이다.
- 미수집 스냅샷을 선택하면 차트의 예수금 선과 자산 구성의 예수금은 표시하지 않고 `예수금 저장 전 데이터입니다`를 표시한다.
- `kt00001` 또는 `kt00018` 호출이 실패하면 해당 계좌의 현재 조회와 스냅샷 저장은 실패로 처리한다. 0원을 대체값으로 사용하지 않는다.

기존 날짜의 예수금은 거래 원장으로 재계산하거나 소급 채우지 않는다.

## 구성 요소와 데이터 흐름

```text
KiwoomClient
  ├─ kt00018 → 종목/평가/총자산
  └─ kt00001 → 예수금
          │
          ▼
AccountSnapshotRunner → AccountSnapshot
          │
          └─ AccountSnapshotRunner → SQLite account_snapshots

SqliteAccountSnapshotStore (IAccountSnapshotReader)
          └─ MainViewModel / AssetHistoryViewModel → 상단 요약·보유 종목·자산 기록·구성
          │
          ▼
AssetHistoryViewModel → 자산 기록 차트·구성
```

`IAccountSnapshotStore`는 쓰기와 중복 확인을 계속 담당한다. 새 `IAccountSnapshotReader`는 앱이 필요한 읽기만 노출한다.

```csharp
public interface IAccountSnapshotReader
{
    Task<IReadOnlyList<AccountSnapshotSummary>> GetSummariesAsync(
        Guid accountId, DateOnly? from, DateOnly? to,
        CancellationToken cancellationToken = default);
}

public sealed record AccountSnapshotSummary(
    DateOnly SnapshotDate,
    DateTimeOffset CapturedAtUtc,
    decimal CashBalance,
    bool HasCashBalance,
    decimal TotalAssetAmount,
    decimal TotalPurchaseAmount,
    decimal TotalEvaluationAmount,
    decimal TotalProfitLoss,
    int TotalProfitRateHundredths);
```

`SqliteAccountSnapshotStore`가 두 인터페이스를 모두 구현한다. 앱과 워커는 `%LOCALAPPDATA%\AssetPilot\assetpilot.db`의 동일한 연결 문자열을 사용한다.

## 화면 설계

기존 테마 토큰을 유지한다. 총자산과 숫자에는 `FontFamily.Numeric`, 설명에는 `FontFamily.Korean`을 쓴다. 상승은 기존 빨강, 하락은 기존 파랑으로 표시한다.

- 요약은 여섯 칸이며 예수금은 `Portfolio.CashBalance`에 바인딩한다.
- 메인 화면의 총자산·예수금·평가금액·매입금액·평가손익·수익률과 보유 종목 표는 모두 SQLite의 가장 최근 스냅샷에서 읽는다. 화면 진입·계좌 변경·기간 변경 시 Kiwoom API를 호출하지 않는다.
- 요약 위에는 가장 최근 스냅샷의 한국 시간 기준 시각을 표시하고, 실시간·현재가라는 표현은 사용하지 않는다. Credential Manager는 계좌 식별과 SnapshotWorker 인증용으로만 사용한다.
- 자산 기록은 `ScrollViewer` 안에서 보유 종목 표 아래에 둔다.
- 선 차트는 총자산과 예수금 두 시리즈다. 예수금 미수집 날짜는 차트에 빈 구간으로 남긴다.
- 도넛 차트는 국내주식과 예수금 두 조각만 가진다. 선택한 기준일의 예수금이 미수집이면 도넛 대신 안내문을 표시한다.
- 데이터가 전혀 없으면 `아직 저장된 자산 기록이 없습니다. 다음 예약 저장 후 표시됩니다.`를 표시한다.

차트는 WPF용 `LiveChartsCore.SkiaSharpView.WPF` 안정 버전 2.0.5를 사용한다. 차트 라이브러리는 자산 기록 영역에만 쓰며 기존 DataGrid는 유지한다.

## 범위 밖

- 과거 스냅샷의 종목별 상세 표 및 전일 대비 계산
- 해외주식·외화 예수금, 신용·대출·미수금의 자산 구성
- 휴장일 판정, 실시간 자동 갱신, 투자 판단이나 매매 추천
- 기존 스냅샷의 예수금 소급 복원

## 수용 기준

1. 현재 보유 현황을 새로고침하면 `kt00018`과 `kt00001`을 사용해 예수금이 표시된다.
2. 워커가 새 스냅샷을 저장하면 실제 예수금과 `cash_balance_captured = 1`이 함께 저장된다.
3. 기존 스냅샷은 예수금을 0원으로 오인하지 않고 미수집 상태로 표시한다.
4. 계좌별로 1주·1개월·3개월·전체 범위의 총자산 기록을 SQLite에서 읽어 차트로 표시한다.
5. 기간/기준일 변경은 키움 API 요청을 만들지 않는다.
6. 자산 구성은 선택 기준일의 국내주식 평가금액과 예수금만 표시한다.
7. 저장 데이터가 없는 계좌도 메인 화면은 정상적으로 열리고 자산 기록에는 빈 상태가 표시된다.
