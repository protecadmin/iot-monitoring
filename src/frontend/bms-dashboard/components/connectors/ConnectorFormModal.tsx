"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Plus, Radio, Trash2 } from "lucide-react";
import { useApi } from "@/hooks/useApi";
import { ConnectorsApi, DeviceTypesApi } from "@/lib/api/resources";
import { ApiError } from "@/lib/api/client";
import { Modal, FormField, inputClass } from "@/components/ui/Modal";
import type {
  ConnectorDto,
  ConnectorFormat,
  ConnectorFraming,
  ConnectorMappingDto,
  ConnectorSettings,
  ConnectorTarget,
  ConnectorTestResult,
  DeviceTypeParameterDto,
} from "@/types/api";

const FRAMINGS: { value: ConnectorFraming; label: string }[] = [
  { value: "StartMarker", label: "Start marker — joined records begin with the same text" },
  { value: "NewLine", label: "New line — one message per line" },
  { value: "FixedLength", label: "Fixed length — every message is N bytes" },
  { value: "EndCharacter", label: "End character — a message ends with a chosen character" },
  { value: "OnePerConnection", label: "One per connection — connect, send, disconnect" },
];

const FORMATS: { value: ConnectorFormat; label: string; hint: string }[] = [
  { value: "Csv", label: "CSV / delimited", hint: "100000005,+022.21,+000.58,7 — values picked by column number" },
  { value: "KeyValue", label: "Key = value", hint: "ID=55;T=24.1;H=60 — values picked by key" },
  { value: "Json", label: "JSON", hint: '{"sn":"7788","data":{"v":230.5}} — values picked by path (data.v)' },
  { value: "Binary", label: "Binary (fixed positions)", hint: "01 03 10 4E DF … — values picked by byte position" },
];

const DEFAULTS: ConnectorSettings = {
  name: "",
  vendor: null,
  defaultDeviceTypeId: null,
  protocol: "Tcp",
  port: 12000,
  framing: "NewLine",
  fixedLength: null,
  endCharacter: null,
  startMarker: null,
  csvFieldCount: null,
  maxMessageBytes: 4096,
  format: "Csv",
  csvSeparator: ",",
  csvOptionalPrefix: false,
  pairSeparator: ";",
  keyValueSeparator: "=",
  binaryHeaderHex: null,
  binaryHeaderOffset: 0,
  binaryChecksum: "None",
  deviceCodePrefix: "",
  isEnabled: true,
  mappings: [],
};

/** "Map to" choices: the special targets, then the device type's parameters. */
const UNMAPPED = "";
const NEW_PARAMETER = "__new__";

/** The editable part of a saved connector (its id and counts are not sent back). */
function settingsOf(c?: ConnectorDto): ConnectorSettings {
  if (!c) return DEFAULTS;
  return Object.fromEntries(Object.keys(DEFAULTS).map((k) => [k, c[k as keyof ConnectorSettings]])) as unknown as ConnectorSettings;
}

function sortSources(sources: string[]) {
  return [...sources].sort((a, b) => {
    const na = Number(a);
    const nb = Number(b);
    return Number.isInteger(na) && Number.isInteger(nb) ? na - nb : 0;
  });
}

/**
 * Creates or edits a connector. The mapping is built from a real message:
 * paste one (or capture it from the port), every value found becomes a row,
 * and each row is pointed at the Sensor ID, the time, a parameter, or nothing.
 * A live preview shows exactly what BMS will receive.
 */
