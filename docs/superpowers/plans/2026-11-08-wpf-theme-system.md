# WPF Theme System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** AssetPilot의 보유 현황과 계좌 관리 창에 재사용 가능한 WPF 테마 리소스를 만들고, 개인 투자 장부 스타일을 적용한다.

**Architecture:** 시각 토큰은 `Themes/Tokens`에, 컨트롤·표면 스타일은 `Themes/Controls`와 `Themes/Components`에 분리한다. `App.xaml`은 `Theme.xaml` 하나만 병합하고, 화면 XAML은 직접 색상·폰트 크기·임의 여백을 선언하지 않고 리소스 키만 사용한다. 손익의 한국 시장 관례(상승/수익 빨강, 하락/손실 파랑)는 App 계층의 값-색상 변환기가 처리한다.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, xUnit

**Scope guard:** `MainViewModel`, `AccountManagementViewModel`, Kiwoom API, Credential Manager, 창 code-behind의 동작은 변경하지 않는다. 새 NuGet 패키지도 추가하지 않는다. 사용자 요청에 따라 커밋/스테이징은 수행하지 않는다.

---

## File map

| Path | Change | Responsibility |
| --- | --- | --- |
| `src/AssetPilot.App/Themes/Tokens/Colors.xaml` | Create | 의미 기반 Color/Brush 토큰과 국내 시장 상승·하락 색상 |
| `src/AssetPilot.App/Themes/Tokens/Spacing.xaml` | Create | 기본 및 의미 기반 Thickness/크기 토큰 |
| `src/AssetPilot.App/Themes/Tokens/Typography.xaml` | Create | 한글·숫자 글꼴과 TextBlock 스타일 |
| `src/AssetPilot.App/Themes/Controls/Buttons.xaml` | Create | Base/Primary/Secondary/Quiet/Danger Button 템플릿과 상태 트리거 |
| `src/AssetPilot.App/Themes/Controls/Inputs.xaml` | Create | TextBox, PasswordBox, ComboBox, ListBox 스타일 |
| `src/AssetPilot.App/Themes/Controls/DataGrid.xaml` | Create | 포트폴리오 표의 DataGrid, 헤더, 행, 셀 스타일 |
| `src/AssetPilot.App/Themes/Components/Surfaces.xaml` | Create | 장부 패널과 상태 메시지 표면 스타일 |
| `src/AssetPilot.App/Themes/Theme.xaml` | Create | 위 ResourceDictionary의 병합 순서 |
| `src/AssetPilot.App/Converters/MarketValueBrushConverter.cs` | Create | 양수/음수/0 값을 Rise/Fall/Neutral Brush로 변환 |
| `src/AssetPilot.App/App.xaml` | Modify | Theme 병합과 변환기 등록 |
| `src/AssetPilot.App/MainWindow.xaml` | Modify | 카드형 대시보드를 계좌 장부형 레이아웃으로 교체 |
| `src/AssetPilot.App/AccountManagementWindow.xaml` | Modify | 계좌 목록·입력·명령 버튼에 공통 테마 적용 |
| `tests/AssetPilot.App.Tests/MarketValueBrushConverterTests.cs` | Create | 상승/하락/보합 Brush 선택 테스트 |

`MainWindow.xaml.cs`, `AccountManagementWindow.xaml.cs`, `MainViewModel.cs`, `AccountManagementViewModel.cs`, `App.xaml.cs`는 변경하지 않는다.

### Task 1: 손익 색상 변환기를 테스트 주도로 추가

**Files:**
- Create: `tests/AssetPilot.App.Tests/MarketValueBrushConverterTests.cs`
- Create: `src/AssetPilot.App/Converters/MarketValueBrushConverter.cs`

- [ ] **Step 1: 실패하는 변환기 테스트 작성**

`MarketValueBrushConverterTests.cs`에 다음 세 테스트를 작성한다. 테스트는 변환기의 Brush 의존성을 직접 주입하므로 `Application.Current`나 실제 WPF 리소스에 의존하지 않는다.

