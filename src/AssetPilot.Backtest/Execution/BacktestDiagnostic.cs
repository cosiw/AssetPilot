namespace AssetPilot.Backtest.Execution;

public sealed record BacktestDiagnostic(string Code, DateOnly TradeDate, string Message)
{
    public string DisplayName => Code switch
    {
        "BelowSma" => "이동평균 미충족",
        "LongSmaSlopeNotPositive" => "장기 이평 기울기 미충족",
        "MinimumSmaDistanceNotMet" => "최소 SMA 이격도 미충족",
        "ShortSmaNotAboveLongSma" => "단기 이평 추세 미충족",
        "BuyBandNotReached" => "매수 구간 미도달",
        "MissingIntradayData" => "5분봉 데이터 없음",
        _ => Code
    };
}
