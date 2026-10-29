"use client";

import { useState } from "react";
import { Activity, Loader2 } from "lucide-react";
import { useAuth } from "@/lib/auth/AuthContext";
import { ApiError } from "@/lib/api/client";

export default function LoginPage() {
  const { login } = useAuth();
  const [email, setEmail] = useState("superadmin@bms.local");
  const [password, setPassword] = useState("Passw0rd!");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await login(email, password);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Login failed");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="flex-1 flex items-center justify-center bg-background px-4">
      <div className="w-full max-w-sm">
        <div className="flex items-center gap-2 justify-center mb-6">
          <div className="h-9 w-9 rounded-lg bg-primary text-primary-foreground flex items-center justify-center">
            <Activity className="h-5 w-5" />
          </div>
          <div>
            <div className="font-semibold text-foreground leading-tight">BMS Platform</div>
            <div className="text-xs text-muted-foreground leading-tight">Industrial IoT Monitoring</div>
          </div>
        </div>

        <div className="bg-card border border-border rounded-lg p-8 shadow-sm">
          <h1 className="text-base font-semibold text-foreground">Sign in</h1>
          <p className="text-sm text-muted-foreground mb-6">Enter your credentials to access the platform.</p>

          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-foreground mb-1">Email</label>
              <input
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className="w-full rounded-md border border-input bg-background text-foreground px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-foreground mb-1">Password</label>
              <input
                type="password"
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="w-full rounded-md border border-input bg-background text-foreground px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
              />
            </div>

            {error && <div className="text-sm text-destructive bg-destructive/5 border border-destructive/20 rounded-md px-3 py-2">{error}</div>}

            <button
              type="submit"
              disabled={submitting}
              className="w-full flex items-center justify-center gap-2 rounded-md bg-primary text-primary-foreground text-sm font-medium py-2 hover:bg-primary/90 disabled:opacity-50"
            >
              {submitting && <Loader2 className="h-4 w-4 animate-spin" />}
              {submitting ? "Signing in..." : "Sign in"}
            </button>
          </form>
        </div>

        <p className="mt-6 text-xs text-muted-foreground text-center">
          Seeded accounts (dev): superadmin@bms.local, admin@whirlpool.example, admin@abc-manufacturing.example, viewer@demo-company.example — password
          Passw0rd!
        </p>
      </div>
    </div>
  );
}
