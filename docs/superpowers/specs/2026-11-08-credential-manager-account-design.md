# Credential Manager 계좌 프로필 설계

## 목표

계좌 프로필용 SQLite를 사용하지 않는다. Windows 자격 증명 관리자에 키움 계좌별 App Key, Secret Key, 계좌번호 및 실전/모의 환경을 저장하고, WPF 메인 화면에서 계좌를 선택해 보유종목을 조회한다.

## 범위

- Windows Generic Credential 기반 계좌 등록, 목록 조회, 수정, 삭제
- 계좌번호를 기본 표시 이름으로 사용하는 계좌 선택 UI
- 선택 계좌의 키로 토큰을 발급하고 `kt00018` 보유종목 조회 실행
- 조회 결과와 오류를 메인 화면에 표시

거래·평가 이력, 주문 이력, 자동매매 규칙은 이번 범위에서 제외한다. 이러한 누적 데이터가 필요해질 때 SQLite를 별도 도입한다.

## 저장 형식

Credential 대상 이름은 다음으로 고정한다.

`AssetPilot:Kiwoom:{계좌번호}`

Windows Credential의 사용자 이름에는 App Key를 저장한다. Credential blob에는 JSON 형태의 다음 값을 UTF-8로 저장한다.

```json
{
  "secretKey": "...",
  "environment": "Real"
}
```

계좌번호는 대상 이름에서 파생한다. 계좌 별칭과 마지막 선택 계좌는 저장하지 않는다. 앱은 시작할 때 `AssetPilot:Kiwoom:*` 항목을 열거해 계좌 목록을 구성하고, 정렬된 첫 계좌를 선택한다.

## 구성

Core에는 `KiwoomAccountProfile` 모델과 계좌 저장소 인터페이스를 둔다. 이 인터페이스는 계좌 목록 조회, 단건 조회, 저장, 삭제를 제공한다. 키움 자격 증명 Provider는 선택된 계좌를 기준으로 App Key와 Secret Key를 반환한다.

Infrastructure에는 Windows Credential Manager P/Invoke 어댑터를 둔다. Generic Credential만 열거하며, 다른 자격 증명은 읽지 않는다. 삭제 시 Windows Credential도 함께 삭제한다.

App에는 다음 최소 UI를 둔다.

- 상단: 계좌번호 ComboBox, 새로고침 버튼, 계좌 관리 버튼
- 계좌 관리 창: 계좌번호, 실전/모의, App Key, Secret Key 입력 및 저장/삭제
- 본문: 총 매입·평가·손익과 보유종목 표

## 흐름과 오류 처리

1. 앱 시작 시 Credential Manager에서 계좌 목록을 읽는다.
2. 목록이 있으면 첫 계좌를 선택한다. 목록이 없으면 계좌 관리 창에서 등록하도록 안내한다.
3. 사용자가 조회하거나 계좌를 전환하면 해당 계좌 자격 증명으로 키움 토큰을 얻고 보유종목 API를 호출한다.
4. Windows 자격 증명, 네트워크, 키움 API 오류는 비밀값 없이 화면 상태 메시지로 표시한다.

## 검증

- 대상 이름 파싱과 저장 blob 직렬화·역직렬화 단위 테스트
- Credential 저장소의 공용 동작은 Win32 호출을 감싼 경계로 분리해 테스트
- 계좌 전환 시 올바른 자격 증명이 API 클라이언트에 전달되는 ViewModel 테스트
- 기존 키움 HTTP 요청/응답 매핑 테스트 유지

## 정리 대상

`AccountProfiles`, `IAccountProfileRepository`, `SqliteAccountProfileRepository`, `AssetPilotDataPaths`와 SQLite 의존성은 계좌 프로필 용도로 제거한다. 기존 개발용 DB는 이후 이력 기능을 도입할 때까지 앱에서 생성하지 않는다.
