"use client";

import { useEffect, useState } from "react";
import { MapContainer, TileLayer, Marker, Popup, useMap } from "react-leaflet";
import { divIcon } from "leaflet";
import type { SiteDto } from "@/types/api";
import "leaflet/dist/leaflet.css";

const pin = divIcon({ className: "", html: '<span style="display:block;width:22px;height:22px;background:#07523a;border:3px solid white;border-radius:50%;box-shadow:0 2px 8px #0005"></span>', iconSize: [22, 22], iconAnchor: [11, 11] });

function MapLayout({ sites }: { sites: SiteDto[] }) {
  const map = useMap();
  useEffect(() => {
    if (sites.length === 1) map.setView([sites[0].latitude!, sites[0].longitude!], 15);
    else if (sites.length > 1) map.fitBounds(sites.map(s => [s.latitude!, s.longitude!] as [number, number]), { padding: [35, 35], maxZoom: 16 });
  }, [map, sites]);
  useEffect(() => {
    const observer = new ResizeObserver(() => map.invalidateSize({ pan: false }));
    observer.observe(map.getContainer());
    return () => observer.disconnect();
  }, [map]);
  return null;
}

export default function SiteMap({ sites, onEdit }: { sites: SiteDto[]; onEdit?: (site: SiteDto) => void }) {
  const [tileError, setTileError] = useState(false);
  return <>
    {tileError && <p role="status" className="px-4 py-2 text-sm text-amber-800 bg-amber-50">Map tiles could not be loaded. Site records remain available below.</p>}
    <div className="h-[280px] sm:h-[320px] lg:h-[400px] relative z-0">
      <MapContainer center={[sites[0].latitude!, sites[0].longitude!]} zoom={15} style={{ height: "100%", width: "100%" }}>
        <TileLayer attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors' url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" eventHandlers={{ tileerror: () => setTileError(true) }} />
        <MapLayout sites={sites} />
        {sites.map(site => <Marker key={site.id} position={[site.latitude!, site.longitude!]} icon={pin} title={site.name}>
          <Popup><div className="space-y-2 min-w-40"><strong>{site.name}</strong><div>Code: {site.code}</div><div>{site.address || "—"}</div><div>{site.latitude}, {site.longitude}</div>{onEdit && <button type="button" onClick={() => onEdit(site)} className="text-sm underline text-green-800">Edit site</button>}</div></Popup>
        </Marker>)}
      </MapContainer>
    </div>
  </>;
}
