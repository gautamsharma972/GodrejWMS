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
  InwardVarianceDto,
  InwardVarianceTypeValue,
  LoginResponse,
  MaterialDto,
  PaginatedList,
  PalletPositionDto,
  PickRemainderDto,
  PickerPulloutDto,
  PulloutAddOptionDto,
  AddPulloutPickResult,
  PickerPlaceInwardPutawayRequest,
  PickerPlaceInwardPutawayResult,
  PulloutDetailDto,
  PulloutHistoryDto,
  PulloutPickChangeOptionDto,
  PulloutResultDto,
  RecordExcessMaterialResult,
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

/** Posted pullouts and legacy allocated drafts with a pick the picker has not confirmed. */
export function getPickerPulloutQueue(
  token: string,
  options: { search?: string; pageNumber?: number; pageSize?: number } = {},
): Promise<PaginatedList<PickerPulloutDto>> {
  return apiRequest<PaginatedList<PickerPulloutDto>>("/api/pullout/picker/queue", {
    token,
    query: { search: options.search, pageNumber: options.pageNumber ?? 1, pageSize: options.pageSize ?? 20 },
  });
}

/** The picker's final Confirm of a pick-list once every pick is confirmed: sets the pick reference to confirmed. */
export function confirmPulloutPicklist(token: string, pulloutId: number): Promise<{ referenceNumber: string; pickCount: number; pickedBoxes: number }> {
  return apiRequest<{ referenceNumber: string; pickCount: number; pickedBoxes: number }>(`/api/pullout/${pulloutId}/confirm-picklist`, { token, method: "POST" });
}

/** The boxes left at a pick's location now, so "move the remaining X" can be offered before the pick is confirmed. */
export function getPickRemainder(token: string, pickId: number): Promise<PickRemainderDto> {
  return apiRequest<PickRemainderDto>(`/api/pullout/picks/${pickId}/remainder`, { token });
}

export function getRelocationSuggestions(token: string, pickId: number, quantity: number): Promise<RelocationSuggestionDto[]> {
  return apiRequest<RelocationSuggestionDto[]>(`/api/pullout/picks/${pickId}/relocation-suggestions`, { token, query: { quantity } });
}

/** Confirms one persisted pick; with moveRemaining the boxes left at that location move in the same transaction. */
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

// ---- Supervisor review (Inward.Confirm)

/** Boxes of a put-away that are damaged / short / do not match the inbound. The put-away row shrinks by that many boxes. */
export function recordInwardVariance(
  token: string,
  putawayId: number,
  request: { type: InwardVarianceTypeValue; quantityBoxes: number; remarks: string },
): Promise<InwardVarianceDto> {
  return apiRequest<InwardVarianceDto>(`/api/supervisor/putaway/putaways/${putawayId}/variances`, { token, method: "POST", body: request });
}

export function removeInwardVariance(token: string, varianceId: number): Promise<void> {
  return apiRequest<void>(`/api/supervisor/putaway/variances/${varianceId}/remove`, { token, method: "POST" });
}

/** Extra material found while unloading: posted to stock at the chosen location straight away. */
export function recordExcessMaterial(
  token: string,
  transactionId: number,
  request: {
    materialId: number;
    mfgMonthText: string;
    palletPositionId: number;
    quantityBoxes: number;
    remarks: string;
    acknowledgeWarning?: boolean;
  },
): Promise<RecordExcessMaterialResult> {
  return apiRequest<RecordExcessMaterialResult>(`/api/supervisor/putaway/${transactionId}/excess`, { token, method: "POST", body: request });
}

export function searchSupervisorMaterials(token: string, search: string, pageNumber = 1, pageSize = 15): Promise<PaginatedList<MaterialDto>> {
  const query = `search=${encodeURIComponent(search)}&pageNumber=${pageNumber}&pageSize=${pageSize}`;
  return apiRequest<PaginatedList<MaterialDto>>(`/api/supervisor/materials?${query}`, { token });
}

// ---- Supervisor: add material to an open pick-list (Pullout.Edit)

export function getPulloutAddOptions(token: string, pulloutId: number, materialId: number): Promise<PulloutAddOptionDto[]> {
  return apiRequest<PulloutAddOptionDto[]>(`/api/pullout/${pulloutId}/add-options?materialId=${materialId}`, { token });
}

export function addPulloutPick(
  token: string,
  pulloutId: number,
  request: { materialId: number; palletPositionId: number; quantityBoxes: number; overrideReason?: string | null },
): Promise<AddPulloutPickResult> {
  return apiRequest<AddPulloutPickResult>(`/api/pullout/${pulloutId}/add-pick`, { token, method: "POST", body: request });
}
