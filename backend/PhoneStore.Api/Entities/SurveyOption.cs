namespace PhoneStore.Api.Entities;

public class SurveyOption
{
    public long Id { get; set; }
    public long QuestionId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public SurveyQuestion Question { get; set; } = null!;
    public ICollection<SurveyAnswerOption> Answers { get; set; } = new List<SurveyAnswerOption>();
}

