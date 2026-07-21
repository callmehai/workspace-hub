#!/usr/bin/env python3
"""UML class diagram generator: JSON spec -> .drawio XML.

Input JSON:
{
  "title": "Page name",
  "colWidth": 300,          # optional, default 300
  "colGap": 90,             # optional
  "rowGap": 90,             # optional
  "classes": [
    {"id":"c1","name":"ItemService","stereotype":"service","col":0,"row":0,
     "attrs":["- _repo: IItemRepository"],
     "methods":["+ GetAsync(id): ItemDto"],
     "color":"green"}
  ],
  "relations": [
    {"from":"c1","to":"c2","type":"implement|inherit|assoc|compose|aggregate|dep","label":"uses"}
  ],
  "notes": [ {"text":"...", "x":40, "y":900, "w":600, "h":80} ]
}
"""
import argparse, json
from xml.sax.saxutils import escape

PALETTE = {
    "blue":   ("#dae8fc", "#6c8ebf"),
    "green":  ("#d5e8d4", "#82b366"),
    "orange": ("#ffe6cc", "#d79b00"),
    "yellow": ("#fff2cc", "#d6b656"),
    "purple": ("#e1d5e7", "#9673a6"),
    "red":    ("#f8cecc", "#b85450"),
    "grey":   ("#f5f5f5", "#666666"),
}

TITLE_H, ROW_H, PAD = 30, 20, 8
SEP = ("line;strokeWidth=1;fillColor=none;align=left;verticalAlign=middle;spacingTop=-1;"
       "spacingLeft=3;spacingRight=10;rotatable=0;labelPosition=left;points=[];"
       "portConstraint=eastwest;")
ROW = ("text;strokeColor=none;fillColor=none;align=left;verticalAlign=middle;spacingLeft=6;"
       "spacingRight=4;overflow=hidden;rotatable=0;points=[[0,0.5],[1,0.5]];"
       "portConstraint=eastwest;fontSize=11;html=1;")
EDGE = {
    "inherit":   "endArrow=block;endFill=0;html=1;rounded=1;edgeStyle=orthogonalEdgeStyle;orthogonalLoop=1;jettySize=auto;",
    "implement": "endArrow=block;endFill=0;dashed=1;html=1;rounded=1;edgeStyle=orthogonalEdgeStyle;orthogonalLoop=1;jettySize=auto;",
    "compose":   "endArrow=diamondThin;endFill=1;html=1;rounded=1;edgeStyle=orthogonalEdgeStyle;orthogonalLoop=1;jettySize=auto;",
    "aggregate": "endArrow=diamondThin;endFill=0;html=1;rounded=1;edgeStyle=orthogonalEdgeStyle;orthogonalLoop=1;jettySize=auto;",
    "assoc":     "endArrow=open;endFill=0;html=1;rounded=1;edgeStyle=orthogonalEdgeStyle;orthogonalLoop=1;jettySize=auto;",
    "dep":       "endArrow=open;endFill=0;dashed=1;html=1;rounded=1;edgeStyle=orthogonalEdgeStyle;orthogonalLoop=1;jettySize=auto;",
}


def esc(v):
    return escape(str(v), {'"': "&quot;"})


def title_h(c):
    return 44 if c.get("stereotype") else TITLE_H


def box_height(c):
    n = len(c.get("attrs", [])) + len(c.get("methods", []))
    sep = 1 if c.get("attrs") and c.get("methods") else 0
    return title_h(c) + ROW_H * n + (ROW_H * sep) + PAD


def side_of(pos, node, other, kind):
    """Cạnh của `node` mà đường nối tới `other` nên xuất phát/đi vào."""
    (c1, r1), (c2, r2) = pos[node], pos[other]
    if r1 == r2:
        return "right" if c2 > c1 else "left"
    return "bottom" if r2 > r1 else "top"


def port_xy(side, idx, total):
    """Trải đều `total` điểm nối trên một cạnh; idx tính từ 1."""
    f = round(idx / (total + 1), 3)
    if side == "bottom":
        return f, 1
    if side == "top":
        return f, 0
    if side == "right":
        return 1, f
    return 0, f


