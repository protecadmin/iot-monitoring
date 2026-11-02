# BW-NB-WM400 prefixed CSV

The Protec inclinometer connector on TCP port 12000 accepts the acceleration-enabled prefix format described in FG.pdf, pages 7 and 11.

Example record:

```text
BW-NB-WM400,100000005,-016.6001,-000.2656,+077.0541,-000.0259,-000.0290,+000.9975,+031.0773,5,7397097255
```

Use CSV format, Start marker framing, marker `BW-NB-WM400,`, skip leading label enabled, and expected field count **10 after removing the prefix**. This is 11 fields on the wire.

| Source column after prefix | Target | Unit |
| --- | --- | --- |
| 1 | Sensor ID | — |
| 2 | xDeg | ° |
| 3 | yDeg | ° |
| 4 | zDeg | ° |
| 5 | axG | g |
| 6 | ayG | g |
| 7 | azG | g |
| 8 | temperature | °C |
| 9 | batteryLevel | 0–9 |
| 10 | Ignore phone-number field | — |

Scale is 1 and offset is 0: these are already engineering values. The device code remains `protec-incl-100000005`. CSV has no timestamp, so the connector assigns reception time.

Each marker starts a record. TCP reads may split anywhere, including inside the marker. The connector buffers partial data, closes a record at the next marker or CR/LF, and reads the final record when the device disconnects. An incomplete field count is rejected; the old compact five-field layout is also rejected. Keep this wake/send/disconnect behaviour: a complete final record on a connection left open has no unambiguous terminator. Variable-length phone numbers mean no fixed byte length should be assumed.

Z and acceleration are measured parameters on the existing Inclinometer type. The daily tilt metadata now maps Z to zDeg. Existing saved daily snapshots and the onboarding reference remain fixed; where an old reference has no Z, the corresponding Z difference remains blank.

Simulator, from `simulators/python-publisher` (PowerShell):

```powershell
.\.venv\Scripts\python.exe send_tcp_fgwm400.py --text-layout extended --prefix --phone --phone-number 7397097255 --no-newline --interval 300 --x -16.6001 --y -0.2656 --z 77.0541 --temperature 31.0773 --battery 5
```

Add `--once` to send one burst. The default is ten records per wake-up, 200 ms apart; the interval is 300 seconds after the burst. The simulator is for testing and does not configure hardware wake-up settings.
