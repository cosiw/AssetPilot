# Credential Manager Account Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Windows Credential Manager에 저장된 키움 계좌를 WPF에서 안전하게 관리·선택하고, 선택 계좌의 포트폴리오를 `KiwoomClient`로 조회한다.

**Architecture:** Core의 `IKiwoomAccountStore`와 `IKiwoomPortfolioClient`는 그대로 유지한다. Infrastructure는 Credential target/blob 검증과 Windows P/Invoke만 담당하며, App은 계좌 관리 화면과 선택 상태만 담당한다. Secret Key는 PasswordBox에서 저장 순간에만 사용하고 다시 화면으로 읽어 오지 않는다.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, Windows Generic Credential, xUnit.

---

## File map

- `src/AssetPilot.Core/Accounts/*`: 계좌 모델과 저장소 포트. Windows 형식은 두지 않는다.
- `src/AssetPilot.Infrastructure/Accounts/KiwoomCredentialPayload.cs`: blob 유효성 검사와 직렬화.
- `src/AssetPilot.Infrastructure/Accounts/WindowsKiwoomAccountStore.cs`: Credential ↔ Core 모델 변환, 손상 항목 격리.
- `src/AssetPilot.App/ViewModels/MainViewModel.cs`: 계좌 목록, 선택, 포트폴리오 조회 상태.
- `src/AssetPilot.App/ViewModels/AccountManagementViewModel.cs`: 계좌 저장·삭제 입력 상태.
- `src/AssetPilot.App/AccountManagementWindow.*`: PasswordBox를 포함한 계좌 관리 UI.
- `tests/AssetPilot.Core.Tests/WindowsCredentialFormatTests.cs`: 형식·store 회귀 테스트.
- `tests/AssetPilot.App.Tests/MainViewModelTests.cs`: 화면과 독립적인 ViewModel 동작 테스트.

### Task 1: Harden Credential Manager account storage

**Files:**
- Modify: `src/AssetPilot.Infrastructure/Accounts/KiwoomCredentialPayload.cs`
- Modify: `src/AssetPilot.Infrastructure/Accounts/WindowsKiwoomAccountStore.cs`
- Modify: `tests/AssetPilot.Core.Tests/WindowsCredentialFormatTests.cs`

- [x] Step 1: Write failing tests for malformed blob, empty Secret Key, invalid `KiwoomEnvironment`, direct lookup, delete, and corrupted-list-item handling.

```csharp
[Fact]
public void Deserialize_rejects_malformed_payload()
{
    Assert.Throws<InvalidOperationException>(() =>
        KiwoomCredentialPayload.Deserialize("not-json"u8.ToArray()));
}

[Fact]
public async Task GetAllAsync_skips_a_corrupted_assetpilot_credential()
{
    // valid target/payload one, same-prefix invalid payload one
    // expect only the valid KiwoomAccountProfile
}
```

- [x] Step 2: Run `rtk "C:\Program Files\dotnet\dotnet.exe" test tests\AssetPilot.Core.Tests\AssetPilot.Core.Tests.csproj --no-restore --filter WindowsCredentialFormatTests`. Expected: assertion or compilation failure for the unimplemented validation.

- [x] Step 3: Make `Deserialize` catch `JsonException`, require nonblank `SecretKey`, and require `Enum.IsDefined(payload.Environment)`; throw one `InvalidOperationException` that has no App Key or Secret Key text.

```csharp
public static KiwoomCredentialPayload Deserialize(byte[] value)
{
    try
    {
        var payload = JsonSerializer.Deserialize<KiwoomCredentialPayload>(value);
        if (payload is null || string.IsNullOrWhiteSpace(payload.SecretKey)
            || !Enum.IsDefined(payload.Environment))
        {
            throw new InvalidOperationException();
        }
        return payload;
    }
    catch (JsonException exception)
    {
        throw new InvalidOperationException("Invalid Kiwoom credential format.", exception);
    }
}
```

- [x] Step 4: In `WindowsKiwoomAccountStore`, validate nonblank App Key on save; make `GetAllAsync` skip only entries whose target/payload cannot become a valid profile; make `GetAsync` and `DeleteAsync` use only `WindowsCredentialTarget.Create(accountNumber)`.

- [x] Step 5: Re-run the focused tests and build Infrastructure. Expected: tests pass or any Windows application-control block is reported separately; build has zero warnings/errors.

### Task 2: Add an application-level ViewModel test project

**Files:**
- Create: `tests/AssetPilot.App.Tests/AssetPilot.App.Tests.csproj`
- Create: `tests/AssetPilot.App.Tests/MainViewModelTests.cs`
- Modify: `AssetPilot.slnx`
- Modify: `src/AssetPilot.App/ViewModels/MainViewModel.cs`

- [x] Step 1: Create a `net10.0-windows` xUnit project that references `AssetPilot.App`, `AssetPilot.Core`, and uses package versions matching `tests/AssetPilot.Core.Tests`.

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
  <IsPackable>false</IsPackable>