def build(spec):
    w = spec.get("colWidth", 300)
    cgap, rgap = spec.get("colGap", 90), spec.get("rowGap", 90)
    classes = spec["classes"]

    rows = {}
    for c in classes:
        rows.setdefault(c.get("row", 0), []).append(c)
    row_y, y = {}, spec.get("startY", 70)
    for r in sorted(rows):
        row_y[r] = y
        y += max(box_height(c) for c in rows[r]) + rgap

    out = []
    for c in classes:
        fill, stroke = PALETTE.get(c.get("color", "blue"), PALETTE["blue"])
        x = spec.get("startX", 40) + c.get("col", 0) * (w + cgap)
        cy = row_y[c.get("row", 0)]
        h = box_height(c)
        name = esc(c["name"])
        if c.get("stereotype"):
            name = f"&lt;&lt;{esc(c['stereotype'])}&gt;&gt;&#xa;{name}"
        out.append(
            f'<mxCell id="{c["id"]}" value="{name}" style="swimlane;fontStyle=1;align=center;'
            f'verticalAlign=middle;childLayout=stackLayout;horizontal=1;startSize={title_h(c)};'
            f'horizontalStack=0;resizeParent=0;resizeLast=1;collapsible=0;marginBottom=0;html=1;'
            f'fillColor={fill};strokeColor={stroke};fontSize=12;" vertex="1" parent="1">'
            f'<mxGeometry x="{x}" y="{cy}" width="{w}" height="{h}" as="geometry" /></mxCell>')
        oy = title_h(c)
        for i, a in enumerate(c.get("attrs", [])):
            out.append(
                f'<mxCell id="{c["id"]}_a{i}" value="{esc(a)}" style="{ROW}" vertex="1" parent="{c["id"]}">'
                f'<mxGeometry y="{oy}" width="{w}" height="{ROW_H}" as="geometry" /></mxCell>')
            oy += ROW_H
        if c.get("attrs") and c.get("methods"):
            out.append(
                f'<mxCell id="{c["id"]}_sep" value="" style="{SEP}" vertex="1" parent="{c["id"]}">'
                f'<mxGeometry y="{oy}" width="{w}" height="{ROW_H}" as="geometry" /></mxCell>')
            oy += ROW_H
        for i, m in enumerate(c.get("methods", [])):
            out.append(
                f'<mxCell id="{c["id"]}_m{i}" value="{esc(m)}" style="{ROW}" vertex="1" parent="{c["id"]}">'
                f'<mxGeometry y="{oy}" width="{w}" height="{ROW_H}" as="geometry" /></mxCell>')
            oy += ROW_H

    bottom = max(row_y[c.get("row", 0)] + box_height(c) for c in classes)
    for i, n in enumerate(spec.get("notes", [])):
        if "y" not in n:
            n = dict(n, y=bottom + 60)
        out.append(
            f'<mxCell id="note{i}" value="{esc(n["text"])}" style="rounded=0;whiteSpace=wrap;html=1;'
            f'fontSize=11;align=left;spacingLeft=10;verticalAlign=middle;fillColor=#fff2cc;'
            f'strokeColor=#d6b656;dashed=1;" vertex="1" parent="1">'
            f'<mxGeometry x="{n["x"]}" y="{n["y"]}" width="{n["w"]}" height="{n["h"]}" as="geometry" /></mxCell>')

    pos = {c["id"]: (c.get("col", 0), c.get("row", 0)) for c in classes}
    rels = spec.get("relations", [])
    # đếm số cạnh rời/vào mỗi box theo hướng để trải đều điểm nối trên cạnh hộp
    side_count, side_idx = {}, {}
    for r in rels:
        for node, other, key in ((r["from"], r["to"], "out"), (r["to"], r["from"], "in")):
            side_count[(node, side_of(pos, node, other, key))] = \
                side_count.get((node, side_of(pos, node, other, key)), 0) + 1

    for i, r in enumerate(rels):
        style = EDGE.get(r.get("type", "assoc"), EDGE["assoc"])
        ports = ""
        if r["from"] in pos and r["to"] in pos:
            so = side_of(pos, r["from"], r["to"], "out")
            si = side_of(pos, r["to"], r["from"], "in")
            k1 = (r["from"], so); k2 = (r["to"], si)
            side_idx[k1] = side_idx.get(k1, 0) + 1
            side_idx[k2] = side_idx.get(k2, 0) + 1
            ex, ey = port_xy(so, side_idx[k1], side_count[k1])
            ix, iy = port_xy(si, side_idx[k2], side_count[k2])
            ports = (f"exitX={ex};exitY={ey};exitDx=0;exitDy=0;"
                     f"entryX={ix};entryY={iy};entryDx=0;entryDy=0;")
        extra = ports + r.get("style", "")
        label = esc(r.get("label", ""))
        out.append(
            f'<mxCell id="r{i}" value="{label}" style="{style}fontSize=11;labelBackgroundColor=#ffffff;{extra}" '
            f'edge="1" parent="1" source="{r["from"]}" target="{r["to"]}">'
            f'<mxGeometry relative="1" as="geometry" /></mxCell>')

    title = esc(spec.get("title", "Class Diagram"))
    body = "\n        ".join(out)
    return (f'<?xml version="1.0" encoding="UTF-8"?>\n<mxfile host="drawio" version="26.0.0">\n'
            f'  <diagram name="{title}">\n'
            f'    <mxGraphModel grid="1" gridSize="10" page="1" pageScale="1" '
            f'pageWidth="1600" pageHeight="1200" math="0" shadow="0">\n      <root>\n'
            f'        <mxCell id="0" />\n        <mxCell id="1" parent="0" />\n'
            f'        {body}\n      </root>\n    </mxGraphModel>\n  </diagram>\n</mxfile>\n')


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("spec")
    p.add_argument("-o", "--out", required=True)
    a = p.parse_args()
    with open(a.spec) as f:
        spec = json.load(f)
    with open(a.out, "w") as f:
        f.write(build(spec))
    print(f"{a.spec} -> {a.out}")
