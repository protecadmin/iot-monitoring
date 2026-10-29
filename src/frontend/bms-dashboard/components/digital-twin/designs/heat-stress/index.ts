import type { TwinTemplate } from "../../types";
import { HeatStressIllustration } from "./HeatStressIllustration";

/** WBGT = 0.7 × wet bulb + 0.2 × globe + 0.1 × dry bulb (outdoor). */
export const heatStressTemplate: TwinTemplate = {
  id: "heat-stress",
  name: "Heat Stress Station",
  description: "Globe, natural wet bulb and shielded dry bulb on a mast.",
  profile: "heat-stress",
  assetLabel: "Heat Stress Station",
  Illustration: HeatStressIllustration,
  viewBox: { width: 520, height: 320 },
  mountPoints: [{ id: "logger", label: "Logger on the mast", x: 468, y: 178 }],
};
