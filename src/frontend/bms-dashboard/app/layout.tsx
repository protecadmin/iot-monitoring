import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { AuthProvider } from "@/lib/auth/AuthContext";
import { TenantScopeProvider } from "@/lib/tenant/TenantScopeContext";
import { ThemeProvider } from "@/lib/theme/ThemeContext";
import "./globals.css";

// Runs before React hydrates so the page never flashes light-then-dark for
// a user who previously chose dark mode — ThemeProvider's effect alone
// would only apply the class after the first paint.
const NO_FLASH_THEME_SCRIPT = `
try {
  if (localStorage.getItem("bms.theme") === "dark") document.documentElement.classList.add("dark");
} catch (e) {}
`;

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "BMS Industrial IoT Platform",
  description: "Industrial IoT / BMS monitoring dashboard",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    // suppressHydrationWarning: the no-flash script above mutates this
    // element's class (adding "dark") before React hydrates, which is
    // exactly what it's for — React would otherwise flag that as a
    // mismatch against the server-rendered className, even though it's a
    // deliberate, expected difference (see NO_FLASH_THEME_SCRIPT).
    <html lang="en" className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`} suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: NO_FLASH_THEME_SCRIPT }} />
      </head>
      <body className="min-h-full flex flex-col">
        <ThemeProvider>
          <AuthProvider>
            <TenantScopeProvider>{children}</TenantScopeProvider>
          </AuthProvider>
        </ThemeProvider>
      </body>
    </html>
  );
}
