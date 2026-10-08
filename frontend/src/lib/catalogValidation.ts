export function validateSpecifications(text: string): boolean {
  if (!text.trim()) return true
  try {
    const value: unknown = JSON.parse(text)
    if (!value || typeof value !== 'object' || Array.isArray(value)) return false
    const depth = (v: unknown, level: number): boolean => v === null || typeof v !== 'object' || (level <= 16 && Object.values(v).every(x => depth(x, level + 1)))
    return depth(value, 1)
  } catch { return false }
}
