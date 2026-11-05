"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";
import { GatewaysApi } from "@/lib/api/resources";
import { useApi } from "@/hooks/useApi";
import { useRealtime } from "@/lib/websocket/useRealtime";
import { LoadingState } from "@/components/ui/States";
import { GatewayMonitoringView, type GatewayMessage, type GatewayResponse } from "@/components/gateways/GatewayMonitoringView";
import { latestGatewayStatus } from "@/lib/gateway-status";

function parsePayload(payload: string): Record<string, unknown> | null {
  try {
    const value = JSON.parse(payload) as unknown;
    return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : null;
  } catch { return null; }
}

export default function GatewayDetailPage() {
  const params = useParams<{ id: string }>();
  const gateway = useApi(() => GatewaysApi.get(params.id), [params.id]);
  const gatewayList = useApi(() => GatewaysApi.list({ page: 1, pageSize: 500 }), [params.id]);
  const [busy, setBusy] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [messages, setMessages] = useState<GatewayMessage[]>([]);
  const [responses, setResponses] = useState<GatewayResponse[]>([]);
  const [statusResponse, setStatusResponse] = useState<GatewayResponse | undefined>();

  useEffect(() => { setBusy(null); setNotice(null); setMessages([]); setResponses([]); setStatusResponse(undefined); }, [params.id]);

  useRealtime({ GatewayMessageReceived: (value) => {
    const message = value as GatewayMessage;
    if (!message || message.gatewayCode !== (gateway.data?.gatewayCode ?? "")) return;
    const data = parsePayload(message.payload);
    if (data?.type === "status") {
      setStatusResponse(previous => latestGatewayStatus([{ message, data }, ...(previous ? [previous] : [])]));
    }
    setMessages(previous => [message, ...previous].slice(0, 30));
    setResponses(previous => [{ message, data }, ...previous].slice(0, 30));
    setNotice(`Response received on ${message.topic}.`);
    setBusy(null);
  }});

  async function send(op: string, payload: Record<string, unknown> = {}) {
    if (busy !== null) return;
    setBusy(op); setNotice(null);
    try {
      await GatewaysApi.command(params.id, op, payload);
      setNotice(`${op.replaceAll("_", " ")} request sent - waiting for gateway response...`);
      window.setTimeout(() => setBusy(current => current === op ? null : current), 15000);
    } catch (e) { setBusy(null); setNotice(e instanceof Error ? e.message : "Command failed"); }
  }

  if (gateway.loading) return <LoadingState label="Loading gateway..." />;
  const listItem = gatewayList.data?.items.find(x => x.id === gateway.data?.id);
  const g = gateway.data ? { ...gateway.data, tenantName: gateway.data.tenantName || listItem?.tenantName, siteName: gateway.data.siteName || listItem?.siteName || "" } : undefined;
  if (gateway.error || !g) return <main className="p-8"><Link href="/gateways" className="text-primary hover:underline">&larr; Back to gateways</Link><p role="alert" className="mt-6 text-destructive">{gateway.error || "Gateway not found"}</p></main>;

  return <GatewayMonitoringView key={g.id} gateway={g} responses={responses} messages={messages} busy={busy} notice={notice} send={send} statusResponse={statusResponse} />;
}
