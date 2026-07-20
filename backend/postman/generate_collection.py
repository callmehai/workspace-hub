#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Generate Workspace-Hub.postman_collection.json tu spec compact.

Nguon su that: backend/src/WorkspaceHub.Api/Controllers/*.cs (123 endpoints).
"""
import json, os, sys

OUT_DIR = "/Users/tranviethaitrustsoft/Developer/workspace-hub-plan/backend/postman"

# ---------------------------------------------------------------- helpers ---

def url(path, query=None):
    """path: 'api/items/{{noteItemId}}/status' -> postman url object"""
    raw = "{{baseUrl}}/" + path
    segs = [s for s in path.split("/") if s != ""]
    u = {"raw": raw, "host": ["{{baseUrl}}"], "path": segs}
    if query:
        u["raw"] = raw + "?" + "&".join("%s=%s" % (k, v) for k, v in query)
        u["query"] = [{"key": k, "value": v} for k, v in query]
    return u


def script(lines):
    return {"type": "text/javascript", "exec": lines}


def req(name, method, path, body=None, query=None, tests=None, prereq=None,
        noauth=False, headers=None, formdata=None, desc=None, adminToken=False):
    r = {"method": method, "header": []}
    if headers:
        for k, v in headers:
            r["header"].append({"key": k, "value": v})
    if noauth:
        r["auth"] = {"type": "noauth"}
    elif adminToken:
        r["auth"] = {"type": "bearer",
                     "bearer": [{"key": "token", "value": "{{adminToken}}", "type": "string"}]}
    if body is not None:
        r["header"].append({"key": "Content-Type", "value": "application/json"})
        r["body"] = {"mode": "raw", "raw": json.dumps(body, ensure_ascii=False, indent=2)}
    if formdata:
        r["body"] = {"mode": "formdata", "formdata": formdata}
    r["url"] = url(path, query)
    item = {"name": name, "request": r}
    if desc:
        r["description"] = desc
    ev = []
    if prereq:
        ev.append({"listen": "prerequest", "script": script(prereq)})
    if tests:
        ev.append({"listen": "test", "script": script(tests)})
    if ev:
        item["event"] = ev
    return item


def ok(code, extra=None, need_token=True):
    """Test script chuan: skip neu chua login, assert status code."""
    lines = []
    if need_token:
        lines.append("if (!pm.environment.get('accessToken')) { pm.test.skip('skipped — can Login truoc'); return; }")
    lines.append("pm.test('%d', function () { pm.response.to.have.status(%d); });" % (code, code))
    if extra:
        lines += extra
    return lines


def save(varname, jsonpath):
    return ["if (pm.response.code < 300) { var _b = pm.response.json(); if (%s) pm.environment.set('%s', %s); }"
            % (jsonpath, varname, jsonpath)]


def manual(reason, code=200):
    """Endpoint phu thuoc provider that (Google/Jira) — chi assert khi co bien can thiet."""
    return ["// MANUAL: %s" % reason,
            "pm.test.skip('manual — %s');" % reason]


# ------------------------------------------------------------------ folders ---
folders = []


def folder(name, items, desc=None):
    f = {"name": name, "item": items}
    if desc:
        f["description"] = desc
    folders.append(f)


# 00 — Health
folder("00 — Health", [
    req("Health check (200)", "GET", "api/health", noauth=True,
        tests=ok(200, [
            "var b = pm.response.json();",
            "pm.test('shape HealthDto', function () {",
            "  pm.expect(b).to.have.property('status');",
            "  pm.expect(b).to.have.property('database');",
            "  pm.expect(b).to.have.property('userCount');",
            "  pm.expect(b).to.have.property('serverTimeUtc');",
            "});"], need_token=False)),
], "Endpoint duy nhat khong can auth — dung de smoke-test API con song.")

# 01 — Auth
folder("01 — Auth", [
    req("Register (201 — requires OTP)", "POST", "api/auth/register", noauth=True,
        body={"email": "{{throwawayEmail}}", "password": "{{userPassword}}",
              "fullName": "Postman Test User", "inviteToken": None},
        prereq=["// Email throwaway de dang ky lap lai khong dung tai khoan test.",
                "pm.environment.set('throwawayEmail', 'wh_pm_' + Date.now() + '@test.local');"],
        tests=["pm.test('201 Created', function () { pm.response.to.have.status(201); });",
               "var b = pm.response.json();",
               "pm.test('RegisterResult — chua co token, phai verify OTP', function () {",
               "  pm.expect(b).to.have.property('email');",
               "  pm.expect(b).to.have.property('requiresEmailVerification', true);",
               "  pm.expect(b).to.have.property('resendCooldownSeconds').that.is.a('number');",
               "  pm.expect(b).to.not.have.property('accessToken');",
               "});"]),
    req("Register — email trung (409)", "POST", "api/auth/register", noauth=True,
        body={"email": "{{userEmail}}", "password": "{{userPassword}}", "fullName": "Dup"},
        tests=["if (!pm.environment.get('userEmail')) { pm.test.skip('skipped — set userEmail truoc'); return; }",
               "pm.test('409 Conflict', function () { pm.response.to.have.status(409); });"]),
    req("Register — payload sai (400)", "POST", "api/auth/register", noauth=True,
        body={"email": "not-an-email", "password": "123", "fullName": ""},
        tests=["pm.test('400 ValidationError', function () { pm.response.to.have.status(400); });",
               "pm.test('co details[]', function () { pm.expect(pm.response.json()).to.have.property('details'); });"]),
    req("Send OTP (200 — chong enumeration)", "POST", "api/auth/send-otp", noauth=True,
        body={"email": "khong-ton-tai-{{$timestamp}}@test.local"},
        tests=["// Dung email CHUA TUNG dang ky: nhanh anti-enumeration luon tra 200, khong dinh cooldown.",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
               "pm.test('tra cooldown', function () { pm.expect(pm.response.json()).to.have.property('resendCooldownSeconds'); });"]),
    req("Send OTP lai — con cooldown sau Register", "POST", "api/auth/send-otp", noauth=True,
        body={"email": "{{throwawayEmail}}"},
        tests=["if (!pm.environment.get('throwawayEmail')) { pm.test.skip('skipped — chay Register truoc'); return; }",
               "// Register vua gui OTP nen email nay con cooldown 60s; chay Runner lien tuc co the dinh rate limit 429.",
               "pm.test('200 / 422 cooldown / 429 rate limit', function () {",
               "  pm.expect([200, 422, 429]).to.include(pm.response.code);",
               "});"]),
    req("Verify OTP (200 — set cookie + token)", "POST", "api/auth/verify-otp", noauth=True,
        body={"email": "{{throwawayEmail}}", "code": "{{otpCode}}"},
        tests=["// Lay otpCode tu log API (LogEmailSender) roi set bien moi truong otpCode.",
               "if (!pm.environment.get('otpCode')) { pm.test.skip('manual — set bien otpCode tu log API'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });"]),
    req("Verify OTP — code sai (422)", "POST", "api/auth/verify-otp", noauth=True,
        body={"email": "{{throwawayEmail}}", "code": "000000"},
        tests=["pm.test('422 BusinessRuleError', function () { pm.response.to.have.status(422); });"]),
    req("Login (200 — luu accessToken)", "POST", "api/auth/login", noauth=True,
        body={"email": "{{userEmail}}", "password": "{{userPassword}}"},
        tests=["if (!pm.environment.get('userEmail')) { pm.test.skip('skipped — set userEmail/userPassword truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
               "var b = pm.response.json();",
               "pm.test('AuthResultDto', function () {",
               "  pm.expect(b).to.have.property('expiresIn').that.is.a('number');",
               "  pm.expect(b).to.have.property('user');",
               "});",
               "// Token nam trong cookie HttpOnly wh_access — lay ra de dung Bearer cho cac request sau.",
               "var c = pm.cookies.get('wh_access');",
               "if (c) pm.environment.set('accessToken', c);",
               "// wh_csrf can cho request mutating nao van di bang cookie (vd Logout).",
               "var csrf = pm.cookies.get('wh_csrf');",
               "if (csrf) pm.environment.set('csrfToken', csrf);",
               "if (b.user && b.user.id) pm.environment.set('userId', b.user.id);"]),
    req("Login — sai mat khau (401)", "POST", "api/auth/login", noauth=True,
        body={"email": "{{userEmail}}", "password": "SaiMatKhau!123"},
        tests=["pm.test('401 UnauthorizedError', function () { pm.response.to.have.status(401); });"]),
    req("Me (200)", "GET", "api/auth/me",
        tests=ok(200, ["var b = pm.response.json();",
                       "pm.test('UserDto', function () {",
                       "  pm.expect(b).to.have.property('id');",
                       "  pm.expect(b).to.have.property('email');",
                       "  pm.expect(b).to.have.property('role');",
                       "});"])),
    req("Me — token sai (401)", "GET", "api/auth/me", noauth=True,
        headers=[("Authorization", "Bearer khong.hop.le")],
        tests=["// Gui Bearer rac de chac chan 401 — neu de trong, cookie jar cua Postman van gui wh_access.",
               "pm.test('401', function () { pm.response.to.have.status(401); });"]),
    req("Google Sign-In — start (200)", "POST", "api/auth/google/start", noauth=True,
        tests=["pm.test('200 OK', function () { pm.response.to.have.status(200); });",
               "pm.test('co authorizationUrl + state', function () {",
               "  var b = pm.response.json();",
               "  pm.expect(b).to.have.property('authorizationUrl');",
               "  pm.expect(b).to.have.property('state');",
               "});"]),
    req("Google Sign-In — callback (manual)", "POST", "api/auth/google/callback", noauth=True,
        body={"code": "{{googleAuthCode}}", "state": "{{googleAuthState}}"},
        tests=manual("can code+state that tu Google consent screen")),
    req("Refresh token (200)", "POST", "api/auth/refresh", noauth=True,
        tests=["// Dung cookie wh_refresh (Path /api/auth/refresh) — phai Login truoc trong cung cookie jar.",
               "pm.test('200 hoac 401 neu chua co cookie refresh', function () {",
               "  pm.expect([200, 401]).to.include(pm.response.code);",
               "});"]),
    req("Logout (204)", "POST", "api/auth/logout", noauth=True,
        headers=[("X-CSRF-Token", "{{csrfToken}}")],
        tests=["// /api/auth/logout KHONG nam trong CSRF-exempt list. Postman/Newman gui cookie wh_access tu",
               "// cookie jar nen middleware bat buoc double-submit: header X-CSRF-Token phai khop cookie wh_csrf",
               "// (bien csrfToken duoc Login luu lai).",
               "pm.test('204 No Content', function () { pm.response.to.have.status(204); });"]),
], "Auth cookie HttpOnly (wh_access/wh_csrf/wh_refresh) + CSRF header X-CSRF-Token. "
   "Collection dung Bearer {{accessToken}} de bo qua CSRF — handler uu tien header Authorization.")

# 02 — Users
folder("02 — Users", [
    req("Update profile (200)", "PATCH", "api/users/me",
        body={"fullName": "Postman Updated Name"},
        tests=ok(200, ["pm.test('fullName da doi', function () {",
                       "  pm.expect(pm.response.json().fullName).to.eql('Postman Updated Name');",
                       "});"])),
    req("Update profile — rong (400)", "PATCH", "api/users/me", body={"fullName": ""},
        tests=ok(400)),
    req("Change password — sai mat khau hien tai (422)", "POST", "api/users/me/change-password",
        body={"currentPassword": "SaiHoanToan!123", "newPassword": "MatKhauMoi!123"},
        tests=ok(422)),
    req("Upload avatar (200 — multipart)", "POST", "api/users/me/avatar",
        formdata=[{"key": "file", "type": "file", "src": []}],
        tests=["if (!pm.environment.get('accessToken')) { pm.test.skip('skipped — can Login truoc'); return; }",
               "pm.test('200 hoac 422 neu chua chon file', function () {",
               "  pm.expect([200, 422]).to.include(pm.response.code);",
               "});"]),
    req("Delete avatar (200)", "DELETE", "api/users/me/avatar", tests=ok(200)),
])

# 03 — Integrations
folder("03 — Integrations", [
    req("List integrations (200)", "GET", "api/integrations",
        tests=ok(200, ["pm.test('array IntegrationResponse', function () {",
                       "  var a = pm.response.json();",
                       "  pm.expect(a).to.be.an('array');",
                       "  if (a.length) { pm.expect(a[0]).to.have.property('key'); pm.expect(a[0]).to.have.property('isEnabled'); }",
                       "});"])),
])

# 04 — Connections
folder("04 — Connections", [
    req("OAuth start (200)", "POST", "api/connections/oauth/start",
        body={"integrationKey": "google", "serviceType": "Gmail",
              "redirectUri": "{{baseUrl}}/oauth/callback"},
        tests=ok(200, ["pm.test('co authorizationUrl + state', function () {",
                       "  var b = pm.response.json();",
                       "  pm.expect(b).to.have.property('authorizationUrl');",
                       "  pm.expect(b).to.have.property('state');",
                       "});"])),
    req("OAuth callback (201 — manual)", "POST", "api/connections/oauth/callback",
        body={"code": "{{oauthCode}}", "state": "{{oauthState}}"},
        tests=manual("can code+state that tu Google/Atlassian consent screen")),
    req("List connections (200)", "GET", "api/connections",
        tests=ok(200, ["var a = pm.response.json();",
                       "pm.test('array ConnectionDto', function () { pm.expect(a).to.be.an('array'); });",
                       "// Luu connectionId theo tung service de cac folder sau dung.",
                       "(a || []).forEach(function (c) {",
                       "  if (c.serviceType === 'Gmail') pm.environment.set('gmailConnectionId', c.id);",
                       "  if (c.serviceType === 'Calendar') pm.environment.set('gcalConnectionId', c.id);",
                       "  if (c.serviceType === 'Drive') pm.environment.set('driveConnectionId', c.id);",
                       "  if (c.serviceType === 'Jira') pm.environment.set('jiraConnectionId', c.id);",
                       "});"])),
    req("Refresh connection (200)", "POST", "api/connections/{{gmailConnectionId}}/refresh",
        tests=["if (!pm.environment.get('gmailConnectionId')) { pm.test.skip('skipped — can Gmail connection'); return; }",
               "pm.test('200 hoac 422 neu refresh token het han', function () {",
               "  pm.expect([200, 422, 502]).to.include(pm.response.code);",
               "});"]),
    req("Gmail profile (200)", "GET", "api/connections/{{gmailConnectionId}}/gmail-profile",
        tests=["if (!pm.environment.get('gmailConnectionId')) { pm.test.skip('skipped — can Gmail connection'); return; }",
               "pm.test('200 hoac 502 neu provider loi', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Gmail sample (200)", "GET", "api/connections/{{gmailConnectionId}}/gmail-sample",
        tests=["if (!pm.environment.get('gmailConnectionId')) { pm.test.skip('skipped — can Gmail connection'); return; }",
               "pm.test('200 hoac 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Sync connection (200)", "POST", "api/connections/{{gmailConnectionId}}/sync",
        tests=["if (!pm.environment.get('gmailConnectionId')) { pm.test.skip('skipped — can Gmail connection'); return; }",
               "pm.test('200 hoac 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });",
               "if (pm.response.code === 200) {",
               "  pm.test('SyncResult', function () {",
               "    var b = pm.response.json();",
               "    pm.expect(b).to.have.property('scanned');",
               "    pm.expect(b).to.have.property('created');",
               "    pm.expect(b).to.have.property('skipped');",
               "  });",
               "}"]),
    req("Connection khong ton tai (404)", "GET", "api/connections/{{missingId}}/gmail-profile",
        tests=ok(404)),
    req("Disconnect (204)", "DELETE", "api/connections/{{disposableConnectionId}}",
        tests=["pm.test.skip('manual — set disposableConnectionId truoc khi chay (thao tac pha huy)');"]),
], "Mo hinh B: moi ServiceType = 1 row Connections. Loi provider tra 502.")

# 05 — Folders
folder("05 — Folders", [
    req("Create folder (201)", "POST", "api/folders",
        body={"name": "Postman Folder {{$timestamp}}", "color": "#3B82F6", "icon": "folder"},
        tests=ok(201, ["var b = pm.response.json();",
                       "pm.environment.set('folderId', b.id);",
                       "pm.test('FolderResponse', function () {",
                       "  pm.expect(b).to.have.property('id');",
                       "  pm.expect(b).to.have.property('itemCount');",
                       "  pm.expect(b).to.have.property('isOwner', true);",
                       "});"])),
    req("Create folder — ten rong (400)", "POST", "api/folders",
        body={"name": "", "color": "#FFF", "icon": "folder"}, tests=ok(400)),
    req("List folders (200 — OData $filter/$orderby)", "GET", "api/folders",
        query=[("includeShared", "true"), ("$orderby", "name"), ("$top", "50")],
        tests=ok(200, ["pm.test('array', function () { pm.expect(pm.response.json()).to.be.an('array'); });"])),
    req("Update folder (200)", "PUT", "api/folders/{{folderId}}",
        body={"name": "Postman Folder Renamed", "color": "#EF4444", "icon": "star", "sortOrder": 1},
        tests=ok(200)),
    req("Add item to folder (201)", "POST", "api/folders/{{folderId}}/items",
        body={"itemId": "{{noteItemId}}"},
        tests=["if (!pm.environment.get('noteItemId')) { pm.test.skip('skipped — tao Note truoc (folder 06)'); return; }",
               "pm.test('201 Created', function () { pm.response.to.have.status(201); });"]),
    req("Add items bulk (200)", "POST", "api/folders/{{folderId}}/items/bulk",
        body={"itemIds": ["{{noteItemId}}"]},
        tests=["if (!pm.environment.get('noteItemId')) { pm.test.skip('skipped — tao Note truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });"]),
    req("Add items bulk — mang rong (400)", "POST", "api/folders/{{folderId}}/items/bulk",
        body={"itemIds": []}, tests=ok(400)),
    req("Remove item from folder (204)", "DELETE", "api/folders/{{folderId}}/items/{{noteItemId}}",
        tests=["if (!pm.environment.get('noteItemId')) { pm.test.skip('skipped — tao Note truoc'); return; }",
               "pm.test('204 No Content', function () { pm.response.to.have.status(204); });"]),
    req("Remove items bulk (204 — DELETE co body)", "DELETE", "api/folders/{{folderId}}/items/bulk",
        body={"itemIds": ["{{noteItemId}}"]},
        tests=["if (!pm.environment.get('noteItemId')) { pm.test.skip('skipped — tao Note truoc'); return; }",
               "pm.test('204 hoac 404 neu item da go', function () { pm.expect([204, 404]).to.include(pm.response.code); });"]),
    req("Folder khong ton tai (404)", "PUT", "api/folders/{{missingId}}",
        body={"name": "X", "color": "#000", "icon": "folder", "sortOrder": 0}, tests=ok(404)),
    req("Delete folder (204)", "DELETE", "api/folders/{{folderId}}", tests=ok(204)),
])

# 06 — Folder sharing
folder("06 — Folder Sharing", [
    req("Invite share (201)", "POST", "api/folders/{{folderId}}/shares",
        body={"friendUserId": "{{friendUserId}}", "permission": "Viewer"},
        tests=["if (!pm.environment.get('friendUserId')) { pm.test.skip('skipped — can friendUserId (folder 07 — Friends)'); return; }",
               "pm.test('201 Created', function () { pm.response.to.have.status(201); });",
               "if (pm.response.code === 201) pm.environment.set('shareId', pm.response.json().shareId);"]),
    req("Invite share — khong phai ban be (422)", "POST", "api/folders/{{folderId}}/shares",
        body={"friendUserId": "{{missingId}}", "permission": "Viewer"},
        tests=["if (!pm.environment.get('folderId')) { pm.test.skip('skipped — tao folder truoc'); return; }",
               "pm.test('422 hoac 404', function () { pm.expect([422, 404]).to.include(pm.response.code); });"]),
    req("List shares cua folder (200)", "GET", "api/folders/{{folderId}}/shares",
        tests=ok(200, ["pm.test('array FolderShareDto', function () { pm.expect(pm.response.json()).to.be.an('array'); });"])),
    req("Update share role (200)", "PATCH", "api/folders/{{folderId}}/shares/{{shareId}}",
        body={"permission": "Editor"},
        tests=["if (!pm.environment.get('shareId')) { pm.test.skip('skipped — can shareId'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });"]),
    req("Shared with me (200)", "GET", "api/folders/shared-with-me",
        tests=ok(200, ["pm.test('array SharedFolderDto', function () { pm.expect(pm.response.json()).to.be.an('array'); });"])),
    req("Accept share (200)", "POST", "api/folders/shares/{{incomingShareId}}/accept",
        tests=["pm.test.skip('manual — dang nhap bang tai khoan duoc share roi set incomingShareId');"]),
    req("Decline share (204)", "POST", "api/folders/shares/{{incomingShareId}}/decline",
        tests=["pm.test.skip('manual — dang nhap bang tai khoan duoc share roi set incomingShareId');"]),
    req("Revoke share (204)", "DELETE", "api/folders/{{folderId}}/shares/{{shareId}}",
        tests=["if (!pm.environment.get('shareId')) { pm.test.skip('skipped — can shareId'); return; }",
               "pm.test('204 No Content', function () { pm.response.to.have.status(204); });"]),
    req("Leave folder (204)", "DELETE", "api/folders/{{sharedFolderId}}/leave",
        tests=["pm.test.skip('manual — dang nhap bang tai khoan duoc share roi set sharedFolderId');"]),
], "Chia se folder cho ban be — Viewer/Editor. Khong phai owner -> 403, chua ket ban -> 422.")

# 07 — Friends
folder("07 — Friends", [
    req("Overview (200)", "GET", "api/friends",
        tests=ok(200, ["var b = pm.response.json();",
                       "pm.test('FriendsOverviewDto', function () {",
                       "  pm.expect(b).to.have.property('friends');",
                       "  pm.expect(b).to.have.property('incomingRequests');",
                       "  pm.expect(b).to.have.property('outgoingRequests');",
                       "  pm.expect(b).to.have.property('emailInvites');",
                       "});",
                       "if (b.friends && b.friends.length) {",
                       "  pm.environment.set('friendUserId', b.friends[0].userId);",
                       "  pm.environment.set('friendshipId', b.friends[0].friendshipId);",
                       "}"])),
    req("Send friend request (200)", "POST", "api/friends/requests",
        body={"email": "{{friendEmail}}", "connectionId": "{{gmailConnectionId}}"},
        tests=["if (!pm.environment.get('friendEmail')) { pm.test.skip('skipped — set friendEmail truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
               "if (pm.response.code === 200) {",
               "  var b = pm.response.json();",
               "  pm.expect(['RequestSent', 'AutoAccepted', 'InviteCreated']).to.include(b.outcome);",
               "  if (b.invite) pm.environment.set('friendInviteId', b.invite.id);",
               "}"]),
    req("Send friend request — chinh minh (422)", "POST", "api/friends/requests",
        body={"email": "{{userEmail}}"},
        tests=["if (!pm.environment.get('userEmail')) { pm.test.skip('skipped — set userEmail'); return; }",
               "pm.test('422 BusinessRuleError', function () { pm.response.to.have.status(422); });"]),
    req("Accept friend request (200)", "POST", "api/friends/{{incomingFriendshipId}}/accept",
        tests=["pm.test.skip('manual — can loi moi den (set incomingFriendshipId)');"]),
    req("Set tier (200)", "PATCH", "api/friends/{{friendshipId}}/tier",
        body={"tier": "CloseFriend"},
        tests=["if (!pm.environment.get('friendshipId')) { pm.test.skip('skipped — can friendshipId'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });"]),
    req("Get invite by token (200 — anonymous)", "GET", "api/friends/invites/by-token/{{inviteToken}}", noauth=True,
        tests=["if (!pm.environment.get('inviteToken')) { pm.test.skip('skipped — set inviteToken tu email moi'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });"]),
    req("Cancel invite (204)", "DELETE", "api/friends/invites/{{friendInviteId}}",
        tests=["if (!pm.environment.get('friendInviteId')) { pm.test.skip('skipped — can friendInviteId'); return; }",
               "pm.test('204 No Content', function () { pm.response.to.have.status(204); });"]),
    req("Remove friend / huy loi moi (204)", "DELETE", "api/friends/{{friendshipId}}",
        tests=["pm.test.skip('manual — thao tac pha huy, chay tay khi can');"]),
])

# 08 — Items (core)
folder("08 — Items", [
    req("Create note (201)", "POST", "api/items/note",
        body={"title": "Postman Note {{$timestamp}}",
              "contentMarkdown": "# Note tu Postman\n\nNoi dung test.", "folderId": None},
        tests=ok(201, ["var b = pm.response.json();",
                       "pm.environment.set('noteItemId', b.id);",
                       "pm.test('ItemResponse type=Note', function () {",
                       "  pm.expect(b.type).to.eql('Note');",
                       "  pm.expect(b.status).to.eql('Inbox');",
                       "});"])),
    req("Create note — title rong (400)", "POST", "api/items/note",
        body={"title": "", "contentMarkdown": "x"}, tests=ok(400)),
    req("List items (200 — full filter)", "GET", "api/items",
        query=[("page", "1"), ("limit", "20"), ("statuses", "Inbox"), ("types", "Note"),
               ("search", "Postman")],
        tests=ok(200, ["var b = pm.response.json();",
                       "pm.test('PagedResult', function () {",
                       "  pm.expect(b).to.have.property('items').that.is.an('array');",
                       "  pm.expect(b).to.have.property('total');",
                       "  pm.expect(b).to.have.property('page');",
                       "  pm.expect(b).to.have.property('limit');",
                       "});"])),
    req("List items — limit vuot 200 (400)", "GET", "api/items",
        query=[("limit", "500")], tests=ok(400)),
    req("Get item by id (200)", "GET", "api/items/{{noteItemId}}",
        tests=["if (!pm.environment.get('noteItemId')) { pm.test.skip('skipped — tao Note truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });"]),
    req("Get item — khong ton tai (404)", "GET", "api/items/{{missingId}}", tests=ok(404)),
    req("Update status (200)", "PATCH", "api/items/{{noteItemId}}/status",
        body={"status": "Doing"},
        tests=["if (!pm.environment.get('noteItemId')) { pm.test.skip('skipped — tao Note truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
               "pm.test('status = Doing', function () { pm.expect(pm.response.json().status).to.eql('Doing'); });"]),
    req("Update status — enum sai (400)", "PATCH", "api/items/{{noteItemId}}/status",
        body={"status": "KhongTonTai"}, tests=ok(400)),
    req("Toggle important (200)", "PATCH", "api/items/{{noteItemId}}/important",
        body={"isImportant": True},
        tests=["if (!pm.environment.get('noteItemId')) { pm.test.skip('skipped — tao Note truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
               "pm.test('isImportant = true', function () { pm.expect(pm.response.json().isImportant).to.eql(true); });"]),
    req("Patch item — doi title Note (200)", "PATCH", "api/items/{{noteItemId}}",
        body={"title": "Postman Note (da sua)"},
        tests=["if (!pm.environment.get('noteItemId')) { pm.test.skip('skipped — tao Note truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });"]),
    req("Delete item (204)", "DELETE", "api/items/{{noteItemId}}",
        tests=["if (!pm.environment.get('noteItemId')) { pm.test.skip('skipped — tao Note truoc'); return; }",
               "pm.test('204 No Content', function () { pm.response.to.have.status(204); });",
               "pm.environment.unset('noteItemId');"]),
], "Note tao/sua/xoa hoan toan local — chay duoc khong can connection Google.")

# 09 — Items: Calendar / Event
folder("09 — Items · Calendar Event", [
    req("Create event (201)", "POST", "api/items/event",
        body={"connectionId": "{{gcalConnectionId}}", "title": "Postman Event",
              "start": "2026-12-01T09:00:00Z", "end": "2026-12-01T10:00:00Z",
              "location": "Meet", "attendees": [], "description": "Tao tu Postman",
              "allDay": False, "driveItemIds": [],
              "reminders": [{"reminderType": "GooglePopup", "offsetValue": 10, "offsetUnit": "Minutes"}],
              "recurrence": [], "guestsCanModify": False, "guestsCanInviteOthers": True,
              "guestsCanSeeOtherGuests": True, "sendUpdates": True},
        tests=["if (!pm.environment.get('gcalConnectionId')) { pm.test.skip('skipped — can Calendar connection'); return; }",
               "pm.test('201 hoac 502 neu Google loi', function () { pm.expect([201, 502]).to.include(pm.response.code); });",
               "if (pm.response.code === 201) pm.environment.set('eventItemId', pm.response.json().id);"]),
    req("Create event — thieu end (400)", "POST", "api/items/event",
        body={"connectionId": "{{gcalConnectionId}}", "title": "Thieu end",
              "start": "2026-12-01T09:00:00Z"},
        tests=["if (!pm.environment.get('gcalConnectionId')) { pm.test.skip('skipped — can Calendar connection'); return; }",
               "pm.test('400 ValidationError', function () { pm.response.to.have.status(400); });"]),
    req("Calendar details (200)", "GET", "api/items/{{eventItemId}}/calendar-details",
        tests=["if (!pm.environment.get('eventItemId')) { pm.test.skip('skipped — tao event truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });"]),
    req("Patch event — doi gio (200)", "PATCH", "api/items/{{eventItemId}}",
        body={"title": "Postman Event (doi gio)", "start": "2026-12-01T14:00:00Z",
              "end": "2026-12-01T15:00:00Z"},
        tests=["if (!pm.environment.get('eventItemId')) { pm.test.skip('skipped — tao event truoc'); return; }",
               "pm.test('200 / 409 conflict ETag / 502', function () { pm.expect([200, 409, 502]).to.include(pm.response.code); });"]),
    req("RSVP event (204)", "PATCH", "api/items/{{eventItemId}}/rsvp",
        body={"response": "accepted", "comment": "OK tu Postman"},
        tests=["if (!pm.environment.get('eventItemId')) { pm.test.skip('skipped — tao event truoc'); return; }",
               "pm.test('204 / 422 neu khong phai guest / 502', function () { pm.expect([204, 422, 502]).to.include(pm.response.code); });"]),
    req("RSVP — response sai (400)", "PATCH", "api/items/{{eventItemId}}/rsvp",
        body={"response": "maybe-not"},
        tests=["if (!pm.environment.get('eventItemId')) { pm.test.skip('skipped — tao event truoc'); return; }",
               "pm.test('400 ValidationError', function () { pm.response.to.have.status(400); });"]),
    req("Send email to guests (204)", "POST", "api/items/{{eventItemId}}/send-email-guests",
        body={"recipientEmails": ["{{friendEmail}}"], "subject": "Nhac hop",
              "bodyHtml": "<p>Nhac lich hop</p>", "sendCopyToMe": True},
        tests=["if (!pm.environment.get('eventItemId')) { pm.test.skip('skipped — tao event truoc'); return; }",
               "pm.test('204 / 400 / 502', function () { pm.expect([204, 400, 502]).to.include(pm.response.code); });"]),
])

# 10 — Calendar invitations
folder("10 — Calendar Invitations", [
    req("List invitations (200)", "GET", "api/CalendarInvitations",
        query=[("from", "2026-01-01T00:00:00Z"), ("to", "2027-01-01T00:00:00Z")],
        tests=ok(200, ["var a = pm.response.json();",
                       "pm.test('array', function () { pm.expect(a).to.be.an('array'); });",
                       "if (a.length) pm.environment.set('invitationId', a[0].id);"])),
    req("Get invitation by id (200)", "GET", "api/CalendarInvitations/{{invitationId}}",
        tests=["if (!pm.environment.get('invitationId')) { pm.test.skip('skipped — chua co loi moi lich'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });"]),
    req("Respond invitation (200)", "POST", "api/CalendarInvitations/{{invitationId}}/respond",
        body={"response": "Accepted", "comment": "Tham gia"},
        tests=["if (!pm.environment.get('invitationId')) { pm.test.skip('skipped — chua co loi moi lich'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Respond — enum sai (400)", "POST", "api/CalendarInvitations/{{invitationId}}/respond",
        body={"response": "SaiEnum"},
        tests=["if (!pm.environment.get('invitationId')) { pm.test.skip('skipped — chua co loi moi lich'); return; }",
               "pm.test('400', function () { pm.response.to.have.status(400); });"]),
])

# 11 — Emails
folder("11 — Emails", [
    req("Send email (200)", "POST", "api/emails/send",
        body={"connectionId": "{{gmailConnectionId}}", "to": ["{{userEmail}}"], "cc": [], "bcc": [],
              "subject": "Postman test {{$timestamp}}",
              "bodyHtml": "<p>Gui tu Postman</p>", "attachments": []},
        tests=["if (!pm.environment.get('gmailConnectionId')) { pm.test.skip('skipped — can Gmail connection'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Send email — to rong (400)", "POST", "api/emails/send",
        body={"connectionId": "{{gmailConnectionId}}", "to": [], "cc": [], "bcc": [],
              "subject": "x", "bodyHtml": "<p>x</p>", "attachments": []},
        tests=["if (!pm.environment.get('gmailConnectionId')) { pm.test.skip('skipped — can Gmail connection'); return; }",
               "pm.test('400 ValidationError', function () { pm.response.to.have.status(400); });"]),
    req("Get signature (200)", "GET", "api/emails/signature",
        query=[("connectionId", "{{gmailConnectionId}}")],
        tests=["if (!pm.environment.get('gmailConnectionId')) { pm.test.skip('skipped — can Gmail connection'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
               "pm.test('co field signature', function () { pm.expect(pm.response.json()).to.have.property('signature'); });"]),
    req("Get thread (200)", "GET", "api/emails/{{emailItemId}}/thread",
        tests=["if (!pm.environment.get('emailItemId')) { pm.test.skip('skipped — set emailItemId (sync Gmail truoc)'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Reply (200)", "POST", "api/emails/reply",
        body={"connectionId": "{{gmailConnectionId}}", "itemId": "{{emailItemId}}",
              "bodyHtml": "<p>Tra loi tu Postman</p>", "replyAll": False,
              "cc": [], "bcc": [], "attachments": []},
        tests=["if (!pm.environment.get('emailItemId')) { pm.test.skip('skipped — set emailItemId'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Forward (200)", "POST", "api/emails/forward",
        body={"connectionId": "{{gmailConnectionId}}", "itemId": "{{emailItemId}}",
              "to": ["{{userEmail}}"], "cc": [], "bcc": [],
              "bodyHtml": "<p>Chuyen tiep</p>", "includeAttachments": True, "attachments": []},
        tests=["if (!pm.environment.get('emailItemId')) { pm.test.skip('skipped — set emailItemId'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Download 1 attachment", "GET",
        "api/emails/{{emailItemId}}/messages/{{gmailMessageId}}/attachments/{{gmailAttachmentId}}",
        query=[("filename", "file.pdf"), ("mimeType", "application/pdf")],
        tests=manual("can emailItemId + gmailMessageId + gmailAttachmentId that")),
    req("Download all attachments (zip)", "GET",
        "api/emails/{{emailItemId}}/messages/{{gmailMessageId}}/attachments/zip",
        tests=manual("can email co attachment")),
    req("Create draft (200)", "POST", "api/emails/drafts",
        body={"connectionId": "{{gmailConnectionId}}", "to": ["{{userEmail}}"], "cc": [], "bcc": [],
              "subject": "Draft Postman", "bodyHtml": "<p>Nhap</p>",
              "threadId": None, "inReplyToMessageId": None, "attachments": []},
        tests=["if (!pm.environment.get('gmailConnectionId')) { pm.test.skip('skipped — can Gmail connection'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });",
               "if (pm.response.code === 200) pm.environment.set('draftItemId', pm.response.json().id);"]),
    req("Update draft (200)", "PUT", "api/emails/drafts/{{draftItemId}}",
        body={"connectionId": "{{gmailConnectionId}}", "to": ["{{userEmail}}"], "cc": [], "bcc": [],
              "subject": "Draft Postman (sua)", "bodyHtml": "<p>Da sua</p>", "attachments": []},
        tests=["if (!pm.environment.get('draftItemId')) { pm.test.skip('skipped — tao draft truoc'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Send draft (200)", "POST", "api/emails/drafts/{{draftItemId}}/send",
        tests=["pm.test.skip('manual — gui that, chay tay khi can');"]),
    req("Discard draft (204)", "DELETE", "api/emails/drafts/{{draftItemId}}",
        tests=["if (!pm.environment.get('draftItemId')) { pm.test.skip('skipped — tao draft truoc'); return; }",
               "pm.test('204 / 502', function () { pm.expect([204, 502]).to.include(pm.response.code); });",
               "if (pm.response.code === 204) pm.environment.unset('draftItemId');"]),
], "Can Gmail connection that. Loi provider -> 502.")

# 12 — Email contact suggestions (OData)
folder("12 — Email Contact Suggestions (OData)", [
    req("Suggestions (200)", "GET", "api/EmailContactSuggestions",
        query=[("connectionId", "{{gmailConnectionId}}"), ("$top", "10"), ("$orderby", "email")],
        tests=["if (!pm.environment.get('gmailConnectionId')) { pm.test.skip('skipped — can Gmail connection'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });"]),
    req("Suggestions — thieu connectionId (400)", "GET", "api/EmailContactSuggestions",
        tests=ok(400)),
])

# 13 — Scheduled emails
folder("13 — Scheduled Emails", [
    req("Create scheduled email (201)", "POST", "api/scheduled-emails",
        body={"connectionId": "{{gmailConnectionId}}", "to": ["{{userEmail}}"], "cc": [], "bcc": [],
              "subject": "Hen gio tu Postman", "bodyHtml": "<p>Noi dung</p>",
              "attachments": [], "sendAt": "2027-01-01T09:00:00Z"},
        tests=["if (!pm.environment.get('gmailConnectionId')) { pm.test.skip('skipped — can Gmail connection'); return; }",
               "pm.test('201 Created', function () { pm.response.to.have.status(201); });",
               "if (pm.response.code === 201) {",
               "  var b = pm.response.json();",
               "  pm.environment.set('scheduledEmailId', b.id);",
               "  pm.expect(b.status).to.eql('Pending');",
               "}"]),
    req("Create — sendAt qua khu (400)", "POST", "api/scheduled-emails",
        body={"connectionId": "{{gmailConnectionId}}", "to": ["{{userEmail}}"], "cc": [], "bcc": [],
              "subject": "x", "bodyHtml": "<p>x</p>", "attachments": [],
              "sendAt": "2020-01-01T00:00:00Z"},
        tests=["if (!pm.environment.get('gmailConnectionId')) { pm.test.skip('skipped — can Gmail connection'); return; }",
               "pm.test('400 ValidationError', function () { pm.response.to.have.status(400); });"]),
    req("List (OData $filter status)", "GET", "api/ScheduledEmails",
        query=[("$filter", "status eq 'Pending'"), ("$orderby", "sendAt"), ("$top", "20"), ("$count", "true")],
        tests=ok(200)),
    req("Get by id (200)", "GET", "api/scheduled-emails/{{scheduledEmailId}}",
        tests=["if (!pm.environment.get('scheduledEmailId')) { pm.test.skip('skipped — tao scheduled email truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });"]),
    req("Get by id — khong ton tai (404)", "GET", "api/scheduled-emails/{{missingId}}", tests=ok(404)),
    req("Cancel (200)", "PATCH", "api/scheduled-emails/{{scheduledEmailId}}/cancel",
        tests=["if (!pm.environment.get('scheduledEmailId')) { pm.test.skip('skipped — tao scheduled email truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
               "pm.test('status = Cancelled', function () { pm.expect(pm.response.json().status).to.eql('Cancelled'); });"]),
])

# 14 — Tags
folder("14 — Tags", [
    req("Create tag (201)", "POST", "api/tags",
        body={"name": "postman-{{$timestamp}}", "color": "#10B981"},
        tests=ok(201, ["var b = pm.response.json();",
                       "pm.environment.set('tagId', b.id);",
                       "pm.test('TagResponse', function () {",
                       "  pm.expect(b).to.have.property('id');",
                       "  pm.expect(b).to.have.property('itemCount', 0);",
                       "});"])),
    req("Create tag — trung ten (409)", "POST", "api/tags",
        body={"name": "postman-dup-fixed", "color": "#000000"},
        tests=["if (!pm.environment.get('accessToken')) { pm.test.skip('skipped — can Login truoc'); return; }",
               "pm.test('201 lan dau, 409 lan sau', function () {",
               "  pm.expect([201, 409]).to.include(pm.response.code);",
               "});"]),
    req("Create tag — ten rong (400)", "POST", "api/tags", body={"name": "", "color": "#000"},
        tests=ok(400)),
    req("List tags (200 — OData)", "GET", "api/tags",
        query=[("$orderby", "name"), ("$top", "50")],
        tests=ok(200, ["pm.test('array', function () { pm.expect(pm.response.json()).to.be.an('array'); });"])),
    req("Update tag (200)", "PUT", "api/tags/{{tagId}}",
        body={"name": "postman-renamed-{{$timestamp}}", "color": "#F59E0B"},
        tests=["if (!pm.environment.get('tagId')) { pm.test.skip('skipped — tao tag truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });"]),
    req("Assign tag vao item (201)", "POST", "api/tags/{{tagId}}/items",
        body={"itemId": "{{noteItemId}}"},
        tests=["if (!pm.environment.get('noteItemId') || !pm.environment.get('tagId')) { pm.test.skip('skipped — can tag + note'); return; }",
               "pm.test('201 Created', function () { pm.response.to.have.status(201); });"]),
    req("Assign tag — item khong ton tai (404)", "POST", "api/tags/{{tagId}}/items",
        body={"itemId": "{{missingId}}"},
        tests=["if (!pm.environment.get('tagId')) { pm.test.skip('skipped — tao tag truoc'); return; }",
               "pm.test('404 NotFound', function () { pm.response.to.have.status(404); });"]),
    req("Unassign tag (204)", "DELETE", "api/tags/{{tagId}}/items/{{noteItemId}}",
        tests=["if (!pm.environment.get('noteItemId') || !pm.environment.get('tagId')) { pm.test.skip('skipped — can tag + note'); return; }",
               "pm.test('204 No Content', function () { pm.response.to.have.status(204); });"]),
    req("Delete tag (204)", "DELETE", "api/tags/{{tagId}}",
        tests=["if (!pm.environment.get('tagId')) { pm.test.skip('skipped — tao tag truoc'); return; }",
               "pm.test('204 No Content', function () { pm.response.to.have.status(204); });",
               "pm.environment.unset('tagId');"]),
])

# 15 — Important contacts
folder("15 — Important Contacts", [
    req("Create (201)", "POST", "api/importantcontacts",
        body={"type": "Email", "identifier": "sep-{{$timestamp}}@congty.com", "label": "Sep"},
        tests=ok(201, ["var b = pm.response.json();",
                       "pm.environment.set('contactId', b.id);",
                       "pm.test('ImportantContactResponse', function () {",
                       "  pm.expect(b).to.have.property('id');",
                       "  pm.expect(b.type).to.eql('Email');",
                       "});"])),
    req("Create — email sai dinh dang (400)", "POST", "api/importantcontacts",
        body={"type": "Email", "identifier": "khong-phai-email", "label": "X"}, tests=ok(400)),
    req("List (200)", "GET", "api/importantcontacts", query=[("type", "Email")],
        tests=ok(200, ["pm.test('array', function () { pm.expect(pm.response.json()).to.be.an('array'); });"])),
    req("List JiraAccount (200)", "GET", "api/importantcontacts", query=[("type", "JiraAccount")],
        tests=ok(200)),
    req("Delete (204)", "DELETE", "api/importantcontacts/{{contactId}}",
        tests=["if (!pm.environment.get('contactId')) { pm.test.skip('skipped — tao contact truoc'); return; }",
               "pm.test('204 No Content', function () { pm.response.to.have.status(204); });"]),
    req("Delete — khong ton tai (404)", "DELETE", "api/importantcontacts/{{missingId}}", tests=ok(404)),
])

# 16 — Drive
folder("16 — Drive", [
    req("Create Drive folder (201)", "POST", "api/drive/folders",
        body={"connectionId": "{{driveConnectionId}}", "name": "Postman Folder", "parentItemId": None},
        tests=["if (!pm.environment.get('driveConnectionId')) { pm.test.skip('skipped — can Drive connection'); return; }",
               "pm.test('201 / 502', function () { pm.expect([201, 502]).to.include(pm.response.code); });",
               "if (pm.response.code === 201) pm.environment.set('driveFolderItemId', pm.response.json().id);"]),
    req("Upload file (201 — multipart)", "POST", "api/drive/files",
        formdata=[{"key": "connectionId", "value": "{{driveConnectionId}}", "type": "text"},
                  {"key": "parentItemId", "value": "{{driveFolderItemId}}", "type": "text"},
                  {"key": "file", "type": "file", "src": []}],
        tests=["pm.test.skip('manual — chon file truoc khi chay (gioi han 100MB)');"]),
    req("Upload folder (200 — multipart nhieu file)", "POST", "api/drive/folders/upload",
        formdata=[{"key": "connectionId", "value": "{{driveConnectionId}}", "type": "text"},
                  {"key": "parentItemId", "value": "{{driveFolderItemId}}", "type": "text"},
                  {"key": "files", "type": "file", "src": []},
                  {"key": "paths", "value": "DuAn/docs/readme.pdf", "type": "text"}],
        tests=["pm.test.skip('manual — chon nhieu file + paths tuong ung (<=200 file, <=500MB)');"]),
    req("Download content", "GET", "api/drive/items/{{driveFileItemId}}/content",
        query=[("dl", "true")],
        tests=manual("can driveFileItemId that")),
    req("Thumbnail (200/204)", "GET", "api/drive/items/{{driveFileItemId}}/thumbnail",
        tests=["if (!pm.environment.get('driveFileItemId')) { pm.test.skip('skipped — set driveFileItemId'); return; }",
               "pm.test('200 hoac 204 neu khong co thumbnail', function () { pm.expect([200, 204]).to.include(pm.response.code); });"]),
    req("List permissions (200)", "GET", "api/drive/items/{{driveFileItemId}}/permissions",
        tests=["if (!pm.environment.get('driveFileItemId')) { pm.test.skip('skipped — set driveFileItemId'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Add permission (201)", "POST", "api/drive/items/{{driveFileItemId}}/permissions",
        body={"email": "{{friendEmail}}", "role": "reader", "notify": False},
        tests=["if (!pm.environment.get('driveFileItemId')) { pm.test.skip('skipped — set driveFileItemId'); return; }",
               "pm.test('201 / 502', function () { pm.expect([201, 502]).to.include(pm.response.code); });",
               "if (pm.response.code === 201) pm.environment.set('drivePermissionId', pm.response.json().id);"]),
    req("Update permission (200)", "PATCH",
        "api/drive/items/{{driveFileItemId}}/permissions/{{drivePermissionId}}",
        body={"role": "writer"},
        tests=["if (!pm.environment.get('drivePermissionId')) { pm.test.skip('skipped — them permission truoc'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Remove permission (204)", "DELETE",
        "api/drive/items/{{driveFileItemId}}/permissions/{{drivePermissionId}}",
        tests=["if (!pm.environment.get('drivePermissionId')) { pm.test.skip('skipped — them permission truoc'); return; }",
               "pm.test('204 / 502', function () { pm.expect([204, 502]).to.include(pm.response.code); });"]),
    req("Set link sharing (200/409)", "PUT", "api/drive/items/{{driveFileItemId}}/link-sharing",
        body={"enabled": True, "role": "reader", "confirmRestrictParent": False},
        tests=["if (!pm.environment.get('driveFileItemId')) { pm.test.skip('skipped — set driveFileItemId'); return; }",
               "pm.test('200 / 409 LINK_RESTRICT_AFFECTS_PARENT / 502', function () {",
               "  pm.expect([200, 409, 502]).to.include(pm.response.code);",
               "});"]),
    req("Restrict conflict check (200/204)", "GET",
        "api/drive/items/{{driveFileItemId}}/link-sharing/restrict-conflict",
        tests=["if (!pm.environment.get('driveFileItemId')) { pm.test.skip('skipped — set driveFileItemId'); return; }",
               "pm.test('200 hoac 204 neu khong xung dot', function () { pm.expect([200, 204]).to.include(pm.response.code); });"]),
], "Can Drive connection that. Upload dung multipart/form-data.")

# 17 — Items: Jira ticket
folder("17 — Items · Jira Ticket", [
    req("Create ticket (201)", "POST", "api/items/ticket",
        body={"connectionId": "{{jiraConnectionId}}", "projectKey": "{{jiraProjectKey}}",
              "issueType": "Task", "summary": "Postman ticket {{$timestamp}}",
              "description": "Tao tu Postman", "assignee": None, "priority": None, "labels": []},
        tests=["if (!pm.environment.get('jiraConnectionId')) { pm.test.skip('skipped — can Jira connection'); return; }",
               "pm.test('201 / 502', function () { pm.expect([201, 502]).to.include(pm.response.code); });",
               "if (pm.response.code === 201) pm.environment.set('ticketItemId', pm.response.json().id);"]),
    req("Create ticket — thieu projectKey (400)", "POST", "api/items/ticket",
        body={"connectionId": "{{jiraConnectionId}}", "projectKey": "", "issueType": "Task",
              "summary": "x"},
        tests=["if (!pm.environment.get('jiraConnectionId')) { pm.test.skip('skipped — can Jira connection'); return; }",
               "pm.test('400 ValidationError', function () { pm.response.to.have.status(400); });"]),
    req("Patch ticket (200)", "PATCH", "api/items/{{ticketItemId}}",
        body={"summary": "Postman ticket (sua)", "description": "Noi dung moi",
              "priority": "High", "labels": ["postman"]},
        tests=["if (!pm.environment.get('ticketItemId')) { pm.test.skip('skipped — tao ticket truoc'); return; }",
               "pm.test('200 / 409 / 502', function () { pm.expect([200, 409, 502]).to.include(pm.response.code); });"]),
    req("Get assignees (200)", "GET", "api/items/assignees", tests=ok(200)),
    req("List comments (200)", "GET", "api/items/{{ticketItemId}}/comments",
        tests=["if (!pm.environment.get('ticketItemId')) { pm.test.skip('skipped — tao ticket truoc'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Add comment (200)", "POST", "api/items/{{ticketItemId}}/comments",
        body={"body": "Comment tu **Postman**", "mediaIds": []},
        tests=["if (!pm.environment.get('ticketItemId')) { pm.test.skip('skipped — tao ticket truoc'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });",
               "if (pm.response.code === 200) pm.environment.set('jiraCommentId', pm.response.json().id);"]),
    req("Update comment (200)", "PUT", "api/items/{{ticketItemId}}/comments/{{jiraCommentId}}",
        body={"body": "Comment da sua"},
        tests=["if (!pm.environment.get('jiraCommentId')) { pm.test.skip('skipped — them comment truoc'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Delete comment (204)", "DELETE", "api/items/{{ticketItemId}}/comments/{{jiraCommentId}}",
        tests=["if (!pm.environment.get('jiraCommentId')) { pm.test.skip('skipped — them comment truoc'); return; }",
               "pm.test('204 / 502', function () { pm.expect([204, 502]).to.include(pm.response.code); });"]),
    req("List attachments (200)", "GET", "api/items/{{ticketItemId}}/attachments",
        tests=["if (!pm.environment.get('ticketItemId')) { pm.test.skip('skipped — tao ticket truoc'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Upload attachment (200 — multipart)", "POST", "api/items/{{ticketItemId}}/attachments",
        formdata=[{"key": "file", "type": "file", "src": []}],
        tests=["pm.test.skip('manual — chon file truoc (gioi han 30MB)');"]),
    req("Download attachment", "GET", "api/items/{{ticketItemId}}/attachments/{{jiraAttachmentId}}/download",
        tests=manual("can jiraAttachmentId that")),
    req("Delete attachment (204)", "DELETE", "api/items/{{ticketItemId}}/attachments/{{jiraAttachmentId}}",
        tests=["if (!pm.environment.get('jiraAttachmentId')) { pm.test.skip('skipped — upload attachment truoc'); return; }",
               "pm.test('204 / 502', function () { pm.expect([204, 502]).to.include(pm.response.code); });"]),
    req("Delete ticket (204)", "DELETE", "api/items/{{ticketItemId}}",
        tests=["if (!pm.environment.get('ticketItemId')) { pm.test.skip('skipped — tao ticket truoc'); return; }",
               "pm.test('204 / 502', function () { pm.expect([204, 502]).to.include(pm.response.code); });",
               "if (pm.response.code === 204) pm.environment.unset('ticketItemId');"]),
])

# 18 — Jira metadata
folder("18 — Jira Metadata", [
    req("Projects (200)", "GET", "api/jira/projects",
        query=[("connectionId", "{{jiraConnectionId}}")],
        tests=["if (!pm.environment.get('jiraConnectionId')) { pm.test.skip('skipped — can Jira connection'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });",
               "if (pm.response.code === 200) { var a = pm.response.json(); if (a.length) pm.environment.set('jiraProjectKey', a[0].key); }"]),
    req("Issue types (200)", "GET", "api/jira/issue-types",
        query=[("connectionId", "{{jiraConnectionId}}"), ("projectKey", "{{jiraProjectKey}}")],
        tests=["if (!pm.environment.get('jiraProjectKey')) { pm.test.skip('skipped — lay projects truoc'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Priorities (200)", "GET", "api/jira/priorities",
        query=[("connectionId", "{{jiraConnectionId}}")],
        tests=["if (!pm.environment.get('jiraConnectionId')) { pm.test.skip('skipped — can Jira connection'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Assignable users (200)", "GET", "api/jira/assignable-users",
        query=[("connectionId", "{{jiraConnectionId}}"), ("projectKey", "{{jiraProjectKey}}"), ("query", "")],
        tests=["if (!pm.environment.get('jiraProjectKey')) { pm.test.skip('skipped — lay projects truoc'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Transitions (200)", "GET", "api/jira/transitions",
        query=[("connectionId", "{{jiraConnectionId}}"), ("itemId", "{{ticketItemId}}")],
        tests=["if (!pm.environment.get('ticketItemId')) { pm.test.skip('skipped — tao ticket truoc'); return; }",
               "pm.test('200 / 502', function () { pm.expect([200, 502]).to.include(pm.response.code); });"]),
    req("Site (200/204)", "GET", "api/jira/site",
        query=[("connectionId", "{{jiraConnectionId}}")],
        tests=["if (!pm.environment.get('jiraConnectionId')) { pm.test.skip('skipped — can Jira connection'); return; }",
               "pm.test('200 / 204 / 502', function () { pm.expect([200, 204, 502]).to.include(pm.response.code); });"]),
    req("Thieu connectionId (400)", "GET", "api/jira/projects", tests=ok(400)),
])

# 19 — Notifications
folder("19 — Notifications", [
    req("List (OData — chua doc)", "GET", "api/Notifications",
        query=[("$filter", "isRead eq false"), ("$orderby", "createdAt desc"),
               ("$top", "20"), ("$count", "true")],
        tests=ok(200, ["var b = pm.response.json();",
                       "var arr = b.value || b;",
                       "pm.test('tra danh sach', function () { pm.expect(arr).to.be.an('array'); });",
                       "if (arr.length) pm.environment.set('notificationId', arr[0].id);"])),
    req("List — $top vuot 100 (400)", "GET", "api/Notifications", query=[("$top", "500")],
        tests=ok(400)),
    req("Mark as read (204)", "PATCH", "api/notifications/{{notificationId}}/read",
        tests=["if (!pm.environment.get('notificationId')) { pm.test.skip('skipped — chua co notification'); return; }",
               "pm.test('204 No Content', function () { pm.response.to.have.status(204); });"]),
    req("Mark all as read (204)", "POST", "api/notifications/read-all", tests=ok(204)),
    req("Dev seed (chi DEBUG + Development)", "POST", "api/notifications/dev/seed",
        tests=["if (!pm.environment.get('accessToken')) { pm.test.skip('skipped — can Login truoc'); return; }",
               "pm.test('200 khi Development, 404 khi khac', function () {",
               "  pm.expect([200, 404]).to.include(pm.response.code);",
               "});",
               "if (pm.response.code === 200) pm.environment.set('notificationId', pm.response.json().id);"]),
], "OData: dung $filter/$orderby/$top (MaxTop=100). Realtime qua SignalR hub /api/hubs/notifications.")

# 20 — Admin
folder("20 — Admin (RBAC)", [
    req("List users (200 — admin)", "GET", "api/admin/users",
        query=[("page", "1"), ("limit", "20"), ("search", "")], adminToken=True,
        tests=["if (!pm.environment.get('adminToken')) { pm.test.skip('skipped — set adminToken truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
               "var b = pm.response.json();",
               "pm.test('PagedResult<AdminUserDto>', function () {",
               "  pm.expect(b).to.have.property('items').that.is.an('array');",
               "  pm.expect(b).to.have.property('total');",
               "});",
               "if (b.items && b.items.length) pm.environment.set('adminTargetUserId', b.items[0].id);"]),
    req("List users — limit vuot 100 (400)", "GET", "api/admin/users",
        query=[("limit", "500")], adminToken=True,
        tests=["if (!pm.environment.get('adminToken')) { pm.test.skip('skipped — set adminToken truoc'); return; }",
               "pm.test('400 ValidationError', function () { pm.response.to.have.status(400); });"]),
    req("Stats (200 — admin)", "GET", "api/admin/stats", adminToken=True,
        tests=["if (!pm.environment.get('adminToken')) { pm.test.skip('skipped — set adminToken truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
               "pm.test('AdminStatsDto', function () {",
               "  var b = pm.response.json();",
               "  pm.expect(b).to.have.property('totalUsers');",
               "  pm.expect(b).to.have.property('connectionsByStatus');",
               "  pm.expect(b).to.have.property('syncErrorsLast24h');",
               "});"]),
    req("Toggle user active (200 — admin)", "POST", "api/admin/users/{{adminTargetUserId}}/toggle-active",
        adminToken=True,
        tests=["pm.test.skip('manual — thao tac khoa/mo tai khoan, chay tay khi can');"]),
    req("RBAC — user thuong goi /admin/users (403)", "GET", "api/admin/users",
        tests=ok(403)),
    req("RBAC — user thuong goi /admin/stats (403)", "GET", "api/admin/stats",
        tests=ok(403)),
    req("List integrations (200 — admin)", "GET", "api/admin/integrations", adminToken=True,
        tests=["if (!pm.environment.get('adminToken')) { pm.test.skip('skipped — set adminToken truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });"]),
    req("Toggle integration (200 — admin)", "PATCH", "api/admin/integrations/google/enable",
        body={"isEnabled": True}, adminToken=True,
        tests=["if (!pm.environment.get('adminToken')) { pm.test.skip('skipped — set adminToken truoc'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
               "pm.test('isEnabled = true', function () { pm.expect(pm.response.json().isEnabled).to.eql(true); });"]),
    req("Toggle integration — key khong ton tai (404)", "PATCH",
        "api/admin/integrations/khong-ton-tai/enable", body={"isEnabled": True}, adminToken=True,
        tests=["if (!pm.environment.get('adminToken')) { pm.test.skip('skipped — set adminToken truoc'); return; }",
               "pm.test('404 NotFound', function () { pm.response.to.have.status(404); });"]),
    req("RBAC — user thuong toggle integration (403)", "PATCH",
        "api/admin/integrations/google/enable", body={"isEnabled": True}, tests=ok(403)),
], "Can adminToken (login bang tai khoan Role=Admin roi copy cookie wh_access vao bien adminToken).")

# 21 — Internal (cron)
folder("21 — Internal · Cron", [
    req("Process scheduled emails (200)", "POST", "api/internal/process-scheduled", noauth=True,
        headers=[("X-Cron-Secret", "{{cronSecret}}")],
        tests=["if (!pm.environment.get('cronSecret')) { pm.test.skip('skipped — set cronSecret (config Cron:Secret)'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
               "pm.test('ProcessScheduledResult', function () {",
               "  var b = pm.response.json();",
               "  pm.expect(b).to.have.property('total');",
               "  pm.expect(b).to.have.property('sent');",
               "  pm.expect(b).to.have.property('failed');",
               "});"]),
    req("Process scheduled — sai secret (401)", "POST", "api/internal/process-scheduled", noauth=True,
        headers=[("X-Cron-Secret", "sai-secret")],
        tests=["pm.test('401 UnauthorizedError', function () { pm.response.to.have.status(401); });"]),
    req("Process scheduled — thieu header (401)", "POST", "api/internal/process-scheduled", noauth=True,
        tests=["pm.test('401 UnauthorizedError', function () { pm.response.to.have.status(401); });"]),
    req("Process sync (200)", "POST", "api/internal/process-sync", noauth=True,
        headers=[("X-Cron-Secret", "{{cronSecret}}")],
        tests=["if (!pm.environment.get('cronSecret')) { pm.test.skip('skipped — set cronSecret'); return; }",
               "pm.test('200 OK', function () { pm.response.to.have.status(200); });",
               "pm.test('ProcessSyncResult', function () {",
               "  var b = pm.response.json();",
               "  pm.expect(b).to.have.property('totalConnections');",
               "  pm.expect(b).to.have.property('details').that.is.an('array');",
               "});"]),
    req("Process sync — sai secret (401)", "POST", "api/internal/process-sync", noauth=True,
        headers=[("X-Cron-Secret", "sai-secret")],
        tests=["pm.test('401 UnauthorizedError', function () { pm.response.to.have.status(401); });"]),
], "Endpoint cho cron job — bao ve bang header X-Cron-Secret (config Cron:Secret), khong dung JWT.")

# 99 — Session helpers
folder("99 — Session", [
    req("Clear session vars", "GET", "api/health", noauth=True,
        tests=["['accessToken','adminToken','userId','folderId','noteItemId','tagId','contactId',",
               " 'eventItemId','ticketItemId','draftItemId','scheduledEmailId','shareId','friendshipId',",
               " 'friendUserId','friendInviteId','notificationId','drivePermissionId','driveFolderItemId',",
               " 'jiraCommentId','otpCode','throwawayEmail','csrfToken'].forEach(function (k) { pm.environment.unset(k); });",
               "pm.test('da xoa bien session', function () { pm.expect(true).to.eql(true); });"]),
    req("Whoami (kiem tra token con han)", "GET", "api/auth/me",
        tests=["if (!pm.environment.get('accessToken')) { pm.test.skip('chua login'); return; }",
               "pm.test('200 = token con han', function () { pm.response.to.have.status(200); });"]),
])

# ------------------------------------------------------------------ build ---
DESCRIPTION = """Collection API day du cho **Workspace Hub** backend (ASP.NET Core 8) — sinh lai tu source controller moi nhat (123 endpoints / 22 controller).

## Chay nhanh
1. Import ca 2 file: collection + `Workspace-Hub.postman_environment.json`, chon environment **Workspace Hub - Local (https)**.
2. Chay backend HTTPS: `dotnet run --project src/WorkspaceHub.Api --launch-profile https` (mac dinh https://localhost:7010). Can Docker `wh-sqlserver` + Redis.
3. Postman > Settings > tat **SSL certificate verification** (dev cert self-signed).
4. Set bien `userEmail` / `userPassword` cua tai khoan da verify email, roi chay **01 - Auth > Login** de nap `accessToken`.
5. Chay **04 - Connections > List connections** de tu dong nap `gmailConnectionId` / `gcalConnectionId` / `driveConnectionId` / `jiraConnectionId`.

## Auth
Backend dung **HttpOnly cookie** (`wh_access`, `wh_csrf`, `wh_refresh`) + header CSRF `X-CSRF-Token`.
Collection nay dung **Bearer `{{accessToken}}`** o cap collection — khi co header `Authorization`, backend uu tien header va **bo qua CSRF check**, nen khong can gui `X-CSRF-Token`.
Request Login tu dong doc cookie `wh_access` tu cookie jar va luu vao bien `accessToken`.

## Quy uoc test script
- Request khong the chay neu thieu bien phu thuoc (connection Google/Jira, item id...) se tu **skip** thay vi fail.
- Request cham provider that (Gmail/Calendar/Drive/Jira) chap nhan **502** vi loi provider duoc map thanh `ProviderError`.
- Test global: moi response >= 400 (tru 405 va body rong) phai co envelope `{error, message, traceId}`.

## Status code chuan (ExceptionMiddleware)
400 ValidationError - 401 UnauthorizedError - 403 ForbiddenError/CsrfError - 404 NotFoundError -
409 ConflictError (conflict ETag write-back) - 422 BusinessRuleError - 429 rate limit (register/send-otp) -
502 ProviderError (Google/Jira loi) - 500 InternalError.
"""

collection = {
    "info": {
        "_postman_id": "wh-api-collection",
        "name": "Workspace Hub API",
        "description": DESCRIPTION,
        "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
    },
    "auth": {"type": "bearer",
             "bearer": [{"key": "token", "value": "{{accessToken}}", "type": "string"}]},
    "event": [
        {"listen": "prerequest", "script": script([
            "// Default password cho tai khoan test neu chua set.",
            "if (!pm.environment.get('userPassword')) {",
            "  pm.environment.set('userPassword', 'Password123!');",
            "}"])},
        {"listen": "test", "script": script([
            "// Global: moi loi do APP sinh (ExceptionMiddleware) phai co envelope {error,message,traceId}.",
            "// Bo qua: 405 (routing framework, khong qua middleware) va body rong (path-variable rong).",
            "var raw = (pm.response.text() || '').trim();",
            "if (pm.response.code >= 400 && pm.response.code !== 405 && raw.length > 0) {",
            "  pm.test('error body co shape chuan {error,message,traceId}', function () {",
            "    var b = {}; try { b = pm.response.json(); } catch (e) { return; }",
            "    pm.expect(b).to.have.property('error');",
            "    pm.expect(b).to.have.property('message');",
            "    pm.expect(b).to.have.property('traceId');",
            "  });",
            "}"])},
    ],
    "item": folders,
}

ENV_VARS = [
    ("baseUrl", "https://localhost:7010", "default"),
    ("accessToken", "", "secret"),
    ("csrfToken", "", "secret"),
    ("adminToken", "", "secret"),
    ("userEmail", "", "default"),
    ("userPassword", "Password123!", "default"),
    ("userId", "", "default"),
    ("otpCode", "", "default"),
    ("throwawayEmail", "", "default"),
    ("cronSecret", "", "secret"),
    ("gmailConnectionId", "", "default"),
    ("gcalConnectionId", "", "default"),
    ("driveConnectionId", "", "default"),
    ("jiraConnectionId", "", "default"),
    ("folderId", "", "default"),
    ("shareId", "", "default"),
    ("sharedFolderId", "", "default"),
    ("incomingShareId", "", "default"),
    ("noteItemId", "", "default"),
    ("eventItemId", "", "default"),
    ("emailItemId", "", "default"),
    ("ticketItemId", "", "default"),
    ("draftItemId", "", "default"),
    ("scheduledEmailId", "", "default"),
    ("tagId", "", "default"),
    ("contactId", "", "default"),
    ("notificationId", "", "default"),
    ("friendEmail", "", "default"),
    ("friendUserId", "", "default"),
    ("friendshipId", "", "default"),
    ("incomingFriendshipId", "", "default"),
    ("friendInviteId", "", "default"),
    ("inviteToken", "", "default"),
    ("driveFolderItemId", "", "default"),
    ("driveFileItemId", "", "default"),
    ("drivePermissionId", "", "default"),
    ("jiraProjectKey", "", "default"),
    ("jiraCommentId", "", "default"),
    ("jiraAttachmentId", "", "default"),
    ("gmailMessageId", "", "default"),
    ("gmailAttachmentId", "", "default"),
    ("adminTargetUserId", "", "default"),
    ("disposableConnectionId", "", "default"),
    ("googleAuthCode", "", "default"),
    ("googleAuthState", "", "default"),
    ("oauthCode", "", "default"),
    ("oauthState", "", "default"),
    ("missingId", "00000000-0000-0000-0000-0000000000ff", "default"),
]

environment = {
    "id": "wh-env-local",
    "name": "Workspace Hub - Local (https)",
    "values": [{"key": k, "value": v, "type": t, "enabled": True} for k, v, t in ENV_VARS],
    "_postman_variable_scope": "environment",
}

os.makedirs(OUT_DIR, exist_ok=True)
with open(os.path.join(OUT_DIR, "Workspace-Hub.postman_collection.json"), "w", encoding="utf-8") as f:
    json.dump(collection, f, ensure_ascii=False, indent=2)
    f.write("\n")
with open(os.path.join(OUT_DIR, "Workspace-Hub.postman_environment.json"), "w", encoding="utf-8") as f:
    json.dump(environment, f, ensure_ascii=False, indent=2)
    f.write("\n")

total = sum(len(f["item"]) for f in folders)
print("folders:", len(folders), "requests:", total)
for f in folders:
    print("  %-40s %d" % (f["name"], len(f["item"])))
