# 키움 공통 API Client 설계

## 목표

현재 필요한 키움 기능을 하나의 Client에 모은다. 보유종목·예수금·미체결·주문 등 API가 충분히 늘어날 때에만 공통 계층과 기능별 Client로 분리한다.

## 구성

`KiwoomClient`는 `IKiwoomPortfolioClient`를 구현한다. 이 클래스는 `IKiwoomAccountStore`에서 선택 계좌의 App Key와 Secret Key를 읽고, OAuth `au10001` 토큰을 발급·캐시한다. 이어서 인증 헤더와 `api-id`를 붙여 `kt00018`을 호출하고 응답을 Core 도메인 `Portfolio`로 매핑한다.

토큰은 계좌번호와 실전/모의 환경을 기준으로 프로세스 메모리에만 보관한다. Credential Manager와 SQLite에는 접근 토큰을 저장하지 않는다. 내부 `SendAsync` 메서드는 향후 두 번째 조회 API가 생길 때까지 public 공통 계층으로 분리하지 않는다.

## 토큰 정책

- 계좌번호와 실전/모의 환경별로 캐시한다.
- OAuth 응답의 `expires_dt`를 파싱한다.
- 만료 5분 전부터는 만료된 것으로 보고 다음 호출 시 재발급한다.
- 같은 계좌에서 동시에 토큰이 필요한 경우 하나의 발급 요청만 수행한다.
- 인증 실패(401)가 발생하면 해당 계좌 토큰을 폐기하고, 토큰을 한 번 재발급한 뒤 원래 API 요청을 한 번만 재시도한다.
- 두 번째 호출도 실패하면 응답 오류를 호출자에게 전달한다.

## 공통 호출 형태

`KiwoomClient` 내부에는 다음 형태의 private 제네릭 메서드를 둔다.

```csharp
Task<TResponse> PostAsync<TRequest, TResponse>(
    string accountNumber,
    string apiId,
    string path,
    TRequest body,
    CancellationToken cancellationToken = default);
```

현재는 `GetPortfolioAsync`만 이 메서드를 사용한다. 키움 JSON DTO는 `KiwoomClient` 내부에만 둔다. `Portfolio`와 `PortfolioPosition`은 키움 API·화면·기록·자동매매가 함께 사용하는 유일한 앱 모델이다. 화면 전용 색상·문자열처럼 UI에만 필요한 값은 나중에 App ViewModel에서만 추가한다. 현재는 Service가 단순 전달만 하게 되므로 만들지 않으며, DB 기록·여러 API 조합·자동매매 판단이 들어갈 때 `PortfolioService`를 추가한다.

## 오류와 보안

App Key, Secret Key, Access Token은 예외 메시지·상태 메시지·로그에 넣지 않는다. API ID, HTTP 상태 코드, 키움의 비밀값 없는 오류 메시지만 전달한다. 네트워크 오류와 JSON 형식 오류는 재시도하지 않으며, 401 인증 실패만 토큰 갱신 재시도 대상이다.

## 검증

- 토큰이 유효하면 재발급하지 않는 테스트
- 만료 임박 토큰은 갱신하는 테스트
- 동시 호출 시 단일 토큰 요청만 발생하는 테스트
- 공통 Client가 Authorization과 `api-id` 헤더를 붙이는 테스트
- 401 뒤 한 번 갱신·재시도하고, 두 번째 401은 그대로 실패시키는 테스트
- `KiwoomClient`가 `kt00018` 응답을 도메인 `Portfolio`로 변환하는 테스트
