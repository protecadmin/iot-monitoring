import { useEffect, useRef, useState } from "react";
import { ChevronDown, Image as ImageIcon, Tv } from "lucide-react";
import { HubsApi } from "@/lib/api/resources";
import { tupleKey, type HubSnapshot } from "@/lib/hubs";
import { Badge, duration, JsonView, Panel } from "./ConsoleWidgets";
import { Modal } from "@/components/ui/Modal";

export function SensorPanel({ id, snapshot, snapshotAge, writesEnabled, onNotice, onRefresh }: { id: string; snapshot: HubSnapshot | null; snapshotAge: number | null; writesEnabled: boolean; onNotice: (message: string) => void; onRefresh: () => void }) {
  const [busy, setBusy] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  async function uploadLogo(logo: File) {
    setFile(null); setBusy(true); onNotice("");
    try {
      if (!writesEnabled || snapshot?.capabilities?.logo_base64 !== true) throw new Error("Client-logo upload is unavailable on this HUB.");
      if (!logo.size || logo.size > 262144 || !["image/png", "image/jpeg", "image/webp"].includes(logo.type)) throw new Error("Choose PNG, JPEG or WebP up to 256 KiB.");
      const bitmap = await createImageBitmap(logo); const pixels = bitmap.width * bitmap.height; bitmap.close();
      if (pixels > 4000000) throw new Error("Client logo must be at most four million pixels.");
      const bytes = new Uint8Array(await logo.arrayBuffer());
      const hash = Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", bytes)), b => b.toString(16).padStart(2, "0")).join("");
      const transfer = `logo-${crypto.randomUUID()}`; const version = snapshot.config_version;
      const chunks = Math.ceil(bytes.length / 12288);
      async function sendAndWait(action: string, args: Record<string, unknown>) {
        if (!mounted.current) throw new Error("Upload cancelled: HUB page closed.");
        const res = await HubsApi.command(id, action, args, version); const deadline = Date.now() + 120000;
        while (Date.now() < deadline && mounted.current) {
          const result = (await HubsApi.commands(id)).find(c => c.commandId === res.commandId);
          if (result && ["rejected", "timed_out", "publish_failed"].includes(result.state)) throw new Error(result.message || result.code || result.state);
          if (result && (action === "logo.commit" ? result.state === "applied" : ["received", "accepted", "succeeded", "applied"].includes(result.state))) return;
          await new Promise(resolve => setTimeout(resolve, 1500));
        }
        throw new Error("Logo acknowledgement timed out. Request a snapshot before retrying.");
      }
      await sendAndWait("logo.begin", { transfer, mime: logo.type, bytes: bytes.length, sha256: hash, chunks });
      for (let index = 0; index < chunks; index++) {
        if (mounted.current) onNotice(`Uploading client logo: chunk ${index + 1} of ${chunks}…`);
        const block = bytes.slice(index * 12288, (index + 1) * 12288);
        await sendAndWait("logo.chunk", { transfer, index, base64: btoa(String.fromCharCode(...block)) });
      }
      await sendAndWait("logo.commit", { transfer });
      if (mounted.current) { onNotice("Client logo applied and verified in the authoritative snapshot."); onRefresh(); }
    } catch (e) { if (mounted.current) onNotice(e instanceof Error ? e.message : "Logo upload failed"); }
    finally { if (mounted.current) setBusy(false); }
  }
  return <Panel icon={Tv} title="Sensors & HDMI Display" subtitle="Sensor inventory, measurement freshness and channel limits" extra={<span>{snapshot?.tv?.mode ?? "Mode unavailable"}</span>}>
    <div className="mqtt-sensor-grid">{snapshot?.tv?.cards?.map((card, i) => <article className="mqtt-sensor-card" key={card.device_id ?? `${card.label}-${i}`}><div><strong>{card.sensor}</strong><Badge state={card.state ?? "Unavailable"} /></div><p>{card.label}</p><small>Measured: {card.measured_at ? new Date(card.measured_at * 1000).toLocaleString() : "Unavailable"}</small><div className="mqtt-channels">{card.channels?.map(channel => <div key={channel.key} className={channel.limit_status === "exceeded" ? "exceeded" : ""}><span>{channel.label || channel.key}</span><strong>{channel.valid !== false && channel.value != null ? `${channel.value} ${channel.unit || ""}` : "Unavailable"}</strong><small>{channel.limit != null ? `Limit ${channel.limit} ${channel.unit || ""} · ${channel.limit_status ?? "unknown"}` : "No configured limit"}</small></div>)}</div>{card.alert_state === "exceeded" && <p className="mqtt-error-text">Threshold exceeded · valid measurement, not a device fault</p>}</article>)}</div>
    {!snapshot?.tv?.cards?.length && <div className="mqtt-empty">No sensor cards in the latest snapshot. Use Get Snapshot to request current state.</div>}
    {!!snapshot?.devices?.length && <details className="mqtt-inventory"><summary>Full inventory · {snapshot.devices.length} sensors <ChevronDown size={14} /></summary><div className="mqtt-table-scroll"><table><thead><tr><th>Source</th><th>Site / Sensor</th><th>State</th><th>Measurement age</th><th>Latest value</th><th>Selected</th></tr></thead><tbody>{snapshot.devices.map((device, index) => <tr key={`${tupleKey(device)}-${index}`}><td>{device.source}</td><td>{device.site}<small>{device.sensor}</small></td><td><Badge state={device.state ?? "Unavailable"} /></td><td>{duration(device.age_seconds == null ? null : device.age_seconds + (snapshotAge ?? 0))}</td><td>{device.latest_value ?? "—"}</td><td>{device.selected ? "Yes" : "No"}</td></tr>)}</tbody></table></div></details>}
    <div className="mqtt-branding"><ImageIcon size={18} /><div><strong>Client logo</strong><small>{snapshot?.branding?.has_client_logo ? `Present · revision ${snapshot.branding.client_logo_revision ?? "unavailable"}` : "No client logo reported"} · PNG / JPEG / WebP, ≤256 KiB, ≤4M pixels</small></div><label className={`mqtt-upload ${!writesEnabled || !snapshot?.capabilities?.logo_base64 || busy ? "disabled" : ""}`}>{busy ? "Uploading…" : "Upload client logo"}<input aria-label="Upload client logo" type="file" accept="image/png,image/jpeg,image/webp" disabled={!writesEnabled || !snapshot?.capabilities?.logo_base64 || busy} onChange={e => { const f = e.target.files?.[0]; if (f) setFile(f); e.target.value = ""; }} /></label></div>
    <details className="mqtt-inventory"><summary>Reported capabilities <ChevronDown size={14} /></summary><JsonView value={snapshot?.capabilities} />{snapshot?.ota && <JsonView value={snapshot.ota} />}</details>
    {file && <Modal title="Confirm client-logo upload" onClose={() => setFile(null)}><div className="mqtt-form"><p>Upload <strong>{file.name}</strong> to this HUB? The transfer waits for every chunk ACK and verifies the new logo revision after commit. The fixed PRO-TEC brand mark is preserved.</p><button className="mqtt-send" onClick={() => void uploadLogo(file)}>Confirm upload</button></div></Modal>}
  </Panel>;
}
