using Bms.Application.Abstractions.Calculations;
using Bms.Application.Abstractions.Persistence;
using Bms.Application.Abstractions.Telemetry;
using Bms.Domain;
using Bms.Domain.Entities;

namespace Bms.Calculations;

public sealed class CalculationReader(
    ICalculationRuleRepository rules,
    ICalculationStateRepository states,
    IDeviceCalculationAssignmentRepository assignments,
    IDeviceTypeRepository deviceTypes,
    IEnumerable<ICalculationHandler> handlers,
    TimeProvider clock) : ICalculationReader
{
    public async Task<IReadOnlyList<CalculationRule>> GetEffectiveRulesAsync(Device device, CancellationToken ct)
    {
        var applicable = await rules.GetApplicableRulesAsync(device.TenantId, device.SiteId, device.Id, device.DeviceTypeId, ct);
        if (applicable.Count == 0)
            return [];

        var parameters = (await deviceTypes.GetByIdAsync(device.DeviceTypeId, ct))?.Parameters.ToList() ?? [];
        return applicable.Where(r => IsEligible(r, parameters)).ToList();
    }

    public async Task<bool> AppliesToAsync(CalculationRule rule, Device device, CancellationToken ct)
    {
        if (!CalculationScope.Matches(rule, device))
            return false;

        if (rule.AssignmentMode == CalculationAssignmentMode.Selected && !await assignments.IsAssignedAsync(rule.Id, device.Id, ct))
            return false;

        var parameters = (await deviceTypes.GetByIdAsync(device.DeviceTypeId, ct))?.Parameters.ToList() ?? [];
        return IsEligible(rule, parameters);
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<CalculatedPoint>>> GetHistoryAsync(Device device, TelemetryRange range, CancellationToken ct)
    {
        var window = TelemetryRangeWindows.Resolve(range);
        var to = clock.GetUtcNow();
        return QueryEffectiveAsync(device, to - window.Duration, to, window.AggregateEvery, ct);
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<CalculatedPoint>>> GetHistoryAsync(Device device, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        QueryEffectiveAsync(device, from, to, null, ct);

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<CalculatedRollupPoint>>> GetRollupAsync(
        Device device, DateTimeOffset from, DateTimeOffset to, string every, CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyList<CalculatedRollupPoint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in await GetEffectiveRulesAsync(device, ct))
            result[rule.OutputKey] = await Handler(rule)!.RollupAsync(rule, device, from, to, every, ct);
        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<LatestCalculatedValue>>> GetLatestAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken ct)
    {
        if (deviceIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<LatestCalculatedValue>>();

        var rows = await states.ListWithRulesForDevicesAsync(deviceIds, ct);
        return rows
            .Where(s => s.CalculationRule is { IsEnabled: true } && s.LastOutputValue is not null && s.LastOutputTimestamp is not null)
            .GroupBy(s => s.DeviceId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<LatestCalculatedValue>)g.Select(ToLatest).ToList());
    }

    public async Task<CalculationResults> GetResultsAsync(
        CalculationRule rule, Device device, DateTimeOffset from, DateTimeOffset to, CalculationGranularity granularity, CancellationToken ct)
    {
        var handler = Handler(rule) ?? throw new InvalidOperationException($"No calculation handler registered for {rule.Type}");

        var aggregateEvery = granularity switch
        {
            CalculationGranularity.FiveMinutes => "5m",
            CalculationGranularity.Hourly => "1h",
            CalculationGranularity.Daily => "1d",
            CalculationGranularity.Monthly => "1mo",
            _ => null
        };

        var points = await handler.QueryAsync(rule, device, from, to, aggregateEvery, timestampAtWindowStart: true, ct);

        // Windows partition the deltas, so summing the buckets is exactly the
        // sum of every valid delta in the range — a total, never an average.
        var total = points.Sum(p => p.Value);

        var state = (await states.ListWithRulesForDevicesAsync([device.Id], ct)).FirstOrDefault(s => s.CalculationRuleId == rule.Id);
        var latest = state is { LastOutputValue: not null, LastOutputTimestamp: not null, CalculationRule: not null } ? ToLatest(state) : null;

        return new CalculationResults(points, total, latest);
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<CalculatedPoint>>> QueryEffectiveAsync(
        Device device, DateTimeOffset from, DateTimeOffset to, string? aggregateEvery, CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyList<CalculatedPoint>>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in await GetEffectiveRulesAsync(device, ct))
        {
            // timestampAtWindowStart: false — raw telemetry history labels its
            // aggregate windows by their end (Flux's default), and the derived
            // series has to land on the same timestamps to merge with it.
            result[rule.OutputKey] = await Handler(rule)!.QueryAsync(rule, device, from, to, aggregateEvery, timestampAtWindowStart: false, ct);
        }

        return result;
    }

    private bool IsEligible(CalculationRule rule, IReadOnlyList<DeviceTypeParameter> parameters)
    {
        var handler = Handler(rule);
        return handler is not null && CalculationScope.IsEligible(handler, rule, parameters);
    }

    private ICalculationHandler? Handler(CalculationRule rule) => handlers.FirstOrDefault(h => h.Type == rule.Type);

    private static LatestCalculatedValue ToLatest(CalculationState s) => new(
        s.CalculationRuleId, s.CalculationRule!.OutputKey, s.CalculationRule.OutputName, s.CalculationRule.OutputUnit,
        s.LastOutputValue!.Value, s.LastOutputTimestamp!.Value);
}
