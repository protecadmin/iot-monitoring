import type { TwinTemplate } from "../../types";
import { CompressorIllustration } from "./CompressorIllustration";

export const compressorTemplate: TwinTemplate = {
  id: "compressor",
  name: "Air Compressor",
  description: "Receiver tank with discharge pipe and an electric motor.",
  profile: "vibration",
  assetLabel: "Air Compressor",
  Illustration: CompressorIllustration,
  viewBox: { width: 520, height: 320 },
  mountPoints: [
    { id: "motor-nde", label: "Motor – non-drive end", x: 472, y: 150 },
    { id: "motor-de", label: "Motor – drive end", x: 356, y: 150 },
    { id: "tank", label: "Receiver tank", x: 180, y: 138 },
  ],
};
