import type { TwinTemplate } from "../../types";
import { MotorFanIllustration } from "./MotorFanIllustration";

export const motorFanTemplate: TwinTemplate = {
  id: "motor-fan",
  name: "Motor & Fan",
  description: "Centrifugal fan or blower driven by an electric motor.",
  profile: "vibration",
  assetLabel: "Centrifugal Fan",
  Illustration: MotorFanIllustration,
  viewBox: { width: 520, height: 320 },
  mountPoints: [
    { id: "fan-bearing", label: "Fan bearing", x: 241, y: 156 },
    { id: "motor-de", label: "Motor – drive end", x: 196, y: 146 },
    { id: "motor-nde", label: "Motor – non-drive end", x: 46, y: 146 },
    { id: "fan-housing", label: "Fan housing", x: 330, y: 100 },
  ],
};
