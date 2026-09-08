using System.ComponentModel.DataAnnotations;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
public class ArticleLearningController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    private Task<bool> ParentExists(Guid id, CancellationToken ct) => db.Articles.AnyAsync(
        a => a.Id == id && a.WorkspaceId == User.GetWorkspaceId(), ct);
    private IQueryable<ArticleFeedback> OwnFeedback(Guid id) => db.ArticleFeedback.Where(f =>
        f.ArticleId == id && f.WorkspaceId == User.GetWorkspaceId() && f.TeammateId == User.GetTeammateId());

    [HttpPost("api/articles/{id:guid}/clone")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ArticleResponse>> Clone(Guid id, CloneArticleRequest request, CancellationToken ct)
    {
        var source = await db.Articles.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.WorkspaceId == User.GetWorkspaceId(), ct);
        if (source is null) return NotFound();
        if (!await db.ArticleCollections.AnyAsync(c => c.Id == request.CollectionId && c.WorkspaceId == source.WorkspaceId, ct))
            return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Unavailable collection" });
        var slug = Slugs.From(request.Slug) ?? Slugs.From(request.Title);
        if (slug is null) return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Unusable slug" });
        if (await db.Articles.AnyAsync(a => a.WorkspaceId == source.WorkspaceId && a.Slug == slug, ct)) return SlugConflict();
        var now = clock.GetUtcNow();
        var article = new Article { WorkspaceId = source.WorkspaceId, CollectionId = request.CollectionId!.Value,
            Title = request.Title, Slug = slug, Body = source.Body, Status = ArticleStatus.Draft, PublishedAt = null,
            CreatedAt = now, UpdatedAt = now };
        db.Articles.Add(article);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            db.Entry(article).State = EntityState.Detached;
            if (!await db.Articles.AnyAsync(a => a.WorkspaceId == source.WorkspaceId && a.Slug == slug, ct)) throw;
            return SlugConflict();
        }
        return CreatedAtAction("GetArticle", "HelpCenter", new { id = article.Id }, article.ToResponse());
    }

    private ObjectResult SlugConflict() => Conflict(new ProblemDetails { Status = 409, Title = "Duplicate slug" });

    [HttpGet("api/articles/{id:guid}/revisions")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<ArticleRevisionMetadata>>> Revisions(Guid id, [FromQuery] PageRequest paging, CancellationToken ct)
    {
        if (!await ParentExists(id, ct)) return NotFound();
        return await db.ArticleRevisions.AsNoTracking().Where(r => r.ArticleId == id && r.WorkspaceId == User.GetWorkspaceId())
            .OrderByDescending(r => r.RecordedAt).ThenBy(r => r.Id)
            .Select(r => new ArticleRevisionMetadata(r.Id, r.ArticleId, r.ActorTeammateId, r.Reason, r.SourceVersion, r.RecordedAt))
            .ToPageAsync(paging, Response, ct);
    }

    [HttpGet("api/articles/{id:guid}/revisions/{revisionId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ArticleRevisionResponse>> Revision(Guid id, Guid revisionId, CancellationToken ct)
    {
        if (!await ParentExists(id, ct)) return NotFound();
        var response = await db.ArticleRevisions.AsNoTracking()
            .Where(r => r.ArticleId == id && r.Id == revisionId && r.WorkspaceId == User.GetWorkspaceId())
            .Select(r => new ArticleRevisionResponse(r.Id, r.ArticleId, r.ActorTeammateId, r.Reason, r.SourceVersion,
                r.RecordedAt, r.Title, r.Body, r.Slug, r.CollectionId, r.Status, r.PublishedAt)).SingleOrDefaultAsync(ct);
        return response is null ? NotFound() : response;
    }

    [HttpPut("api/articles/{id:guid}/my-feedback")]
    public async Task<ActionResult<ArticleFeedbackResponse>> SetFeedback(Guid id, SetArticleFeedbackRequest request, CancellationToken ct)
    {
        if (!await ParentExists(id, ct)) return NotFound();
        var feedback = await OwnFeedback(id).SingleOrDefaultAsync(ct);
        var create = feedback is null;
        if (create)
        {
            feedback = new ArticleFeedback { ArticleId = id, WorkspaceId = User.GetWorkspaceId(), TeammateId = User.GetTeammateId() };
            db.ArticleFeedback.Add(feedback);
        }
        feedback!.Helpful = request.Helpful!.Value;
        feedback.UpdatedAt = clock.GetUtcNow();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (create && ex.InnerException is SqliteException { SqliteExtendedErrorCode: 1555 or 2067 })
        {
            db.Entry(feedback).State = EntityState.Detached;
            if (!await OwnFeedback(id).AnyAsync(ct)) throw;
            return Conflict(new ProblemDetails { Status = 409, Title = "Feedback was created concurrently", Detail = "Reload your vote and retry." });
        }
        return new ArticleFeedbackResponse(feedback.Helpful, feedback.UpdatedAt);
    }

    [HttpGet("api/articles/{id:guid}/my-feedback")]
    public async Task<ActionResult<ArticleFeedbackResponse>> GetFeedback(Guid id, CancellationToken ct)
    {
        if (!await ParentExists(id, ct)) return NotFound();
        var response = await OwnFeedback(id).AsNoTracking().Select(f => new ArticleFeedbackResponse(f.Helpful, f.UpdatedAt)).SingleOrDefaultAsync(ct);
        return response is null ? NotFound() : response;
    }

    [HttpDelete("api/articles/{id:guid}/my-feedback")]
    public async Task<IActionResult> DeleteFeedback(Guid id, CancellationToken ct)
    {
        if (!await ParentExists(id, ct)) return NotFound();
        await OwnFeedback(id).ExecuteDeleteAsync(ct);
        return NoContent();
    }

    [HttpGet("api/articles/{id:guid}/feedback-summary")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ArticleFeedbackSummary>> FeedbackSummary(Guid id, CancellationToken ct)
    {
        if (!await ParentExists(id, ct)) return NotFound();
        return await db.ArticleFeedback.AsNoTracking().Where(f => f.ArticleId == id && f.WorkspaceId == User.GetWorkspaceId())
            .GroupBy(f => 1).Select(g => new ArticleFeedbackSummary(g.Count(), g.Count(f => f.Helpful), g.Count(f => !f.Helpful)))
            .SingleOrDefaultAsync(ct) ?? new ArticleFeedbackSummary(0, 0, 0);
    }

    [HttpGet("api/public/workspaces/{workspaceId:guid}/articles/{slug}/related")]
    [AllowAnonymous]
    public async Task<ActionResult<List<SuggestedArticleResponse>>> Related(Guid workspaceId, string slug,
        [FromQuery, Range(1, 10)] int limit = 5, CancellationToken ct = default)
    {
        var source = await ArticlePublications.Public(db, workspaceId).SingleOrDefaultAsync(a => a.Slug == slug, ct);
        if (source is null) return NotFound();
        var keywords = Keywords.Extract([source.Title, source.Body]);
        if (keywords.Count == 0) return new List<SuggestedArticleResponse>();
        var candidates = await ArticlePublications.Public(db, workspaceId).Where(a => a.CollectionId == source.CollectionId && a.ArticleId != source.ArticleId)
            .OrderByDescending(a => a.UpdatedAt).ThenBy(a => a.ArticleId).Take(200).ToListAsync(ct);
        return ArticleSuggestions.Rank(candidates.Select(ArticlePublications.ForRanking), keywords, limit).Select(a => a.ToResponse()).ToList();
    }
}
