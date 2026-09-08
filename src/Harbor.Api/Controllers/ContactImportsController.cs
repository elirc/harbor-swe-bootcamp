using System.Text;
using System.Text.Json;
using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
public class ContactImportsController(HarborDbContext db, TimeProvider clock) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpPost("api/workspaces/{workspaceId:guid}/contact-imports")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<ActionResult<ContactImportResponse>> Stage(Guid workspaceId, StageContactImportRequest request, CancellationToken ct)
    {
        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(request.Rows, Json)) > 3 * 1024 * 1024)
            return StatusCode(413, new ProblemDetails { Status = 413, Title = "Import exceeds 3 MiB" });
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct)) return NotFound();
        var now = clock.GetUtcNow();
        var batch = new ContactImportBatch { WorkspaceId = workspaceId, CreatedByTeammateId = User.GetTeammateId(),
            CreatedAt = now, ExpiresAt = now.AddHours(24) };
        for (var i = 0; i < request.Rows!.Count; i++)
        {
            ImportValidation validation;
            try { validation = ContactImportValidation.Validate(request.Rows[i].Deserialize<CreateContactRequest>(Json)); }
            catch (JsonException) { validation = new ImportValidation(null, [new("row", "The row does not match the contact input shape.")]); }
            if (validation.Errors.Count > 0) batch.Status = ContactImportStatus.Invalid;
            batch.Rows.Add(new ContactImportRow { BatchId = batch.Id, RowNumber = i + 1,
                NormalizedJson = validation.Contact is null ? null : JsonSerializer.Serialize(validation.Contact, Json),
                ErrorsJson = JsonSerializer.Serialize(validation.Errors, Json) });
        }
        db.ContactImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = batch.Id }, ResponseFor(batch));
    }

    [HttpGet("api/contact-imports/{id:guid}")]
    public async Task<ActionResult<ContactImportResponse>> Get(Guid id, CancellationToken ct)
    {
        var batch = await db.ContactImportBatches.AsNoTracking().Include(b => b.Rows)
            .SingleOrDefaultAsync(b => b.Id == id && b.WorkspaceId == User.GetWorkspaceId(), ct);
        return batch is null ? NotFound() : ResponseFor(batch);
    }

    [HttpPost("api/contact-imports/{id:guid}/commit")]
    public Task<ActionResult<ContactImportResponse>> Commit(Guid id, CancellationToken ct) =>
        WriteTransactions.RunAsync<ActionResult<ContactImportResponse>>(db, async () =>
        {
            var batch = await db.ContactImportBatches.Include(b => b.Rows)
                .SingleOrDefaultAsync(b => b.Id == id && b.WorkspaceId == User.GetWorkspaceId(), ct);
            if (batch is null) return NotFound();
            if (batch.Status == ContactImportStatus.Committed) return ResponseFor(batch);
            var now = clock.GetUtcNow();
            if (now >= batch.ExpiresAt) return StatusCode(410, new ProblemDetails { Status = 410, Title = "Import expired" });
            if (batch.Status != ContactImportStatus.Ready || batch.SchemaVersion != 1)
                return UnprocessableEntity(new ProblemDetails { Status = 422, Title = "Import is not ready" });
            foreach (var row in batch.Rows.OrderBy(r => r.RowNumber))
            {
                var input = JsonSerializer.Deserialize<ValidatedImportContact>(row.NormalizedJson!, Json)!;
                var contact = new Contact { WorkspaceId = batch.WorkspaceId, Name = input.Name, Email = input.Email,
                    ExternalId = input.ExternalId, AttributesJson = input.AttributesJson, CreatedAt = now };
                db.Contacts.Add(contact);
                row.ContactId = contact.Id;
            }
            batch.Status = ContactImportStatus.Committed;
            batch.CommittedAt = now;
            await db.SaveChangesAsync(ct);
            return ResponseFor(batch);
        }, ct);

    private static ContactImportResponse ResponseFor(ContactImportBatch batch) => new(batch.Id, batch.Status,
        batch.ExpiresAt, batch.CommittedAt, batch.Rows.OrderBy(r => r.RowNumber).Select(r => new ContactImportRowResponse(r.RowNumber,
            r.NormalizedJson is null ? null : JsonSerializer.Deserialize<ValidatedImportContact>(r.NormalizedJson, Json),
            JsonSerializer.Deserialize<List<ImportFieldError>>(r.ErrorsJson, Json)!, r.ContactId)).ToList());
}
