using System.Net;
using System.Net.Http.Json;
using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Harbor.Tests.Integration;

public class S13AttachmentTests(HarborApiFactory factory) : ApiTestBase(factory)
{
    private Task<HttpResponseMessage> Upload(Guid conversation, byte[] bytes, string filename = "proof.txt")
    {
        var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes), "file", filename);
        return Client.PostAsync($"/api/conversations/{conversation}/attachments", form);
    }

    [Fact]
    public async Task OnlyUploaderCanClaimStagedFileAndAttachedDownloadUsesConversationScope()
    {
        var ws = await CreateWorkspaceAsync(); var inbox = await CreateInboxAsync(ws.Id); var contact = await CreateContactAsync(ws.Id);
        var conversation = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var other = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var agent = await CreateTeammateAsync(ws.Id);
        var upload = await ReadAsync<AttachmentResponse>(await Upload(conversation.Id, [1, 2, 3], "../proof.txt"));
        Assert.DoesNotContain('/', upload.FileName);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PostAsJsonAsync($"/api/conversations/{other.Id}/messages",
            new AddMessageRequest(AuthorType.Contact, contact.Id, MessageKind.Reply, "Wrong thread", [upload.Id]), Json)).StatusCode);
        ActAs(agent.ApiKey);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/attachments/{upload.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages",
            new AddMessageRequest(AuthorType.Teammate, agent.Id, MessageKind.Note, "Wrong uploader", [upload.Id]), Json)).StatusCode);
        ActAsAdminOf(ws.Id);
        var message = await ReadAsync<MessageResponse>(await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages",
            new AddMessageRequest(AuthorType.Contact, contact.Id, MessageKind.Reply, "Evidence", [upload.Id]), Json));
        Assert.Equal(upload.Id, Assert.Single(message.Attachments!).Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages",
            new AddMessageRequest(AuthorType.Contact, contact.Id, MessageKind.Reply, "Second claim", [upload.Id]), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.DeleteAsync($"/api/attachments/{upload.Id}")).StatusCode);
        ActAs(agent.ApiKey);
        using var download = await Client.GetAsync($"/api/attachments/{upload.Id}/content");
        Assert.Equal("application/octet-stream", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(new byte[] { 1, 2, 3 }, await download.Content.ReadAsByteArrayAsync());
        var list = await ReadAsync<List<MessageResponse>>(await Client.GetAsync($"/api/conversations/{conversation.Id}/messages"));
        Assert.Single(list.Single(m => m.Id == message.Id).Attachments!);
    }

    [Fact]
    public async Task FailedMessageLeavesUploadStagedAndMissingBlobIsAnExplicitFailure()
    {
        var ws = await CreateWorkspaceAsync(); var inbox = await CreateInboxAsync(ws.Id); var contact = await CreateContactAsync(ws.Id);
        var conversation = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        var upload = await ReadAsync<AttachmentResponse>(await Upload(conversation.Id, [42]));
        Factory.FailCommand = command => command.CommandText.Contains("INSERT INTO \"Messages\"");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages",
                new AddMessageRequest(AuthorType.Contact, contact.Id, MessageKind.Reply, "Failure", [upload.Id]), Json)).StatusCode);
        }
        finally { Factory.FailCommand = null; }
        Guid storage = default;
        Factory.WithDb(db => { var row = db.MessageAttachments.Single(a => a.Id == upload.Id); Assert.Equal(AttachmentState.Uploaded, row.State); Assert.Null(row.MessageId); storage = row.StorageId; });
        Factory.Services.GetRequiredService<PrivateFiles>().Delete(ws.Id, ArtifactKind.Attachments, storage);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Client.GetAsync($"/api/attachments/{upload.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/attachments/{upload.Id}")).StatusCode);
    }

    [Fact]
    public async Task SizeLimitAndExpiryCleanupNeverExposeOrphanFiles()
    {
        var ws = await CreateWorkspaceAsync(); var inbox = await CreateInboxAsync(ws.Id); var contact = await CreateContactAsync(ws.Id);
        var conversation = await StartConversationAsync(ws.Id, inbox.Id, contact.Id);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Upload(conversation.Id, new byte[5 * 1024 * 1024 + 1])).StatusCode);
        var upload = await ReadAsync<AttachmentResponse>(await Upload(conversation.Id, [7]));
        var files = Factory.Services.GetRequiredService<PrivateFiles>();
        var orphan = await files.StoreAsync(ws.Id, ArtifactKind.Attachments, new MemoryStream([9]), 10, default);
        File.SetLastWriteTimeUtc(files.PathFor(ws.Id, ArtifactKind.Attachments, orphan.StorageId), DateTime.UtcNow.AddDays(-2));
        Factory.FrozenTime = upload.ExpiresAt;
        try
        {
            Assert.Equal(HttpStatusCode.Gone, (await Client.GetAsync($"/api/attachments/{upload.Id}/content")).StatusCode);
            (await Client.PostAsync($"/api/workspaces/{ws.Id}/attachments/reconcile", null)).EnsureSuccessStatusCode();
            Factory.WithDb(db => Assert.Empty(db.MessageAttachments.Where(a => a.WorkspaceId == ws.Id)));
            Assert.Empty(files.Artifacts(ws.Id, ArtifactKind.Attachments));
        }
        finally { Factory.FrozenTime = null; }
    }
}
