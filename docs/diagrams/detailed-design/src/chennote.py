import re, sys, pathlib
f = pathlib.Path(sys.argv[1]); s = f.read_text()
DY = 120
s = re.sub(r'(<mxGeometry x="(-?\d+)" y=")(-?\d+)(")', lambda m: m.group(1)+str(int(m.group(3))+DY)+m.group(4), s)
s = re.sub(r'(<mxPoint x="(-?\d+)" y=")(-?\d+)(")', lambda m: m.group(1)+str(int(m.group(3))+DY)+m.group(4), s)
ys, hs = [], []
for m in re.finditer(r'<mxGeometry x="(-?\d+)" y="(-?\d+)" width="(\d+)" height="(\d+)"', s):
    ys.append(int(m.group(2))); hs.append(int(m.group(4)))
bottom = max(y + h for y, h in zip(ys, hs))
title = ('<mxCell id="erdTitle" value="Workspace Hub — Entity Relationship Diagram (15 thực thể · 20 quan hệ)" '
         'style="text;html=1;align=left;verticalAlign=middle;fontSize=24;fontStyle=1;fontColor=#333333;" vertex="1" parent="1">'
         '<mxGeometry x="20" y="20" width="1300" height="45" as="geometry" /></mxCell>')
note = ('<mxCell id="erdNote" value="Ký hiệu: chữ nhật = thực thể · hình thoi = quan hệ · 1 / M = số lượng tham gia. '
        'Danh sách quan hệ suy ra từ khoá ngoại trong schema thật (dotnet ef dbcontext script, nhánh develop).&#xa;'
        'Hai quan hệ M:N — Folder &lt;contains&gt; Item và Tag &lt;tags&gt; Item — hiện thực bằng bảng nối khoá chính kép: ItemFolders(ItemId, FolderId, Position) và TagAssignments(TagId, ItemId).&#xa;'
        'FolderShare, CalendarInvitation, Friendship, FriendInvite được vẽ thành thực thể (không phải hình thoi) vì bản thân chúng mang thuộc tính riêng — quyền chia sẻ, trạng thái mời, hạng bạn bè, token mời.&#xa;'
        'Sơ đồ đầy đủ cột / kiểu dữ liệu / cascade: xem erd-full-columns.drawio." '
        'style="rounded=0;whiteSpace=wrap;html=1;fontSize=13;align=left;spacingLeft=14;verticalAlign=middle;fillColor=#fff2cc;strokeColor=#d6b656;dashed=1;" vertex="1" parent="1">'
        f'<mxGeometry x="20" y="{bottom + 70}" width="1600" height="130" as="geometry" /></mxCell>')
s = s.replace('</root>', f'        {title}\n        {note}\n      </root>')
f.write_text(s); print("ok")
