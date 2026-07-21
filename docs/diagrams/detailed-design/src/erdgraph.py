#!/usr/bin/env python3
"""erd.json (spec của classgen) -> graph JSON cho autolayout.py (Graphviz)."""
import json

PAL = {"grey":("#f5f5f5","#666666"),"purple":("#e1d5e7","#9673a6"),"green":("#d5e8d4","#82b366"),
       "orange":("#ffe6cc","#d79b00"),"blue":("#dae8fc","#6c8ebf"),"yellow":("#fff2cc","#d6b656")}
spec = json.load(open("erd.json"))

nodes = []
for c in spec["classes"]:
    fill, stroke = PAL[c["color"]]
    lines = [f"<b>{c['name']}</b>"] + c["attrs"]
    label = "&#xa;".join(lines)
    w = max(210, int(max(len(a) for a in c["attrs"]) * 6.9) + 24)
    h = 30 + 15 * (len(c["attrs"]) + 1)
    nodes.append({"id": c["id"], "label": label, "width": w, "height": h,
                  "style": ("rounded=0;whiteSpace=wrap;html=1;align=left;verticalAlign=top;"
                            "spacingLeft=8;spacingTop=2;fontSize=11;"
                            f"fillColor={fill};strokeColor={stroke};")})

edges = [{"source": r["from"], "target": r["to"], "label": r["label"],
          "style": ("edgeStyle=orthogonalEdgeStyle;html=1;rounded=1;fontSize=10;"
                    "labelBackgroundColor=#ffffff;jettySize=auto;"
                    "startArrow=ERmany;startFill=0;endArrow=ERone;endFill=0;")}
         for r in spec["relations"]]

json.dump({"direction": "TB", "nodes": nodes, "edges": edges},
          open("erd-graph.json", "w"), ensure_ascii=False, indent=1)
print(f"{len(nodes)} bảng, {len(edges)} FK -> erd-graph.json")
