"use client";

import { useEffect, useRef } from "react";
import * as signalR from "@microsoft/signalr";
import { useAuth } from "@/lib/auth/AuthContext";
import { getValidAccessToken } from "@/lib/api/client";

const WS_URL = process.env.NEXT_PUBLIC_WS_URL ?? "http://localhost:5000";

export type RealtimeEvent =
  | "TelemetryUpdated"
  | "DeviceStatusChanged"
  | "AlertCreated"
  | "AlertResolved"
  | "CommandStatusChanged"
  | "FirmwareDeploymentUpdated"
  | "CalculationUpdated";

/**
 * One shared SignalR connection per mounted component tree, joined to the
 * caller's own tenant group (or the SuperAdmin all-tenants group) — see
 * Bms.Api's TelemetryHub.JoinTenant. Reconnects automatically; callers
 * just register handlers and don't need to think about connection state.
 */
export function useRealtime(handlers: Partial<Record<RealtimeEvent, (payload: unknown) => void>>) {
  const { accessToken } = useAuth();
  const handlersRef = useRef(handlers);

  // Kept current on every render, in an effect rather than during render —
  // the connection effect below intentionally only depends on
  // accessToken (a reconnect must not be torn down just because the
  // caller passed a new inline handlers object), so it reads this ref
  // from inside its event callbacks instead.
  useEffect(() => {
    handlersRef.current = handlers;
  });

  useEffect(() => {
    if (!accessToken) return;

    const connection = new signalR.HubConnectionBuilder()
      // A function, not the accessToken captured above: SignalR calls it again
      // on every negotiate and automatic reconnect, and this state value is only
      // set at login — it goes stale once the 15-minute token is refreshed,
      // which turned every reconnect after that point into a 401.
      .withUrl(`${WS_URL}/hubs/realtime`, { accessTokenFactory: getValidAccessToken })
      .withAutomaticReconnect()
      // SignalR's own internal logger writes error-level entries straight
      // to console.error *before* start()'s promise rejects — our own
      // `stopped`-guarded catch below runs too late to suppress those, so
      // the noisy-but-harmless StrictMode "stopped during negotiation"
      // case would still show up as a dev console error otherwise. Setting
      // Critical here silences the library's own logging (still no worse
      // than swallowing that one case), while our explicit console.error
      // below remains the single place real failures get reported.
      .configureLogging(signalR.LogLevel.Critical)
      .build();

    const events: RealtimeEvent[] = [
      "TelemetryUpdated",
      "DeviceStatusChanged",
      "AlertCreated",
      "AlertResolved",
      "CommandStatusChanged",
      "FirmwareDeploymentUpdated",
      "CalculationUpdated",
    ];

    for (const event of events) {
      connection.on(event, (payload: unknown) => handlersRef.current[event]?.(payload));
    }

    connection.onreconnected(() => connection.invoke("JoinTenant").catch(() => {}));

    // React StrictMode intentionally mounts this effect, runs its cleanup,
    // then mounts it again (dev only) — if that cleanup's connection.stop()
    // lands while start() is still negotiating, SignalR rejects start()
    // with "The connection was stopped during negotiation." That's not a
    // real connectivity failure, just this effect's own first, discarded
    // instance being torn down; only the second (surviving) instance's
    // outcome is worth reporting.
    let stopped = false;

    connection
      .start()
      .then(() => {
        if (!stopped) return connection.invoke("JoinTenant");
      })
      .catch((err) => {
        if (!stopped) console.error("SignalR connection failed", err);
      });

    return () => {
      stopped = true;
      connection.stop().catch(() => {});
    };
  }, [accessToken]);
}
