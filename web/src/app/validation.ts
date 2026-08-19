export interface ValidationIssue {
  ruleId: string;
  severity: 'Warning' | 'Error';
  location: string;
  message: string;
}
