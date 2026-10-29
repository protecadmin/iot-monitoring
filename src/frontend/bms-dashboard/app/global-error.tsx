"use client";

import { useEffect } from "react";

/**
 * Last resort: the root layout itself failed, so neither the app's styles nor
 * its theme are available — this page carries its own, following the OS
 * light/dark setting.
 */
export default function GlobalError({ error, retry }: { error: Error & { digest?: string }; retry: () => void }) {
  useEffect(() => {
    console.error(error);
  }, [error]);
  return (
    <html lang="en">
      <body>
        <title>Something went wrong · BMS</title>
        <style>{`
          body { margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center;
                 font: 14px system-ui, -apple-system, sans-serif; background: #f8fafc; color: #0f172a; }
          .box { max-width: 420px; padding: 24px; text-align: center; }
          h1 { font-size: 18px; margin: 0 0 8px; }
          p { color: #64748b; margin: 0 0 16px; line-height: 1.5; }
          button, a { font: inherit; padding: 6px 12px; border-radius: 6px; cursor: pointer; text-decoration: none; }
          button { background: #2563eb; color: #fff; border: 0; margin-right: 8px; }
          a { border: 1px solid #cbd5e1; color: inherit; }
          small { display: block; margin-top: 16px; color: #94a3b8; font-size: 11px; }
          @media (prefers-color-scheme: dark) {
            body { background: #0b1220; color: #e2e8f0; } p { color: #94a3b8; } a { border-color: #334155; }
          }
        `}</style>
        <div className="box">
          <h1>Something went wrong</h1>
          <p>BMS couldn&apos;t load. It&apos;s usually temporary — try again, and if it keeps happening, contact your administrator.</p>
          <button onClick={() => retry()}>Try again</button>
          {/* A full reload, not client navigation: the app's own router is what failed. */}
          <a href="/dashboard">Go to dashboard</a>
          {error.digest && <small>Reference: {error.digest}</small>}
        </div>
      </body>
    </html>
  );
}
