using Harbor.Domain;
using Harbor.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Harbor.Infrastructure;

public class HarborDbContext(DbContextOptions<HarborDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Inbox> Inboxes => Set<Inbox>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Teammate> Teammates => Set<Teammate>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ConversationTag> ConversationTags => Set<ConversationTag>();
    public DbSet<CannedReply> CannedReplies => Set<CannedReply>();
    public DbSet<AssignmentEvent> AssignmentEvents => Set<AssignmentEvent>();
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<SlaBreachEvent> SlaBreachEvents => Set<SlaBreachEvent>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<ArticleCollection> ArticleCollections => Set<ArticleCollection>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Segment> Segments => Set<Segment>();
    public DbSet<SavedQueue> SavedQueues => Set<SavedQueue>();
    public DbSet<ConversationBookmark> ConversationBookmarks => Set<ConversationBookmark>();
    public DbSet<ReplyDraft> ReplyDrafts => Set<ReplyDraft>();
    public DbSet<ConversationTransferEvent> ConversationTransferEvents => Set<ConversationTransferEvent>();
    public DbSet<ArticleRevision> ArticleRevisions => Set<ArticleRevision>();
    public DbSet<ArticleFeedback> ArticleFeedback => Set<ArticleFeedback>();
    public DbSet<CreationReceipt> CreationReceipts => Set<CreationReceipt>();
    public DbSet<InboundEmailReceipt> InboundEmailReceipts => Set<InboundEmailReceipt>();
    public DbSet<WebhookAttempt> WebhookAttempts => Set<WebhookAttempt>();
    public DbSet<TeammateApiKey> TeammateApiKeys => Set<TeammateApiKey>();
    public DbSet<ContactMergePlan> ContactMergePlans => Set<ContactMergePlan>();
    public DbSet<ContactImportBatch> ContactImportBatches => Set<ContactImportBatch>();
    public DbSet<ContactImportRow> ContactImportRows => Set<ContactImportRow>();
    public DbSet<InboxRoutingDefinition> InboxRoutingDefinitions => Set<InboxRoutingDefinition>();
    public DbSet<InboxRoutingVersion> InboxRoutingVersions => Set<InboxRoutingVersion>();
    public DbSet<ConversationWatcher> ConversationWatchers => Set<ConversationWatcher>();
    public DbSet<ReplyNotification> ReplyNotifications => Set<ReplyNotification>();
    public DbSet<ArticlePublicationSnapshot> ArticlePublicationSnapshots => Set<ArticlePublicationSnapshot>();
    public DbSet<ActiveArticlePublication> ActiveArticlePublications => Set<ActiveArticlePublication>();
    public DbSet<ArticlePublicationSchedule> ArticlePublicationSchedules => Set<ArticlePublicationSchedule>();
    public DbSet<WorkingCalendar> WorkingCalendars => Set<WorkingCalendar>();
    public DbSet<SlaRecalculationPlan> SlaRecalculationPlans => Set<SlaRecalculationPlan>();
    public DbSet<ConversationChecklistItem> ConversationChecklistItems => Set<ConversationChecklistItem>();
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();
    public DbSet<WorkspaceEvent> WorkspaceEvents => Set<WorkspaceEvent>();
    public DbSet<WorkspaceEventWatermark> WorkspaceEventWatermarks => Set<WorkspaceEventWatermark>();
    public DbSet<ConfigurationAuditEvent> ConfigurationAuditEvents => Set<ConfigurationAuditEvent>();
    public DbSet<ConversationExportJob> ConversationExportJobs => Set<ConversationExportJob>();

    /// <summary>
    /// SQLite's json_extract, mapped so segment rules over custom contact
    /// attributes are evaluated by the database rather than in memory.
    /// Never called in .NET — EF translates it into SQL.
    /// </summary>
    [DbFunction("json_extract", IsBuiltIn = true)]
    public static string? JsonExtract(string? json, string path) =>
        throw new NotSupportedException("json_extract is only callable inside a query.");

    /// <summary>
    /// SQLite trim with an explicit character set. The default SQL trim removes
    /// only ordinary spaces, which is narrower than .NET whitespace semantics.
    /// </summary>
    [DbFunction("trim", IsBuiltIn = true)]
    public static string? TrimCharacters(string? value, string characters) =>
        throw new NotSupportedException("trim is only callable inside a query.");

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no native DateTimeOffset ordering/comparison; store UTC ticks.
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<UtcDateTimeOffsetConverter>();
    }

    /// <summary>
    /// Rolls the concurrency token of every modified <see cref="IHasVersion"/>.
    ///
    /// EF puts the token's original value in the UPDATE's WHERE clause and the
    /// new one in its SET, so a writer working from a stale copy matches no row
    /// and gets a DbUpdateConcurrencyException instead of quietly overwriting
    /// whoever got there first. SQLite has no rowversion of its own, so the
    /// token is an ordinary Guid column we maintain here — one place, rather
    /// than every call site remembering.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        BumpVersions();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        BumpVersions();
        return base.SaveChanges();
    }

    private void BumpVersions()
    {
        foreach (var entry in ChangeTracker.Entries<IHasVersion>()
                     .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.Version = Guid.NewGuid();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ArticlePublicationSnapshot>(e =>
        {
            e.Property(p => p.Title).HasMaxLength(300);
            e.Property(p => p.Slug).HasMaxLength(200);
            e.Property(p => p.Body).HasMaxLength(100_000);
            e.HasIndex(p => new { p.ArticleId, p.PublishedAt });
            e.HasOne<Article>().WithMany().HasForeignKey(p => p.ArticleId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ActiveArticlePublication>(e =>
        {
            e.HasKey(p => p.ArticleId);
            e.Property(p => p.Slug).HasMaxLength(200);
            e.HasIndex(p => new { p.WorkspaceId, p.Slug }).IsUnique();
            e.HasOne<Article>().WithOne(a => a.ActivePublication).HasForeignKey<ActiveArticlePublication>(p => p.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Snapshot).WithMany().HasForeignKey(p => p.SnapshotId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ArticleCollection>().WithMany().HasForeignKey(p => p.CollectionId).OnDelete(DeleteBehavior.Restrict);
            e.Navigation(p => p.Snapshot).AutoInclude();
        });
        modelBuilder.Entity<ArticlePublicationSchedule>(e =>
        {
            e.Property(s => s.Version).IsConcurrencyToken();
            e.Property(s => s.Reason).HasMaxLength(100);
            e.HasIndex(s => s.ArticleId).IsUnique();
            e.HasIndex(s => new { s.Status, s.PublishAt });
            e.HasOne<Article>().WithMany().HasForeignKey(s => s.ArticleId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<InboxRoutingDefinition>(e =>
        {
            e.HasKey(d => d.InboxId);
            e.Property(d => d.Version).IsConcurrencyToken();
            e.HasOne<Inbox>().WithMany().HasForeignKey(d => d.InboxId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<InboxRoutingVersion>(e =>
        {
            e.Property(v => v.RulesJson).HasMaxLength(100_000);
            e.HasIndex(v => new { v.InboxId, v.CreatedAt });
            e.HasOne<Inbox>().WithMany().HasForeignKey(v => v.InboxId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ConversationWatcher>(e =>
        {
            e.HasKey(w => new { w.ConversationId, w.TeammateId });
            e.HasIndex(w => new { w.WorkspaceId, w.TeammateId });
            e.HasOne<Conversation>().WithMany().HasForeignKey(w => w.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(w => w.Teammate).WithMany().HasForeignKey(w => w.TeammateId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ReplyNotification>(e =>
        {
            e.HasIndex(n => new { n.MessageId, n.TeammateId }).IsUnique();
            e.HasIndex(n => new { n.WorkspaceId, n.TeammateId, n.CreatedAt });
            e.HasOne<Message>().WithMany().HasForeignKey(n => n.MessageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Conversation>().WithMany().HasForeignKey(n => n.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Teammate>().WithMany().HasForeignKey(n => n.TeammateId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ContactMergePlan>(e =>
        {
            e.Property(p => p.Fingerprint).HasMaxLength(64);
            e.Property(p => p.PreviewJson).HasMaxLength(100_000);
            e.HasIndex(p => new { p.WorkspaceId, p.CreatedAt });
            e.HasOne<Workspace>().WithMany().HasForeignKey(p => p.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ContactImportBatch>(e =>
        {
            e.HasIndex(b => new { b.WorkspaceId, b.CreatedAt });
            e.HasOne<Workspace>().WithMany().HasForeignKey(b => b.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ContactImportRow>(e =>
        {
            e.HasKey(r => new { r.BatchId, r.RowNumber });
            e.Property(r => r.NormalizedJson).HasMaxLength(20_000);
            e.Property(r => r.ErrorsJson).HasMaxLength(10_000);
            e.HasOne(r => r.Batch).WithMany(b => b.Rows).HasForeignKey(r => r.BatchId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<TeammateApiKey>(e =>
        {
            e.Property(k => k.Name).HasMaxLength(100);
            e.Property(k => k.Hash).HasMaxLength(64);
            e.HasIndex(k => k.Hash).IsUnique();
            e.HasIndex(k => new { k.WorkspaceId, k.TeammateId });
            e.HasOne(k => k.Teammate).WithMany().HasForeignKey(k => k.TeammateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Workspace>().WithMany().HasForeignKey(k => k.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<WebhookAttempt>(e =>
        {
            e.Property(a => a.FailureCode).HasMaxLength(100);
            e.HasIndex(a => new { a.DeliveryId, a.ClaimGeneration }).IsUnique();
            e.HasIndex(a => new { a.DeliveryId, a.AttemptNumber }).IsUnique();
            e.HasIndex(a => new { a.WorkspaceId, a.StartedAt });
            e.HasOne<WebhookDelivery>().WithMany().HasForeignKey(a => a.DeliveryId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CreationReceipt>(e =>
        {
            e.Property(r => r.Key).HasMaxLength(128);
            e.Property(r => r.Operation).HasMaxLength(100);
            e.Property(r => r.RequestHash).HasMaxLength(64);
            e.Property(r => r.ResponseJson).HasMaxLength(262_144);
            e.HasIndex(r => new { r.WorkspaceId, r.TeammateId, r.Operation, r.Key }).IsUnique();
            e.HasOne<Workspace>().WithMany().HasForeignKey(r => r.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Teammate>().WithMany().HasForeignKey(r => r.TeammateId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<InboundEmailReceipt>(e =>
        {
            e.Property(r => r.MessageId).HasMaxLength(500);
            e.Property(r => r.RequestHash).HasMaxLength(64);
            e.HasIndex(r => new { r.WorkspaceId, r.InboxId, r.MessageId }).IsUnique();
            e.HasOne<Workspace>().WithMany().HasForeignKey(r => r.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            // Result IDs are historical values: a later contact merge must not rewrite a receipt.
        });
        modelBuilder.Entity<ArticleRevision>(e =>
        {
            e.Property(r => r.Title).HasMaxLength(300);
            e.Property(r => r.Body).HasMaxLength(100_000);
            e.Property(r => r.Slug).HasMaxLength(200);
            e.HasIndex(r => new { r.ArticleId, r.RecordedAt, r.Id });
            e.HasOne<Article>().WithMany().HasForeignKey(r => r.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Workspace>().WithMany().HasForeignKey(r => r.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ArticleFeedback>(e =>
        {
            e.HasKey(f => new { f.ArticleId, f.TeammateId });
            e.HasIndex(f => new { f.WorkspaceId, f.TeammateId });
            e.HasOne<Article>().WithMany().HasForeignKey(f => f.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Teammate>().WithMany().HasForeignKey(f => f.TeammateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Workspace>().WithMany().HasForeignKey(f => f.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ConversationTransferEvent>(e =>
        {
            e.HasIndex(t => new { t.ConversationId, t.CreatedAt, t.Id });
            e.HasOne<Conversation>().WithMany().HasForeignKey(t => t.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SavedQueue>(e =>
        {
            e.Property(q => q.Name).HasMaxLength(100);
            e.Property(q => q.FilterJson).HasMaxLength(10_000);
            e.HasIndex(q => new { q.WorkspaceId, q.OwnerTeammateId });
            e.HasOne<Workspace>().WithMany().HasForeignKey(q => q.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Teammate>().WithMany().HasForeignKey(q => q.OwnerTeammateId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ConversationBookmark>(e =>
        {
            e.HasKey(b => new { b.TeammateId, b.ConversationId });
            e.HasIndex(b => new { b.WorkspaceId, b.TeammateId, b.CreatedAt });
            e.HasOne<Workspace>().WithMany().HasForeignKey(b => b.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Teammate>().WithMany().HasForeignKey(b => b.TeammateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(b => b.Conversation).WithMany().HasForeignKey(b => b.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ReplyDraft>(e =>
        {
            e.Property(d => d.Body).HasMaxLength(20_000);
            e.Property(d => d.Version).IsConcurrencyToken();
            e.HasIndex(d => new { d.ConversationId, d.OwnerTeammateId }).IsUnique();
            e.HasIndex(d => new { d.WorkspaceId, d.OwnerTeammateId });
            e.HasOne<Workspace>().WithMany().HasForeignKey(d => d.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Teammate>().WithMany().HasForeignKey(d => d.OwnerTeammateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Conversation>().WithMany().HasForeignKey(d => d.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Workspace>(e =>
        {
            e.Property(w => w.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<Inbox>(e =>
        {
            e.Property(i => i.Name).HasMaxLength(200);
            e.Property(i => i.EmailAddress).HasMaxLength(320);
            // Inbound mail is routed by this address, so it must identify one
            // inbox. SQLite treats NULLs as distinct, which is what we want:
            // any number of inboxes can be chat-only.
            e.HasIndex(i => new { i.WorkspaceId, i.EmailAddress }).IsUnique();
            e.HasOne(i => i.Workspace)
                .WithMany(w => w.Inboxes)
                .HasForeignKey(i => i.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Contact>(e =>
        {
            // Existing rows receive a nonempty initial token on upgrade. Each new
            // Contact still supplies its own token; row identity also scopes every update.
            e.Property(c => c.Version).IsConcurrencyToken()
                .HasDefaultValue(new Guid("7e665f7d-f82d-4a96-b10b-96b74b4dc102"));
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.Email).HasMaxLength(320);
            e.Property(c => c.ExternalId).HasMaxLength(200);
            e.Property(c => c.AttributesJson).HasMaxLength(10_000);
            e.HasIndex(c => new { c.WorkspaceId, c.Email });
            e.HasOne(c => c.Workspace)
                .WithMany()
                .HasForeignKey(c => c.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Teammate>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(200);
            e.Property(t => t.Email).HasMaxLength(320);
            e.Property(t => t.ApiKeyHash).HasMaxLength(64);
            e.HasIndex(t => new { t.WorkspaceId, t.Email }).IsUnique();
            e.HasIndex(t => t.ApiKeyHash).IsUnique();
            e.HasOne(t => t.Workspace)
                .WithMany()
                .HasForeignKey(t => t.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(200);
            e.HasIndex(t => new { t.WorkspaceId, t.Name }).IsUnique();
            e.HasOne(t => t.Workspace)
                .WithMany()
                .HasForeignKey(t => t.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TeamMembership>(e =>
        {
            e.HasKey(m => new { m.TeamId, m.TeammateId });
            e.HasOne(m => m.Team)
                .WithMany(t => t.Members)
                .HasForeignKey(m => m.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.Teammate)
                .WithMany(t => t.Memberships)
                .HasForeignKey(m => m.TeammateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Conversation>(e =>
        {
            e.Property(c => c.Subject).HasMaxLength(500);
            e.Property(c => c.Version).IsConcurrencyToken();
            e.HasIndex(c => new { c.WorkspaceId, c.State });
            e.HasIndex(c => c.LastMessageAt);
            e.HasOne(c => c.Workspace)
                .WithMany()
                .HasForeignKey(c => c.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Inbox)
                .WithMany()
                .HasForeignKey(c => c.InboxId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.Contact)
                .WithMany()
                .HasForeignKey(c => c.ContactId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.AssignedTeammate)
                .WithMany()
                .HasForeignKey(c => c.AssignedTeammateId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(c => c.AssignedTeam)
                .WithMany()
                .HasForeignKey(c => c.AssignedTeamId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Message>(e =>
        {
            e.Property(m => m.Body).HasMaxLength(20_000);
            e.Property(m => m.EmailMessageId).HasMaxLength(500);
            e.HasIndex(m => new { m.ConversationId, m.CreatedAt });
            // Inbound threading looks messages up by their Message-ID.
            e.HasIndex(m => m.EmailMessageId);
            e.HasOne(m => m.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.AuthorContact)
                .WithMany()
                .HasForeignKey(m => m.AuthorContactId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.AuthorTeammate)
                .WithMany()
                .HasForeignKey(m => m.AuthorTeammateId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Tag>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(100);
            e.HasIndex(t => new { t.WorkspaceId, t.Name }).IsUnique();
            e.HasOne(t => t.Workspace)
                .WithMany()
                .HasForeignKey(t => t.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConversationTag>(e =>
        {
            e.HasKey(ct => new { ct.ConversationId, ct.TagId });
            e.HasOne(ct => ct.Conversation)
                .WithMany(c => c.Tags)
                .HasForeignKey(ct => ct.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ct => ct.Tag)
                .WithMany()
                .HasForeignKey(ct => ct.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AssignmentEvent>(e =>
        {
            // Actor/from/to ids are intentionally unconstrained: the audit
            // trail must survive directory changes.
            e.HasIndex(a => new { a.ConversationId, a.CreatedAt });
            e.HasOne(a => a.Conversation)
                .WithMany()
                .HasForeignKey(a => a.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SlaPolicy>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200);
            e.HasIndex(p => new { p.WorkspaceId, p.InboxId, p.Priority });
            e.HasOne(p => p.Workspace)
                .WithMany()
                .HasForeignKey(p => p.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            // Deleting an inbox drops the policies written for it; workspace-wide
            // policies (null InboxId) are unaffected.
            e.HasOne(p => p.Inbox)
                .WithMany()
                .HasForeignKey(p => p.InboxId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SlaBreachEvent>(e =>
        {
            // One event per conversation per kind makes detection idempotent.
            e.HasIndex(b => new { b.ConversationId, b.Kind }).IsUnique();
            e.HasOne(b => b.Conversation)
                .WithMany()
                .HasForeignKey(b => b.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookSubscription>(e =>
        {
            e.Property(s => s.Url).HasMaxLength(2_000);
            e.Property(s => s.Secret).HasMaxLength(100);
            e.HasOne(s => s.Workspace)
                .WithMany()
                .HasForeignKey(s => s.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookSubscriptionEvent>(e =>
        {
            e.HasKey(x => new { x.SubscriptionId, x.EventType });
            e.HasOne(x => x.Subscription)
                .WithMany(s => s.Events)
                .HasForeignKey(x => x.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookDelivery>(e =>
        {
            e.HasIndex(d => d.OriginalDeliveryId).IsUnique();
            e.Property(d => d.Version).IsConcurrencyToken();
            e.Property(d => d.Payload).HasMaxLength(100_000);
            e.Property(d => d.Error).HasMaxLength(2_000);
            // The dispatcher's hot query: pending deliveries that are due.
            e.HasIndex(d => new { d.WorkspaceId, d.Status, d.NextAttemptAt });
            e.HasOne(d => d.Subscription)
                .WithMany()
                .HasForeignKey(d => d.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ArticleCollection>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.Slug).HasMaxLength(200);
            e.Property(c => c.Description).HasMaxLength(2_000);
            e.HasIndex(c => new { c.WorkspaceId, c.Slug }).IsUnique();
            e.HasOne(c => c.Workspace)
                .WithMany()
                .HasForeignKey(c => c.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Article>(e =>
        {
            e.Navigation(a => a.ActivePublication).AutoInclude();
            e.Property(a => a.Version).IsConcurrencyToken()
                .HasDefaultValue(new Guid("8bb6174c-b78a-448f-b65f-bad2a07e7919"));
            e.Property(a => a.Title).HasMaxLength(300);
            e.Property(a => a.Slug).HasMaxLength(200);
            e.Property(a => a.Body).HasMaxLength(100_000);
            e.HasIndex(a => new { a.WorkspaceId, a.Slug }).IsUnique();
            // The public listing's hot path: published articles in a workspace.
            e.HasIndex(a => new { a.WorkspaceId, a.Status });
            e.HasOne(a => a.Workspace)
                .WithMany()
                .HasForeignKey(a => a.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            // Restrict, not Cascade: deleting a collection must not silently
            // take its articles with it. The API returns 409 instead.
            e.HasOne(a => a.Collection)
                .WithMany(c => c.Articles)
                .HasForeignKey(a => a.CollectionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Segment>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(200);
            e.Property(s => s.RulesJson).HasMaxLength(10_000);
            e.HasIndex(s => new { s.WorkspaceId, s.Name }).IsUnique();
            e.HasOne(s => s.Workspace)
                .WithMany()
                .HasForeignKey(s => s.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkingCalendar>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.DefinitionJson).HasMaxLength(20_000);
            e.HasIndex(c => new { c.WorkspaceId, c.CreatedAt });
            e.HasOne<Workspace>().WithMany().HasForeignKey(c => c.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ConversationExportJob>(e =>
        {
            e.Property(j => j.Version).IsConcurrencyToken();
            e.Property(j => j.FilterJson).HasMaxLength(20_000);
            e.Property(j => j.CapturedSegmentRulesJson).HasMaxLength(10_000);
            e.Property(j => j.FailureCode).HasMaxLength(100);
            e.Property(j => j.Sha256).HasMaxLength(64);
            e.HasIndex(j => new { j.Status, j.LeaseExpiresAt, j.CreatedAt });
            e.HasIndex(j => new { j.WorkspaceId, j.CreatedAt });
            e.HasOne<Workspace>().WithMany().HasForeignKey(j => j.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<WorkspaceEvent>(e =>
        {
            e.HasKey(x => x.Sequence);
            e.Property(x => x.Sequence).ValueGeneratedOnAdd();
            e.Property(x => x.MetadataJson).HasMaxLength(4000);
            e.HasIndex(x => new { x.WorkspaceId, x.Sequence });
            e.HasIndex(x => x.EventId).IsUnique();
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<WorkspaceEventWatermark>(e =>
        {
            e.HasKey(x => x.WorkspaceId);
            e.HasOne<Workspace>().WithOne().HasForeignKey<WorkspaceEventWatermark>(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ConfigurationAuditEvent>(e =>
        {
            e.Property(x => x.ChangedFieldsJson).HasMaxLength(1000);
            e.Property(x => x.AllowedChangesJson).HasMaxLength(4000);
            e.HasIndex(x => new { x.WorkspaceId, x.OccurredAt, x.Id });
            e.HasIndex(x => new { x.WorkspaceId, x.ActorTeammateId });
            e.HasIndex(x => new { x.WorkspaceId, x.EntityKind, x.EntityId });
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MessageAttachment>(e =>
        {
            e.Property(a => a.Version).IsConcurrencyToken();
            e.Property(a => a.FileName).HasMaxLength(160);
            e.Property(a => a.Sha256).HasMaxLength(64);
            e.HasIndex(a => a.StorageId).IsUnique();
            e.HasIndex(a => new { a.WorkspaceId, a.State, a.ExpiresAt });
            e.HasOne<Conversation>().WithMany().HasForeignKey(a => a.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Message>().WithMany(m => m.Attachments).HasForeignKey(a => a.MessageId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Message>().Navigation(m => m.Attachments).AutoInclude();
        modelBuilder.Entity<ConversationChecklistItem>(e =>
        {
            e.Property(i => i.Title).HasMaxLength(200);
            e.Property(i => i.Version).IsConcurrencyToken();
            e.HasIndex(i => new { i.ConversationId, i.Required, i.CompletedAt });
            e.HasOne<Conversation>().WithMany().HasForeignKey(i => i.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SlaPolicy>().HasOne<WorkingCalendar>().WithMany().HasForeignKey(p => p.WorkingCalendarId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Conversation>().HasOne<WorkingCalendar>().WithMany().HasForeignKey(c => c.AppliedWorkingCalendarId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SlaRecalculationPlan>(e =>
        {
            e.Property(p => p.Fingerprint).HasMaxLength(64);
            e.Property(p => p.ItemsJson).HasMaxLength(200_000);
            e.Property(p => p.ResultJson).HasMaxLength(200_000);
            e.HasIndex(p => new { p.WorkspaceId, p.CreatedAt });
            e.HasOne<Workspace>().WithMany().HasForeignKey(p => p.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CannedReply>(e =>
        {
            e.Property(r => r.Shortcut).HasMaxLength(100);
            e.Property(r => r.Title).HasMaxLength(200);
            e.Property(r => r.Body).HasMaxLength(20_000);
            e.HasIndex(r => new { r.WorkspaceId, r.Shortcut }).IsUnique();
            e.HasOne(r => r.Workspace)
                .WithMany()
                .HasForeignKey(r => r.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
