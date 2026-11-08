import { apiDownload, apiRequest, apiUpload } from "./client";
import type {
  AopLocationOptionDto,
  AssignInwardPutawayPkmRequest,
  ChangeInwardPutawayLocationRequest,
  ChangePulloutPickLocationRequest,
  ConfirmPickRequest,
  ConfirmPickResult,
  InwardDetailDto,
  InwardHistoryDto,
  InwardPalletChangeOptionDto,
  InwardResultDto,
  InwardSheetImportOutcome,
  LoginResponse,
  MaterialDto,
  PaginatedList,
  PalletPositionDto,
  PickRemainderDto,
  PickerPlaceInwardPutawayRequest,
  PickerPlaceInwardPutawayResult,
  PulloutDetailDto,
  PulloutHistoryDto,
  PulloutPickChangeOptionDto,
  PulloutResultDto,
  RelocationSuggestionDto,
  SubmitInwardWithoutPkmRequest,
  WarehouseOption,
} from "../types/api";

export function login(email: string, password: string, coords?: { latitude: number; longitude: number }): Promise<LoginResponse> {
  return apiRequest<LoginResponse>("/api/auth/login", {
    method: "POST",
    body: { email, password, latitude: coords?.latitude, longitude: coords?.longitude },
  });
}

export function getWarehouses(token: string): Promise<WarehouseOption[]> {
  return apiRequest<WarehouseOption[]>("/api/warehouses", { token });
}

export function searchMaterials(token: string, search: string, pageNumber = 1, pageSize = 15): Promise<PaginatedList<MaterialDto>> {
  return apiRequest<PaginatedList<MaterialDto>>("/api/materials", {
    token,
    query: { search, pageNumber, pageSize },
  });
}

export function submitInwardWithoutPkm(token: string, request: SubmitInwardWithoutPkmRequest): Promise<InwardResultDto> {
  return apiRequest<InwardResultDto>("/api/inward-no-pkm/submit", { token, method: "POST", body: request });
}

export function importInwardWorkbook(token: string, fileUri: string, fileName: string, warehouseId: number): Promise<InwardSheetImportOutcome[]> {
  const form = new FormData();
  // React Native's FormData accepts this { uri, name, type } shape for file fields - standard
  // Expo pattern for uploading a document-picker result.
  form.append("file", {
    uri: fileUri,
    name: fileName,
    type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
  } as unknown as Blob);
  form.append("warehouseId", String(warehouseId));
  return apiUpload<InwardSheetImportOutcome[]>("/api/inward-no-pkm/import", form, token);
}

export function getSampleTemplate(token: string) {
  return apiDownload("/api/inward-no-pkm/sample-template", token);
}

export function getInwardHistory(
  token: string,
  options: { search?: string; warehouseId?: number; pageNumber?: number; pageSize?: number } = {},
): Promise<PaginatedList<InwardHistoryDto>> {
  return apiRequest<PaginatedList<InwardHistoryDto>>("/api/inward-no-pkm/history", {
    token,
    query: {
      search: options.search,
      warehouseId: options.warehouseId,
      pageNumber: options.pageNumber ?? 1,
      pageSize: options.pageSize ?? 20,
    },
  });
}

export function getInwardDetail(token: string, id: number): Promise<InwardDetailDto> {
  return apiRequest<InwardDetailDto>(`/api/inward-no-pkm/${id}`, { token });
}

export function downloadInwardDetail(token: string, id: number) {
  return apiDownload(`/api/inward-no-pkm/${id}/download`, token);
}

export function assignInwardPutawayPkm(token: string, request: AssignInwardPutawayPkmRequest): Promise<InwardResultDto> {
  return apiRequest<InwardResultDto>("/api/inward-no-pkm/assign-pkm", { token, method: "POST", body: request });
}

export function getPalletPosition(token: string, locationCode: string): Promise<PalletPositionDto> {
  return apiRequest<PalletPositionDto>(`/api/racks/pallet-position/${encodeURIComponent(locationCode)}`, { token });
}

// --- Floor Supervisor: Inward putaway edit/confirm ---

export function getInwardPutawayLocationOptions(token: string, putawayId: number): Promise<InwardPalletChangeOptionDto[]> {
  return apiRequest<InwardPalletChangeOptionDto[]>(`/api/inward-no-pkm/putaways/${putawayId}/location-options`, { token });
}

export function changeInwardPutawayLocation(token: string, putawayId: number, request: ChangeInwardPutawayLocationRequest): Promise<InwardResultDto> {
  return apiRequest<InwardResultDto>(`/api/inward-no-pkm/putaways/${putawayId}/change-location`, { token, method: "POST", body: request });
}

