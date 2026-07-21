#!/usr/bin/env python3
"""model.sql (EF `dotnet ef dbcontext script`) -> classgen spec JSON cho ERD."""
import json, re, sys

sql = open("model.sql").read()
sql = re.sub(r"/\*.*?\*/|--[^\n]*", "", sql, flags=re.S)

LAYOUT = {  # table: (col, row, color)
    "Integrations": (0, 0, "grey"),   "Users": (1, 0, "purple"),
    "Tags": (2, 0, "green"),          "ImportantContacts": (3, 0, "green"),
    "Connections": (0, 1, "orange"),  "Folders": (1, 1, "blue"),
    "TagAssignments": (2, 1, "green"),"Notifications": (3, 1, "green"),
    "Items": (0, 2, "yellow"),        "FolderShares": (1, 2, "blue"),
    "ItemFolders": (2, 2, "yellow"),  "ScheduledEmails": (3, 2, "orange"),
    "GoogleContacts": (0, 3, "grey"), "EventReminders": (1, 3, "yellow"),
    "CalendarInvitations": (2, 3, "yellow"), "Friendships": (3, 3, "purple"),
    "FriendInvites": (0, 4, "purple"),
}

def unquote(n):
    return n.strip().strip('[]"`').split(".")[-1]

def split_top(body):
    parts, depth, cur = [], 0, ""
    for ch in body:
        if ch == "(": depth += 1
        elif ch == ")": depth -= 1
        if ch == "," and depth == 0:
            parts.append(cur); cur = ""
        else:
            cur += ch
    if cur.strip(): parts.append(cur)
    return [p.strip() for p in parts]

tables, rels = {}, []
for m in re.finditer(r"CREATE TABLE \[(\w+)\] \(", sql):
    name = m.group(1)
    i = m.end() - 1; depth = 0
    for j in range(i, len(sql)):
        if sql[j] == "(": depth += 1
        elif sql[j] == ")":
            depth -= 1
            if depth == 0: break
    body = sql[i + 1:j]
    cols, pk, fks = [], set(), {}
    for part in split_top(body):
        if re.match(r"CONSTRAINT \[\w+\] PRIMARY KEY", part, re.I):
            pk |= {unquote(c) for c in re.search(r"PRIMARY KEY \((.+?)\)", part, re.I).group(1).split(",")}
        elif re.match(r"CONSTRAINT \[\w+\] FOREIGN KEY", part, re.I):
            g = re.search(r"FOREIGN KEY \((.+?)\) REFERENCES \[(\w+)\]", part, re.I)
            col = unquote(g.group(1).split(",")[0])
            fks[col] = g.group(2)
            cascade = "CASCADE" if "ON DELETE CASCADE" in part.upper() else "NO ACTION"
            rels.append((name, g.group(2), col, cascade))
        elif part.startswith("["):
            g = re.match(r"\[(\w+)\] ([\w()]+(?: \(\d+\))?)( NOT NULL| NULL)?", part)
            if g:
                cols.append((g.group(1), g.group(2), (g.group(3) or "").strip() == "NULL"))
    tables[name] = (cols, pk, fks)

classes = []
for name, (cols, pk, fks) in tables.items():
    if name not in LAYOUT:
        print("bỏ qua bảng chưa có vị trí:", name, file=sys.stderr); continue
    col, row, color = LAYOUT[name]
    attrs = []
    for cname, ctype, nullable in cols:
        mark = "PK" if cname in pk else ("FK" if cname in fks else "  ")
        attrs.append(f"{mark}  {cname} : {ctype}{'?' if nullable else ''}")
    classes.append({"id": name.lower(), "name": name, "stereotype": "table",
                    "col": col, "row": row, "color": color, "attrs": attrs})

ER = ("startArrow=ERmany;startFill=0;endArrow=ERone;endFill=0;")
relations = [{"from": s.lower(), "to": t.lower(), "type": "assoc",
              "label": c + ("  ⟂" if cas == "NO ACTION" else ""),
              "style": ER} for s, t, c, cas in rels]

spec = {
    "title": "3.1 Entity Relationship Diagram — Workspace Hub",
    "colWidth": 360, "colGap": 90, "rowGap": 110, "startX": 40, "startY": 60,
    "classes": classes, "relations": relations,
    "notes": [{"text": "Sinh từ schema thật: dotnet ef dbcontext script (17 bảng, "
               + str(len(rels)) + " khoá ngoại).  Quy ước: PK Guid · enum lưu string · datetime2 UTC · JSON nvarchar(max) · KHÔNG soft-delete (dùng IsArchived).  "
               "Ký hiệu ⟂ = FK để NO ACTION ở DB để tránh multiple cascade path (Items.ConnectionId, ScheduledEmails.ConnectionId, CalendarInvitations.*) — service layer tự dọn/SET NULL khi xoá Connection.  "
               "Bảng nối dùng PK kép: ItemFolders(ItemId, FolderId) · TagAssignments(TagId, ItemId).",
               "w": 1700, "h": 100}],
}
json.dump(spec, open("erd.json", "w"), ensure_ascii=False, indent=1)
print(f"{len(classes)} bảng, {len(relations)} quan hệ -> erd.json")
