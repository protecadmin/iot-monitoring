using Bms.DeviceSimulator;

Console.WriteLine("BMS Device Simulator");
Console.WriteLine("Usage: dotnet run -- --device-id=device-001 | --count=5 [--interval=5] [--mqtt-host=localhost] [--malformed-rate=0.05] [--duplicate-rate=0.05] [--delay-ms=0]");
Console.WriteLine();

var options = SimulatorOptions.Parse(args);

var profiles = options.DeviceId is not null
    ? new[] { DeviceProfile.FindByCode(options.DeviceId) ?? throw new ArgumentException($"Unknown device id '{options.DeviceId}'. Known: {string.Join(", ", DeviceProfile.All.Select(d => d.DeviceCode))}") }
    : DeviceProfile.All.Take(Math.Clamp(options.Count, 1, DeviceProfile.All.Count)).ToArray();

Console.WriteLine($"Simulating {profiles.Length} device(s): {string.Join(", ", profiles.Select(p => p.DeviceCode))} over MQTT");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\nShutting down (graceful)...");
    cts.Cancel();
};

var tasks = profiles.Select(profile => new MqttDeviceRunner(profile, options).RunAsync(cts.Token)).ToArray();

try
{
    await Task.WhenAll(tasks);
}
catch (OperationCanceledException)
{
    // expected on Ctrl+C
}

Console.WriteLine("All devices stopped.");