```csharp
using System.Globalization;
using System.Windows.Media;
using AssetPilot.App.Converters;

namespace AssetPilot.App.Tests;

public sealed class MarketValueBrushConverterTests
{
    private static readonly Brush Rise = new SolidColorBrush(Colors.Red);
    private static readonly Brush Fall = new SolidColorBrush(Colors.Blue);
    private static readonly Brush Neutral = new SolidColorBrush(Colors.Black);

    private static MarketValueBrushConverter CreateConverter() => new()
    {
        RiseBrush = Rise,
        FallBrush = Fall,
        NeutralBrush = Neutral
    };

    [Fact]
    public void Convert_returns_rise_brush_for_positive_values()
    {
        var converter = CreateConverter();

        Assert.Same(Rise, converter.Convert(1m, typeof(Brush), null, CultureInfo.InvariantCulture));
        Assert.Same(Rise, converter.Convert(0.01m, typeof(Brush), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Convert_returns_fall_brush_for_negative_values()
    {
        var converter = CreateConverter();

        Assert.Same(Fall, converter.Convert(-1m, typeof(Brush), null, CultureInfo.InvariantCulture));
        Assert.Same(Fall, converter.Convert(-0.01m, typeof(Brush), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Convert_returns_neutral_brush_for_zero_or_missing_value()
    {
        var converter = CreateConverter();

        Assert.Same(Neutral, converter.Convert(0m, typeof(Brush), null, CultureInfo.InvariantCulture));
        Assert.Same(Neutral, converter.Convert(null, typeof(Brush), null, CultureInfo.InvariantCulture));
    }
}
```

- [ ] **Step 2: 테스트가 컴파일/실행 실패하는지 확인**

Run:

```powershell
rtk "C:\Program Files\dotnet\dotnet.exe" test tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --no-restore --filter FullyQualifiedName~MarketValueBrushConverterTests
```

Expected: `MarketValueBrushConverter` 타입이 없어서 컴파일 실패한다.

- [ ] **Step 3: 최소 변환기 구현**

`src/AssetPilot.App/Converters/MarketValueBrushConverter.cs`를 다음 내용으로 만든다.

