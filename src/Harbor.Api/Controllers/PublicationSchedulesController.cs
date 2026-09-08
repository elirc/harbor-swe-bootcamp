using System.ComponentModel.DataAnnotations;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

public sealed record SetPublicationScheduleRequest([Required] Guid ExpectedArticleVersion, DateTimeOffset PublishAt);
public sealed record PublicationScheduleResponse(Guid ArticleVersion, ArticlePublicationSchedule? Schedule);

[ApiController, Authorize(Roles = "Admin")]
[Route("api/articles/{id:guid}/publication-schedule")]
public sealed class PublicationSchedulesController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PublicationScheduleResponse>> Get(Guid id, CancellationToken ct)
    {
        var article = await db.Articles.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.WorkspaceId == User.GetWorkspaceId(), ct);
        if (article is null) return NotFound();
        return new PublicationScheduleResponse(article.Version, await db.ArticlePublicationSchedules.AsNoTracking().SingleOrDefaultAsync(s => s.ArticleId == id, ct));
    }

    [HttpPut]
    public Task<ActionResult<PublicationScheduleResponse>> Set(Guid id, SetPublicationScheduleRequest request, CancellationToken ct) =>
        WriteTransactions.RunAsync<ActionResult<PublicationScheduleResponse>>(db, async () =>
        {
            var article = await db.Articles.SingleOrDefaultAsync(a => a.Id == id && a.WorkspaceId == User.GetWorkspaceId(), ct);
            if (article is null) return NotFound();
            if (request.ExpectedArticleVersion == Guid.Empty) return BadRequest();
            if (article.Version != request.ExpectedArticleVersion) return Conflict(new ProblemDetails { Title = "Reload the article before scheduling.", Status = 409 });
            var now = clock.GetUtcNow();
            if (article.Status != ArticleStatus.Draft || request.PublishAt <= now || request.PublishAt > now.AddDays(90))
                return UnprocessableEntity(new ProblemDetails { Title = "Schedule a draft strictly in the future, within 90 days.", Status = 422 });
            var schedule = await db.ArticlePublicationSchedules.SingleOrDefaultAsync(s => s.ArticleId == id, ct);
            if (schedule is null)
            {
                schedule = new ArticlePublicationSchedule { ArticleId = id, WorkspaceId = article.WorkspaceId };
                db.ArticlePublicationSchedules.Add(schedule);
            }
            schedule.ScheduledByTeammateId = User.GetTeammateId();
            schedule.ExpectedArticleVersion = article.Version;
            schedule.PublishAt = request.PublishAt.ToUniversalTime();
            schedule.UpdatedAt = now;
            schedule.Status = PublicationScheduleStatus.Pending;
            schedule.Reason = null;
            await db.SaveChangesAsync(ct);
            return new PublicationScheduleResponse(article.Version, schedule);
        }, ct);

    [HttpDelete]
    public Task<IActionResult> Cancel(Guid id, CancellationToken ct) => WriteTransactions.RunAsync<IActionResult>(db, async () =>
    {
        if (!await db.Articles.AnyAsync(a => a.Id == id && a.WorkspaceId == User.GetWorkspaceId(), ct)) return NotFound();
        var schedule = await db.ArticlePublicationSchedules.SingleOrDefaultAsync(s => s.ArticleId == id, ct);
        if (schedule?.Status is PublicationScheduleStatus.Pending or PublicationScheduleStatus.NeedsReview)
        {
            schedule.Status = PublicationScheduleStatus.Cancelled;
            schedule.Reason = "CancelledByAdmin";
            schedule.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }, ct);
}
