import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  {
    rules: {
      // This app's data-fetching hooks (useApi, TelemetryChart, etc.) reset
      // loading/error state at the start of a fetch effect and set data in
      // .then() — a standard, correct pattern. This rule (new as of the
      // React Compiler-oriented eslint-plugin-react-hooks releases) treats
      // that as an error; downgraded to a warning rather than restructuring
      // every data-fetching hook around it for no behavioral change.
      "react-hooks/set-state-in-effect": "warn",
    },
  },
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
  ]),
]);

export default eslintConfig;
