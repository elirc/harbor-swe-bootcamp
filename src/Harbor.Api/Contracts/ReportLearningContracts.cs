namespace Harbor.Api.Contracts;

public record BacklogAgeResponse(DateTimeOffset EvaluatedAt, int Total, int Open, int Snoozed,
    int Under24Hours, int From24To72Hours, int AtLeast72Hours);
public record TeamWorkloadResponse(Guid Id, string Name, int MemberCount, int AvailableMemberCount,
    int Conversations, int Open, int Snoozed, int Closed);
