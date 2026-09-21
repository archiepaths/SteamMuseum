export type Role = "Driver" | "Guard" | "Fireman" | "StationStaff";
export const roles: Role[] = ["Driver", "Guard", "Fireman", "StationStaff"];
export interface Account {
  id: string;
  email: string;
  displayName: string;
  active: boolean;
  mustChangePassword: boolean;
  roles: string[];
}
export interface Member {
  id: string;
  displayName: string;
  active: boolean;
}
export interface Reference {
  id: string;
  name: string;
}
export interface Window {
  id: string;
  name: string;
  kind: "Monthly" | "SpecialEvent";
  start: string;
  end: string;
  dateRanges?: { start: string; end: string }[];
  notes?: string | null;
  submissionDeadlineUtc: string;
  isOpen: boolean;
}
export interface Day {
  date: string;
  status: "Available" | "Unavailable";
  from: string | null;
  until: string | null;
  preferredRole: Role | null;
  note: string | null;
}
export interface Availability {
  window: Window;
  maximumAssignments: number | null;
  assigned: number;
  days: Day[];
}
export interface Duty {
  id: string;
  name: string;
  date: string;
  start: string;
  end: string;
  role: Role;
  railwayId: string;
  locomotiveId: string | null;
}
export interface Roster {
  duty: Duty;
  assignment: {
    id: string;
    memberId: string;
    status: "Draft" | "Published" | "Cancelled";
  } | null;
  issues: string[];
  preferredRole: Role | null;
}
export interface Competence {
  id: string;
  role: Role;
  railwayId: string;
  locomotiveId: string | null;
  validFrom: string;
  validUntil: string | null;
  evidence: string;
  revokedAtUtc: string | null;
  revocationReason: string | null;
}
export interface Training {
  id: string;
  date: string;
  notes: string;
}
export interface Audit {
  id: string;
  actorId: string;
  atUtc: string;
  action: string;
  details: string;
}