export function ConnectorFormModal({ connector, onClose, onSaved }: { connector?: ConnectorDto; onClose: () => void; onSaved: () => void }) {
  const isEdit = !!connector;
  const [s, setS] = useState<ConnectorSettings>(() => settingsOf(connector));
  const [sample, setSample] = useState("");
  const [sampleIsHex, setSampleIsHex] = useState(connector?.format === "Binary");
  const [test, setTest] = useState<ConnectorTestResult | null>(null);
  const [testError, setTestError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [capture, setCapture] = useState<{ id: string; until: string } | null>(null);
  const [newParamFor, setNewParamFor] = useState<string | null>(null);

  const typesQuery = useApi(() => DeviceTypesApi.list(), []);

  // A new connector starts on the first free port, not on one another connector already uses.
  const connectorsQuery = useApi(() => (isEdit ? Promise.resolve([]) : ConnectorsApi.list()), [isEdit]);
  useEffect(() => {
    if (isEdit || !connectorsQuery.data) return;
    const taken = new Set(connectorsQuery.data.map((c) => c.port));
    let port = DEFAULTS.port;
    while (taken.has(port)) port++;
    setS((prev) => (prev.port === DEFAULTS.port ? { ...prev, port } : prev));
  }, [isEdit, connectorsQuery.data]);
  const deviceType = typesQuery.data?.find((t) => t.id === s.defaultDeviceTypeId);

  // Parameters a value can go to: the chosen type's measured ones, or every type's when none is chosen.
  const parameters = useMemo(() => {
    const source = deviceType ? [deviceType] : (typesQuery.data ?? []);
    const byKey = new Map<string, DeviceTypeParameterDto>();
    for (const t of source) for (const p of t.parameters) if (p.kind !== "Derived" && !byKey.has(p.key)) byKey.set(p.key, p);
    return [...byKey.values()].sort((a, b) => a.label.localeCompare(b.label));
  }, [deviceType, typesQuery.data]);

  const update = (patch: Partial<ConnectorSettings>) => setS((prev) => ({ ...prev, ...patch }));
  const mappingFor = (source: string) => s.mappings.find((m) => m.source === source);

  function setMapping(source: string, patch: Partial<ConnectorMappingDto> | null) {
    setS((prev) => {
      const others = prev.mappings.filter((m) => m.source !== source);
      if (patch === null) return { ...prev, mappings: others };
      const existing = prev.mappings.find((m) => m.source === source) ?? { source, target: "Parameter" as ConnectorTarget, parameterKey: null };
      return { ...prev, mappings: [...others, { ...existing, ...patch }] };
    });
  }

  function onTargetChange(source: string, value: string) {
    if (value === UNMAPPED) return setMapping(source, null);
    if (value === NEW_PARAMETER) return setNewParamFor(source);
    if (value === "SensorId" || value === "Time" || value === "Ignore") return setMapping(source, { target: value, parameterKey: null });
    setMapping(source, { target: "Parameter", parameterKey: value });
  }

  // Re-decode the sample whenever the connector or the sample changes, so the preview is always current.
  const testSeq = useRef(0);
  useEffect(() => {
    if (!sample.trim()) return;
    const seq = ++testSeq.current;
    const timer = setTimeout(async () => {
      try {
        const result = await ConnectorsApi.test(s, sample, sampleIsHex);
        if (seq === testSeq.current) {
          setTest(result);
          setTestError(null);
        }
      } catch (err) {
        if (seq === testSeq.current) setTestError(err instanceof ApiError ? err.message : "Couldn't read the sample");
      }
    }, 400);
    return () => clearTimeout(timer);
  }, [s, sample, sampleIsHex]);

  // "Capture from port": wait for the Connector Service to hand over the next real message.
  useEffect(() => {
    if (!capture) return;
    const timer = setInterval(async () => {
      try {
        const c = await ConnectorsApi.getCapture(capture.id);
        if (c.capturedAt && c.sample) {
          setSample(c.sample);
          setSampleIsHex(c.sampleIsHex);
          setCapture(null);
        } else if (new Date(c.expiresAt) < new Date()) {
          setCapture(null);
          setTestError("No message arrived on the port while waiting. Check the device is sending to this port, then try again.");
        }
      } catch {
        // keep waiting; a transient error shouldn't cancel the capture
      }
    }, 3000);
    return () => clearInterval(timer);
  }, [capture]);

  async function startCapture() {
    if (!connector) return;
    setTestError(null);
    try {
      const c = await ConnectorsApi.capture(connector.id);
      setCapture({ id: c.id, until: c.expiresAt });
    } catch (err) {
      setTestError(err instanceof ApiError ? err.message : "Couldn't start the capture");
    }
  }

  // Rows: every value the sample has, plus any source already mapped (binary positions, or keys not in this sample).
  const rows = useMemo(() => {
    const fromSample = test?.values ?? [];
    const sources = new Set([...fromSample.map((v) => v.source), ...s.mappings.map((m) => m.source)]);
    return sortSources([...sources]).map((source) => ({ source, value: fromSample.find((v) => v.source === source)?.value }));
  }, [test, s.mappings]);

  const isBinary = s.format === "Binary";

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSaving(true);
    try {
      if (isEdit) await ConnectorsApi.update(connector.id, s);
      else await ConnectorsApi.create(s);
      onSaved();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't save the connector");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title={isEdit ? `Edit connector — ${connector.name}` : "New connector"} onClose={onClose} size="xl">
      <form onSubmit={handleSubmit} className="space-y-5">
        <Section title="1. Basics">
          <div className="grid sm:grid-cols-2 gap-3">
            <FormField label="Name">
              <input required value={s.name} onChange={(e) => update({ name: e.target.value })} placeholder="e.g. FG-WM400 tilt sensor" className={inputClass} />
            </FormField>
            <FormField label="Vendor (optional)">
              <input value={s.vendor ?? ""} onChange={(e) => update({ vendor: e.target.value || null })} placeholder="e.g. BWSENSING" className={inputClass} />
            </FormField>
            <FormField label="Device type its devices usually are">
              <select value={s.defaultDeviceTypeId ?? ""} onChange={(e) => update({ defaultDeviceTypeId: e.target.value || null })} className={inputClass}>
                <option value="">— any —</option>
                {typesQuery.data?.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </select>
            </FormField>
            <div className="grid grid-cols-2 gap-3">
              <FormField label="Protocol">
                <select value="Tcp" disabled className={inputClass}>
                  <option value="Tcp">TCP</option>
                </select>
              </FormField>
              <FormField label="Port">
                <input type="number" required value={s.port} onChange={(e) => update({ port: Number(e.target.value) })} className={inputClass} />
              </FormField>
            </div>
            <FormField label="Device code prefix">
              <input
                required
                value={s.deviceCodePrefix}
                onChange={(e) => update({ deviceCodePrefix: e.target.value })}
                placeholder="e.g. fgwm400-"
                disabled={isEdit && connector.deviceCount > 0}
                className={inputClass}
              />
              <p className="text-xs text-muted-foreground mt-1">
                Device code = prefix + the sensor ID in each message, e.g. <code>fgwm400-100000005</code>.
                {isEdit && connector.deviceCount > 0 && " Fixed while devices use this connector."}
              </p>
            </FormField>
            <label className="flex items-center gap-2 text-sm text-foreground self-center">
              <input type="checkbox" checked={s.isEnabled} onChange={(e) => update({ isEnabled: e.target.checked })} />
              Enabled (the Connector Service listens on the port)
            </label>
          </div>
        </Section>

        <Section title="2. Message format">
          <div className="grid sm:grid-cols-2 gap-3">
            <FormField label="How messages are separated">
              <select value={s.framing} onChange={(e) => update({ framing: e.target.value as ConnectorFraming })} className={inputClass}>
                {FRAMINGS.map((f) => (
                  <option key={f.value} value={f.value}>
                    {f.label}
                  </option>
                ))}
              </select>
            </FormField>
            <FormField label="What's inside a message">
              <select
                value={s.format}
                onChange={(e) => {
                  const format = e.target.value as ConnectorFormat;
                  update({ format, framing: format === "Binary" && (s.framing === "NewLine" || s.framing === "StartMarker") ? "FixedLength" : format !== "Csv" && s.framing === "StartMarker" ? "NewLine" : s.framing });
                  setSampleIsHex(format === "Binary");
                }}
                className={inputClass}
              >
                {FORMATS.map((f) => (
                  <option key={f.value} value={f.value}>
                    {f.label}
                  </option>
                ))}
              </select>
              <p className="text-xs text-muted-foreground mt-1">{FORMATS.find((f) => f.value === s.format)?.hint}</p>
            </FormField>

            {s.framing === "StartMarker" && (
              <FormField label="Record start marker">
                <input required maxLength={64} value={s.startMarker ?? ""} onChange={(e) => update({ startMarker: e.target.value || null })} placeholder="BW-NB-WM400," className={inputClass} />
                <p className="mt-1 text-xs text-muted-foreground">Each marker starts a record. The final record is read when the device disconnects.</p>
              </FormField>
            )}
            {s.framing === "FixedLength" && (
              <FormField label="Message length (bytes)">
                <input type="number" min={1} value={s.fixedLength ?? ""} onChange={(e) => update({ fixedLength: e.target.value ? Number(e.target.value) : null })} className={inputClass} />
              </FormField>
            )}
            {s.framing === "EndCharacter" && (
              <FormField label="End character">
                <input maxLength={1} value={s.endCharacter ?? ""} onChange={(e) => update({ endCharacter: e.target.value || null })} placeholder="#" className={inputClass} />
              </FormField>
            )}

            {s.format === "Csv" && (
              <>
                <FormField label="Separator">
                  <input maxLength={4} value={s.csvSeparator} onChange={(e) => update({ csvSeparator: e.target.value })} className={inputClass} />
                </FormField>
                <FormField label="Expected field count after removing the prefix">
                  <input type="number" min={2} max={128} required={s.framing === "StartMarker"} value={s.csvFieldCount ?? ""} onChange={(e) => update({ csvFieldCount: e.target.value ? Number(e.target.value) : null })} placeholder="Optional, e.g. 10" className={inputClass} />
                  <p className="mt-1 text-xs text-muted-foreground">Rejects incomplete records or an unexpected layout.</p>
                </FormField>
                <label className="flex items-center gap-2 text-sm text-foreground self-center">
                  <input type="checkbox" checked={s.csvOptionalPrefix} onChange={(e) => update({ csvOptionalPrefix: e.target.checked })} />
                  Skip a text label in front of the ID (e.g. &quot;BW-NB-WM400,&quot;)
                </label>
              </>
            )}
            {s.format === "KeyValue" && (
              <div className="grid grid-cols-2 gap-3">
                <FormField label="Between pairs">
                  <input maxLength={4} value={s.pairSeparator} onChange={(e) => update({ pairSeparator: e.target.value })} className={inputClass} />
                </FormField>
                <FormField label="Between key and value">
                  <input maxLength={4} value={s.keyValueSeparator} onChange={(e) => update({ keyValueSeparator: e.target.value })} className={inputClass} />
                </FormField>
              </div>
            )}
            {isBinary && (
              <>
                <div className="grid grid-cols-2 gap-3">
                  <FormField label="Header bytes (hex, optional)">
                    <input value={s.binaryHeaderHex ?? ""} onChange={(e) => update({ binaryHeaderHex: e.target.value || null })} placeholder="03 10" className={inputClass} />
                  </FormField>
                  <FormField label="Header at byte">
                    <input type="number" min={0} value={s.binaryHeaderOffset} onChange={(e) => update({ binaryHeaderOffset: Number(e.target.value) })} className={inputClass} />
                  </FormField>
                </div>
                <FormField label="Checksum">
                  <select value={s.binaryChecksum} onChange={(e) => update({ binaryChecksum: e.target.value as ConnectorSettings["binaryChecksum"] })} className={inputClass}>
                    <option value="None">None</option>
                    <option value="Crc16Modbus">CRC-16/Modbus (last 2 bytes)</option>
                  </select>
                </FormField>
              </>
            )}
          </div>
        </Section>

        <Section title="3. Sample message">
          <textarea
            value={sample}
            onChange={(e) => setSample(e.target.value)}
            rows={3}
            placeholder={isBinary ? "01 03 10 4E DF 4E 60 70 7E 4F 6A 4E 8E 74 D3 59 15 00 09 9D E7" : "Paste one real message from the device"}
            className={`${inputClass} font-mono text-xs`}
          />
          <div className="flex flex-wrap items-center gap-3 mt-2">
            <label className="flex items-center gap-2 text-xs text-muted-foreground">
              <input type="checkbox" checked={sampleIsHex} onChange={(e) => setSampleIsHex(e.target.checked)} />
              Sample is hex (binary)
            </label>
            {isEdit && (
              <button type="button" onClick={startCapture} disabled={!!capture} className="inline-flex items-center gap-1.5 text-xs px-2.5 py-1 rounded-md border border-border hover:bg-muted disabled:opacity-60">
                <Radio className="h-3.5 w-3.5" />
                {capture ? `Waiting for a message on port ${s.port}…` : "Capture from port"}
              </button>
            )}
            {!isEdit && (
              <span className="text-xs text-muted-foreground">
                No sample yet? Create the connector without a mapping, then Edit → Capture from port to grab a real message.
              </span>
            )}
          </div>
          {testError && <p className="text-xs text-destructive mt-2">{testError}</p>}
        </Section>

        <Section title="4. Field mapping">
          {rows.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              {isBinary ? "Add the byte positions to read below." : "Paste a sample above — every value in it appears here to map."}
            </p>
          ) : (
            <div className="overflow-x-auto border border-border rounded-md">
              <table className="w-full text-sm">
                <thead className="bg-muted/50 text-xs text-muted-foreground">
                  <tr>
                    <th className="text-left px-3 py-1.5">{s.format === "Csv" ? "Column" : isBinary ? "Byte" : "Key"}</th>
                    {isBinary && <th className="text-left px-3 py-1.5">Size</th>}
                    <th className="text-left px-3 py-1.5">Sample value</th>
                    <th className="text-left px-3 py-1.5">Map to</th>
                    <th className="text-left px-3 py-1.5" title="value = (raw − offset) × scale">Offset / Scale</th>
                    {isBinary && <th />}
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {rows.map(({ source, value }) => {
                    const m = mappingFor(source);
                    const target = !m ? UNMAPPED : m.target === "Parameter" ? (m.parameterKey ?? UNMAPPED) : m.target;
                    const numeric = m?.target === "Parameter";
                    return (
                      <tr key={source}>
                        <td className="px-3 py-1.5 font-mono text-xs">{source}</td>
                        {isBinary && (
                          <td className="px-3 py-1.5 text-xs">
                            <select value={m?.byteLength ?? 1} onChange={(e) => setMapping(source, { byteLength: Number(e.target.value) })} className="border border-input rounded px-1 py-0.5 bg-background">
                              <option value={1}>1 byte</option>
                              <option value={2}>2 bytes</option>
                              <option value={4}>4 bytes</option>
                            </select>
                            <label className="ml-2">
                              <input type="checkbox" checked={!!m?.signed} onChange={(e) => setMapping(source, { signed: e.target.checked })} /> signed
                            </label>
                            <label className="ml-2">
                              <input type="checkbox" checked={!!m?.littleEndian} onChange={(e) => setMapping(source, { littleEndian: e.target.checked })} /> little-endian
                            </label>
                          </td>
                        )}
                        <td className="px-3 py-1.5 font-mono text-xs text-muted-foreground max-w-[12rem] truncate" title={value}>
                          {value ?? "—"}
                        </td>
                        <td className="px-3 py-1.5">
                          <select value={target} onChange={(e) => onTargetChange(source, e.target.value)} className="w-full border border-input rounded px-1.5 py-1 bg-background text-sm">
                            <option value={UNMAPPED}>— not mapped —</option>
                            <option value="SensorId">Sensor ID</option>
                            <option value="Time">Time</option>
                            <option value="Ignore">Ignore</option>
                            <optgroup label={deviceType ? `${deviceType.name} parameters` : "Parameters"}>
                              {parameters.map((p) => (
                                <option key={p.key} value={p.key}>
                                  {p.label} ({p.key}){p.unit ? ` · ${p.unit}` : ""}
                                </option>
                              ))}
                            </optgroup>
                            {deviceType && <option value={NEW_PARAMETER}>➕ New parameter…</option>}
                          </select>
                        </td>
                        <td className="px-3 py-1.5">
                          {numeric && (
                            <div className="flex gap-1">
                              <input type="number" step="any" value={m?.offset ?? 0} onChange={(e) => setMapping(source, { offset: Number(e.target.value) })} className="w-20 border border-input rounded px-1 py-0.5 bg-background text-xs" />
                              <input type="number" step="any" value={m?.scale ?? 1} onChange={(e) => setMapping(source, { scale: Number(e.target.value) })} className="w-20 border border-input rounded px-1 py-0.5 bg-background text-xs" />
                            </div>
                          )}
                        </td>
                        {isBinary && (
                          <td className="px-2">
                            <button type="button" onClick={() => setMapping(source, null)} className="text-muted-foreground hover:text-destructive" title="Remove">
                              <Trash2 className="h-3.5 w-3.5" />
                            </button>
                          </td>
                        )}
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
          {isBinary && <AddByteRow onAdd={(start) => setMapping(String(start), { target: "Parameter", parameterKey: null, byteLength: 2 })} />}
          <p className="text-xs text-muted-foreground mt-2">
            Keys match exactly as the device sends them, case included. A value not mapped is not stored — the Connectors page lists keys that keep arriving
            unmapped.
          </p>
        </Section>

        <Section title="5. Preview — what BMS will receive">
          {!test ? (
            <p className="text-sm text-muted-foreground">Appears once there is a sample.</p>
          ) : (
            <div className="space-y-2 text-sm">
              <p>
                Device code:{" "}
                {test.deviceCode ? <code className="font-mono">{test.deviceCode}</code> : <span className="text-amber-600 dark:text-amber-400">no sensor ID mapped yet</span>}
                {test.time && <span className="text-muted-foreground"> · time {new Date(test.time).toLocaleString()}</span>}
                {!test.time && <span className="text-muted-foreground"> · time: when it arrives</span>}
              </p>
              <pre className="bg-muted/50 rounded-md p-2 text-xs overflow-x-auto">{JSON.stringify(test.fields, null, 1)}</pre>
              {test.error && <p className="text-xs text-destructive">{test.error}</p>}
              {test.unmappedSources.length > 0 && <p className="text-xs text-muted-foreground">Not mapped: {test.unmappedSources.join(", ")}</p>}
            </div>
          )}
          {test && test.validationErrors.length > 0 && (
            <ul className="mt-2 text-xs text-amber-700 dark:text-amber-400 list-disc pl-5 space-y-0.5">
              {test.validationErrors.map((v) => (
                <li key={v}>{v}</li>
              ))}
            </ul>
          )}
        </Section>

        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Saving..." : isEdit ? "Save connector" : "Create connector"}
        </button>
      </form>

      {newParamFor && deviceType && (
        <NewParameterModal
          deviceTypeId={deviceType.id}
          deviceTypeName={deviceType.name}
          onClose={() => setNewParamFor(null)}
          onCreated={(key) => {
            typesQuery.refetch();
            setMapping(newParamFor, { target: "Parameter", parameterKey: key });
            setNewParamFor(null);
          }}
        />
      )}
    </Modal>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="space-y-2 dark:space-y-3 dark:rounded-xl dark:border dark:border-border dark:bg-background/25 dark:p-4">
      <h3 className="text-sm font-semibold text-foreground dark:text-info">{title}</h3>
      {children}
    </section>
  );
}

function AddByteRow({ onAdd }: { onAdd: (start: number) => void }) {
  const [start, setStart] = useState("");
  return (
    <div className="flex items-center gap-2 mt-2">
      <input type="number" min={0} value={start} onChange={(e) => setStart(e.target.value)} placeholder="start byte" className="w-28 border border-input rounded px-2 py-1 bg-background text-sm" />
      <button
        type="button"
        disabled={start === ""}
        onClick={() => {
          onAdd(Number(start));
          setStart("");
        }}
        className="inline-flex items-center gap-1 text-xs px-2.5 py-1 rounded-md border border-border hover:bg-muted disabled:opacity-50"
      >
        <Plus className="h-3.5 w-3.5" /> Add byte field
      </button>
    </div>
  );
}

/** Adds a parameter to the connector's device type without leaving the mapping. */
function NewParameterModal({
  deviceTypeId,
  deviceTypeName,
  onClose,
  onCreated,
}: {
  deviceTypeId: string;
  deviceTypeName: string;
  onClose: () => void;
  onCreated: (key: string) => void;
}) {
  const [key, setKey] = useState("");
  const [label, setLabel] = useState("");
  const [unit, setUnit] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function save(e: React.FormEvent) {
    e.preventDefault();
    e.stopPropagation();
    setError(null);
    if (!/^[A-Za-z][A-Za-z0-9_]{0,63}$/.test(key)) return setError("Key: letters, digits and _, starting with a letter (e.g. motorCurrent).");
    setSaving(true);
    try {
      const created = await DeviceTypesApi.addParameter(deviceTypeId, { key, label: label || key, unit: unit || undefined, kind: "Instantaneous" });
      onCreated(created.key);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't add the parameter");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal title={`New parameter on ${deviceTypeName}`} onClose={onClose}>
      <form onSubmit={save} className="space-y-3">
        <FormField label="Key">
          <input required value={key} onChange={(e) => setKey(e.target.value)} placeholder="signalStrength" className={inputClass} />
        </FormField>
        <FormField label="Label">
          <input value={label} onChange={(e) => setLabel(e.target.value)} placeholder="Signal strength" className={inputClass} />
        </FormField>
        <FormField label="Unit (optional)">
          <input value={unit} onChange={(e) => setUnit(e.target.value)} placeholder="dBm" className={inputClass} />
        </FormField>
        <p className="text-xs text-muted-foreground">Added to {deviceTypeName} for every company. Change its kind, limits or dashboard widget on Device Types.</p>
        {error && <p className="text-xs text-destructive">{error}</p>}
        <button disabled={saving} className="w-full text-sm px-3 py-1.5 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 disabled:opacity-50">
          {saving ? "Adding..." : "Add parameter"}
        </button>
      </form>
    </Modal>
  );
}
