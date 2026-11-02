import { api } from "./client";
import type { TiltSummary } from "@/types/tilt";

export const TiltApi = {
  get: (deviceId: string, from?: string, to?: string) => {
    const query = new URLSearchParams();
    if (from) query.set("from", from);
    if (to) query.set("to", to);
    return api.get<TiltSummary>(`/api/devices/${deviceId}/tilt${query.size ? `?${query}` : ""}`);
  },
  configure: (deviceId: string, closingTime: string, timeZoneId: string) =>
    api.put<TiltSummary>(`/api/devices/${deviceId}/tilt`, { closingTime, timeZoneId }),
};
