# Connectors — devices that send their own format

Most devices reach BMS through a gateway that publishes BMS-format JSON over
MQTT. Some can't: a 4G sensor that opens a TCP connection and sends a line of
comma-separated numbers, a meter that sends key=value text, a frame of binary
registers. For those, a **connector** describes the format once, and the
**Connector Service** translates every message into a normal MQTT reading.
Ingestion, alerts, calculations and the dashboard see no difference.

```
Device ──TCP──► Connector Service ──MQTT (BMS JSON)──► Mosquitto ──► Bms.Ingestion ──► BMS
                 frame → decode → map → find company/site → publish
                        ▲
                        └── connectors + devices from the API (every 60 s)
```

v1 supports **TCP**, with messages as **CSV / delimited**, **key = value**,
**JSON** or **binary at fixed positions**, separated by **new line**, **fixed
length**, an **end character**, or **one message per connection**.

---

## One-time setup

### 1. Broker login for the service

The service publishes as `bms-connectors`, which the ACL
(`infrastructure/mosquitto/acl`) allows to **write device telemetry topics
only** — it can't read anything or send commands.

```bash
docker run --rm -it -v "$(pwd)/infrastructure/mosquitto:/mosquitto/config" \
  eclipse-mosquitto:2 \
  mosquitto_passwd -b /mosquitto/config/passwd bms-connectors <password>
```

Put the same password in `.env` as `MQTT_CONNECTORS_PASSWORD`.

### 2. Service key

The service authenticates to the API with a shared key. Generate one
(`openssl rand -base64 32`) and set it as `CONNECTOR_SERVICE_API_KEY` in `.env`
— compose passes it to both the API (`Connectors__ServiceApiKey`) and the
service (`ConnectorService__ApiKey`). An empty key disables the service's API.

### 3. Ports

Devices connect to ports in **12000–12099** (`CONNECTOR_PORT_RANGE`). Open the
range **once** in the firewall / AWS security group; connectors can only use
ports inside it, so adding a vendor never needs an infrastructure change.
The API's `Connectors:PortRangeStart/End` must match.

### 4. Run it

```bash
docker compose up -d connectors            # or, outside Docker:
dotnet run --project src/backend/Bms.Connectors
```

**Check:** Infrastructure → **Connectors** shows *Connector Service online*
within a minute. If it stays offline: the API key differs, or the service
can't reach `ConnectorService:ApiBaseUrl`.

---

## Adding a vendor (no code)

Infrastructure → Connectors → **Add connector** (SuperAdmin).

1. **Basics** — name, vendor, the device type its devices usually are, a port
   from the range, and a **device code prefix** (device code = prefix + the
   sensor ID in each message).
2. **Message format** — how messages are separated and what's inside.
3. **Sample** — paste one real message, or save first and use **Capture from
   port** to grab the next message a device sends (waits up to 10 minutes, for
   sensors that sleep between reports).
4. **Field mapping** — every value in the sample is a row: map one to
   **Sensor ID**, optionally one to **Time**, the rest to parameters or
   **Ignore**. *➕ New parameter* adds one to the device type on the spot.
   Binary values are added by start byte, with size, sign, byte order, and
   `value = (raw − offset) × scale`.
5. **Preview** — the exact reading BMS will receive. Save once it's right.

Keys match exactly as the device sends them, case included. Parts of messages
no mapping uses are counted and shown on the connector as *unmapped*.

## Adding a device on a connector

Devices → **Add device** → Connection: **Connector**, pick the connector, type
the **Sensor ID** from the device label. The device code is filled in, and the
form checks that the connector fills at least one of the device type's
parameters. The connection is fixed after creation.

**Unknown devices** — a sensor ID that sends to a connector but matches no
device is listed on the Connectors page (it is never published). *Add device*
opens the form pre-filled.

---

## Example: FG-WM400 4G tilt sensor (BWSENSING)

The sensor connects over 4G as a TCP client, wakes about every 5 minutes and
sends a burst of ~10 lines:

```
100000005,+022.2149,+000.5842,+088.8487,+000.1818,+000.0289,+000.9814,+028.8700,7
```

| Connector setting | Value |
|---|---|
| Port | 12000 |
| Framing | New line |
| Format | CSV, separator `,`, **skip a text label in front of the ID** on (for `BW-NB-WM400,` prefix mode) |
| Device code prefix | `fgwm400-` |
| Column 1 | Sensor ID |
| Columns 2–4 | `xDeg`, `yDeg`, `zDeg` (°) |
| Columns 5–7 | `axG`, `ayG`, `azG` (g) |
| Column 8 | `temperature` (°C) |
| Column 9 | `batteryLevel` (0–9) |
| Column 10 | Ignore (phone / SIM number — personal data) |
| Time | not mapped — the sensor sends none; readings are stamped on arrival |

The binary mode (21 bytes) is also supported: Fixed length 21, Binary, header
`03 10` at byte 1, CRC-16/Modbus; byte 0 = Sensor ID (1 byte), then 2-byte
values at bytes 3, 5, 7 … with offset 20000 and scale 0.01 (0.0001 for
acceleration). Use text mode where possible: binary carries only the last byte
of the sensor ID.

**Device type** *Inclinometer*: the eight parameters above; **Reports every
5 min** (so it isn't marked offline between bursts); **Can receive commands**
off (no verified downlink).

**Calculations:** Battery % — Formula `batteryLevel * 100 / 9`; X and Y tilt
change — **Change from baseline** on `xDeg` and `yDeg`.

**Alerts:** Low battery; tilt-change Alert / Alarm levels — **from the project
engineer**, per structure.

After installation, open each device → Overview → **Set baseline**.

Point the sensor (manufacturer's tool, TCP mode) at the server's public IP,
port 12000.

### Testing without hardware

```bash
printf '100000005,+022.2149,+000.5842,+088.8487,+000.1818,+000.0289,+000.9814,+028.8700,7\n' \
  | nc <server> 12000
```

Register `fgwm400-100000005` first, or it appears under Unknown devices.

---

## How readings are timed

A device that sends no time is stamped when its message **arrives** at the
service; the reading carries `"tsSource": "received"`, the device is marked
accordingly, and its readings table says **Received at** instead of Time.
A connector with a Time mapping uses the device's own time.

## Failure behaviour

| Situation | What happens |
|---|---|
| API unreachable | the service keeps running on the last connectors and devices it read |
| Broker unreachable | readings queue in memory (10 000) and are published when it's back; beyond that the oldest are dropped and counted in the heartbeat |
| Connector service down | Connectors page shows *offline*; devices on connectors go Offline in BMS after missing two reports |
| Malformed message | rejected with a reason (counted per connector; last error shown) |
| The same message twice within 2 s | the repeat is dropped |
| More than 20 connections from one IP, or 500 in total | further connections are refused |
