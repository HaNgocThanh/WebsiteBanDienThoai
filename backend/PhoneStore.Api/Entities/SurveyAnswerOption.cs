namespace PhoneStore.Api.Entities;

public class SurveyAnswerOption
{
    public long AnswerId { get; set; }
    public long QuestionId { get; set; }
    public long OptionId { get; set; }

    public SurveyAnswer Answer { get; set; } = null!;
    public SurveyOption Option { get; set; } = null!;
}

