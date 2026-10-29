"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { getRefreshToken, setToken, setRefreshToken } from "@/lib/api/client";
import { AuthApi } from "@/lib/api/resources";
import type { LoginResponse, UserDto } from "@/types/api";

interface AuthContextValue {
  user: UserDto | null;
  accessToken: string | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  /** Changes the signed-in user's password and keeps this session on the new tokens. */
  changePassword: (currentPassword: string, newPassword: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserDto | null>(null);
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const router = useRouter();

  useEffect(() => {
    const storedToken = window.localStorage.getItem("bms.accessToken");
    const storedUser = window.localStorage.getItem("bms.user");
    if (storedToken && storedUser) {
      setAccessToken(storedToken);
      setUser(JSON.parse(storedUser));
    }
    setLoading(false);
  }, []);

  function startSession(response: LoginResponse) {
    setToken(response.accessToken);
    setRefreshToken(response.refreshToken);
    window.localStorage.setItem("bms.user", JSON.stringify(response.user));
    setAccessToken(response.accessToken);
    setUser(response.user);
  }

  async function login(email: string, password: string) {
    startSession(await AuthApi.login(email, password));
    router.push("/dashboard");
  }

  async function changePassword(currentPassword: string, newPassword: string) {
    startSession(await AuthApi.changePassword(currentPassword, newPassword));
  }

  function logout() {
    // Fire-and-forget: revokes the refresh token server-side so it can't
    // be silently redeemed after this point — logout previously only ever
    // forgot the token client-side while it stayed valid until it expired
    // on its own. Not awaited: local state should clear immediately
    // regardless of whether this network call succeeds.
    AuthApi.logout(getRefreshToken()).catch(() => {});
    setToken(null);
    setRefreshToken(null);
    window.localStorage.removeItem("bms.user");
    setAccessToken(null);
    setUser(null);
    router.push("/login");
  }

  return <AuthContext.Provider value={{ user, accessToken, loading, login, changePassword, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
