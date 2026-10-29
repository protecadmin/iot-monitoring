# Payload Decoding

## Where decoding sits in the pipeline

```
Raw Payload (bytes, from RawDeviceMessage)
     |
     v
IPayloadDecoder      <- this document
     |
     v
Decoded Data          (a structured, in-memory representation)
     |
     v
NormalizedTelemetry   <- see normalization.md (fields kept when they are
                         parameters of the device type)
```

Decoding answers one question: **"how do I turn these bytes into a
structured shape I can read fields from?"** It does not know what any
field *means* — a decoded key is kept when it is a parameter of the
device's type. Our gateway sends BMS parameter keys, so one
`JsonPayloadDecoder` serves every device.

## IPayloadDecoder implementations

| Decoder | Input | Output |
|---|---|---|
| `JsonPayloadDecoder` | JSON bytes | Flat field dictionary, plus `seq`/`ts` when present |

JSON is the only payload format: every device reports through our own
gateway, which publishes JSON over MQTT.

Which decoder runs is chosen from the device's registered `PayloadFormat`
(resolved during [device identification](../architecture/data-flow.md)),
never guessed from the bytes.

## Failure handling

An `IPayloadDecoder` failure (malformed JSON) is caught at the decoding stage, logged with the message's
`CorrelationId`, and the message is rejected — the
`RawDeviceMessage` is still persisted (see
[data-flow.md](../architecture/data-flow.md#rawdevicemessage)) so the
failure is debuggable and the payload can be replayed
(`docs/troubleshooting` /
[core.txt](../../core.txt) section 51) once, say, a decoder bug is fixed.
It never propagates up and stops other devices' messages from processing.
