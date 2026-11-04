# Platform MQTT gateway monitoring

Every authenticated MQTT account can subscribe to `protec/gateway/#` and read
gateway traffic across all companies. Use the account's existing password.
Company publishing permissions remain limited to registered device topics.
Dashboard/API permissions are unchanged.

```cmd
mosquitto_sub -h 52.74.238.107 -p 1883 -u protec-geosystems -P "YOUR_MQTT_PASSWORD" -t "protec/gateway/#" -v
```

An optional separate `protec-platform-monitor` account is also available.
Its gateway publishing and internal `bms/` subscriptions are denied. Its
generated password is stored on the VM at
`/opt/protec/shared/mosquitto/platform-monitor.json`, readable only by root.
The normal deployment backup includes this directory. No password belongs in Git.

The non-secret ACL is:

```text
pattern read protec/gateway/#
```

Restart the broker after changing its password file or ACL so cached permissions
are refreshed. Use each company's device credentials on its gateways; the
platform account is for observing gateway traffic.
