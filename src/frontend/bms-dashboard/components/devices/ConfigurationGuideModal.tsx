"use client";

import { useState } from "react";
import { Modal } from "@/components/ui/Modal";
import { CodeBlock } from "@/components/ui/CodeBlock";
import { StatusBadge } from "@/components/ui/States";
import { useApi } from "@/hooks/useApi";
import { DevicesApi, SitesApi, TenantsApi } from "@/lib/api/resources";
import type { DeviceDto, DeviceLifecycleSettingsDto } from "@/types/api";

function formatTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : "Never";
}

function formatSeconds(seconds: number): string {
  if (seconds < 60) return `${seconds} s`;
  const minutes = seconds / 60;
  return Number.isInteger(minutes) ? `${minutes} min` : `${seconds} s (${minutes.toFixed(1)} min)`;
}

function latest(...times: (string | null)[]): Date | null {
  const valid = times.filter((t): t is string => !!t).map((t) => new Date(t).getTime());
  return valid.length ? new Date(Math.max(...valid)) : null;
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="space-y-2">
      <h3 className="text-sm font-medium text-foreground">{title}</h3>
      {children}
    </section>
  );
}

function Bullets({ items }: { items: React.ReactNode[] }) {
  return (
    <ul className="list-disc pl-4 space-y-1 text-sm text-muted-foreground">
      {items.map((item, i) => (
        <li key={i}>{item}</li>
      ))}
    </ul>
  );
}

const TABS = ["Overview", "Reporting interval", "Heartbeat & status", "Temperature threshold"] as const;
type Tab = (typeof TABS)[number];

export interface ConfigurationValues {
  reportingIntervalSeconds: number;
  heartbeatIntervalSeconds: number;
  temperatureThreshold: number | null;
}

/**
 * Everything on the Configuration tab explained for the device team and
 * operators: what "Save & push" publishes, what the device must do with each
 * field, and how heartbeats drive Online/Offline — using this device's real
 * topic, the values currently in the form, and the backend's actual timing
 * settings.
 */
export function ConfigurationGuideModal({
  deviceId,
  values,
  version,
  initialTab = "Overview",
  onClose,
}: {
  deviceId: string;
  /** What's in the form now — what the next "Save & push" would send. */
  values: ConfigurationValues;
  /** Version last pushed; the next save sends version + 1. */
  version: number | undefined;
  initialTab?: Tab;
  onClose: () => void;
}) {
  const [tab, setTab] = useState<Tab>(initialTab);
  const deviceQuery = useApi(() => DevicesApi.get(deviceId), [deviceId]);
  const settingsQuery = useApi(() => DevicesApi.lifecycleSettings(), []);
  const device = deviceQuery.data;
  const tenantQuery = useApi(() => (device ? TenantsApi.get(device.tenantId) : Promise.resolve(null)), [device?.tenantId]);
  const siteQuery = useApi(() => (device ? SitesApi.get(device.siteId, device.tenantId) : Promise.resolve(null)), [device?.siteId, device?.tenantId]);

  const tenantSlug = tenantQuery.data?.slug ?? "{tenant}";
  const siteCode = siteQuery.data?.code ?? "{site}";
  const prefix = `iot/${tenantSlug}/${siteCode}/${device?.deviceCode ?? "{device}"}`;

  return (
    <Modal title={`Configuration guide${device ? ` — ${device.name} (${device.deviceCode})` : ""}`} onClose={onClose} size="xl">
      <div className="space-y-4">
        <div className="flex flex-wrap gap-1 border-b border-border">
          {TABS.map((t) => (
            <button
              key={t}
              onClick={() => setTab(t)}
              className={`text-xs px-3 py-1.5 -mb-px border-b-2 ${
                tab === t ? "border-primary text-primary font-medium" : "border-transparent text-muted-foreground hover:text-foreground"
              }`}
            >
              {t}
            </button>
          ))}
        </div>

        {tab === "Overview" && <OverviewTab prefix={prefix} values={values} version={version} />}
        {tab === "Reporting interval" && <ReportingTab prefix={prefix} values={values} settings={settingsQuery.data} />}
        {tab === "Heartbeat & status" && (
          <HeartbeatTab prefix={prefix} tenantSlug={tenantSlug} device={device} heartbeat={values.heartbeatIntervalSeconds} settings={settingsQuery.data} />
        )}
        {tab === "Temperature threshold" && <ThresholdTab values={values} />}
      </div>
    </Modal>
  );
}

