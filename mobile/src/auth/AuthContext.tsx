import * as SecureStore from "expo-secure-store";
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { getWarehouses, login as loginRequest } from "../api/endpoints";
import type { WarehouseOption } from "../types/api";

const TOKEN_KEY = "godrejwms.pickers.accessToken";
const USER_KEY = "godrejwms.pickers.user";

interface StoredUser {
  userId: string;
  fullName: string;
  roles: string[];
  permissions: string[];
}

interface AuthContextValue {
  isLoading: boolean;
  token: string | null;
  user: StoredUser | null;
  permissions: string[];
  warehouses: WarehouseOption[];
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

// Only Pickers (Inward.Create) need the warehouse picker today - calling it for a role that
// doesn't hold that permission would just 403. Keeping this check here (rather than only
// try/catching) means a Floor Supervisor session never fires a request it knows will be rejected.
async function loadWarehousesIfPermitted(token: string, permissions: string[]): Promise<WarehouseOption[]> {
  if (!permissions.includes("Inward.Create")) {
    return [];
  }
  try {
    return await getWarehouses(token);
  } catch {
    return [];
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isLoading, setIsLoading] = useState(true);
  const [token, setToken] = useState<string | null>(null);
  const [user, setUser] = useState<StoredUser | null>(null);
  const [warehouses, setWarehouses] = useState<WarehouseOption[]>([]);

  useEffect(() => {
    (async () => {
      const [storedToken, storedUser] = await Promise.all([
        SecureStore.getItemAsync(TOKEN_KEY),
        SecureStore.getItemAsync(USER_KEY),
      ]);
      if (storedToken && storedUser) {
        setToken(storedToken);
        const parsedUser = JSON.parse(storedUser) as StoredUser;
        setUser(parsedUser);
        setWarehouses(await loadWarehousesIfPermitted(storedToken, parsedUser.permissions ?? []));
      }
      setIsLoading(false);
    })();
  }, []);

  const login = useCallback(async (email: string, password: string) => {
    const result = await loginRequest(email, password);
    const nextUser: StoredUser = {
      userId: result.userId,
      fullName: result.fullName,
      roles: result.roles,
      permissions: result.permissions,
    };
    await SecureStore.setItemAsync(TOKEN_KEY, result.accessToken);
    await SecureStore.setItemAsync(USER_KEY, JSON.stringify(nextUser));
    setToken(result.accessToken);
    setUser(nextUser);
    setWarehouses(await loadWarehousesIfPermitted(result.accessToken, nextUser.permissions));
  }, []);

  const logout = useCallback(async () => {
    await SecureStore.deleteItemAsync(TOKEN_KEY);
    await SecureStore.deleteItemAsync(USER_KEY);
    setToken(null);
    setUser(null);
    setWarehouses([]);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({ isLoading, token, user, permissions: user?.permissions ?? [], warehouses, login, logout }),
    [isLoading, token, user, warehouses, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within AuthProvider");
  }
  return ctx;
}
