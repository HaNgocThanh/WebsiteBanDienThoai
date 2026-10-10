export type ImageSize = 'thumbnail' | 'card' | 'display'
export function catalogImageUrl(url: string, size: ImageSize): string {
  return url + '?size=' + size
}