function OverviewTab({ prefix, values, version }: { prefix: string; values: ConfigurationValues; version: number | undefined }) {
  const nextVersion = (version ?? 0) + 1;
  const message = JSON.stringify(
    {
      version: nextVersion,
      reportingInterval: values.reportingIntervalSeconds,
      heartbeatInterval: values.heartbeatIntervalSeconds,
      temperatureThreshold: values.temperatureThreshold,
    },
    null,
    2,
  );

  return (
    <div className="space-y-5">
      <p className="text-sm text-muted-foreground">
        These settings are for the <span className="font-medium text-foreground">device itself</span> — they tell it how to behave. They don&apos;t change
        anything inside BMS.
      </p>

      <Section title="What “Save & push to device” does">
        <Bullets
          items={[
            <>BMS stores the settings as a new version (currently {version ?? "none"}; the next save is version {nextVersion}).</>,
            <>BMS publishes the whole configuration, all fields at once, to the device&apos;s config topic over MQTT.</>,
            <>The device applies it. BMS does not wait for a reply — there is no acknowledgement for configuration, unlike commands.</>,
          ]}
        />
      </Section>

      <Section title="The message the device receives (with the values in the form now)">
        <CodeBlock label="Topic" value={`${prefix}/config`} />
        <CodeBlock label="Message" value={message} />
        <div className="border border-border rounded-md overflow-x-auto">
          <table className="w-full text-xs">
            <thead className="bg-muted/50 text-muted-foreground">
              <tr>
                <th className="text-left px-3 py-1.5">Field</th>
                <th className="text-left px-3 py-1.5">Meaning</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {[
                ["version", "Increases with every save. Ignore a message whose version is older than the one already applied."],
                ["reportingInterval", "Seconds between telemetry readings — see the Reporting interval tab."],
                ["heartbeatInterval", "Seconds between heartbeats — see the Heartbeat & status tab."],
                ["temperatureThreshold", "Optional limit for the device's own use, or null — see the Temperature threshold tab."],
              ].map(([field, meaning]) => (
                <tr key={field}>
                  <td className="px-3 py-1.5 font-mono whitespace-nowrap">{field}</td>
                  <td className="px-3 py-1.5 text-muted-foreground">{meaning}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Section>

      <Section title="What the device must do">
        <Bullets
          items={[
            <>
              Subscribe to <code className="font-mono text-xs">{prefix}/config</code> after every connect, at QoS 1.
            </>,
            "Apply every field in the message, and save them so they survive a reboot.",
            <>
              Connect with a <span className="font-medium text-foreground">fixed client ID and a persistent session</span> (clean session off). The
              configuration isn&apos;t retained, so a device that was offline during the push only receives it on reconnect if the broker kept its session.
            </>,
          ]}
        />
      </Section>
    </div>
  );
}

function ReportingTab({ prefix, values, settings }: { prefix: string; values: ConfigurationValues; settings: DeviceLifecycleSettingsDto | undefined }) {
  const interval = values.reportingIntervalSeconds;
  const perHour = interval > 0 ? Math.round(3600 / interval) : 0;
  const noTelemetry = settings?.noTelemetryAlertAfterSeconds ?? 120;

  return (
    <div className="space-y-5">
      <p className="text-sm text-muted-foreground">
        How often the device sends a <span className="font-medium text-foreground">telemetry reading</span> — the actual data behind the Live chart,
        History, Reports and threshold alerts.
      </p>

      <Section title="What the device sends">
        <p className="text-xs text-muted-foreground">
          Every <span className="font-medium text-foreground">{formatSeconds(interval)}</span> — about {perHour.toLocaleString()} readings an hour:
        </p>
        <CodeBlock label="Topic" value={`${prefix}/telemetry`} />
        <CodeBlock label="Message (example — the keys are the parameter keys of the device's type)" value={`{"temperature": 24.6, "humidity": 58.2, "seq": 1790656201}`} />
        <p className="text-xs text-muted-foreground">
          <code className="font-mono">seq</code> must change with every reading — BMS rejects a repeated one as a duplicate. Keys that aren&apos;t a parameter of the
          device&apos;s type are dropped.
        </p>
      </Section>

      <Section title="Choosing a value">
        <div className="border border-border rounded-md overflow-x-auto">
          <table className="w-full text-xs">
            <thead className="bg-muted/50 text-muted-foreground">
              <tr>
                <th className="text-left px-3 py-1.5">Interval</th>
                <th className="text-left px-3 py-1.5">Good for</th>
                <th className="text-left px-3 py-1.5">Trade-off</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              <tr>
                <td className="px-3 py-1.5">5–15 s</td>
                <td className="px-3 py-1.5">Fast-changing values, quick alerts (vibration, noise)</td>
                <td className="px-3 py-1.5 text-muted-foreground">More data and network traffic</td>
              </tr>
              <tr>
                <td className="px-3 py-1.5">30–60 s</td>
                <td className="px-3 py-1.5">Most environmental sensors (temperature, WBGT)</td>
                <td className="px-3 py-1.5 text-muted-foreground">Alerts up to a minute late</td>
              </tr>
              <tr>
                <td className="px-3 py-1.5">5 min or more</td>
                <td className="px-3 py-1.5">Slow values, battery-powered devices</td>
                <td className="px-3 py-1.5 text-muted-foreground">Needs a heartbeat to stay Online between readings</td>
              </tr>
            </tbody>
          </table>
        </div>
        {interval > noTelemetry && (
          <p className="text-xs text-amber-600 dark:text-amber-400">
            {formatSeconds(interval)} is longer than the {formatSeconds(noTelemetry)} &quot;No Telemetry&quot; alert time, so that alert will fire between
            readings. Raise the alert rule&apos;s threshold above {formatSeconds(interval)}.
          </p>
        )}
      </Section>

      <Section title="Change it without a full configuration push">
        <p className="text-xs text-muted-foreground">
          The <span className="font-medium text-foreground">Change Reporting Interval</span> command on the Commands tab changes only this value, and the
          device acknowledges it — useful when you need confirmation that it was applied.
        </p>
      </Section>
    </div>
  );
}

function HeartbeatTab({
  prefix,
  tenantSlug,
  device,
  heartbeat,
  settings,
}: {
  prefix: string;
  tenantSlug: string;
  device: DeviceDto | undefined;
  heartbeat: number;
  settings: DeviceLifecycleSettingsDto | undefined;
}) {
  const offlineAfter = settings?.offlineAfterSeconds ?? 90;
  const sweep = settings?.sweepIntervalSeconds ?? 30;
  const noTelemetryAlert = settings?.noTelemetryAlertAfterSeconds ?? 120;
  const offlineAlert = settings?.deviceOfflineAlertAfterSeconds ?? 300;

  const lastMessage = device ? latest(device.lastHeartbeatAt, device.lastTelemetryAt, device.lastSeenAt) : null;
  const offlineBy = lastMessage ? new Date(lastMessage.getTime() + offlineAfter * 1000) : null;
  const missedBeforeOffline = heartbeat > 0 ? Math.floor(offlineAfter / heartbeat) : 0;

  return (
    <div className="space-y-5">
      <p className="text-sm text-muted-foreground">
        A heartbeat is a small &quot;I&apos;m alive&quot; message the <span className="font-medium text-foreground">device sends</span> to BMS. It is used{" "}
        <span className="font-medium text-foreground">only for Online / Offline status</span> — it carries no readings and triggers no threshold alerts.
      </p>

      {device && (
        <Section title="This device right now">
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 text-xs">
            <div className="border border-border rounded-md p-2.5">
              <div className="text-muted-foreground mb-1">Status</div>
              <StatusBadge status={device.status} />
            </div>
            <div className="border border-border rounded-md p-2.5">
              <div className="text-muted-foreground">Last reading</div>
              <div className="mt-0.5 text-foreground">{formatTime(device.lastTelemetryAt)}</div>
            </div>
            <div className="border border-border rounded-md p-2.5">
              <div className="text-muted-foreground">Last heartbeat</div>
              <div className="mt-0.5 text-foreground">{formatTime(device.lastHeartbeatAt)}</div>
            </div>
            <div className="border border-border rounded-md p-2.5">
              <div className="text-muted-foreground">Goes Offline at</div>
              <div className="mt-0.5 text-foreground">
                {device.status === "Online" && offlineBy ? `${offlineBy.toLocaleTimeString()} if nothing else arrives` : "—"}
              </div>
            </div>
          </div>
        </Section>
      )}

      <Section title="What the device sends">
        <p className="text-xs text-muted-foreground">
          Every <span className="font-medium text-foreground">{formatSeconds(heartbeat)}</span>, over MQTT with the tenant&apos;s login:
        </p>
        <CodeBlock label="Topic" value={`${prefix}/heartbeat`} />
        <CodeBlock label="Message" value="{}" />
        <p className="text-xs text-muted-foreground">
          BMS ignores the message content — only its arrival counts. <code className="font-mono">{"{}"}</code> is recommended; an empty message works the same.
        </p>
      </Section>

      <Section title="When the status changes">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-xs">
          <div className="border border-emerald-500/30 bg-emerald-500/5 rounded-md p-3 space-y-1">
            <div className="font-medium text-emerald-700 dark:text-emerald-400">Online — immediately</div>
            <div className="text-muted-foreground">As soon as any message arrives — a reading or a heartbeat — while the device is Registered, Provisioned or Offline.</div>
          </div>
          <div className="border border-destructive/30 bg-destructive/5 rounded-md p-3 space-y-1">
            <div className="font-medium text-destructive">Offline — after {formatSeconds(offlineAfter)} of silence</div>
            <div className="text-muted-foreground">
              When nothing at all (no reading, no heartbeat) has arrived for over {formatSeconds(offlineAfter)}. BMS checks every {formatSeconds(sweep)}, so it
              happens {offlineAfter}–{offlineAfter + sweep} s after the last message.
            </div>
          </div>
        </div>
        {heartbeat > 0 && (
          <p className={`text-xs ${missedBeforeOffline >= 3 ? "text-muted-foreground" : "text-amber-600 dark:text-amber-400"}`}>
            With a {formatSeconds(heartbeat)} heartbeat, the device must miss {missedBeforeOffline <= 1 ? "just one heartbeat" : `${missedBeforeOffline} heartbeats in a row`}{" "}
            before it goes Offline.
            {missedBeforeOffline < 3 && ` A heartbeat of ${formatSeconds(Math.floor(offlineAfter / 3))} or less is recommended, so a single lost message doesn't flip it Offline.`}
          </p>
        )}
      </Section>

      <Section title="Status and alerts">
        <div className="border border-border rounded-md overflow-x-auto">
          <table className="w-full text-xs">
            <thead className="bg-muted/50 text-muted-foreground">
              <tr>
                <th className="text-left px-3 py-1.5">Situation</th>
                <th className="text-left px-3 py-1.5">Status</th>
                <th className="text-left px-3 py-1.5">Alert</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              <tr>
                <td className="px-3 py-1.5">Readings and heartbeats arriving</td>
                <td className="px-3 py-1.5">Online</td>
                <td className="px-3 py-1.5 text-muted-foreground">None</td>
              </tr>
              <tr>
                <td className="px-3 py-1.5">Heartbeats arriving, no readings (sensor fault)</td>
                <td className="px-3 py-1.5">Online</td>
                <td className="px-3 py-1.5">&quot;No Telemetry&quot; after {formatSeconds(noTelemetryAlert)}</td>
              </tr>
              <tr>
                <td className="px-3 py-1.5">Readings arriving, no heartbeats</td>
                <td className="px-3 py-1.5">Online</td>
                <td className="px-3 py-1.5 text-muted-foreground">None — readings also prove it&apos;s alive</td>
              </tr>
              <tr>
                <td className="px-3 py-1.5">Nothing arriving (power or network lost)</td>
                <td className="px-3 py-1.5">Offline after {formatSeconds(offlineAfter)}</td>
                <td className="px-3 py-1.5">&quot;Device Offline&quot; after {formatSeconds(offlineAlert)}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <p className="text-xs text-muted-foreground">
          Alert times are the defaults; an alert rule with its own threshold uses that instead. The Offline status comes first, so a short drop-out shows Offline
          briefly without raising an alert.
        </p>
      </Section>

      <Section title="Test it (Mosquitto tools)">
        <p className="text-xs text-muted-foreground">
          Send one heartbeat by hand — the device turns Online and stays so for {formatSeconds(offlineAfter)}. Replace{" "}
          <code className="font-mono">YOUR_PASSWORD</code> with the tenant&apos;s MQTT password.
        </p>
        <CodeBlock value={`mosquitto_pub -h localhost -p 1883 -u ${tenantSlug} -P YOUR_PASSWORD \\\n  -t '${prefix}/heartbeat' -m '{}'`} />
      </Section>
    </div>
  );
}

function ThresholdTab({ values }: { values: ConfigurationValues }) {
  return (
    <div className="space-y-5">
      <p className="text-sm text-muted-foreground">
        An optional temperature limit (°C) passed to the device for <span className="font-medium text-foreground">its own use</span> — for example a local
        buzzer, relay or display warning when the temperature goes above it.
      </p>

      <Section title="In the config message">
        <CodeBlock
          value={
            values.temperatureThreshold === null
              ? `"temperatureThreshold": null     // empty in the form — the device keeps no limit`
              : `"temperatureThreshold": ${values.temperatureThreshold}`
          }
        />
      </Section>

      <Section title="What it does — and doesn't do">
        <Bullets
          items={[
            <>
              <span className="font-medium text-foreground">The device</span> decides what to do with it. A device with no local alarm can ignore it.
            </>,
            <>
              <span className="font-medium text-foreground">BMS does not use it.</span> BMS alerts and notifications (email, Telegram) come from{" "}
              <span className="font-medium text-foreground">Alert Rules</span> only — changing this value raises no alert in BMS.
            </>,
            "Leave it empty for devices without a temperature sensor, such as noise meters.",
          ]}
        />
      </Section>

      <Section title="If you want BMS to alert">
        <p className="text-xs text-muted-foreground">
          Create an alert rule on the <span className="font-medium text-foreground">Alerts</span> page instead — e.g. metric{" "}
          <code className="font-mono">temperature</code>, operator GreaterThan, threshold 45. That alerts in BMS, notifies, and shows on the dashboard.
        </p>
      </Section>
    </div>
  );
}
