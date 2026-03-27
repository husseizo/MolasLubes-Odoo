export type UserRole = 'Viewer' | 'Planner' | 'Executor' | 'Supervisor' | 'Admin';

export const ROLE_HIERARCHY: Record<UserRole, number> = {
  Viewer: 0,
  Planner: 1,
  Executor: 2,
  Supervisor: 3,
  Admin: 4,
};

export function hasMinimumRole(userRole: UserRole, requiredRole: UserRole): boolean {
  return ROLE_HIERARCHY[userRole] >= ROLE_HIERARCHY[requiredRole];
}

export function canApprove(role: UserRole): boolean {
  return hasMinimumRole(role, 'Supervisor');
}

export function canExecute(role: UserRole): boolean {
  return hasMinimumRole(role, 'Executor');
}

export function canGenerateDraft(role: UserRole): boolean {
  return hasMinimumRole(role, 'Planner');
}

export function canSubmit(role: UserRole): boolean {
  return hasMinimumRole(role, 'Planner');
}

export function canReject(role: UserRole): boolean {
  return hasMinimumRole(role, 'Supervisor');
}

export function canRetry(role: UserRole): boolean {
  return hasMinimumRole(role, 'Executor');
}
