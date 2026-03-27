import type { ReplenishmentStatus, LineExecutionStatus } from '@/features/replenishment/types';

export const REPLENISHMENT_STATUS_LABELS: Record<ReplenishmentStatus, string> = {
  DRAFT: 'Draft',
  PENDING_APPROVAL: 'Pending Approval',
  APPROVED: 'Approved',
  REJECTED: 'Rejected',
  EXECUTING: 'Executing',
  EXECUTED: 'Executed',
  PARTIAL: 'Partial',
  FAILED: 'Failed',
};

export const LINE_EXECUTION_STATUS_LABELS: Record<LineExecutionStatus, string> = {
  PENDING: 'Pending',
  EXECUTED: 'Executed',
  GI_ISSUED: 'GI Issued',
  FAILED: 'Failed',
};

export const REPLENISHMENT_STATUS_ORDER: ReplenishmentStatus[] = [
  'DRAFT',
  'PENDING_APPROVAL',
  'APPROVED',
  'REJECTED',
  'EXECUTING',
  'EXECUTED',
  'PARTIAL',
  'FAILED',
];