```csharp
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AssetPilot.App.Converters;

public sealed class MarketValueBrushConverter : IValueConverter
{
    public Brush? RiseBrush { get; set; }
    public Brush? FallBrush { get; set; }
    public Brush? NeutralBrush { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not decimal number)
        {
            return NeutralBrush;
        }

        return number > 0m ? RiseBrush : number < 0m ? FallBrush : NeutralBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 4: 변환기 테스트 통과 확인**

Run:

```powershell
rtk "C:\Program Files\dotnet\dotnet.exe" test tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --no-restore --filter FullyQualifiedName~MarketValueBrushConverterTests
```

Expected: 3 tests passed.

### Task 2: 토큰과 테마 진입점을 작성

**Files:**
- Create: `src/AssetPilot.App/Themes/Tokens/Colors.xaml`
- Create: `src/AssetPilot.App/Themes/Tokens/Spacing.xaml`
- Create: `src/AssetPilot.App/Themes/Tokens/Typography.xaml`

- [ ] **Step 1: 색상 토큰 정의**

`Colors.xaml`에 아래 키를 정의한다. `Color.*`은 원색, `Brush.*`는 화면에서 실제로 참조할 Brush다.

```xml
<Color x:Key="Color.AppBackground">#F7F8F9</Color>
<Color x:Key="Color.Surface">#FFFFFFFF</Color>
<Color x:Key="Color.TextPrimary">#FF172235</Color>
<Color x:Key="Color.TextSecondary">#FF667085</Color>
<Color x:Key="Color.Border">#FFDCE2EA</Color>
<Color x:Key="Color.BorderStrong">#FFAAB7C8</Color>
<Color x:Key="Color.Action">#FF123452</Color>
<Color x:Key="Color.ActionPressed">#FF0A2742</Color>
<Color x:Key="Color.Focus">#FF4B739E</Color>
<Color x:Key="Color.MarketRise">#FFC63C4C</Color>
<Color x:Key="Color.MarketFall">#FF1E62B7</Color>
```

각 Color에 대응하는 `Brush.AppBackground`, `Brush.Surface`, `Brush.Text.Primary`, `Brush.Text.Secondary`, `Brush.Border`, `Brush.Border.Strong`, `Brush.Action`, `Brush.Action.Pressed`, `Brush.Focus`, `Brush.Market.Rise`, `Brush.Market.Fall`를 선언한다. 보합은 `Brush.Text.Primary`를 사용하므로 별도의 회색 손익 Brush는 만들지 않는다.

- [ ] **Step 2: 간격과 서체 토큰 정의**

`Spacing.xaml`에는 `Space.4`, `Space.8`, `Space.12`, `Space.18`, `Space.24`, `Space.38` Thickness와 `Padding.Panel`, `Padding.Dialog`, `Margin.SectionTop`, `Margin.LabelBottom`을 선언한다. 값은 각각 디자인 보드에서 합의한 4/8/12/18/24/38px 체계를 사용한다.

`Typography.xaml`에는 `FontFamily.Korean` (`Pretendard, Noto Sans KR, Malgun Gothic`), `FontFamily.Numeric` (`Bahnschrift, Segoe UI`), `Text.PageTitle`, `Text.SectionTitle`, `Text.Label`, `Text.Body`, `Text.MoneyLarge`, `Text.Numeric` 스타일을 선언한다. `Text.MoneyLarge`과 `Text.Numeric`에는 `FontFamily.Numeric` 및 `Typography.NumeralAlignment="Tabular"`을 사용할 수 없으므로, WPF의 `TextOptions.TextFormattingMode="Display"`와 고정폭 숫자 글꼴을 사용한다.


- [ ] **Step 3: 토큰 Dictionary만 포함한 App 프로젝트 빌드로 XAML 검증**

Run:

```powershell
rtk "C:\Program Files\dotnet\dotnet.exe" build src\AssetPilot.App\AssetPilot.App.csproj --no-restore --output C:\Users\cosiw\AppData\Local\Temp\AssetPilot-Theme-Build
```

Expected: 0 warnings, 0 errors.

### Task 3: 공통 컨트롤과 표면 스타일 구현

**Files:**
- Create: `src/AssetPilot.App/Themes/Controls/Buttons.xaml`
- Create: `src/AssetPilot.App/Themes/Controls/Inputs.xaml`
- Create: `src/AssetPilot.App/Themes/Controls/DataGrid.xaml`
- Create: `src/AssetPilot.App/Themes/Components/Surfaces.xaml`
- Create: `src/AssetPilot.App/Themes/Theme.xaml`
- Modify: `src/AssetPilot.App/App.xaml`

- [ ] **Step 1: Button 템플릿과 변형 추가**

`Buttons.xaml`에서 `Button.Base`에 높이 38, 가로 Padding 15, `FocusVisualStyle` 및 `ControlTemplate`을 정의한다. 템플릿의 최상위 요소는 `CornerRadius="4"`인 `Border`여야 한다. `IsMouseOver`, `IsPressed`, `IsEnabled`, `IsKeyboardFocused` 트리거를 포함한다.

파생 스타일은 다음 역할을 가진다.

| Key | Background / Border | Usage |
| --- | --- | --- |
| `Button.Primary` | `Brush.Action` / `Brush.Action` | 잔고 새로고침, 저장 |
| `Button.Secondary` | `Brush.Surface` / `Brush.Border.Strong` | 계좌 관리, 닫기 |
| `Button.Quiet` | 투명 / 투명 | 취소, 상세 보기 |
| `Button.Danger` | `Brush.Surface` / `Brush.Market.Rise` | 삭제 확인 |

`Button.Danger`는 삭제 확인 창 또는 계좌 관리의 명시적 삭제에만 사용하며, 포트폴리오 화면에는 사용하지 않는다.

- [ ] **Step 2: 입력·목록 컨트롤 스타일 추가**

`Inputs.xaml`에는 `TextBox.Standard`, `PasswordBox.Standard`, `ComboBox.Account`, `ListBox.Accounts`, `ListBoxItem.Account` 키를 정의한다. 높이는 38, 모서리는 4, 테두리는 `Brush.Border.Strong`으로 통일한다. 키보드 포커스 시 `Brush.Focus` 테두리와 2px 두께를 사용한다. 계좌 목록의 선택 배경은 `#FFEFF4F8`의 `Brush.Selection.Background` 리소스로 추가하고, 선택 상태가 수익/손실을 의미하지 않도록 시장색 Brush를 사용하지 않는다.

