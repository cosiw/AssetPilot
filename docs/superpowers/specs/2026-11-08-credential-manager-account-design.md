# Credential Manager Account Design

## Goal

Windows Credential Manager를 키움 계좌의 유일한 저장소로 사용한다. WPF 사용자는 계좌를 추가·교체·삭제·선택할 수 있고, 선택한 계좌만 `KiwoomClient`가 읽어 포트폴리오를 조회한다.

SQLite는 체결·주문·일별 평가 같은 이력 데이터가 필요해질 때만 사용한다. 계좌번호, App Key, Secret Key, 실전/모의 환경은 SQLite에 저장하지 않는다.

## Layer boundaries

```text
App ViewModel
  -> IKiwoomAccountStore            (Core port)
  -> IKiwoomPortfolioClient         (Core port)

WindowsKiwoomAccountStore           (Infrastructure adapter)
  -> IWindowsCredentialManager
  -> Windows Credential Manager

KiwoomClient                        (Infrastructure adapter)
  -> IKiwoomAccountStore.GetAsync(selectedAccountNumber)
  -> Kiwoom REST API
```

Core에는 Windows API, Credential target 형식, JSON blob 형식을 넣지 않는다. Core의 `KiwoomAccountProfile`, `KiwoomAccountCredentials`, `IKiwoomAccountStore`는 계좌 기능이 필요로 하는 모델과 포트만 정의한다.

`KiwoomAccountCredentials`에는 비밀 값이 있지만 이는 Credential Manager 구현이 아닌 애플리케이션 계약이다. App ViewModel은 목록·저장·삭제에만 사용하고, 조회된 비밀 값은 `KiwoomClient`만 사용한다. ViewModel과 화면은 Secret Key를 다시 표시하거나 로그에 남기지 않는다.

## Credential format

계좌 하나는 Windows Generic Credential 하나와 대응한다.

| 위치 | 값 |
| --- | --- |
| TargetName | `AssetPilot:Kiwoom:{AccountNumber}` |
| UserName | App Key |
| CredentialBlob | UTF-8 JSON `{ "secretKey": "...", "environment": "Real" }` |

`WindowsCredentialTarget`은 빈 계좌번호를 거부한다. `KiwoomCredentialPayload`는 손상 JSON, 빈 Secret Key, 정의되지 않은 환경 값을 `InvalidOperationException`으로 변환한다. `GetAllAsync`는 손상된 AssetPilot Credential을 건너뛰어 하나의 깨진 항목이 전체 계좌 목록을 막지 않게 한다. `GetAsync`는 선택된 계좌가 없거나 손상되었을 때만 비밀 값 없는 오류를 반환한다.

## Application flow

1. 앱 시작 시 `MainViewModel.InitializeAsync`가 `GetAllAsync`로 계좌 목록을 읽고 첫 계좌를 선택한다.
2. 계좌가 없으면 화면은 안내 문구만 보여주며 HTTP 요청을 보내지 않는다.
3. 계좌 관리 창은 계좌번호, 실전/모의 환경, App Key, Secret Key를 받아 `SaveAsync`한다. 기존 계좌를 수정할 때도 Secret Key를 다시 입력받고 기존 비밀은 화면에 채우지 않는다.
4. 메인 화면에서 선택 계좌를 변경하거나 새로고침하면 `IKiwoomPortfolioClient.GetPortfolioAsync(SelectedAccount.AccountNumber)`를 호출한다.
5. `KiwoomClient`가 해당 계좌 Credential을 읽고 토큰을 발급·캐시한 뒤 `Portfolio`를 반환한다.

## UI boundary

`AccountManagementWindow`는 WPF 표현만 담당한다. PasswordBox의 값은 저장 클릭 시에만 ViewModel 메서드에 전달한 뒤 즉시 지운다. MainViewModel은 계좌 목록, 선택 상태, 로딩 상태, `Portfolio`를 보유하지만 Window를 직접 열지 않는다. Window가 대화상자를 닫은 뒤 MainViewModel의 목록을 다시 불러온다.

## Tests

- Credential target 및 blob의 정상/손상 입력 테스트
- 저장·조회·목록·삭제의 store 매핑 테스트(가짜 native credential manager 사용)
- MainViewModel의 계좌 없음, 첫 계좌 선택, 선택 계좌 조회, 오류 상태 테스트
- KiwoomClient의 HTTP 테스트는 가짜 `HttpMessageHandler`만 사용하며 실서버·모의서버에 요청하지 않는다.
