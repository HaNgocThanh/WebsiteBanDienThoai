namespace PhoneStore.Api.Entities;

public class SurveyQuestion
{
    public long Id { get; set; }
    public long SurveyId { get; set; }
    public string Text { get; set; } = string.Empty;
    public SurveyQuestionType Type { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }

    public Survey Survey { get; set; } = null!;
    public ICollection<SurveyOption> Options { get; set; } = new List<SurveyOption>();
    public ICollection<SurveyAnswer> Answers { get; set; } = new List<SurveyAnswer>();
}