export function confirmInwardPutaway(token: string, referenceNumber: string): Promise<InwardResultDto> {
  return apiRequest<InwardResultDto>(`/api/inward-no-pkm/${encodeURIComponent(referenceNumber)}/confirm`, { token, method: "POST" });
}

// --- Floor Supervisor: Pullout (outward) queue, edit pick location, confirm ---

export function getPulloutQueue(
  token: string,
  options: { search?: string; warehouseId?: number; pageNumber?: number; pageSize?: number } = {},
): Promise<PaginatedList<PulloutHistoryDto>> {
  return apiRequest<PaginatedList<PulloutHistoryDto>>("/api/pullout/queue", {
    token,
    query: {
      search: options.search,
      warehouseId: options.warehouseId,
      pageNumber: options.pageNumber ?? 1,
      pageSize: options.pageSize ?? 20,
    },
  });
}

export function getPulloutDetail(token: string, id: number): Promise<PulloutDetailDto> {
  return apiRequest<PulloutDetailDto>(`/api/pullout/${id}`, { token });
}

export function getPulloutPickOptions(
  token: string,
  params: { referenceNumber: string; materialNumber: number; currentLocationCode: string; mfgMonthLabel: string },
): Promise<PulloutPickChangeOptionDto[]> {
  return apiRequest<PulloutPickChangeOptionDto[]>("/api/pullout/picker/pick-options", { token, query: params });
}

export function changePulloutPickLocation(token: string, request: ChangePulloutPickLocationRequest): Promise<PulloutResultDto> {
  return apiRequest<PulloutResultDto>("/api/pullout/picker/change-pick-location", { token, method: "POST", body: request });
}

/** The boxes left at a pick's location now, so "move the remaining X" can be offered before the pick is confirmed. */
export function getPickRemainder(token: string, pickId: number): Promise<PickRemainderDto> {
  return apiRequest<PickRemainderDto>(`/api/pullout/picks/${pickId}/remainder`, { token });
}

export function getRelocationSuggestions(token: string, pickId: number, quantity: number): Promise<RelocationSuggestionDto[]> {
  return apiRequest<RelocationSuggestionDto[]>(`/api/pullout/picks/${pickId}/relocation-suggestions`, { token, query: { quantity } });
}

/** Confirms one pick of a posted pullout; with moveRemaining the boxes left at that location move in the same transaction. */
export function confirmPulloutPick(token: string, pickId: number, request: ConfirmPickRequest): Promise<ConfirmPickResult> {
  return apiRequest<ConfirmPickResult>(`/api/pullout/picks/${pickId}/confirm`, { token, method: "POST", body: request });
}

export function confirmPullout(token: string, referenceNumber: string): Promise<PulloutResultDto> {
  return apiRequest<PulloutResultDto>(`/api/pullout/${encodeURIComponent(referenceNumber)}/confirm`, { token, method: "POST" });
}

// --- Picker Put-away: confirm/change qty, PKM and location for pending GRN locations ---

export function getPickerPutawayQueue(
  token: string,
  options: { search?: string; pageNumber?: number; pageSize?: number } = {},
): Promise<PaginatedList<InwardHistoryDto>> {
  return apiRequest<PaginatedList<InwardHistoryDto>>("/api/picker-putaway/queue", {
    token,
    query: {
      search: options.search,
      pageNumber: options.pageNumber ?? 1,
      pageSize: options.pageSize ?? 10,
    },
  });
}

export function getPickerLocationOptions(token: string): Promise<AopLocationOptionDto[]> {
  return apiRequest<AopLocationOptionDto[]>("/api/picker-putaway/location-options", { token });
}

/** The picker's final Confirm of a GRN posted on upload; locks its locations against further picker changes. */
export function confirmPickerPutaway(token: string, transactionId: number): Promise<{ referenceNumber: string; locationCount: number }> {
  return apiRequest<{ referenceNumber: string; locationCount: number }>(`/api/picker-putaway/${transactionId}/confirm`, { token, method: "POST" });
}

export function placePickerPutaway(
  token: string,
  putawayId: number,
  request: PickerPlaceInwardPutawayRequest,
): Promise<PickerPlaceInwardPutawayResult> {
  return apiRequest<PickerPlaceInwardPutawayResult>(`/api/picker-putaway/putaways/${putawayId}/place`, {
    token,
    method: "POST",
    body: request,
  });
}
