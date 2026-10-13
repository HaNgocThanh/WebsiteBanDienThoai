import { request } from './client'
import { isRecord } from './errors'
import { parseEntityId,pagedDecoder } from '../lib/contracts'
import { parseVersion } from './catalog'
export interface PublicReview { id:string;rating:number;content:string;variant:string;adminReply:string|null;createdAt:string;updatedAt:string|null }
export interface OwnerReview { id:string;orderItemId:string;rating:number;content:string;status:'Visible'|'Hidden';moderationReason:string|null;adminReply:string|null;createdAt:string;updatedAt:string|null;version:string }
export interface ItemReview { orderItemId:string;productName:string;variant:string;canReview:boolean;review:OwnerReview|null }
export interface AdminReview { review:OwnerReview;productId:string;productName:string;variant:string;orderNumber:string;authorUserId:string }
export interface PublicReviewPage { items:PublicReview[];page:number;pageSize:number;totalCount:number;averageRating:number|null }
const text=(v:unknown):string=>{if(typeof v!=='string')throw new TypeError('Invalid review text');return v}
const nullable=(v:unknown)=>v===null?null:text(v)
const date=(v:unknown)=>{const s=text(v);if(!/^\d{4}-\d{2}-\d{2}T.*Z$/.test(s)||!Number.isFinite(Date.parse(s)))throw new TypeError('Invalid review time');return s}
const rating=(v:unknown)=>{if(typeof v!=='number'||!Number.isInteger(v)||v<1||v>5)throw new TypeError('Invalid rating');return v}
export function decodeOwnerReview(v:unknown):OwnerReview {if(!isRecord(v)||v.status!=='Visible'&&v.status!=='Hidden')throw new TypeError('Invalid review');return {id:parseEntityId(v.id),orderItemId:parseEntityId(v.orderItemId),rating:rating(v.rating),content:text(v.content),status:v.status,moderationReason:nullable(v.moderationReason),adminReply:nullable(v.adminReply),createdAt:date(v.createdAt),updatedAt:v.updatedAt===null?null:date(v.updatedAt),version:parseVersion(v.version)}}
function publicReview(v:unknown):PublicReview {if(!isRecord(v))throw new TypeError('Invalid review');return{id:parseEntityId(v.id),rating:rating(v.rating),content:text(v.content),variant:text(v.variant),adminReply:nullable(v.adminReply),createdAt:date(v.createdAt),updatedAt:v.updatedAt===null?null:date(v.updatedAt)}}
export function decodePublicReviews(v:unknown):PublicReviewPage {const page=pagedDecoder(publicReview)(v);if(!isRecord(v)||!(v.averageRating===null||typeof v.averageRating==='number'&&Number.isFinite(v.averageRating)&&v.averageRating>=1&&v.averageRating<=5)||page.totalCount===0&&v.averageRating!==null||page.totalCount>0&&v.averageRating===null)throw new TypeError('Invalid review average');return{...page,averageRating:v.averageRating}}
function adminReview(v:unknown):AdminReview {if(!isRecord(v)||typeof v.authorUserId!=='string'||!/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(v.authorUserId))throw new TypeError('Invalid admin review');return{review:decodeOwnerReview(v.review),productId:parseEntityId(v.productId),productName:text(v.productName),variant:text(v.variant),orderNumber:text(v.orderNumber),authorUserId:v.authorUserId}}
export const reviews={
 public:(id:string,page=1,signal?:AbortSignal)=>request('/api/v1/products/'+parseEntityId(id)+'/reviews?page='+page,decodePublicReviews,{signal}),
 mine:(id:string,signal?:AbortSignal)=>request('/api/v1/me/orders/'+parseEntityId(id)+'/reviews',v=>{if(!Array.isArray(v))throw new TypeError('Invalid review items');return v.map(x=>{if(!isRecord(x)||typeof x.canReview!=='boolean')throw new TypeError('Invalid review eligibility');const item={orderItemId:parseEntityId(x.orderItemId),productName:text(x.productName),variant:text(x.variant),canReview:x.canReview,review:x.review===null?null:decodeOwnerReview(x.review)};if(item.review&&item.review.orderItemId!==item.orderItemId)throw new TypeError('Wrong review item');return item})},{signal}),
 save:(item:ItemReview,body:{rating:number;content:string},signal?:AbortSignal)=>request(item.review?'/api/v1/me/reviews/'+item.review.id:'/api/v1/me/order-items/'+item.orderItemId+'/review',decodeOwnerReview,{method:item.review?'PATCH':'POST',version:item.review?.version,body,signal}),
 admin:(query:URLSearchParams,signal?:AbortSignal)=>request('/api/v1/admin/reviews?'+query,pagedDecoder(adminReview),{signal}),
 moderate:(v:OwnerReview,status:'Hidden'|'Visible',reason:string,signal?:AbortSignal)=>request('/api/v1/admin/reviews/'+v.id+'/moderation',decodeOwnerReview,{method:'POST',version:v.version,body:{status,reason},signal}),
 reply:(v:OwnerReview,content:string,signal?:AbortSignal)=>request('/api/v1/admin/reviews/'+v.id+'/reply',decodeOwnerReview,{method:'POST',version:v.version,body:{content},signal}),
}
