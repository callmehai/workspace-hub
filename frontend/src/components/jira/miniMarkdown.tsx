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
    // `code` inline — chip mono như Jira
    if (text[i] === '`') {
      const end = text.indexOf('`', i + 1);
      if (end > i + 1) {
        pushText();
        out.push(
          <code key={`${keyBase}-c${k++}`} className="px-1.5 py-0.5 rounded-md bg-slate-100 dark:bg-slate-800 ring-1 ring-slate-200 dark:ring-slate-700 text-[12px] font-mono text-rose-600 dark:text-rose-400 break-all">
            {text.slice(i + 1, end)}
          </code>
        );
        i = end + 1;
        continue;
      }
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

/** Class heading theo level — Jira-style: H1/H2 to đậm, H3+ nhỏ dần. */
function headingClass(level: number): string {
  if (level <= 1) return 'text-[16px] font-bold mt-3 mb-1';
  if (level === 2) return 'text-[15px] font-bold mt-2.5 mb-1';
  if (level === 3) return 'text-[14px] font-semibold mt-2 mb-0.5';
  return 'text-[13.5px] font-semibold mt-1.5 mb-0.5';
}

/** Render markdown subset → React (khối: paragraph, heading, bullet/ordered list, fenced code). */
export function renderRichText(text: string, renderAttach?: AttachRenderer): React.ReactNode {
  const lines = (text ?? '').replace(/\r\n/g, '\n').split('\n');
  const blocks: React.ReactNode[] = [];
  let bi = 0;
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];

    // Fenced code block ``` ... ```
    if (line.trimStart().startsWith('```')) {
      const codeLines: string[] = [];
      i++;
      while (i < lines.length && !(lines[i] ?? '').trimStart().startsWith('```')) { codeLines.push(lines[i]); i++; }
      if (i < lines.length) i++; // bỏ dòng đóng
      blocks.push(
        <pre key={`cb${bi++}`} className="my-1.5 px-3 py-2.5 rounded-lg bg-slate-100 dark:bg-slate-800 ring-1 ring-slate-200 dark:ring-slate-700 text-[12px] font-mono leading-[1.6] text-slate-800 dark:text-slate-200 overflow-x-auto whitespace-pre">
          {codeLines.join('\n')}
        </pre>
      );
      continue;
    }

    // Heading "# " → "###### "
    const heading = /^(#{1,6})\s+(.*)$/.exec(line);
    if (heading) {
      const level = heading[1].length;
      blocks.push(
        <p key={`h${bi++}`} className={`${headingClass(level)} text-slate-900 dark:text-slate-50`}>
          {renderInline(heading[2], `h${bi}-${i}`, renderAttach)}
        </p>
      );
      i++;
      continue;
    }

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
    if (line.trim() === '') {
      // Dòng trống = ngắt đoạn — chèn khoảng cách (1 lần cho cả cụm dòng trống) để giữ bố cục gốc.
      if (blocks.length > 0) {
        blocks.push(<div key={`sp${bi++}`} className="h-2.5" aria-hidden />);
        while (i < lines.length && (lines[i] ?? '').trim() === '') i++;
      } else {
        i++;
      }
      continue;
    }
    blocks.push(<p key={`p${bi++}`} className="my-0.5">{renderInline(line, `p${bi}-${i}`, renderAttach)}</p>);
    i++;
  }
  return <>{blocks}</>;
}
