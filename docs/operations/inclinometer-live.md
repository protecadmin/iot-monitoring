# Protec inclinometer live demo

This local demo uses **simulated readings**, not a connection to physical hardware.

| Setting | Value |
|---|---|
| Company | Protec Geosystems |
| Site | Protec Demo Site (placeholder site; no location invented) |
| Device type | Inclinometer |
| Sensor ID | `100000005` |
| Device code | `protec-incl-100000005` |
| Connector | Protec Inclinometer - 5-field CSV |
| Local TCP destination | `127.0.0.1:12000` |
| Framing | One CSV message per newline |
| Timestamp | Receipt time; the sensor packet contains no timestamp |

## Packet mapping

```text
100000005,-016.5923,-000.2516,+031.2983,5
```

| CSV column (1-based) | Parameter | Unit |
|---|---|---|
| 1 | Sensor ID | ID |
| 2 | `xDeg`: X-axis inclination | Degrees |
| 3 | `yDeg`: Y-axis inclination | Degrees |
| 4 | `temperature`: Sensor temperature | Celsius |
| 5 | `batteryLevel`: Battery level | Raw scale 0-9, not percent |

The values are stored without offsets or scaling. All four measured values are enabled on the dashboard. No structural alarm thresholds are invented; obtain project-specific limits before configuring tilt alarms.

## View live data

Open http://localhost:3000/dashboard as SuperAdmin and select **Protec Geosystems** in the company selector. The existing Whirlpool account cannot see another company's devices. Select the inclinometer under Devices for its live readings, history and raw messages.

The three backend services (API, Ingestion and Connectors) and the MQTT broker must be running. Connector configuration refreshes periodically, so a newly created listener may take up to about a minute to appear.

## Run the simulator on Windows

From the repository root:

```powershell
& '.\simulators\python-publisher\.venv\Scripts\python.exe' -u '.\simulators\python-publisher\send_tcp_fgwm400.py' --host 127.0.0.1 --port 12000 --text-layout compact --sensor-id 100000005 --x=-16.5923 --y=-0.2516 --temperature 31.2983 --battery 5 --interval 300 --burst 10 --gap-ms 200
```

The simulator emits ten readings per burst, then waits five minutes. The device type is configured to expect reports every 300 seconds so it stays online between bursts. Small variations in tilt and temperature emulate the sample sensor. Stop an interactive run with Ctrl+C.

The background instance started during setup writes to:

- `logs/protec-inclinometer-simulator.log`
- `logs/protec-inclinometer-simulator.err.log`
- `logs/protec-inclinometer-simulator.pid`

To stop that background instance, first inspect the PID and its Python process, then stop it:

```powershell
$simulatorPid = [int](Get-Content '.\logs\protec-inclinometer-simulator.pid')
Get-Process -Id $simulatorPid
Stop-Process -Id $simulatorPid
```

Do not run multiple simulator instances for the same sensor. Stop simulation before connecting the real device under this ID; otherwise real and simulated readings will mix.

The existing nine-field format remains the default. Use `--text-layout compact` only with the five-field connector mapping above. To connect real hardware, point it at the Connector Service host and this port, and set the device type's expected reporting interval to the hardware's actual cadence.

## Packet regression tests

```powershell
Set-Location '.\simulators\python-publisher'
& '.\.venv\Scripts\python.exe' -m unittest discover -p 'test_*.py' -v
```

## Display precision checks

Inclination is displayed to four decimal places on dashboard cards, device readings and history. Normal measured fields retain their existing two-decimal display. Values in storage retain the received numeric precision.

From `src/frontend/bms-dashboard`:

```powershell
node --experimental-strip-types --test tests/inclinometer-format.test.cjs
```