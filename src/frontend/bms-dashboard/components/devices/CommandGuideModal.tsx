"use client";

import { useState } from "react";
import { Modal } from "@/components/ui/Modal";
import { CodeBlock } from "@/components/ui/CodeBlock";
import { useApi } from "@/hooks/useApi";
import { SitesApi, TenantsApi } from "@/lib/api/resources";
import type { DeviceDto } from "@/types/api";

// Stand-in used in examples when this device has no command awaiting an
// acknowledgement. When one does, its real id is used instead, so the
// copied ack actually completes it.
const SAMPLE_COMMAND_ID = "3f6c2a1e-8b4d-4c1a-9e2f-7a5b0c9d1e01";

// Must match the backend's CommandType names and what
// MqttDeviceCommandDispatcher / MqttIngestionWorker.HandleCommandAckAsync
// actually send and accept.
const COMMANDS: {
  type: string;
  label: string;
  purpose: string;
  payload: string | null;
  deviceMust: string[];
  failWhen: string;
}[] = [
  {
    type: "Restart",
    label: "Restart",
    purpose: "Reboots the device controller.",
    payload: null,
    deviceMust: [
      "Send the acknowledgement FIRST, then reboot — a device that reboots before replying loses the reply and the command ends as Timeout.",
      "After restarting, reconnect to the broker, re-subscribe to the commands topic and resume telemetry.",
    ],
    failWhen: "a restart isn't allowed right now (e.g. during a firmware update).",
  },
  {
    type: "RequestTelemetry",
    label: "Request Telemetry",
    purpose: "Asks for a fresh reading now, without waiting for the next scheduled report.",
    payload: null,
    deviceMust: [
      "Take a reading and publish it immediately to the telemetry topic, in the device's normal format (with a new seq).",
      "Then publish the acknowledgement.",
    ],
    failWhen: "the sensor can't be read.",
  },
  {
    type: "ChangeReportingInterval",
    label: "Change Reporting Interval",
    purpose: "Changes how often the device sends telemetry. The new interval in seconds is in payload, as text.",
    payload: "30",
    deviceMust: [
      "Read payload as a whole number of seconds — here, send telemetry every 30 s.",
      "Check it's within the range the device supports (e.g. 5–3600 s), apply it from the next report, and keep it after a reboot.",
    ],
    failWhen: "payload is missing, not a number, or out of range — the device keeps its old interval.",
  },
  {
    type: "SynchronizeTime",
    label: "Synchronize Time",
    purpose: "Asks the device to correct its clock, so the timestamps on its readings are accurate.",
    payload: null,
    deviceMust: ["Resynchronise the clock from NTP or the device's time reference.", "Acknowledge once the clock is set."],
    failWhen: "no time source could be reached.",
  },
];

function Step({ n, title, children }: { n: number; title: string; children: React.ReactNode }) {
  return (
    <div className="flex gap-3">
      <div className="shrink-0 h-5 w-5 rounded-full bg-primary/10 text-primary text-[11px] font-semibold flex items-center justify-center mt-0.5">{n}</div>
      <div className="flex-1 min-w-0 space-y-2">
        <div className="text-sm font-medium text-foreground">{title}</div>
        {children}
      </div>
    </div>
  );
}

/**
 * How the Commands buttons map onto MQTT, for whoever builds or integrates
 * the device: the exact topics for this device, what each command sends,
 * what the device must do, and the acknowledgement BMS waits for.
 */
