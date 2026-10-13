import { request } from './client'
import { isRecord } from './errors'
import { parseEntityId, parseMoney, pagedDecoder } from '../lib/contracts'
import { parseVersion } from './catalog'
export interface Voucher { id:string;code:string;name:string;status:string;discountType:string;discountValue:number;maxDiscount:number|null;minimumSpend:number;startsAt:string;endsAt:string;totalLimit:number;perUserLimit:number|null;productIds:string[];tierIds:string[];version:string;used:number }
export type VoucherInput=Omit<Voucher,'id'|'status'|'version'|'used'> & {operationKey:string}
function decode(v:unknown):Voucher {
 if(!isRecord(v)||typeof v.code!=='string'||typeof v.name!=='string'||!['Draft','Published','Disabled'].includes(String(v.status))||!['Fixed','Percent'].includes(String(v.discountType))||typeof v.discountValue!=='number'||v.discountValue<=0||!Number.isFinite(v.discountValue)||!Array.isArray(v.productIds)||!Array.isArray(v.tierIds)||typeof v.startsAt!=='string'||typeof v.endsAt!=='string'||!Number.isFinite(Date.parse(v.startsAt))||!Number.isFinite(Date.parse(v.endsAt))||typeof v.totalLimit!=='number'||!Number.isInteger(v.totalLimit)||v.totalLimit<1||!(v.perUserLimit===null||typeof v.perUserLimit==='number'&&Number.isInteger(v.perUserLimit)&&v.perUserLimit>0)||typeof v.used!=='number'||!Number.isInteger(v.used)||v.used<0)throw new TypeError('Invalid voucher')
 return {...v as unknown as Voucher,id:parseEntityId(v.id),version:parseVersion(v.version),minimumSpend:parseMoney(v.minimumSpend),maxDiscount:v.maxDiscount===null?null:parseMoney(v.maxDiscount),productIds:v.productIds.map(parseEntityId),tierIds:v.tierIds.map(parseEntityId)}
}
const root='/api/v1/admin/vouchers'
export const vouchers={list:(page=1,signal?:AbortSignal)=>request(root+'?page='+page,pagedDecoder(decode),{signal}),create:(body:VoucherInput)=>request(root,decode,{method:'POST',body}),action:(v:Voucher,action:'publish'|'disable')=>request(root+'/'+v.id+'/'+action,decode,{method:'POST',version:v.version})}
