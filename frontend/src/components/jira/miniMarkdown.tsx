/**
 * Renderer markdown subset (khớp AdfConverter BE): đậm, nghiêng, gạch ngang,
 * link [text](url), bullet "- ", ordered "1. ", và marker attachment [[attach:ID]].
 * Không dùng thư viện ngoài — parse thủ công cho đúng subset, an toàn (không dangerouslySetInnerHTML).
 */
import React from 'react';

type AttachRenderer = (id: string) => React.ReactNode;

/** Parse inline 1 dòng → React nodes. */
function renderInline(text: string, keyBase: string, renderAttach?: AttachRenderer): React.ReactNode[] {
  const out: React.ReactNode[] = [];
  let buf = '';
  let k = 0;
  const pushText = () => { if (buf) { out.push(<React.Fragment key={`${keyBase}-t${k++}`}>{buf}</React.Fragment>); buf = ''; } };

  let i = 0;
  while (i < text.length) {
    const rest = text.slice(i);

    // attachment marker
    const am = /^\[\[attach:([^\]]+)\]\]/.exec(rest);
    if (am) {
      pushText();
      out.push(<React.Fragment key={`${keyBase}-a${k++}`}>{renderAttach ? renderAttach(am[1]) : null}</React.Fragment>);
      i += am[0].length;
      continue;
    }
    // link [t](u)
    const lm = /^\[([^\]]+)\]\(([^)\s]+)\)/.exec(rest);
    if (lm) {
      pushText();
      // Link tới attachment Jira (.../attachment/content/{id}) → render chip/preview thay vì link text.
      const att = /\/attachment\/content\/(\d+)/.exec(lm[2]);
      if (att && renderAttach) {
        out.push(<React.Fragment key={`${keyBase}-al${k++}`}>{renderAttach(att[1])}</React.Fragment>);
      } else {
        out.push(
          <a key={`${keyBase}-l${k++}`} href={lm[2]} target="_blank" rel="noopener noreferrer" className="text-brand-600 dark:text-brand-400 underline underline-offset-2 hover:text-brand-700">{lm[1]}</a>
        );
      }
      i += lm[0].length;
      continue;
    }
    // **bold**
    if (rest.startsWith('**')) {
      const end = rest.indexOf('**', 2);
      if (end > 2) { pushText(); out.push(<strong key={`${keyBase}-b${k++}`} className="font-semibold">{renderInline(rest.slice(2, end), `${keyBase}-b${k}`, renderAttach)}</strong>); i += end + 2; continue; }
    }
    // ~~strike~~
    if (rest.startsWith('~~')) {
      const end = rest.indexOf('~~', 2);
      if (end > 2) { pushText(); out.push(<s key={`${keyBase}-s${k++}`}>{renderInline(rest.slice(2, end), `${keyBase}-s${k}`, renderAttach)}</s>); i += end + 2; continue; }
    }
    // *italic* or _italic_
    const ch = text[i];
    if (ch === '*' || ch === '_') {
      const end = text.indexOf(ch, i + 1);
      if (end > i + 1) { pushText(); out.push(<em key={`${keyBase}-i${k++}`}>{text.slice(i + 1, end)}</em>); i = end + 1; continue; }
    }
    buf += ch;
    i++;
  }
  pushText();
  return out;
}

/** Render markdown subset → React (khối: paragraph, bullet/ordered list). */
export function renderRichText(text: string, renderAttach?: AttachRenderer): React.ReactNode {
  const lines = (text ?? '').replace(/\r\n/g, '\n').split('\n');
  const blocks: React.ReactNode[] = [];
  let bi = 0;
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    const bullet = /^\s*[-*]\s+(.*)$/.exec(line);
    const ordered = /^\s*\d+\.\s+(.*)$/.exec(line);

    if (bullet) {
      const items: React.ReactNode[] = [];
      for (let mm: RegExpExecArray | null = bullet; i < lines.length && mm; i++, mm = /^\s*[-*]\s+(.*)$/.exec(lines[i] ?? ''))
        items.push(<li key={i}>{renderInline(mm[1], `b${bi}-${i}`, renderAttach)}</li>);
      blocks.push(<ul key={`ul${bi++}`} className="list-disc pl-5 space-y-0.5 my-1">{items}</ul>);
      continue;
    }
    if (ordered) {
      const items: React.ReactNode[] = [];
      for (let mm: RegExpExecArray | null = ordered; i < lines.length && mm; i++, mm = /^\s*\d+\.\s+(.*)$/.exec(lines[i] ?? ''))
        items.push(<li key={i}>{renderInline(mm[1], `o${bi}-${i}`, renderAttach)}</li>);
      blocks.push(<ol key={`ol${bi++}`} className="list-decimal pl-5 space-y-0.5 my-1">{items}</ol>);
      continue;
    }
    if (line.trim() === '') { i++; continue; }
    blocks.push(<p key={`p${bi++}`} className="my-0.5">{renderInline(line, `p${bi}-${i}`, renderAttach)}</p>);
    i++;
  }
  return <>{blocks}</>;
}
