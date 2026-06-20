using System;

namespace WorkspaceHub.Application.DTOs.Connections;

public record ManualSyncResult(int StatusCode, Guid? JobId = null);
