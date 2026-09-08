using Harbor.Api.Contracts;
using Harbor.Domain;
using Harbor.Domain.Entities;
using Harbor.Infrastructure;

namespace Harbor.Api.Infrastructure;

public static class ManualAssignments
{
    /// <summary>Caller validates target ownership. Assignment, history, and outbox are staged together.</summary>
    public static async Task StageAsync(HarborDbContext db, Conversation conversation, Guid actor,
        Guid? teammateId, Guid? teamId, DateTimeOffset now)
    {
        var previousTeammate = conversation.AssignedTeammateId;
        var previousTeam = conversation.AssignedTeamId;
        if (teammateId is { } teammate) conversation.AssignToTeammate(teammate, now);
        else if (teamId is { } team) conversation.AssignToTeam(team, now);
        else conversation.Unassign(now);
        if (previousTeammate != conversation.AssignedTeammateId || previousTeam != conversation.AssignedTeamId)
            WorkspaceEvents.Assignment(db, conversation, now);
        db.AssignmentEvents.Add(new AssignmentEvent { ConversationId = conversation.Id, Kind = AssignmentKind.Manual,
            ActorTeammateId = actor, FromTeammateId = previousTeammate, FromTeamId = previousTeam,
            ToTeammateId = conversation.AssignedTeammateId, ToTeamId = conversation.AssignedTeamId, CreatedAt = now });
        if (conversation.AssignedTeammateId is not null || conversation.AssignedTeamId is not null)
            await Webhooks.PublishAsync(db, conversation.WorkspaceId, WebhookEventType.ConversationAssigned, conversation.ToSummaryResponse(), now);
    }
}
