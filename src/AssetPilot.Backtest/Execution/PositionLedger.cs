namespace AssetPilot.Backtest.Execution;

public sealed class PositionLedger
{
    private readonly List<BacktestPositionSnapshot> _snapshots = [];
    private readonly List<BacktestPositionCycle> _completedCycles = [];
    private readonly HashSet<Guid> _executionIds = [];
    private decimal _cash;
    private decimal _quantity;
    private decimal _averageEntryPrice;
    private decimal _positionCost;
    private decimal _realizedProfitTotal;
    private Guid? _openingExecutionId;
    private DateTime _openedAt;
    private decimal _cycleBoughtQuantity;
    private decimal _cycleSoldQuantity;
    private decimal _cycleCostBasis;
    private decimal _cycleGrossSaleAmount;
    private decimal _cycleRealizedProfit;

    public PositionLedger(decimal startingCash)
    {
        if (startingCash < 0m) throw new ArgumentOutOfRangeException(nameof(startingCash));
        _cash = startingCash;
    }

    public IReadOnlyList<BacktestPositionSnapshot> Snapshots => _snapshots;
    public IReadOnlyList<BacktestPositionCycle> CompletedCycles => _completedCycles;

    public LedgerApplication Apply(BacktestExecution execution)
    {
        Validate(execution);
        if (execution.Sequence != _snapshots.Count + 1) throw new ArgumentException("체결 순번은 1부터 연속되어야 합니다.", nameof(execution));
        if (!_executionIds.Add(execution.ExecutionId)) throw new ArgumentException("체결 ID가 중복되었습니다.", nameof(execution));
        if (execution.Side == ExecutionSide.Sell && execution.Quantity > _quantity)
        {
            _executionIds.Remove(execution.ExecutionId);
            throw new InvalidOperationException("보유 수량보다 많이 매도할 수 없습니다.");
        }

        BacktestExecution applied;
        BacktestPositionCycle? completedCycle;
        if (execution.Side == ExecutionSide.Buy)
        {
            applied = execution with { RealizedProfit = null };
            completedCycle = ApplyBuy(applied);
        }
        else
        {
            applied = ApplySell(execution, out completedCycle);
        }

        var snapshot = new BacktestPositionSnapshot(
            applied.ExecutionId,
            applied.Sequence,
            applied.ExecutedAt,
            _cash,
            _quantity,
            _averageEntryPrice,
            _positionCost,
            _realizedProfitTotal);
        _snapshots.Add(snapshot);
        if (completedCycle is not null) _completedCycles.Add(completedCycle);
        return new LedgerApplication(applied, snapshot, completedCycle);
    }

    private BacktestPositionCycle? ApplyBuy(BacktestExecution execution)
    {
        var totalCost = execution.GrossAmount + execution.Fee + execution.Tax;
        if (_quantity == 0m)
        {
            _openingExecutionId = execution.ExecutionId;
            _openedAt = execution.ExecutedAt;
            _cycleBoughtQuantity = 0m;
            _cycleSoldQuantity = 0m;
            _cycleCostBasis = 0m;
            _cycleGrossSaleAmount = 0m;
            _cycleRealizedProfit = 0m;
        }

        _cash -= totalCost;
        _positionCost += totalCost;
        _quantity += execution.Quantity;
        _averageEntryPrice = _positionCost / _quantity;
        _cycleBoughtQuantity += execution.Quantity;
        _cycleCostBasis += totalCost;
        return null;
    }

    private BacktestExecution ApplySell(BacktestExecution execution, out BacktestPositionCycle? completedCycle)
    {
        var realizedProfit = execution.GrossAmount - execution.Fee - execution.Tax - (_averageEntryPrice * execution.Quantity);
        var applied = execution with { RealizedProfit = realizedProfit };
        _cash += execution.GrossAmount - execution.Fee - execution.Tax;
        _quantity -= execution.Quantity;
        _positionCost -= _averageEntryPrice * execution.Quantity;
        _realizedProfitTotal += realizedProfit;
        _cycleSoldQuantity += execution.Quantity;
        _cycleGrossSaleAmount += execution.GrossAmount;
        _cycleRealizedProfit += realizedProfit;

        if (_quantity != 0m)
        {
            completedCycle = null;
            return applied;
        }

        var cycle = new BacktestPositionCycle(
            Guid.NewGuid(),
            _openingExecutionId!.Value,
            execution.ExecutionId,
            _openedAt,
            execution.ExecutedAt,
            _cycleBoughtQuantity,
            _cycleSoldQuantity,
            _cycleBoughtQuantity == 0m ? 0m : _cycleCostBasis / _cycleBoughtQuantity,
            _cycleSoldQuantity == 0m ? 0m : _cycleGrossSaleAmount / _cycleSoldQuantity,
            _cycleRealizedProfit,
            _cycleCostBasis == 0m ? 0m : _cycleRealizedProfit / _cycleCostBasis,
            execution.Reason);
        _averageEntryPrice = 0m;
        _positionCost = 0m;
        completedCycle = cycle;
        return applied;
    }

    private static void Validate(BacktestExecution execution)
    {
        if (execution.ExecutionId == Guid.Empty) throw new ArgumentException("체결 ID가 필요합니다.", nameof(execution));
        if (execution.Sequence <= 0) throw new ArgumentOutOfRangeException(nameof(execution));
        if (execution.Price <= 0m || execution.Quantity <= 0m) throw new ArgumentOutOfRangeException(nameof(execution));
        if (execution.Fee < 0m || execution.Tax < 0m) throw new ArgumentOutOfRangeException(nameof(execution));
        if (execution.GrossAmount != execution.Price * execution.Quantity) throw new ArgumentException("체결 금액이 가격과 수량의 곱과 다릅니다.", nameof(execution));
    }
}

public sealed record LedgerApplication(
    BacktestExecution Execution,
    BacktestPositionSnapshot Snapshot,
    BacktestPositionCycle? CompletedCycle);