</PropertyGroup>
```

- [x] Step 2: Add failing ViewModel tests using fake `IKiwoomAccountStore` and `IKiwoomPortfolioClient`: no accounts shows a no-account message and makes no portfolio call; two accounts select the sorted first account; refresh sends the selected account number and assigns a fixed `Portfolio`.

```csharp
await viewModel.InitializeCommand.ExecuteAsync(null);
Assert.Equal("1234567890", viewModel.SelectedAccount!.AccountNumber);
await viewModel.RefreshCommand.ExecuteAsync(null);
Assert.Equal("1234567890", portfolioClient.RequestedAccountNumber);
```

- [x] Step 3: Implement `MainViewModel(IKiwoomAccountStore, IKiwoomPortfolioClient)` with `ObservableCollection<KiwoomAccountProfile> Accounts`, nullable `SelectedAccount`, nullable `Portfolio`, `StatusMessage`, and `IsBusy`. `InitializeAsync` orders accounts by account number and selects its first item. `RefreshAsync` returns early with a clear status if no account is selected; otherwise it awaits `GetPortfolioAsync(SelectedAccount.AccountNumber)`.

- [x] Step 4: Ensure `RefreshAsync` catches expected account/API exceptions into a generic status message and never formats exception messages containing credentials.

- [x] Step 5: Run the App ViewModel tests. Expected: pass; if Windows application-control blocks the App or Infrastructure DLL, record it as environment failure rather than an assertion result.

### Task 3: Add account management ViewModel and dialog

**Files:**
- Create: `src/AssetPilot.App/ViewModels/AccountManagementViewModel.cs`
- Create: `src/AssetPilot.App/AccountManagementWindow.xaml`
- Create: `src/AssetPilot.App/AccountManagementWindow.xaml.cs`
- Modify: `tests/AssetPilot.App.Tests/MainViewModelTests.cs`

- [x] Step 1: Add failing tests for saving a mock account and deleting a selected account using an `IKiwoomAccountStore` fake. Saving requires nonblank account number, App Key, and method-supplied Secret Key.

- [x] Step 2: Implement `AccountManagementViewModel` with `Accounts`, `SelectedAccount`, `AccountNumber`, `Environment`, `AppKey`, `StatusMessage`, `LoadAsync`, `SaveAsync(string secretKey)`, and `DeleteAsync`. `SaveAsync` creates `KiwoomAccountCredentials(new KiwoomAccountProfile(AccountNumber, Environment), AppKey, secretKey)` and reloads the list. It clears only its non-secret input state; it never stores Secret Key in an observable property.

- [x] Step 3: Implement `AccountManagementWindow` with account list, account number, Real/Mock ComboBox, App Key TextBox, Secret Key PasswordBox, Save, and Delete controls. On Save click call `await viewModel.SaveAsync(SecretKeyPasswordBox.Password)` then immediately set `SecretKeyPasswordBox.Password = string.Empty`.

- [x] Step 4: On existing-account selection, copy only the account number and environment into the edit fields. Keep App Key and Secret Key empty so updating an account explicitly requires both keys to be entered again.

- [x] Step 5: Run ViewModel tests and build `src/AssetPilot.App/AssetPilot.App.csproj --no-restore`. Expected: zero warnings/errors.

### Task 4: Connect account selection and portfolio display

**Files:**
- Modify: `src/AssetPilot.App/MainWindow.xaml`
- Modify: `src/AssetPilot.App/MainWindow.xaml.cs`
- Modify: `src/AssetPilot.App/App.xaml.cs`
- Modify: `src/AssetPilot.App/ViewModels/MainViewModel.cs`

- [x] Step 1: Replace the title-only MainWindow content with a ComboBox bound to `Accounts`/`SelectedAccount`, Refresh button bound to `RefreshCommand`, Account Management button, status text, summary values from `Portfolio`, and a holdings DataGrid bound to `Portfolio.Positions`.

- [x] Step 2: Keep dialog creation in `MainWindow.xaml.cs`, not in MainViewModel. Resolve a transient `AccountManagementWindow` from DI, call `ShowDialog()`, then await `viewModel.InitializeAsync()` after it closes.

- [x] Step 3: Register `AccountManagementViewModel` and `AccountManagementWindow` as transient services. Before showing MainWindow, call `await mainViewModel.InitializeAsync()`. Keep the existing `HttpClient`, Credential Manager, account store, and `IKiwoomPortfolioClient` registrations.

- [ ] Step 4: Verify manually with no AssetPilot Credential: window opens, no-account message is visible, and no portfolio HTTP request occurs. Then add a mock account, close the dialog, select it, and verify that refresh uses that account number with a fake HTTP test handler only.

### Task 5: Regression and documentation checks

**Files:**
- Modify: `docs/ARCHITECTURE.md` only if implementation changes this approved design.

- [ ] Step 1: Run `rtk "C:\Program Files\dotnet\dotnet.exe" build AssetPilot.slnx --no-restore`.
- [ ] Step 2: Run `rtk "C:\Program Files\dotnet\dotnet.exe" test AssetPilot.slnx --no-restore`.
- [ ] Step 3: Distinguish assertion failures from the known Windows application-control DLL load block. Do not execute live or mock Kiwoom HTTP requests and do not commit while commits remain paused.
