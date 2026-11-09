// Mirrors the JSON shapes returned by GodrejWMS.PickersApi (src/GodrejWMS.PickersApi), which are
// thin wrappers around the same MediatR DTOs the Blazor app uses. Field names/casing match exactly
// what System.Text.Json's default camelCase output produces for the C# records.

export interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  userId: string;
  fullName: string;
  roles: string[];
  permissions: string[];
}

export interface WarehouseOption {
  id: number;
  code: string;
  name: string;
}

export interface PaginatedList<T> {
  items: T[];
  pageNumber: number;
  totalPages: number;
  totalCount: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface MaterialDto {
  id: number;
  materialNumber: number;
  description: string;
  designType: string;
  packSize: number;
  mrpPrice: number;
  isActive: boolean;
}

// AllocationStatus enum from GodrejWMS.Domain.Enums.AllocationStatus
export const AllocationStatus = {
  Fulfilled: 0,
  Partial: 1,
  Failed: 2,
} as const;
// The const object and the union type deliberately share a name (enum-like pattern).
// eslint-disable-next-line @typescript-eslint/no-redeclare
export type AllocationStatus = (typeof AllocationStatus)[keyof typeof AllocationStatus];

export interface InwardAllocationLineDto {
  locationCode: string;
  quantityBoxes: number;
  reason?: string | null;
  isConfirmed: boolean;
  putawayId?: number | null;
}

export interface InwardLineResultDto {
  materialNumber: number;
  materialDescription: string;
  mfgMonthLabel: string;
  requestedQuantityBoxes: number;
  allocatedQuantityBoxes: number;
  status: AllocationStatus;
  remarks?: string | null;
  allocatedTo: InwardAllocationLineDto[];
  isConfirmed: boolean;
}

export interface InwardUnsavedLineDto {
  codeText: string;
  quantityBoxes: number;
  kind: number;
  reason: string;
  sourceRow?: number | null;
  description?: string | null;
}

export interface InwardResultDto {
  id: number;
  referenceNumber: string;
  createdAt: string;
  lines: InwardLineResultDto[];
  isConfirmed: boolean;
  isRejected: boolean;
  vehicleNumber?: string | null;
  lrNumber?: string | null;
  uploadHeaderJson?: string | null;
  receivedFrom?: string | null;
  unsavedLines?: InwardUnsavedLineDto[] | null;
}

export interface InwardHistoryDto {
  id: number;
  referenceNumber: string;
  createdAt: string;
  lineCount: number;
  totalQuantityBoxes: number;
  status: AllocationStatus;
  isConfirmed: boolean;
  isRejected: boolean;
  isPkmless: boolean;
  vehicleNumber?: string | null;
  lrNumber?: string | null;
  locationCount: number;
  submittedLocationCount: number;
}

export interface InwardPutawayDto {
  locationCode: string;
  quantityBoxes: number;
  isConfirmed: boolean;
  id: number;
  reason?: string | null;
  overrideReason?: string | null;
  confirmedAt?: string | null;
  confirmedByUserName?: string | null;
  excludedLocation?: string | null;
  palletPositionId: number;
  isPickerPlaced: boolean;
  isExcludedLocked: boolean;
  /** The location is an auto-generated virtual overflow location. */
  isVirtual?: boolean;
}

export interface InwardDetailLineDto {
  materialNumber: number;
  materialDescription: string;
  designType: string;
  mfgMonthLabel: string;
  requestedQuantityBoxes: number;
  allocatedQuantityBoxes: number;
  status: AllocationStatus;
  remarks?: string | null;
  putaways: InwardPutawayDto[];
}

export interface InwardDetailDto {
  id: number;
  referenceNumber: string;
  createdAt: string;
  lines: InwardDetailLineDto[];
  isConfirmed: boolean;
  isRejected: boolean;
  rejectedAt?: string | null;
  rejectedByUserName?: string | null;
  rejectionReason?: string | null;
  vehicleNumber?: string | null;
  lrNumber?: string | null;
  uploadHeaderJson?: string | null;
  receivedFrom?: string | null;
  unsavedLines?: InwardUnsavedLineDto[] | null;
  /** Set when the GRN's stock was posted to inventory on upload: the picker only confirms or changes each location. */
  postedAt?: string | null;
}

export const InwardSheetImportStatus = {
  Created: 0,
  SkippedNotOrderSheet: 1,
  SkippedNoData: 2,
  SkippedMissingPkm: 3,
  SkippedDuplicateReference: 4,
  SkippedDuplicateLrVehicleSource: 5,
  Error: 6,
} as const;
// eslint-disable-next-line @typescript-eslint/no-redeclare
export type InwardSheetImportStatus = (typeof InwardSheetImportStatus)[keyof typeof InwardSheetImportStatus];

export interface InwardSkippedLine {
  materialNumber: number;
  mfgMonthLabel: string;
  requestedQuantityBoxes: number;
  allocatedQuantityBoxes: number;
  reason: string;
}

export interface InwardSheetImportOutcome {
  sheetName: string;
  status: InwardSheetImportStatus;
  referenceNumber?: string | null;
  message: string;
  skippedLines?: InwardSkippedLine[] | null;
}

export interface PalletPositionStockDto {
  materialNumber: number;
  materialDescription: string;
  designType: string;
  mfgMonthLabel: string;
  stockSubtypeCode: string;
  stockSubtypeName: string;
  quantityBoxes: number;
}

export interface PalletPositionDto {
  id: number;
  rackId: number;
  locationCode: string;
  flatLabel?: string | null;
  column: number;
  level: number;
  locationTypeCode: string;
  locationTypeName: string;
  locationSubtypeCode: string;
  locationSubtypeName: string;
  zoneTypeCode: string;
  zoneTypeName: string;
  maxPallets: number;
  capacityBoxes?: number | null;
  occupiedBoxes: number;
  freeBoxes?: number | null;
  isFull: boolean;
  isActive: boolean;
  stock: PalletPositionStockDto[];
}

// Request shapes - field names must match the C# command records exactly (System.Text.Json binds
// minimal-API JSON bodies by property name, case-insensitively, but we keep camelCase for clarity).
export interface SubmitInwardWithoutPkmLine {
  materialCode: number;
  quantityBoxes: number;
  description?: string | null;
}

export interface SubmitInwardWithoutPkmRequest {
  lines: SubmitInwardWithoutPkmLine[];
  warehouseId: number;
  vehicleNumber?: string | null;
  lrNumber?: string | null;
  receivedFrom?: string | null;
}

export interface PutawayPkmAssignment {
  putawayId: number;
  mfgMonthText: string;
}

export interface AssignInwardPutawayPkmRequest {
  referenceNumber: string;
  assignments: PutawayPkmAssignment[];
}

export interface ApiFieldErrors {
  errors: Record<string, string[]>;
  detail?: string;
}

// --- Floor Supervisor: Inward putaway edit/confirm ---

export interface InwardPalletChangeOptionDto {
  id: number;
  locationCode: string;
  locationSubtypeId: number;
  locationSubtypeCode: string;
  locationSubtypeName: string;
  zoneTypeId: number;
  zoneTypeCode: string;
  zoneTypeName: string;
  distancePriority: number;
  capacityBoxes: number;
  occupiedBoxes: number;
  freeBoxes: number;
  isCurrent: boolean;
}

export interface ChangeInwardPutawayLocationRequest {
  newPalletPositionId: number;
  reason?: string | null;
  quantityBoxes?: number | null;
}

// --- Floor Supervisor: Pullout (outward) queue, edit pick location, confirm ---

export interface PulloutLoadingSheetFieldsDto {
  transportName?: string | null;
  vehicleNumber?: string | null;
  gateNumber?: string | null;
  driverNumber?: string | null;
  planTime?: string | null;
  dateText?: string | null;
  startTime?: string | null;
  endTime?: string | null;
  weightText?: string | null;
  volumeText?: string | null;
}

export interface PulloutPickDto {
  locationCode: string;
  mfgMonthLabel: string;
  quantityBoxes: number;
  overrideReason?: string | null;
  /** Set on picks of a posted pullout: the pick the picker confirms on the floor. */
  pickId?: number;
  palletPositionId?: number;
  isVirtual?: boolean;
  pickedAt?: string | null;
  pickedQuantityBoxes?: number | null;
  /** The remainder move the picker chose; it is only carried out when the pick-list is confirmed. */
  moveToPalletPositionId?: number | null;
  moveToLocationCode?: string | null;
  moveQuantityBoxes?: number | null;
}

/** A posted pullout the picker still has picks to confirm on (GET /api/pullout/picker/queue). */
export interface PickerPulloutDto {
  id: number;
  referenceNumber: string;
  createdAt: string;
  destination?: string | null;
  vehicleNumber?: string | null;
  lineCount: number;
  pickCount: number;
  pickedCount: number;
  totalBoxes: number;
  virtualPickCount: number;
}

/** What is left on the shelf at a pick's location (after the order's own deduction). */
export interface PickRemainderDto {
  pickId: number;
  locationCode: string;
  plannedBoxes: number;
  remainingBoxes: number;
  isPicked: boolean;
}

export interface RelocationSuggestionDto {
  palletPositionId: number;
  locationCode: string;
  level: number;
  isVirtual: boolean;
  spareBoxes: number;
  reason: string;
}

export interface ConfirmPickRequest {
  pickedQuantityBoxes: number;
  moveRemaining?: { destinationPalletPositionId: number; quantityBoxes: number } | null;
}

export interface ConfirmPickResult {
  referenceNumber: string;
  pickedBoxes: number;
  returnedToStockBoxes: number;
  movedBoxes: number;
  remainingAtSourceBoxes: number;
  destinationLocationCode?: string | null;
  /** Boxes picked beyond the allocation; deducted from the location's stock when the pick-list is confirmed. */
  additionalBoxes?: number;
}

export interface PulloutHistoryDto {
  id: number;
  referenceNumber: string;
  createdAt: string;
  lineCount: number;
  totalQuantityBoxes: number;
  status: AllocationStatus;
  isConfirmed: boolean;
  isRejected: boolean;
  destination?: string | null;
  vehicleNumber?: string | null;
}

export interface PulloutDetailLineDto {
  materialNumber: number;
  materialDescription: string;
  requestedQuantityBoxes: number;
  pickedQuantityBoxes: number;
  status: AllocationStatus;
  remarks?: string | null;
  picks: PulloutPickDto[];
  plantName?: string | null;
  priority?: string | null;
}

// Shape returned by the pullout submit/change-location/confirm endpoints - narrower than
// PulloutDetailDto below (no Id/IsConfirmed/IsRejected; those only exist once the detail is
// fetched back via GET /api/pullout/{id}).
export interface PulloutResultLineDto {
  materialNumber: number;
  materialDescription: string;
  requestedQuantityBoxes: number;
  pickedQuantityBoxes: number;
  status: AllocationStatus;
  remarks?: string | null;
  picks: PulloutPickDto[];
  plantName?: string | null;
  priority?: string | null;
}

export interface PulloutResultDto {
  referenceNumber: string;
  createdAt: string;
  lines: PulloutResultLineDto[];
  headerFields?: PulloutLoadingSheetFieldsDto | null;
  warehouseAddress?: string | null;
}

export interface PulloutDetailDto {
  id: number;
  referenceNumber: string;
  createdAt: string;
  lines: PulloutDetailLineDto[];
  isConfirmed: boolean;
  isRejected: boolean;
  rejectedAt?: string | null;
  rejectedByUserName?: string | null;
  rejectionReason?: string | null;
  headerFields?: PulloutLoadingSheetFieldsDto | null;
  warehouseAddress?: string | null;
  /** The pick-list is confirmed (older confirmed pullout, or a posted one the picker confirmed). isConfirmed only says stock was posted. */
  pickingConfirmed?: boolean;
}

export interface PulloutPickChangeOptionDto {
  palletPositionId: number;
  locationCode: string;
  mfgMonthLabel: string;
  stockBoxes: number;
  availableBoxes: number;
  isCurrent: boolean;
  /** Taking from this cell skips older stock still available (FIFO): a reason is then required. */
  breaksFifo?: boolean;
}

export interface PulloutPickSource {
  palletPositionId: number;
  mfgMonthLabel: string;
  quantityBoxes: number;
}

export interface ChangePulloutPickLocationRequest {
  referenceNumber: string;
  materialNumber: number;
  currentLocationCode: string;
  mfgMonthLabel: string;
  sources: PulloutPickSource[];
  reason: string;
}

// --- Picker Put-away: confirm/change qty, PKM and location for pending GRN locations ---
// Mirrors src/GodrejWMS.Web/Components/Pages/InwardPicker/Index.razor (route /inward-picker),
// gated on Inward.AssignPkm - the same permission the Pickers role already has.

export interface AopLocationOptionDto {
  id: number;
  warehouseId: number;
  warehouseCode: string;
  rackId: number;
  rackCode: string;
  column: number;
  level: number;
  locationCode: string;
}

export interface PickerPlaceInwardPutawayRequest {
  quantityBoxes: number;
  mfgMonthText?: string | null;
  newPalletPositionId?: number | null;
  excludedLocation?: string | null;
  acknowledgeWarning?: boolean;
}

export interface PickerPlaceInwardPutawayResult {
  saved: boolean;
  warning?: string | null;
}
