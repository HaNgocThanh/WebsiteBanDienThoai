const formats: Record<string, string> = { jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', webp: 'image/webp', gif: 'image/gif', bmp: 'image/bmp', svg: 'image/svg+xml' }
export const catalogImageAccept = '.jpg,.jpeg,.png,.webp,.gif,.bmp,.svg'
export function validCatalogImage(file: File | undefined) {
  const extension = file?.name.split('.').pop()?.toLowerCase() ?? ''
  return !!file && file.size > 0 && file.size <= 10 * 1024 * 1024 && Object.hasOwn(formats, extension) && formats[extension] === file.type.toLowerCase()
}
