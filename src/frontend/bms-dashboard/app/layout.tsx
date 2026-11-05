import type { Metadata } from "next";
import { Inter, Geist_Mono } from "next/font/google";
import { AuthProvider } from "@/lib/auth/AuthContext";
import { TenantScopeProvider } from "@/lib/tenant/TenantScopeContext";
import "./globals.css";

const geistSans = Inter({ variable: "--font-geist-sans", subsets: ["latin"], display: "swap" });
const geistMono = Geist_Mono({ variable: "--font-geist-mono", subsets: ["latin"] });

export const metadata: Metadata = {
  title: "Pro-Tec | Monitoring",
  description: "Pro-Tec Smart Monitoring System",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`protec-theme ${geistSans.variable} ${geistMono.variable} h-full antialiased`}>
      <body className="min-h-full flex flex-col">
        <AuthProvider>
          <TenantScopeProvider>{children}</TenantScopeProvider>
        </AuthProvider>
      </body>
    </html>
  );
}
