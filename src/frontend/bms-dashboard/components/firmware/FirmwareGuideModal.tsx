"use client";

import { useState } from "react";
import { Modal } from "@/components/ui/Modal";
import { CodeBlock } from "@/components/ui/CodeBlock";
import { StatusBadge } from "@/components/ui/States";
import type { FirmwareDto } from "@/types/api";

const TABS = ["How it works", "MQTT messages", "Device checklist", "Statuses"] as const;
type Tab = (typeof TABS)[number];

const SAMPLE_DEPLOYMENT_ID = "7c1e4b2a-9d3f-4e8a-b6c1-2f5d8e9a0b13";
const PREFIX = "iot/{tenant}/{site}/{deviceCode}";

function Step({ n, title, children }: { n: number; title: string; children: React.ReactNode }) {
  return (
    <div className="flex gap-3">
      <div className="shrink-0 h-6 w-6 rounded-full bg-primary/10 text-primary text-xs font-semibold flex items-center justify-center">{n}</div>
      <div className="flex-1 min-w-0 space-y-1 pb-1">
        <div className="text-sm font-medium text-foreground">{title}</div>
        <div className="text-sm text-muted-foreground">{children}</div>
      </div>
    </div>
  );
}

/** Firmware over the air end to end: the flow, the exact MQTT messages, and what the device firmware must implement. */
export function FirmwareGuideModal({ release, onClose }: { release?: FirmwareDto; onClose: () => void }) {
  const [tab, setTab] = useState<Tab>("How it works");

  const version = release?.version ?? "1.2.0";
  const firmwareMessage = JSON.stringify(
    {
      deploymentId: SAMPLE_DEPLOYMENT_ID,
      firmwareId: release?.id ?? "a3f29c1d-…",
      version,
      downloadUrl: `https://bms.example.com/api/firmware/${release?.id ?? "a3f29c1d-…"}/download?deployment=${SAMPLE_DEPLOYMENT_ID}&expires=…&signature=…`,
      fileName: release?.fileName ?? `firmware-${version}.bin`,
      sha256Checksum: release?.sha256Checksum || "9b8e…(64 hex characters)",
      sizeBytes: release?.sizeBytes ?? 524288,
    },
    null,
    2,
  );
  const event = (status: string, extra: Record<string, string> = {}) =>
    JSON.stringify({ eventType: "fota", deploymentId: SAMPLE_DEPLOYMENT_ID, status, ...extra });

  return (
    <Modal title={`Firmware guide${release ? ` — v${release.version}` : ""}`} onClose={onClose} size="xl">
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

        {tab === "How it works" && (
          <div className="space-y-4">
            <p className="text-sm text-muted-foreground">
              The instruction travels over <span className="font-medium text-foreground">MQTT</span>; the firmware file itself is downloaded over{" "}
              <span className="font-medium text-foreground">HTTPS</span> from a signed link, because MQTT isn&apos;t suited to large files.
            </p>
            <div className="space-y-3">
              <Step n={1} title="Upload a release">
                Choose the device type, version and file. BMS stores the file on its server and calculates the real SHA-256 checksum.
              </Step>
              <Step n={2} title="Deploy to devices">
                Only devices of that type can be picked. Each device gets its own deployment, tracked separately.
              </Step>
              <Step n={3} title="BMS sends the instruction">
                Online devices get it immediately on <code className="font-mono text-xs">…/firmware</code>. Others show <em>Waiting for device</em> and are sent it
                automatically when they come back Online (for up to 72 hours).
              </Step>
              <Step n={4} title="The device downloads and verifies">
                It downloads the file from the link in the message (valid for 60 minutes) and checks the SHA-256 against the message. A mismatch means the file is
                corrupt or tampered with — it must not install it.
              </Step>
              <Step n={5} title="The device installs and reports">
                It reports each stage on <code className="font-mono text-xs">…/events</code>: Downloading → Verifying → Installing → Rebooting → Success. The dashboard
                updates live, and on Success the device&apos;s firmware version is updated.
              </Step>
              <Step n={6} title="If it goes wrong">
                A reported failure shows <em>Failed</em> with the device&apos;s reason; no Success within 10 minutes of sending shows <em>Timeout</em>. Either can be retried,
                which sends a fresh download link.
              </Step>
            </div>
          </div>
        )}

        {tab === "MQTT messages" && (
          <div className="space-y-5">
            <section className="space-y-2">
              <h3 className="text-sm font-medium text-foreground">1. BMS → device: install this firmware</h3>
              <CodeBlock label="Topic" value={`${PREFIX}/firmware`} />
              <CodeBlock label="Message" value={firmwareMessage} />
              <p className="text-xs text-muted-foreground">
                <code className="font-mono">deploymentId</code> works like a command&apos;s commandId — copy it into every progress report.{" "}
                <code className="font-mono">downloadUrl</code> needs no login; it only works for this deployment and expires.
              </p>
            </section>
            <section className="space-y-2">
              <h3 className="text-sm font-medium text-foreground">2. Device → BMS: progress, one message per stage</h3>
              <CodeBlock label="Topic" value={`${PREFIX}/events`} />
              <CodeBlock
                label="Messages, in order"
                value={[
                  event("DOWNLOADING"),
                  event("VERIFYING"),
                  event("INSTALLING"),
                  event("REBOOTING"),
                  event("SUCCESS", { firmwareVersion: version }),
                ].join("\n")}
              />
              <CodeBlock label="Or, if a stage fails" value={event("FAILED", { error: "Checksum verification failed" })} />
            </section>
            <section className="space-y-2">
              <h3 className="text-sm font-medium text-foreground">3. Device → BMS server: download the file</h3>
              <CodeBlock value={`GET <downloadUrl from the message>\n→ 200 OK, the firmware binary\n→ 403 if the link is invalid or expired (report FAILED; a retry sends a new link)`} />
            </section>
          </div>
        )}

        {tab === "Device checklist" && (
          <div className="space-y-3">
            <p className="text-sm text-muted-foreground">What the device firmware team must implement:</p>
            <ol className="list-decimal pl-5 space-y-2 text-sm text-muted-foreground">
              <li>
                Subscribe to <code className="font-mono text-xs">{PREFIX}/firmware</code> at QoS 1 after every connect.
              </li>
              <li>
                On a message, keep <code className="font-mono text-xs">deploymentId</code> and publish <code className="font-mono text-xs">DOWNLOADING</code>.
              </li>
              <li>
                Download <code className="font-mono text-xs">downloadUrl</code> over HTTP(S). On failure publish <code className="font-mono text-xs">FAILED</code> with the reason.
              </li>
              <li>
                Publish <code className="font-mono text-xs">VERIFYING</code>, compute the file&apos;s SHA-256 and compare it with{" "}
                <code className="font-mono text-xs">sha256Checksum</code>. If they differ, publish <code className="font-mono text-xs">FAILED</code> and do not install.
              </li>
              <li>
                Publish <code className="font-mono text-xs">INSTALLING</code>, write the firmware, then <code className="font-mono text-xs">REBOOTING</code> just before restarting.
              </li>
              <li>
                After the reboot, reconnect and publish <code className="font-mono text-xs">SUCCESS</code> with the new{" "}
                <code className="font-mono text-xs">firmwareVersion</code> — remember the deploymentId across the reboot. If the new firmware fails to start, roll
                back and publish <code className="font-mono text-xs">FAILED</code>.
              </li>
              <li>Finish within 10 minutes of receiving the message, or the deployment is marked Timeout.</li>
            </ol>
          </div>
        )}

        {tab === "Statuses" && (
          <div className="border border-border rounded-md overflow-x-auto">
            <table className="w-full text-xs">
              <tbody className="divide-y divide-border">
                {[
                  ["WaitingForDevice", "Device wasn't Online when deployed; sent automatically when it comes back (gives up after 72 h)"],
                  ["Pending", "Sent to the device; no progress reported yet"],
                  ["Downloading", "Device is downloading the file"],
                  ["Verifying", "Device is checking the SHA-256"],
                  ["Installing", "Device is writing the new firmware"],
                  ["Rebooting", "Device is restarting into the new firmware"],
                  ["Success", "Done — the device's firmware version is updated"],
                  ["Failed", "Device reported a problem (reason shown) — can be retried"],
                  ["Timeout", "No Success within 10 min of sending, or device never came back — can be retried"],
                ].map(([status, meaning]) => (
                  <tr key={status}>
                    <td className="px-3 py-2 w-40">
                      <StatusBadge status={status} />
                    </td>
                    <td className="px-3 py-2 text-muted-foreground">{meaning}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </Modal>
  );
}
