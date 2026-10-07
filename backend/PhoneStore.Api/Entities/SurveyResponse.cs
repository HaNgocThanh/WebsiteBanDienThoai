namespace PhoneStore.Api.Entities;

public class SurveyResponse
{
    public long Id { get; set; }
    public long SurveyId { get; set; }
    public Guid UserId { get; set; }
    public DateTime SubmittedAt { get; set; }

    public Survey Survey { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
    public ICollection<SurveyAnswer> Answers { get; set; } = new List<SurveyAnswer>();
}

