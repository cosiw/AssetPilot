# AssetPilot 아키텍처

AssetPilot은 Windows에서 개인 포트폴리오를 조회하고, 이후 자동매매까지 확장하는 WPF 애플리케이션이다. 이 문서는 새 기능을 추가하거나 구조를 바꿀 때 기준으로 사용한다.

## 목표 구조

```text
WPF 화면 / ViewModel
        │
        ▼
Core 도메인
  Portfolio
  PortfolioPosition
        │
        ▼
Core 포트(인터페이스)
  IKiwoomAccountStore
  IKiwoomPortfolioClient
        │
        ▼
Infrastructure 어댑터
  WindowsKiwoomAccountStore
  KiwoomClient
        │
        ▼
외부 시스템
  Windows Credential Manager
  Kiwoom REST API
```

의존성은 바깥에서 안쪽으로 향한다. Core는 WPF, Windows Credential Manager, 키움 HTTP 규격을 알지 않는다. App은 DI 등록만 담당하고, Infrastructure가 Core의 인터페이스를 구현한다.

## 각 계층의 책임

| 계층 | 책임 | 알면 안 되는 것 |
| --- | --- | --- |
| `AssetPilot.Core` | 투자 의미를 표현하는 도메인 모델과 앱이 요구하는 인터페이스 | WPF 컨트롤, Win32 호출, 키움 JSON 필드명 |
| `AssetPilot.Infrastructure` | Windows 자격 증명 관리자, 키움 REST API 같은 외부 기술 구현 | 화면 표시 방식 |
| `AssetPilot.App` | WPF 화면, ViewModel, DI 조립, 화면 전용 상태와 형식 | 키움 HTTP 요청·응답 세부 규격 |

## 포트폴리오 조회 흐름

계좌 `1234567890`의 보유종목을 조회하는 기준 흐름이다.

```text
MainViewModel
  → IKiwoomPortfolioClient.GetPortfolioAsync(accountNumber)
  → KiwoomClient
      → IKiwoomAccountStore.GetAsync(accountNumber)
      → Windows Credential Manager에서 App Key / Secret Key 읽기
      → 키움 OAuth 토큰 발급 또는 메모리 캐시 토큰 사용
      → 키움 kt00018 호출
      → 키움 JSON DTO를 Portfolio로 변환
  → Portfolio 반환
  → ViewModel이 화면에 표시
```

현재 계좌 조회에 별도 `PortfolioService`는 두지 않는다. 단순히 Client를 한 번 호출해 결과를 전달하는 클래스는 의미가 없기 때문이다. 포트폴리오 기록 저장, 여러 API 결과 조합, 자동매매 판단처럼 앱 규칙이 생기면 그때 `PortfolioService`를 추가한다.

## 도메인 모델

키움의 JSON 형식과 화면 표시 형식 사이에 재사용 가능한 앱 모델을 둔다.

```csharp
public sealed record Portfolio(
    decimal TotalPurchaseAmount,
    decimal TotalEvaluationAmount,
    decimal TotalProfitLoss,
    decimal TotalProfitRate,
    IReadOnlyList<PortfolioPosition> Positions);

public sealed record PortfolioPosition(
    string StockCode,
    string StockName,
    int Quantity,
    decimal AveragePrice,
    decimal CurrentPrice,
    decimal PurchaseAmount,
    decimal EvaluationAmount,
    decimal EvaluationProfitLoss,
    decimal ProfitRate);
```

`Portfolio`는 계좌 전체 결과이고 `PortfolioPosition`은 보유 종목 한 줄이다. 둘은 이름만 다른 중복 모델이 아니라 포함 관계다.

이 모델은 다음 곳에서 공통으로 사용한다.

- WPF 포트폴리오 화면
- 나중의 평가금·수익률 기록 저장
- 자동매매 규칙의 보유 수량·평균 단가 판단
- 백테스트와 실거래 결과 비교

`PortfolioSnapshot`과 `PortfolioHolding`은 이 모델로 교체되었으며 더 이상 사용하지 않는다.

## 키움 DTO 규칙

키움 전용 요청·응답 타입은 `Infrastructure/Kiwoom/Contracts` 안에만 둔다.

```text
키움 JSON
  tot_evlt_amt, rmnd_qty, stk_cd
        ↓ KiwoomClient 내부 변환
Core 도메인
  Portfolio, PortfolioPosition
```

