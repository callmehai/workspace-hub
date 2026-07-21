#!/usr/bin/env python3
"""ERD kiểu Chen: thực thể (chữ nhật) + quan hệ (hình thoi) + số lượng 1/M, không vẽ thuộc tính."""
import json

ENT = ("rounded=0;whiteSpace=wrap;html=1;fontSize=14;fontStyle=1;"
       "fillColor=#d5e8d4;strokeColor=#82b366;")
REL = ("rhombus;whiteSpace=wrap;html=1;fontSize=12;"
       "fillColor=#d6d94a;strokeColor=#9aa22a;")
EDGE = ("edgeStyle=orthogonalEdgeStyle;rounded=1;orthogonalLoop=1;jettySize=auto;html=1;"
        "endArrow=none;startArrow=none;fontSize=14;fontStyle=1;labelBackgroundColor=#ffffff;")

entities = [
    "User", "Integration", "Connection", "Item", "Folder", "Tag",
    "FolderShare", "Notification", "ImportantContact", "ScheduledEmail",
    "GoogleContact", "EventReminder", "CalendarInvitation", "Friendship", "FriendInvite",
]

# (id, nhãn quan hệ, thực thể A, số lượng A, thực thể B, số lượng B)
rels = [
    ("r1",  "makes",       "User", "1", "Connection", "M"),
    ("r2",  "provides",    "Integration", "1", "Connection", "M"),
    ("r3",  "owns",        "User", "1", "Folder", "M"),
    ("r4",  "owns",        "User", "1", "Item", "M"),
    ("r5",  "syncs",       "Connection", "1", "Item", "M"),
    ("r6",  "contains",    "Folder", "M", "Item", "M"),
    ("r7",  "creates",     "User", "1", "Tag", "M"),
    ("r8",  "tags",        "Tag", "M", "Item", "M"),
    ("r9",  "shared by",   "Folder", "1", "FolderShare", "M"),
    ("r10", "shared with", "User", "1", "FolderShare", "M"),
    ("r11", "receives",    "User", "1", "Notification", "M"),
    ("r12", "marks",       "User", "1", "ImportantContact", "M"),
    ("r13", "schedules",   "User", "1", "ScheduledEmail", "M"),
    ("r14", "sends via",   "Connection", "1", "ScheduledEmail", "M"),
    ("r15", "caches",      "Connection", "1", "GoogleContact", "M"),
    ("r16", "reminds",     "Item", "1", "EventReminder", "M"),
    ("r17", "invites for", "Item", "1", "CalendarInvitation", "M"),
    ("r18", "invited",     "User", "1", "CalendarInvitation", "M"),
    ("r19", "befriends",   "User", "1", "Friendship", "M"),
    ("r20", "invites",     "User", "1", "FriendInvite", "M"),
]

nodes = [{"id": e.lower(), "label": e, "style": ENT,
          "width": max(150, len(e) * 9 + 40), "height": 60} for e in entities]
nodes += [{"id": r[0], "label": r[1], "style": REL, "width": 130, "height": 80} for r in rels]

edges = []
for rid, _, a, ca, b, cb in rels:
    edges.append({"source": a.lower(), "target": rid, "label": ca, "style": EDGE})
    edges.append({"source": rid, "target": b.lower(), "label": cb, "style": EDGE})

json.dump({"direction": "TB", "nodes": nodes, "edges": edges},
          open("chen-graph.json", "w"), ensure_ascii=False, indent=1)
print(f"{len(entities)} thực thể, {len(rels)} quan hệ, {len(edges)} cạnh -> chen-graph.json")
