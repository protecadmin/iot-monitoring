"use client";

import { useState, type FormEvent } from "react";
import Image from "next/image";
import { ArrowRight, ArrowUpRight, Eye, EyeOff, Loader2, LockKeyhole, Mail } from "lucide-react";
import { useAuth } from "@/lib/auth/AuthContext";
import { ApiError } from "@/lib/api/client";

const fieldClass = "h-12 w-full rounded-xl border border-input bg-card pl-11 pr-4 text-sm text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring/20 focus:border-ring disabled:opacity-60";

export default function LoginPage() {
  const { login } = useAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (submitting) return;
    setError(null);
    setSubmitting(true);
    try {
      await login(email.trim(), password);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Unable to sign in. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="login-page grid min-h-svh flex-1 bg-background lg:grid-cols-[1.05fr_1fr]">
      <aside className="relative m-4 mr-0 hidden overflow-hidden rounded-3xl bg-[#13251d] lg:block" aria-label="Pro-Tec Geosystems">
        <Image src="/images/protec-monitoring-site.png" alt="Solar-powered monitoring equipment at a construction site" fill sizes="53vw" preload className="object-cover object-[58%_center]" />
        <div className="absolute inset-0 bg-linear-to-t from-[#09170f] via-[#0b1b14]/20 to-[#0b1b14]/35" />
        <div className="relative flex h-full min-h-[calc(100svh-2rem)] flex-col justify-between p-8 xl:p-12">
          <a href="https://protecgeosystems.com.sg/" target="_blank" rel="noopener noreferrer" aria-label="Visit Pro-Tec Geosystems website" className="w-fit rounded-xl bg-white p-3 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-orange-300">
            <Image src="/logo/protecgeosystems-logo.webp" alt="Pro-Tec Geosystems" width={1533} height={931} className="h-auto w-28 xl:w-32" />
          </a>
          <div className="max-w-lg pt-40 text-[#fbf3ea]">
            <div className="mb-6 h-1 w-12 rounded-full bg-accent" />
            <p className="mb-4 text-xs font-medium uppercase tracking-[0.2em] text-white/80">Smart Monitoring System</p>
            <h2 className="text-4xl font-semibold leading-[1.12] tracking-tight xl:text-6xl">Safer sites.<br />Smarter decisions.</h2>
            <div className="mt-10 flex items-center justify-between gap-4 border-t border-white/20 pt-5 text-xs text-white/75">
              <span>Pro-Tec Geosystems Pte Ltd</span>
              <span>Singapore</span>
            </div>
          </div>
        </div>
      </aside>

      <section className="flex min-h-svh min-w-0 flex-col px-6 py-6 sm:px-12 xl:px-20">
        <header className="flex min-h-12 items-center justify-between gap-4">
          <div className="rounded-lg bg-white p-2 lg:hidden">
            <Image src="/logo/protecgeosystems-logo.webp" alt="Pro-Tec Geosystems" width={1533} height={931} className="h-auto w-20" />
          </div>
          <span className="hidden text-xs font-medium tracking-wide text-muted-foreground lg:block">CLIENT PORTAL</span>
          <div className="flex items-center gap-3">
            <a href="https://protecgeosystems.com.sg/" target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-ring">Visit Protec <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" /></a>
          </div>
        </header>

        <div className="mx-auto flex w-full max-w-[400px] flex-1 flex-col justify-center py-12 sm:py-16">
          <div className="mb-9">
            <p className="mb-4 flex items-center gap-2 text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground"><span className="h-1.5 w-1.5 rounded-full bg-orange-400" aria-hidden="true" />Smart Monitoring System</p>
            <h1 className="text-3xl font-semibold tracking-tight text-foreground sm:text-4xl">Welcome back.</h1>
            <p className="mt-3 text-sm leading-6 text-muted-foreground">Sign in to your account to continue.</p>
          </div>

          <form onSubmit={handleSubmit} className="space-y-6" aria-busy={submitting}>
            <div>
              <label htmlFor="login-email" className="mb-2 block text-sm font-medium text-foreground">Email address</label>
              <div className="relative">
                <Mail className="pointer-events-none absolute left-4 top-4 h-4 w-4 text-muted-foreground" aria-hidden="true" />
                <input id="login-email" name="email" type="email" autoComplete="username" inputMode="email" autoCapitalize="none" spellCheck={false} required disabled={submitting} value={email} onChange={(e) => setEmail(e.target.value)} placeholder="you@company.com" className={fieldClass} aria-describedby={error ? "login-error" : undefined} />
              </div>
            </div>
            <div>
              <label htmlFor="login-password" className="mb-2 block text-sm font-medium text-foreground">Password</label>
              <div className="relative">
                <LockKeyhole className="pointer-events-none absolute left-4 top-4 h-4 w-4 text-muted-foreground" aria-hidden="true" />
                <input id="login-password" name="password" type={showPassword ? "text" : "password"} autoComplete="current-password" required disabled={submitting} value={password} onChange={(e) => setPassword(e.target.value)} placeholder="Enter your password" className={`${fieldClass} pr-12`} aria-describedby={error ? "login-error" : undefined} />
                <button type="button" onClick={() => setShowPassword((shown) => !shown)} aria-label={showPassword ? "Hide password" : "Show password"} aria-pressed={showPassword} className="absolute right-2 top-2 rounded-md p-2 text-muted-foreground hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring">
                  {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                </button>
              </div>
            </div>

            {error && <div id="login-error" role="alert" className="rounded-xl border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">{error}</div>}

            <button type="submit" disabled={submitting} className="flex h-12 w-full items-center justify-center gap-3 rounded-xl bg-accent px-5 text-sm font-semibold text-accent-foreground shadow-none transition-colors hover:bg-accent/90 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-accent disabled:cursor-wait disabled:opacity-60">
              {submitting ? <><Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />Signing in...</> : <>Sign in<ArrowRight className="h-4 w-4" aria-hidden="true" /></>}
            </button>
          </form>

          <p className="mt-7 text-center text-xs leading-6 text-muted-foreground">Need access? Contact your administrator.</p>
        </div>

        <footer className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-5 text-[11px] text-muted-foreground">
          <span>© {new Date().getFullYear()} Pro-Tec Geosystems Pte Ltd</span>
          <a href="mailto:sales@protecgeosystems.com.sg" className="inline-flex items-center gap-1 hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-ring">Contact Protec<ArrowUpRight className="h-3 w-3" aria-hidden="true" /></a>
        </footer>
      </section>
    </main>
  );
}
