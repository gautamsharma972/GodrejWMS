import { AllocationStatus } from "../types/api";

export function allocationStatusLabel(status: AllocationStatus): string {
  switch (status) {
    case AllocationStatus.Fulfilled:
      return "Fulfilled";
    case AllocationStatus.Partial:
      return "Partial";
    case AllocationStatus.Failed:
      return "Failed";
    default:
      return "Unknown";
  }
}

export function allocationStatusColor(status: AllocationStatus): string {
  switch (status) {
    case AllocationStatus.Fulfilled:
      return "#16a34a";
    case AllocationStatus.Partial:
      return "#d97706";
    case AllocationStatus.Failed:
      return "#dc2626";
    default:
      return "#64748b";
  }
}
