import type { ApiFieldErrors } from "../types/api";

// Android emulator: host loopback is 10.0.2.2. Real device / `adb reverse tcp:5130 tcp:5130`:
// localhost works too. Override at build time if pointing at a deployed API instead.
export const API_BASE_URL = process.env.EXPO_PUBLIC_API_BASE_URL ?? "http://10.0.2.2:5130";

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public fieldErrors?: Record<string, string[]>,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

type RequestOptions = {
  method?: "GET" | "POST";
  body?: unknown;
  token?: string | null;
  query?: Record<string, string | number | boolean | undefined>;
};

function buildUrl(path: string, query?: RequestOptions["query"]): string {
  const url = new URL(path, API_BASE_URL);
  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null) {
        url.searchParams.set(key, String(value));
      }
    }
  }
  return url.toString();
}

async function parseErrorBody(response: Response): Promise<{ message: string; fieldErrors?: Record<string, string[]> }> {
  try {
    const data = (await response.json()) as ApiFieldErrors & { message?: string; title?: string };
    if (data.errors) {
      return { message: data.detail ?? "Validation failed.", fieldErrors: data.errors };
    }
    return { message: data.message ?? data.title ?? `Request failed (${response.status}).` };
  } catch {
    return { message: `Request failed (${response.status}).` };
  }
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json" };
  if (options.token) {
    headers.Authorization = `Bearer ${options.token}`;
  }

  let body: BodyInit | undefined;
  if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
    body = JSON.stringify(options.body);
  }

  const response = await fetch(buildUrl(path, options.query), {
    method: options.method ?? "GET",
    headers,
    body,
  });

  if (!response.ok) {
    const { message, fieldErrors } = await parseErrorBody(response);
    throw new ApiError(response.status, message, fieldErrors);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

/** For endpoints that return a file (Excel download) rather than JSON. Returns raw bytes rather
 * than a Blob so callers can hand them straight to expo-file-system's `File.write()` (SDK 58's
 * File/Directory API), which takes a Uint8Array/string, not a Blob. */
export async function apiDownload(path: string, token: string | null): Promise<{ bytes: Uint8Array; fileName: string }> {
  const response = await fetch(buildUrl(path), {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });

  if (!response.ok) {
    const { message } = await parseErrorBody(response);
    throw new ApiError(response.status, message);
  }

  const disposition = response.headers.get("content-disposition") ?? "";
  const match = /filename="?([^";]+)"?/i.exec(disposition);
  const fileName = match?.[1] ?? "download.xlsx";

  const buffer = await response.arrayBuffer();
  return { bytes: new Uint8Array(buffer), fileName };
}

export async function apiUpload<T>(path: string, form: FormData, token: string | null): Promise<T> {
  const response = await fetch(buildUrl(path), {
    method: "POST",
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body: form,
  });

  if (!response.ok) {
    const { message, fieldErrors } = await parseErrorBody(response);
    throw new ApiError(response.status, message, fieldErrors);
  }

  return (await response.json()) as T;
}