export function CommandGuideModal({
  device,
  pendingCommandId,
  onClose,
}: {
  device: DeviceDto;
  /** A real command on this device still waiting for its ack — used in the examples so copying the ack completes it. */
  pendingCommandId?: string;
  onClose: () => void;
}) {
  const tenantQuery = useApi(() => TenantsApi.get(device.tenantId), [device.tenantId]);
  const siteQuery = useApi(() => SitesApi.get(device.siteId, device.tenantId), [device.siteId, device.tenantId]);
  const [active, setActive] = useState(COMMANDS[0].type);

  const tenantSlug = tenantQuery.data?.slug ?? "{tenant}";
  const siteCode = siteQuery.data?.code ?? "{site}";
  const prefix = `iot/${tenantSlug}/${siteCode}/${device.deviceCode}`;
  const commandId = pendingCommandId ?? SAMPLE_COMMAND_ID;
  const command = COMMANDS.find((c) => c.type === active)!;

  const sent = JSON.stringify({ commandId, type: command.type, payload: command.payload }, null, 2);
  const ack = JSON.stringify({ commandId, status: "ACKNOWLEDGED" }, null, 2);

  return (
    <Modal title={`Command guide — ${device.name} (${device.deviceCode})`} onClose={onClose} size="xl">
      <div className="space-y-5">
        {/* How it works */}
        <section className="space-y-2">
          <p className="text-sm text-muted-foreground">
            Each command button sends one MQTT message to the device. The device must reply on a separate topic with the same{" "}
            <code className="font-mono text-xs">commandId</code>. With no reply within <span className="font-medium text-foreground">2 minutes</span>, the
            command is marked <span className="font-medium text-foreground">Timeout</span>.
          </p>
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-2 text-xs">
            <div className="border border-border rounded-md p-2.5">
              <div className="text-muted-foreground">Broker login</div>
              <div className="mt-0.5 text-foreground">
                User <code className="font-mono">{tenantSlug}</code> · password from MQTT Credentials
              </div>
            </div>
            <div className="border border-border rounded-md p-2.5">
              <div className="text-muted-foreground">Device subscribes to</div>
              <code className="mt-0.5 block font-mono text-foreground break-all">{prefix}/commands</code>
            </div>
            <div className="border border-border rounded-md p-2.5">
              <div className="text-muted-foreground">Device replies on</div>
              <code className="mt-0.5 block font-mono text-foreground break-all">{prefix}/commands/ack</code>
            </div>
          </div>
        </section>

        {/* Per-command example */}
        <section className="space-y-3">
          <div className="flex flex-wrap gap-1 border-b border-border">
            {COMMANDS.map((c) => (
              <button
                key={c.type}
                onClick={() => setActive(c.type)}
                className={`text-xs px-3 py-1.5 -mb-px border-b-2 ${
                  active === c.type ? "border-primary text-primary font-medium" : "border-transparent text-muted-foreground hover:text-foreground"
                }`}
              >
                {c.label}
              </button>
            ))}
          </div>

          <p className="text-sm text-foreground">{command.purpose}</p>

          <Step n={1} title="BMS sends">
            <CodeBlock label="Topic" value={`${prefix}/commands`} />
            <CodeBlock label="Message" value={sent} />
          </Step>

          <Step n={2} title="Device does">
            <ul className="list-disc pl-4 space-y-1 text-sm text-muted-foreground">
              {command.deviceMust.map((line) => (
                <li key={line}>{line}</li>
              ))}
            </ul>
          </Step>

          <Step n={3} title="Device replies">
            <CodeBlock label="Topic" value={`${prefix}/commands/ack`} />
            <CodeBlock label="Message" value={ack} />
            <p className="text-xs text-muted-foreground">
              Use <code className="font-mono">&quot;status&quot;: &quot;FAILED&quot;</code> instead when {command.failWhen}
            </p>
          </Step>
        </section>

        {/* Statuses */}
        <section className="space-y-2">
          <h3 className="text-sm font-medium text-foreground">Status in the commands list</h3>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-1 text-xs">
            {[
              ["Pending", "Created, not yet published"],
              ["Sent", "Published — waiting for the device's reply"],
              ["Acknowledged", 'Device replied "ACKNOWLEDGED"'],
              ["Failed", 'Device replied "FAILED"'],
              ["Timeout", "No reply within 2 minutes — check the topic and commandId"],
            ].map(([status, meaning]) => (
              <div key={status} className="flex gap-2">
                <span className="w-24 shrink-0 font-medium text-foreground">{status}</span>
                <span className="text-muted-foreground">{meaning}</span>
              </div>
            ))}
          </div>
        </section>

        {/* Manual test */}
        <section className="space-y-2">
          <h3 className="text-sm font-medium text-foreground">Test without a device (Mosquitto tools)</h3>
          <p className="text-xs text-muted-foreground">
            Replace <code className="font-mono">YOUR_PASSWORD</code> with the tenant&apos;s MQTT password.
            {pendingCommandId
              ? " The reply below already carries the id of this device's command that is waiting — run it to acknowledge that command."
              : " Send a command first, then paste its commandId (shown in the first terminal) into the reply."}
          </p>
          <CodeBlock label="Terminal 1 — watch for commands" value={`mosquitto_sub -h localhost -p 1883 -u ${tenantSlug} -P YOUR_PASSWORD \\\n  -t '${prefix}/commands' -v`} />
          <CodeBlock
            label="Terminal 2 — reply within 2 minutes"
            value={`mosquitto_pub -h localhost -p 1883 -u ${tenantSlug} -P YOUR_PASSWORD \\\n  -t '${prefix}/commands/ack' \\\n  -m '{"commandId":"${commandId}","status":"ACKNOWLEDGED"}'`}
          />
        </section>
      </div>
    </Modal>
  );
}