`qry_tp`, `tot_evlt_amt`, `acnt_evlt_remn_indv_tot`처럼 키움 API에만 의미가 있는 필드명은 Core나 WPF에 노출하지 않는다. 키움 API가 바뀌면 `KiwoomClient`와 그 DTO만 수정한다.

문자열 숫자를 파싱하는 공통 규칙은 `Core/Common/NumericParser`에 둔다. App과 Infrastructure는 이를 재사용할 수 있지만, API별 DTO나 HTTP 의존성은 이 공용 코드에 넣지 않는다.

## 화면 모델 규칙

WPF ViewModel은 도메인 `Portfolio`를 바로 사용한다. 단, 화면에만 필요한 값은 ViewModel에 추가한다.

```text
도메인 값:      3363200m
화면 전용 값:   "3,363,200원"

도메인 값:      TotalProfitLoss > 0
화면 전용 값:   IsProfit, 녹색/빨간색 브러시
```

화면 전용 문자열, 색상, 선택 여부, 로딩 여부 때문에 별도 Core VO를 만들지 않는다.

## 계좌와 비밀값

계좌 프로필은 SQLite에 저장하지 않는다. Windows Generic Credential 하나가 계좌 하나를 나타낸다.

| 위치 | 저장 값 |
| --- | --- |
| Credential 대상 이름 | `AssetPilot:Kiwoom:{계좌번호}` |
| 사용자 이름 | App Key |
| Credential blob | Secret Key와 실전/모의 환경 |
| SQLite | 향후 기록·주문 이력·백테스트 결과처럼 누적되는 데이터만 저장 |

App Key, Secret Key, 접근 토큰은 로그·예외 메시지·화면 상태 메시지·Git에 넣지 않는다.

## KiwoomClient 규칙

`KiwoomClient`는 현재 키움 연동의 단일 진입점이다.

- `IKiwoomPortfolioClient`를 구현한다.
- 토큰은 계좌번호와 실전/모의 환경 단위로 메모리 캐시한다.
- 키움 OAuth 응답의 `expires_dt`를 사용하고, 만료 5분 전부터 새 토큰을 발급한다.
- 같은 계좌의 동시 요청은 하나의 토큰 발급 요청만 수행한다.
- 401 응답은 캐시 토큰을 지운 뒤 한 번만 재시도한다.
- 실전은 `https://api.kiwoom.com`, 모의투자는 `https://mockapi.kiwoom.com`을 사용한다.

`KiwoomClient` 내부 private `SendAsync`는 두 번째 이상의 키움 조회 API가 추가될 때까지 별도 `KiwoomApiClient`로 분리하지 않는다.

## 확장 기준

| 상황 | 추가하거나 분리할 것 |
| --- | --- |
| 포트폴리오 기록을 DB에 저장 | `PortfolioService`, SQLite 기록 Repository |
| 예수금·미체결 등 두 번째 조회 API 추가 | `KiwoomClient` 내부 private 요청 메서드를 재사용 |
| 키움 API 기능이 여러 개로 증가 | `KiwoomApiClient`와 기능별 Client로 분리 검토 |
| 다른 증권사 연동 | Core에 증권사 중립 인터페이스를 추가하고 새 Infrastructure 어댑터 구현 |
| 주문·자동매매 추가 | 주문 도메인과 주문 포트, 위험 제한 규칙을 Core에 추가 |

## 현재 구현 상태

현재까지 다음 기반 구조가 구현되어 있다.

- Windows Credential Manager 계좌 저장소와 `KiwoomClient`가 구현되어 있다.
- `KiwoomClient`는 토큰 캐시, OAuth 요청, 인증된 `kt00018` 요청, `Portfolio` 변환을 한곳에서 처리한다.
- WPF 계좌 관리·선택 화면과 실제 포트폴리오 조회 연결은 아직 구현되지 않았다.
- 실제 키움 서버 호출은 자동 실행하지 않는다. 테스트는 가짜 HTTP 응답으로 검증한다.

변경 전에 현재 작업 계획도 함께 확인한다: [키움 Client 계획](superpowers/plans/2026-11-08-kiwoom-api-client.md), [계좌 Credential 계획](superpowers/plans/2026-11-08-credential-manager-accounts.md).
