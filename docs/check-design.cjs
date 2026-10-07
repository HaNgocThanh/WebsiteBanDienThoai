const fs = require('fs');
const path = require('path');
const root = path.resolve('outputs');
const files = ['01-dac-ta-database-va-nghiep-vu.md', '02-so-do-quan-he-database.md', '03-dac-ta-api.md'];
const docs = files.map(f => fs.readFileSync(path.join(root, f), 'utf8'));
const errors = [];
for (let i = 0; i < files.length; i++) {
  const fences = docs[i].match(/^```.*$/gm) || [];
  if (fences.length % 2) errors.push(`${files[i]}: unbalanced fences`);
  if (docs[i].includes('\uFFFD')) errors.push(`${files[i]}: replacement character`);
  for (const m of docs[i].matchAll(/\]\(([^)]+\.md)\)/g)) {
    if (!fs.existsSync(path.resolve(root, m[1]))) errors.push(`Broken link ${m[1]}`);
  }
}
const diagrams = [...docs[1].matchAll(/```mermaid\n([\s\S]*?)```/g)].map(m => m[1]);
const entities = new Set();
for (const block of diagrams) {
  if ((block.match(/\{/g)||[]).length !== (block.match(/\}/g)||[]).length && !block.includes('erDiagram')) errors.push('Unbalanced diagram braces');
  for (const m of block.matchAll(/^\s+(\w+)\s+\{\s*$/gm)) entities.add(m[1]);
  for (const m of block.matchAll(/^\s+(\w+)\s+[|o{}.-]+\s+(\w+)\s*:/gm)) {
    entities.add(m[1]); entities.add(m[2]);
  }
}
for (const name of entities) if (!docs[0].includes(name)) errors.push(`Diagram entity absent from spec: ${name}`);
for (const m of docs[2].matchAll(/```json\n([\s\S]*?)```/g)) {
  try { JSON.parse(m[1]); } catch(e) { errors.push(`Invalid JSON example: ${e.message}`); }
}
const statuses = ['Placed','Reviewing','Approved','Shipping','Delivered','Completed','Cancelled'];
for (const s of statuses) if (!docs[0].includes(s) || !docs[1].includes(s)) errors.push(`Missing status ${s}`);
console.log(JSON.stringify({files:files.length, mermaidBlocks:diagrams.length, entities:entities.size, jsonExamples:[...docs[2].matchAll(/```json/g)].length, errors, scope:'Static document consistency only; no Mermaid rendering or SQL migration execution'},null,2));
process.exitCode = errors.length ? 1 : 0;
