import type { TwinTemplate } from "../../types";
import { NoiseIllustration } from "./NoiseIllustration";

export const noiseTemplate: TwinTemplate = {
  id: "noise",
  name: "Noise Monitoring Point",
  description: "Sound level meter on a tripod beside the machine it monitors.",
  profile: "noise",
  assetLabel: "Noise Monitoring Point",
  Illustration: NoiseIllustration,
  viewBox: { width: 520, height: 320 },
  mountPoints: [{ id: "microphone", label: "Microphone", x: 408, y: 76 }],
};
