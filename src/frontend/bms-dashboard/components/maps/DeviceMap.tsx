"use client";

import { useEffect, useMemo } from "react";
import { useRouter } from "next/navigation";
import { MapContainer, TileLayer, CircleMarker, Tooltip, useMap } from "react-leaflet";
import "leaflet/dist/leaflet.css";

export interface MapPoint {
  id: string;
  label: string;
  latitude: number;
  longitude: number;
  status: string;
}

const STATUS_COLORS: Record<string, string> = {
  Online: "#059669",
  Offline: "#94a3b8",
  Warning: "#d97706",
  Error: "#dc2626",
  Critical: "#dc2626",
  Registered: "#64748b",
  Provisioned: "#2563eb",
  Maintenance: "#7c3aed",
  Decommissioned: "#334155",
};

const LEGEND_ORDER = ["Online", "Offline", "Warning", "Error", "Registered"];

/** Fits the map view to whatever points are currently plotted, once, on mount/when the point set changes. */
function FitBounds({ points }: { points: { latitude: number; longitude: number }[] }) {
  const map = useMap();

  useEffect(() => {
    if (points.length === 0) return;
    if (points.length === 1) {
      map.setView([points[0].latitude, points[0].longitude], 11);
      return;
    }
    const bounds = points.map((p) => [p.latitude, p.longitude] as [number, number]);
    map.fitBounds(bounds, { padding: [40, 40] });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [JSON.stringify(points)]);

  return null;
}

/**
 * A real, tile-based map (OpenStreetMap via Leaflet) — device locations
 * plotted as colored circle markers, click-through to the device's detail
 * page. Devices that share a site's exact lat/long (see seed data) are
 * nudged apart by a tiny fixed offset so they don't render as one
 * indistinguishable marker at any zoom level.
 */
export function DeviceMap({
  points,
  height = 420,
  linkTo,
}: {
  points: MapPoint[];
  height?: number;
  /** Builds the click-through URL for a point's id — e.g. `(id) => `/devices/${id}``. Omit to plot non-clickable points (e.g. site markers, which have no detail page of their own). */
  linkTo?: (id: string) => string;
}) {
  const router = useRouter();
  // Range-checked, not just non-null — a single out-of-range point (e.g.
  // stray test data with latitude: 2323) breaks the map for every OTHER
  // point too: FitBounds computes one bounding box containing every plotted
  // point at once, so one bogus coordinate stretches that box out to
  // somewhere absurd and drags every legitimately-placed marker's visible
  // position along with it, even though their own lat/lng are still correct.
  const valid = useMemo(
    () =>
      points.filter(
        (p) => p.latitude != null && p.longitude != null && p.latitude >= -90 && p.latitude <= 90 && p.longitude >= -180 && p.longitude <= 180,
      ),
    [points],
  );

  const spread = useMemo(() => {
    const groups = new Map<string, MapPoint[]>();
    for (const p of valid) {
      const key = `${p.latitude.toFixed(4)},${p.longitude.toFixed(4)}`;
      const group = groups.get(key);
      if (group) group.push(p);
      else groups.set(key, [p]);
    }

    return [...groups.values()].flatMap((group) => {
      if (group.length === 1) return group.map((p) => ({ ...p, offsetLat: p.latitude, offsetLng: p.longitude }));

      const radiusDegrees = 0.01; // a small, fixed nudge — just enough to separate markers visually at city zoom levels
      return group.map((p, i) => {
        const angle = (2 * Math.PI * i) / group.length;
        return {
          ...p,
          offsetLat: p.latitude + radiusDegrees * Math.sin(angle),
          offsetLng: p.longitude + radiusDegrees * Math.cos(angle),
        };
      });
    });
  }, [valid]);

  const statusesPresent = LEGEND_ORDER.filter((s) => valid.some((p) => p.status === s));

  if (valid.length === 0) {
    return <div className="text-sm text-muted-foreground py-12 text-center">No locations to show.</div>;
  }

  return (
    <div>
      <div style={{ height }} className="rounded-md overflow-hidden border border-border">
        <MapContainer center={[valid[0].latitude, valid[0].longitude]} zoom={11} style={{ height: "100%", width: "100%" }}>
          <TileLayer
            attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
            url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
          />
          <FitBounds points={valid} />
          {spread.map((p) => (
            <CircleMarker
              key={p.id}
              center={[p.offsetLat, p.offsetLng]}
              radius={9}
              pathOptions={{
                color: "#ffffff",
                weight: 2,
                fillColor: STATUS_COLORS[p.status] ?? "#64748b",
                fillOpacity: 1,
              }}
              eventHandlers={linkTo ? { click: () => router.push(linkTo(p.id)) } : {}}
            >
              <Tooltip direction="top" offset={[0, -8]} permanent={false}>
                {p.label} — {p.status}
              </Tooltip>
            </CircleMarker>
          ))}
        </MapContainer>
      </div>

      <div className="flex flex-wrap gap-x-4 gap-y-1 mt-2 text-xs text-muted-foreground">
        {statusesPresent.map((status) => (
          <span key={status} className="inline-flex items-center gap-1.5">
            <span className="inline-block w-2.5 h-2.5 rounded-full" style={{ backgroundColor: STATUS_COLORS[status] }} />
            {status}
          </span>
        ))}
      </div>
    </div>
  );
}