- [ ] **Step 3: DataGrid 스타일 추가**

`DataGrid.xaml`에 `DataGrid.Portfolio`를 정의한다. 요구 사항은 다음과 같다.

- 행 머리글은 숨긴다 (`HeadersVisibility="Column"`, `RowHeaderWidth="0"`).
- `CanUserAddRows="False"`, `IsReadOnly="True"`, `GridLinesVisibility="None"`을 기본값으로 둔다.
- ColumnHeader는 39px 높이, 왼쪽/오른쪽 Padding 17, 밝은 표면, 하단 테두리 1px을 사용한다.
- DataGridRow는 64px 높이, 선택 시만 옅은 청회색 배경을 사용한다.
- DataGridCell은 기본 테두리를 없애고, 숫자 열에 적용할 `DataGridCell.Numeric` 스타일은 오른쪽 정렬과 `FontFamily.Numeric`을 갖는다.

- [ ] **Step 4: 장부 표면과 메시지 표면 추가**

`Surfaces.xaml`에 다음 키를 만든다.

| Key | Target | Definition |
| --- | --- | --- |
| `Surface.Ledger` | `Border` | 흰 배경, 1px `Brush.Border`, 상단 3px `Brush.Action`, 모서리 0 |
| `Surface.Table` | `Border` | 흰 배경, 1px `Brush.Border`, 모서리 0 |
| `Text.StatusMessage` | `TextBlock` | 보조 글자 크기, 줄바꿈, 기본 손익색 미사용 |
| `Text.ErrorMessage` | `TextBlock` | `Brush.Market.Rise`, 줄바꿈 |

- [ ] **Step 5: Theme.xaml과 App.xaml에서 모든 Dictionary 및 변환기 등록**

`Theme.xaml`의 `ResourceDictionary.MergedDictionaries`에 다음 순서를 넣는다.

```xml
<ResourceDictionary.MergedDictionaries>
    <ResourceDictionary Source="Tokens/Colors.xaml" />
    <ResourceDictionary Source="Tokens/Spacing.xaml" />
    <ResourceDictionary Source="Tokens/Typography.xaml" />
    <ResourceDictionary Source="Controls/Buttons.xaml" />
    <ResourceDictionary Source="Controls/Inputs.xaml" />
    <ResourceDictionary Source="Controls/DataGrid.xaml" />
    <ResourceDictionary Source="Components/Surfaces.xaml" />
</ResourceDictionary.MergedDictionaries>
```

`App.xaml`에 `converters` 네임스페이스를 추가하고, 아래 리소스를 선언한다.

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Themes/Theme.xaml" />
        </ResourceDictionary.MergedDictionaries>
        <converters:MarketValueBrushConverter
            x:Key="MarketValueBrushConverter"
            RiseBrush="{StaticResource Brush.Market.Rise}"
            FallBrush="{StaticResource Brush.Market.Fall}"
            NeutralBrush="{StaticResource Brush.Text.Primary}" />
    </ResourceDictionary>
</Application.Resources>
```

- [ ] **Step 6: 모든 Dictionary가 포함된 상태에서 빌드 확인**

Run:

```powershell
rtk "C:\Program Files\dotnet\dotnet.exe" build src\AssetPilot.App\AssetPilot.App.csproj --no-restore --output C:\Users\cosiw\AppData\Local\Temp\AssetPilot-Theme-Build
```

Expected: 0 warnings, 0 errors.

### Task 4: MainWindow를 장부형 보유 현황으로 전환

**Files:**
- Modify: `src/AssetPilot.App/MainWindow.xaml`

- [ ] **Step 1: 화면 구조를 리소스 기반으로 교체**

`MainWindow.xaml`에서 모든 직접 `Background`, `Foreground`, `FontSize`, `Padding`, `Margin`, `CornerRadius` 값을 제거한다. `Grid`의 `Auto`/`*` 레이아웃 값과 열 너비는 직접 사용해도 된다.

새 구조는 아래 순서로 유지한다.

```text
Window
└─ Grid (Padding.Window)
   ├─ 상단: "보유 현황" + 계좌 관리 Button.Secondary
   ├─ 계좌 명령줄: ComboBox.Account + 새로고침 Button.Primary
   ├─ 메시지: StatusMessage / IsBusy일 때 "불러오는 중…"
   ├─ 계좌 요약: Surface.Ledger
   │  └─ 평가금액 / 매입금액 / 평가손익 / 수익률 (한 줄 4열)
   └─ 보유 종목: Surface.Table 안의 DataGrid.Portfolio
