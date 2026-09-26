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
  assignments?: WindowAssignment[];
  days: Day[];
}
export interface Duty {
  competenceRoleId?: string | null;
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

export interface CompetenceElement {
  id: string;
  name: string;
  description: string;
  learningType: "Theory" | "Practical" | "TheoryAndPractical";
  reassessmentMonths: number;
  active: boolean;
}
export interface CompetenceRole {
  id: string;
  name: string;
  baseRoleId: string | null;
  category: Role;
  railwayId: string;
  locomotiveId: string | null;
  active: boolean;
  requirements: { elementId: string }[];
}
export interface ElementAssessment {
  id: string;
  memberId: string;
  elementId: string;
  outcome: "Competent" | "NotCompetent";
  assessedOn: string;
  reassessmentDue: string;
  reassessmentMonths: number;
  sequence: number;
  evidence: string;
  assessedBy: string;
  recordedAtUtc: string;
  revokedAtUtc: string | null;
  revocationReason: string | null;
}
export interface RoleEligibility {
  roleId: string;
  name: string;
  qualified: boolean;
  issues: string[];
  elements: {
    elementId: string;
    name: string;
    status: string;
    reassessmentDue: string | null;
  }[];
}

export interface WindowAssignment {
  dutyId: string;
  name: string;
  date: string;
  start: string;
  end: string;
  roleName: string;
  status: "Draft" | "Published";
}
