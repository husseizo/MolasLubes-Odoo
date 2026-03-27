export function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

export function safeDiv(numerator: number, denominator: number, fallback = 0): number {
  if (denominator === 0) return fallback;
  return numerator / denominator;
}

export function roundTo(value: number, decimals: number): number {
  const factor = Math.pow(10, decimals);
  return Math.round(value * factor) / factor;
}

export function sumBy<T>(arr: T[], fn: (item: T) => number): number {
  return arr.reduce((acc, item) => acc + fn(item), 0);
}

export function percentOf(part: number, total: number): number {
  if (total === 0) return 0;
  return roundTo((part / total) * 100, 1);
}
