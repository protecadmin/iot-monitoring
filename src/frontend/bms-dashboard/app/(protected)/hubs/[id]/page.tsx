"use client";
import Link from "next/link";
import { useParams } from "next/navigation";
import { HubConsole } from "@/components/hubs/HubConsole";
export default function HubDetailPage() {
  const { id } = useParams<{ id: string }>();
  return <main className="mqtt-console"><Link className="mqtt-back" href="/hubs">← PRO-TEC HUBs</Link><HubConsole key={id} id={id} /></main>;
}
