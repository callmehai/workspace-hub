import re, sys, pathlib
f = pathlib.Path(sys.argv[1]); s = f.read_text()
DY = 110
s = re.sub(r'(<mxGeometry x="(-?\d+)" y=")(-?\d+)(")', lambda m: m.group(1)+str(int(m.group(3))+DY)+m.group(4), s)
s = re.sub(r'(<mxPoint x="(-?\d+)" y=")(-?\d+)(")', lambda m: m.group(1)+str(int(m.group(3))+DY)+m.group(4), s)
ys, hs = [], []
for m in re.finditer(r'<mxGeometry x="(-?\d+)" y="(-?\d+)" width="(\d+)" height="(\d+)"', s):
    ys.append(int(m.group(2))); hs.append(int(m.group(4)))
bottom = max(y + h for y, h in zip(ys, hs))
title = ('<mxCell id="erdTitle" value="Workspace Hub — Entity Relationship Diagram (17 bảng · 26 khoá ngoại)" '
         'style="text;html=1;align=left;verticalAlign=middle;fontSize=22;fontStyle=1;fontColor=#333333;" vertex="1" parent="1">'
         '<mxGeometry x="20" y="20" width="1200" height="40" as="geometry" /></mxCell>')
note = ('<mxCell id="erdNote" value="Sinh từ schema thật (dotnet ef dbcontext script, nhánh develop). '
        'Quy ước nền tảng: PK Guid · enum lưu string · datetime2 UTC · JSON nvarchar(max) · KHÔNG soft-delete (dùng IsArchived) · junction dùng PK kép — ItemFolders(ItemId, FolderId), TagAssignments(TagId, ItemId).&#xa;'
        'Ký hiệu ⟂ sau tên khoá ngoại = quan hệ để NO ACTION ở DB (tránh multiple cascade path), service layer tự SET NULL / dọn dữ liệu trước khi xoá — gồm: CalendarInvitations.InviteeItemId · CalendarInvitations.InviteeUserId · CalendarInvitations.OrganizerUserId · Connections.IntegrationId · FolderShares.CreatedByUserId · FolderShares.SharedWithUserId · Friendships.AddresseeId · Friendships.RequesterId · ItemFolders.ItemId · Items.ConnectionId · ScheduledEmails.ConnectionId · TagAssignments.ItemId. Các khoá ngoại còn lại: ON DELETE CASCADE.&#xa;'
        'Đọc quan hệ theo crow&apos;s foot: chân quạ ở bảng chứa khoá ngoại (phía nhiều), đầu gạch ở bảng được tham chiếu (phía một)." '
        'style="rounded=0;whiteSpace=wrap;html=1;fontSize=12;align=left;spacingLeft=12;verticalAlign=middle;fillColor=#fff2cc;strokeColor=#d6b656;dashed=1;" vertex="1" parent="1">'
        f'<mxGeometry x="20" y="{bottom + 70}" width="1560" height="130" as="geometry" /></mxCell>')
s = s.replace('</root>', f'        {title}\n        {note}\n      </root>')
f.write_text(s); print("ok")
