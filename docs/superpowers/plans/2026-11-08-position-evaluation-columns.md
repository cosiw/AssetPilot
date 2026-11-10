# Position Evaluation Columns Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display each domestic stock position's purchase amount, evaluation amount, evaluation profit/loss, and profit rate from the existing Kiwoom `kt00018` response.

**Architecture:** Keep Kiwoom wire names inside Infrastructure DTOs. Map the values into the existing Core `PortfolioPosition` record, then bind those properties directly in the WPF DataGrid. No additional Kiwoom endpoint is called.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, System.Text.Json, xUnit.

---

### Task 1: Map the existing kt00018 position fields

**Files:**
- Modify: `tests/AssetPilot.Core.Tests/KiwoomClientTests.cs`
- Modify: `src/AssetPilot.Core/Portfolio/PortfolioPosition.cs`
- Modify: `src/AssetPilot.Infrastructure/Kiwoom/Contracts/KiwoomAccountHoldingResponse.cs`
- Modify: `src/AssetPilot.Infrastructure/Kiwoom/KiwoomClient.cs`

- [x] **Step 1: Write a failing mapping assertion**

```csharp
Assert.Equal(2_872_800m, position.PurchaseAmount);
Assert.Equal(3_183_600m, position.EvaluationAmount);
Assert.Equal(310_800m, position.EvaluationProfitLoss);
Assert.Equal(10.82m, position.ProfitRate);
```

- [x] **Step 2: Run the focused test and confirm it fails because the properties do not exist.**

Run: `rtk "C:\Program Files\dotnet\dotnet.exe" test tests\AssetPilot.Core.Tests\AssetPilot.Core.Tests.csproj --no-restore --filter GetPortfolioAsync_requests_token_then_maps_account_evaluation_to_portfolio`

- [x] **Step 3: Add the four Core fields, DTO mappings (`pur_amt`, `evlt_amt`, `evltv_prft`, `prft_rt`), and map them in `KiwoomClient`. Correct average price to `pur_pric`.**

- [x] **Step 4: Re-run the focused test and confirm it passes.**

### Task 2: Show the values in the dashboard

**Files:**
- Modify: `src/AssetPilot.App/MainWindow.xaml`

- [x] **Step 1: Add right-aligned DataGrid columns for purchase amount, evaluation amount, evaluation profit/loss, and profit rate.**
- [x] **Step 2: Build the App project and run the focused Core test.**

### Task 3: Regression check

- [x] **Step 1: Run the App ViewModel tests and the Core tests.**
- [x] **Step 2: Do not commit; report pre-existing time-sensitive token-fixture failures separately if they recur.**
