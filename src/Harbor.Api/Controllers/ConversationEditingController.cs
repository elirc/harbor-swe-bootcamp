using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class ConversationEditingController(HarborDbContext db) : ControllerBase
{
    [HttpGet("api/conversations/{id:guid}/subject")]
    public async Task<ActionResult<SubjectResponse>> GetSubject(Guid id, CancellationToken cancellationToken)
    {
        var response = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId())
            .Select(c => new SubjectResponse(c.Id, c.Subject, c.Version))
            .SingleOrDefaultAsync(cancellationToken);
        return response is null ? NotFound() : response;
    }

    [HttpPut("api/conversations/{id:guid}/subject")]
    public async Task<ActionResult<SubjectResponse>> UpdateSubject(
        Guid id, UpdateSubjectRequest request, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.SingleOrDefaultAsync(
            c => c.Id == id && c.WorkspaceId == User.GetWorkspaceId(), cancellationToken);
        if (conversation is null) return NotFound();
        if (conversation.Version != request.ExpectedVersion)
            return Conflict(new ProblemDetails { Status = 409, Title = "Stale conversation", Detail = "Reload the subject and version before applying your edit." });
        conversation.ChangeSubject(request.Subject, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return new SubjectResponse(conversation.Id, conversation.Subject, conversation.Version);
    }
}