```

기존 `UniformGrid` 네 카드와 각 카드의 `CornerRadius="8"`을 제거한다. 요약값은 `Portfolio.TotalEvaluationAmount`, `TotalPurchaseAmount`, `TotalProfitLoss`, `TotalProfitRate` 바인딩을 그대로 사용한다. 평가손익과 수익률 TextBlock의 `Foreground`는 `MarketValueBrushConverter`로 바인딩한다. 숫자는 `{0:N0}` 또는 `{0:N2}%` 포맷을 유지하되, 양수에는 `+` 기호가 보이도록 `StringFormat={}{0:+#,0;-#,0;0}` 및 `StringFormat={}{0:+0.00;-0.00;0.00}%`를 적용한다.

- [ ] **Step 2: 종목 DataGrid를 TemplateColumn 기반으로 전환**

수익/손실 색을 적용해야 하므로 `EvaluationProfitLoss`와 `ProfitRate` 열은 `DataGridTextColumn` 대신 `DataGridTemplateColumn`으로 만든다. 내부 `TextBlock`은 아래 바인딩 형태를 쓴다.

```xml
<TextBlock
    Style="{StaticResource Text.Numeric}"
    Foreground="{Binding EvaluationProfitLoss, Converter={StaticResource MarketValueBrushConverter}}"
    Text="{Binding EvaluationProfitLoss, StringFormat={}{0:+#,0;-#,0;0}}" />
```

`ProfitRate`도 같은 방식으로 `ProfitRate`를 Converter에 넘기고 `StringFormat={}{0:+0.00;-0.00;0.00}%`를 사용한다. 수익률 0은 기본 본문색으로 표시한다. 다른 금액 열은 `DataGridCell.Numeric` 및 `Text.Numeric`를 적용한다.

- [ ] **Step 3: Busy/오류 문구를 문장형 UI로 표시**

`IsBusy`가 true이면 새로고침 버튼 Content를 `불러오는 중…`으로 바꾸고 비활성화한다. 이때 별도 `ProgressBar`와 `조회 중` 상태 배지는 제거한다. `StatusMessage`가 비어 있지 않을 때만 `Text.ErrorMessage` 영역을 보이게 하며, 메시지는 ViewModel의 기존 문구를 그대로 사용한다.

- [ ] **Step 4: 앱 빌드와 수동 확인**

Run:

```powershell
rtk "C:\Program Files\dotnet\dotnet.exe" build src\AssetPilot.App\AssetPilot.App.csproj --no-restore --output C:\Users\cosiw\AppData\Local\Temp\AssetPilot-Theme-Build
```

Expected: 0 warnings, 0 errors.

Manual check after closing an already-running AssetPilot process and starting the rebuilt app:

1. 계좌 선택, 계좌 관리, 새로고침 버튼의 높이와 4px 모서리가 일관적인지 확인한다.
2. 요약이 네 개의 둥근 카드가 아니라 하나의 상단선 장부 패널인지 확인한다.
3. 양수 평가손익/수익률은 빨강, 음수는 파랑, 0은 기본 글자색인지 확인한다.
4. 새로고침 중에는 버튼이 `불러오는 중…`으로 바뀌고 클릭할 수 없는지 확인한다.

### Task 5: 계좌 관리 창에 같은 테마 적용

**Files:**
- Modify: `src/AssetPilot.App/AccountManagementWindow.xaml`

- [ ] **Step 1: 계좌 목록과 폼의 표면 적용**

창의 루트 Grid에 `Padding.Dialog`을 적용한다. `ListBox.Accounts`를 계좌 목록에 적용하고, 각 폼 행의 레이블은 `Text.Label`, 값 입력은 `TextBox.Standard`/`PasswordBox.Standard`/`ComboBox.Account`를 적용한다. 기존의 직접 `Margin="0,16,0,0"`, `Margin="0,8,0,0"`은 `Margin.SectionTop`, `Space.8` 등 Spacing 토큰으로 대체한다.

- [ ] **Step 2: 계정 저장/삭제 행동의 위계 적용**

저장 Button은 `Button.Primary`, 삭제 Button은 `Button.Danger`를 적용한다. 삭제 버튼은 저장 버튼 옆의 동일한 강조 수준으로 보이지 않도록 분리된 오른쪽 명령 영역에 두고, 현재 동작(바로 `DeleteCommand` 실행)은 바꾸지 않는다. 삭제 확인 대화상자는 이 계획 범위 밖이다.

- [ ] **Step 3: 상태 메시지와 비밀값 처리 유지 확인**

`StatusMessage`에는 `Text.ErrorMessage`를 적용한다. `SecretKeyPasswordBox`의 코드비하인드 처리(`SaveCommand.ExecuteAsync(SecretKeyPasswordBox.Password)` 뒤 빈 문자열 할당)는 절대 변경하지 않는다.

- [ ] **Step 4: 빌드와 기존 ViewModel 테스트 확인**

Run:

```powershell
rtk "C:\Program Files\dotnet\dotnet.exe" test tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --no-restore
rtk "C:\Program Files\dotnet\dotnet.exe" build src\AssetPilot.App\AssetPilot.App.csproj --no-restore --output C:\Users\cosiw\AppData\Local\Temp\AssetPilot-Theme-Build
```

Expected: App test suite passes; build reports 0 warnings and 0 errors.

### Task 6: 최종 회귀 확인과 리소스 사용 감사

**Files:**
- Modify only if audit identifies a missed direct visual literal: `src/AssetPilot.App/MainWindow.xaml`, `src/AssetPilot.App/AccountManagementWindow.xaml`

- [ ] **Step 1: App 테스트 전체 실행**

Run:

```powershell
rtk "C:\Program Files\dotnet\dotnet.exe" test tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --no-restore
```

Expected: `MainViewModelTests`와 `MarketValueBrushConverterTests`가 모두 통과한다.

- [ ] **Step 2: 리소스 사용 감사**

다음 명령으로 두 창에 남은 직접 시각 리터럴을 찾는다.

```powershell
rtk rg 'Background="#|Foreground="#|FontSize="[0-9]|Padding="[0-9]|Margin="[0-9]|CornerRadius="[0-9]' src/AssetPilot.App/MainWindow.xaml src/AssetPilot.App/AccountManagementWindow.xaml
```

Expected: 결과 없음. `Auto`, `*`, Grid 열 너비, 바인딩 문자열 포맷은 감사 대상이 아니다.

- [ ] **Step 3: 실제 창 최종 점검**

1. 이미 실행 중인 앱이 있으면 사용자가 닫은 뒤 새로 실행한다.
2. 빈 계좌 목록, 계좌 선택 후 새로고침, API 실패 메시지, 계좌 저장·삭제를 각각 한 번씩 확인한다.
3. 계좌 관리 창을 닫은 뒤 메인 목록이 다시 로드되는 기존 동작이 유지되는지 확인한다.
4. 실행 중인 앱의 출력 DLL 잠금이 있을 때는 프로세스를 강제 종료하지 않고 사용자에게 재시작을 요청한다.

## Self-review

- **범위:** 토큰, 컨트롤, 표면, 두 기존 창, 손익색 변환기, 테스트를 모두 포함한다. API와 ViewModel 행위는 포함하지 않는다.
- **국내 시장 색상:** 양수/수익은 `Brush.Market.Rise` 빨강, 음수/손실은 `Brush.Market.Fall` 파랑으로 Task 2/4에 명시했다.
- **직접 리터럴 금지:** Task 4/5에서 화면 XAML의 직접 시각값을 제거하고 Task 6에서 검색으로 검증한다.
- **테스트 가능성:** UI 상태판단 로직은 App 리소스와 분리해 xUnit으로 검증하고, XAML은 WPF 빌드와 수동 창 확인으로 검증한다.
- **커밋:** 사용자가 커밋을 보류했으므로 이 계획과 구현 모두 커밋/스테이징하지 않는다.
