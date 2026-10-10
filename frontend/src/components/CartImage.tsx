import { catalogImageUrl } from '../lib/catalogImageUrl'
import { useState } from 'react'

export function CartImage({ url, alt }: { url: string | null; alt: string }) {
  const [failed, setFailed] = useState<string>()
  return <div className="cart-image">{url && failed !== url ? <img src={catalogImageUrl(url, "thumbnail")} alt={alt} loading="lazy" decoding="async" onError={() => setFailed(url)} /> : <span role="img" aria-label={`Chưa có ảnh: ${alt}`}><svg viewBox="0 0 24 24" aria-hidden="true"><rect x="7" y="2" width="10" height="20" rx="2" /><path d="M10 18h4" /></svg></span>}</div>
}
