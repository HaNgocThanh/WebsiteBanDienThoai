using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
namespace PhoneStore.Api.DTOs.Surveys;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SurveyQuestionInput([Required,MaxLength(1000)] string Text,[Required,RegularExpression("^(SingleChoice|MultipleChoice|Text)$")] string Type,bool IsRequired,[Required,MaxLength(20)] List<string> Options);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SurveyInput([Required,MaxLength(200)] string Title,[MaxLength(2000)] string? Description,DateTime StartsAt,DateTime EndsAt,[Required,MinLength(1),MaxLength(50)] List<SurveyQuestionInput> Questions,Guid OperationKey);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SurveyAction(Guid OperationKey);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SurveyAnswerInput([Required] string QuestionId,[MaxLength(4000)] string? TextValue,[MaxLength(20)] List<string>? OptionIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SurveySubmit([Required,MaxLength(50)] List<SurveyAnswerInput> Answers);
public sealed record SurveyOptionDto(string Id,string Text);
public sealed record SurveyQuestionDto(string Id,string Text,string Type,bool IsRequired,IReadOnlyList<SurveyOptionDto> Options);
public sealed record SurveyDto(string Id,string Title,string? Description,string Status,DateTime StartsAt,DateTime EndsAt,string Version,IReadOnlyList<SurveyQuestionDto> Questions);
public sealed record SurveySummary(string Id,string Title,string? Description,string Status,DateTime StartsAt,DateTime EndsAt,bool HasResponded);
public sealed record SurveyReceipt(string Id,string Status,string Version);
public sealed record SurveySubmission(string SurveyId,bool Submitted,DateTime? SubmittedAt);
public sealed record SurveyOptionResult(string Id,string Text,long RespondentCount);
public sealed record SurveyQuestionResult(string Id,string Text,string Type,bool IsRequired,long AnsweredCount,long SelectionCount,IReadOnlyList<SurveyOptionResult> Options);
public sealed record SurveyResults(string SurveyId,string Title,long ParticipantCount,IReadOnlyList<SurveyQuestionResult> Questions);
public sealed record SurveyTextResult(string AnswerId,string TextValue,DateTime SubmittedAt);
