using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Attributes;
using WorkspaceHub.Application.DTOs.Friends;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Api.Controllers;

/// <summary>
/// Bạn bè nội bộ app: kết bạn theo email (có tài khoản → pending in-app;
/// chưa có → invite link qua mail), accept/decline/unfriend, hạng bạn bè.
/// </summary>
[Authorize]
[ODataIgnored]
public class FriendsController : ApiControllerBase
{
    private readonly IFriendService _friends;
    private readonly IValidator<SendFriendRequestRequest> _sendValidator;
    private readonly IValidator<UpdateFriendTierRequest> _tierValidator;

    public FriendsController(
        IFriendService friends,
        IValidator<SendFriendRequestRequest> sendValidator,
        IValidator<UpdateFriendTierRequest> tierValidator)
    {
        _friends = friends;
        _sendValidator = sendValidator;
        _tierValidator = tierValidator;
    }

    /// <summary>GET /api/friends — toàn cảnh: bạn bè + lời mời đến/đi + invite email.</summary>
    [HttpGet]
    public async Task<ActionResult<FriendsOverviewDto>> GetOverview(CancellationToken ct = default)
        => Ok(await _friends.GetOverviewAsync(CurrentUserId, ct));

    /// <summary>POST /api/friends/requests — gửi lời mời theo email. 409 nếu đã là bạn/đã mời; 422 tự kết bạn với mình.</summary>
    [HttpPost("requests")]
    public async Task<ActionResult<SendFriendRequestResult>> SendRequest(
        [FromBody] SendFriendRequestRequest request, CancellationToken ct = default)
    {
        await _sendValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _friends.SendRequestAsync(CurrentUserId, request, ct));
    }

    /// <summary>POST /api/friends/{id}/accept — chấp nhận lời mời (chỉ addressee). 403 nếu là người gửi; 409 đã xử lý.</summary>
    [HttpPost("{id:guid}/accept")]
    public async Task<ActionResult<FriendDto>> Accept(Guid id, CancellationToken ct = default)
        => Ok(await _friends.AcceptAsync(CurrentUserId, id, ct));

    /// <summary>DELETE /api/friends/{id} — Pending = từ chối/hủy lời mời; Accepted = unfriend.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct = default)
    {
        await _friends.RemoveAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>PATCH /api/friends/{id}/tier — đổi hạng (Friend | CloseFriend) phía mình.</summary>
    [HttpPatch("{id:guid}/tier")]
    public async Task<ActionResult<FriendDto>> SetTier(
        Guid id, [FromBody] UpdateFriendTierRequest request, CancellationToken ct = default)
    {
        await _tierValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _friends.SetTierAsync(CurrentUserId, id, request, ct));
    }

    /// <summary>DELETE /api/friends/invites/{id} — hủy invite email chưa dùng.</summary>
    [HttpDelete("invites/{id:guid}")]
    public async Task<IActionResult> CancelInvite(Guid id, CancellationToken ct = default)
    {
        await _friends.CancelInviteAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>GET /api/friends/invites/by-token/{token} — public, cho banner trang đăng ký ("X mời bạn").</summary>
    [AllowAnonymous]
    [HttpGet("invites/by-token/{token}")]
    public async Task<ActionResult<FriendInvitePublicDto>> GetInviteByToken(string token, CancellationToken ct = default)
        => Ok(await _friends.GetInviteByTokenAsync(token, ct));
}
