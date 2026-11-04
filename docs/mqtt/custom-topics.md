# MQTT measurement topics

The default topic for an MQTT-direct device is:

```text
protec/gateway/{gatewayId}/measurement
```

Enter the gateway ID in **Gateway ID (device code)**. Each gateway connects to
one sensor. Leave **Custom MQTT topic** blank: the backend generates, subscribes
to, and grants company access to the default topic automatically. The API returns
the resolved address as `effectiveMqttTelemetryTopic`.

For gateway `860738070588839`, the generated topic is
`protec/gateway/860738070588839/measurement`. `any` is not a literal topic segment.

## Optional custom topics

MQTT-direct devices can publish telemetry on any topic layout. There is no
required `iot/` or `sensors/` prefix, suffix, or segment count. Examples:

```text
machine01
factory/temperature
sensors/aa/site01/device01/data
vendor/building/floor/room/machine/readings
```

In **Devices → Add/Edit device**, enter a different exact publish topic in
**Custom MQTT topic** only when an override is needed. One topic belongs to one device. This mapping
provides the company/site/device identity even when the topic contains none.
Leave it blank to use `protec/gateway/{gatewayId}/measurement`. Existing
`iot/{tenant}/{site}/{device}/telemetry` addresses remain supported for legacy
publishers and the TCP connector's internal MQTT path; they are no longer the
default shown when registering an MQTT gateway.

Names are case-sensitive and are saved exactly, including leading or trailing
slashes. Enter an actual publish topic, not placeholder text or a wildcard
subscription (`+`/`#`). Up to 512 characters are supported. Internal `bms/`
and `$SYS/` service addresses are reserved. Existing `iot/` addresses retain
their company/site/device ownership.

Use the company's existing MQTT credentials. Its custom topics are granted by
the PostgreSQL `MqttAllowedTopics` view. Publishing remains company-scoped;
all authenticated MQTT users can read `protec/gateway/#` across companies by
the platform's global read policy. Dashboard/API tenant access is unchanged.
Do not replace company ACLs with `readwrite #`.

This change selects the topic default. The payload decoder is unchanged: send the device type's JSON fields and normal
timestamp/sequence fields. A plain `hello` message is not telemetry. The original
topic and payload are preserved in raw messages; decoding, calculations,
storage, alerts, and live updates use the existing ingestion pipeline. Commands,
heartbeats, status and acknowledgements continue using their standard addresses.

## Rollout

1. Build/restart the API so its database migration adds the optional topic
   column, unique index, and `MqttAllowedTopics` view. Existing devices keep null
   custom topics and their original behavior.
2. Restart the broker with the updated `infrastructure/mosquitto/acl` and
   `docker-entrypoint.sh`. The entrypoint upgrades the previously shipped legacy
   ACL query in a private runtime copy; it does not edit the source config or
   print secrets. For a bespoke broker config, set the following query explicitly:

   ```text
   auth_opt_pg_aclquery select "Topic" from "MqttAllowedTopics" where "Username" = $1 and $2::integer in (1, 2, 4)
   ```

3. Restart ingestion and the frontend with this version, then enter the custom
   topic on the device. Ingestion discovers new exact subscriptions within
   30 seconds; no global wildcard subscription is required. The transport remembers
   subscriptions on reconnect. `$vendor/...` topics work through exact subscriptions.
4. The broker's permission cache may take up to 300 seconds to pick up an added
   or changed topic. Register a topic before beginning transmission. A broker
   restart clears its in-memory cache if immediate testing is needed.

`DefaultProtecGatewayTopics` updates the broker view to include automatically
generated topics and adds an index preventing collisions between defaults and
custom overrides. It does not change existing custom topics or TCP connectors.

The migration is additive. Stop/revert custom-topic users and restore the legacy
broker query before rolling back the migration. This source change alone does
not rebuild or deploy the cloud stack.
