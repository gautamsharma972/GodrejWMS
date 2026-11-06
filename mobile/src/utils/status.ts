import type { BadgeTone } from "../components/ui/StatusBadge";
import { colors } from "../theme";
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
      return colors.success;
    case AllocationStatus.Partial:
      return colors.warning;
    case AllocationStatus.Failed:
      return colors.danger;
    default:
      return colors.textMuted;
  }
}

export function allocationStatusTone(status: AllocationStatus): BadgeTone {
  switch (status) {
    case AllocationStatus.Fulfilled:
      return "success";
    case AllocationStatus.Partial:
      return "warning";
    case AllocationStatus.Failed:
      return "danger";
    default:
      return "neutral";
  }
}

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

// e.g. "06 Nov 2026, 07:06 PM"
export function formatDateTime(value: string | Date): string {
  const date = typeof value === "string" ? new Date(value) : value;
  if (Number.isNaN(date.getTime())) return "";
  const day = String(date.getDate()).padStart(2, "0");
  const month = MONTHS[date.getMonth()];
  const year = date.getFullYear();
  let hours = date.getHours();
  const minutes = String(date.getMinutes()).padStart(2, "0");
  const period = hours >= 12 ? "PM" : "AM";
  hours = hours % 12 || 12;
  return `${day} ${month} ${year}, ${String(hours).padStart(2, "0")}:${minutes} ${period}`;
}
