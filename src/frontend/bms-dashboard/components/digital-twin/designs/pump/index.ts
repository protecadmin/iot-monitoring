import type { TwinTemplate } from "../../types";
import { PumpIllustration } from "./PumpIllustration";

export const pumpTemplate: TwinTemplate = {
  id: "pump",
  name: "Centrifugal Pump",
  description: "End-suction pump with motor, coupling and bearing housing.",
  profile: "vibration",
  assetLabel: "Centrifugal Pump",
  Illustration: PumpIllustration,
  viewBox: { width: 520, height: 320 },
  mountPoints: [
    { id: "pump-bearing", label: "Pump bearing housing", x: 284, y: 154 },
    { id: "motor-de", label: "Motor – drive end", x: 200, y: 140 },
    { id: "motor-nde", label: "Motor – non-drive end", x: 50, y: 140 },
    { id: "pump-casing", label: "Pump casing", x: 372, y: 126 },
  ],
};
