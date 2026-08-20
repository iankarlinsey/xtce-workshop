export interface ValidationIssue {
  ruleId: string;
  severity: 'Warning' | 'Error';
  location: string;
  message: string;
  candidateNumber?: number | null;
}

export type CandidateStatus =
  | 'Pass'
  | 'Fail'
  | 'SchemaPass'
  | 'SchemaFail'
  | 'NotEvaluated'
  | 'NotApplicable'
  | 'Info';

export interface CandidateReportRow {
  candidateNumber: number;
  ownerPath: string;
  disposition: string;
  ruleId: string | null;
  status: CandidateStatus;
  findings: ValidationIssue[];
  notes: string;
}

export interface RuleReportRow {
  ruleId: string;
  executed: boolean;
  findingCount: number;
}

export interface ConformanceReport {
  schemaValid: boolean;
  schemaErrors: string[];
  candidates: CandidateReportRow[];
  rules: RuleReportRow[];
  summary: Record<string, number>;
}

export interface PacketLayoutRow {
  name: string;
  kind: string;
  sourceContainer: string;
  offsetInBits: number | null;
  sizeInBits: number | null;
  isVariable: boolean;
  note: string | null;
}

export interface PacketLayout {
  rows: PacketLayoutRow[];
  totalSizeInBits: number | null;
}
