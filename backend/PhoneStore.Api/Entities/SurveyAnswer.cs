namespace PhoneStore.Api.Entities;

public class SurveyAnswer
{
    public long Id { get; set; }
    public long SurveyId { get; set; }
    public long ResponseId { get; set; }
    public long QuestionId { get; set; }
    public string? TextValue { get; set; }

    public SurveyResponse Response { get; set; } = null!;
    public SurveyQuestion Question { get; set; } = null!;
    public ICollection<SurveyAnswerOption> SelectedOptions { get; set; } = new List<SurveyAnswerOption>();
}

