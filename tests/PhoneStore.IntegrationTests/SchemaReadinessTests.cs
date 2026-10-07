using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PhoneStore.Api.Entities;

namespace PhoneStore.IntegrationTests;

public sealed class ReadyFactory(string connection) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
        .UseEnvironment("Testing")
        .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?>
        { ["ConnectionStrings:DefaultConnection"] = connection }));
}

[Collection("SQL")]
public class SchemaReadinessTests(SqlFixture fixture)
{
    [Fact]
    public async Task ReadyRequiresAppliedSchemaAndDoesNotMigrateOnRequest()
    {
        using var ready = new ReadyFactory(fixture.ConnectionString);
        Assert.Equal(HttpStatusCode.OK, (await ready.CreateClient().GetAsync("/api/ready")).StatusCode);
        var old = new SqlFixture();
        await old.InitializeAtAsync("20261006173535_InitialCreate");
        try
        {
            using var pending = new ReadyFactory(old.ConnectionString);
            var response = await pending.CreateClient().GetAsync("/api/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("NOT_READY", json.GetProperty("code").GetString());
            await using var db = old.CreateContext();
            Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        }
        finally { await old.DisposeAsync(); }
    }

    [Fact]
    public async Task SurveyCompositeFkRejectsOptionFromAnotherQuestion()
    {
        await using var db = fixture.CreateContext();
        var user = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Fixture", Email = Guid.NewGuid() + "@example.invalid" };
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        var survey = new Survey { Title = "Fixture", CreatedByUser = user, StartsAt = DateTime.UtcNow, EndsAt = DateTime.UtcNow.AddDays(1) };
        var first = new SurveyQuestion { Survey = survey, Text = "Question 1", Type = SurveyQuestionType.SingleChoice, SortOrder = 0 };
        var second = new SurveyQuestion { Survey = survey, Text = "Question 2", Type = SurveyQuestionType.SingleChoice, SortOrder = 1 };
        var option = new SurveyOption { Question = second, Text = "Wrong question option", SortOrder = 0 };
        var response = new SurveyResponse { Survey = survey, User = user, SubmittedAt = DateTime.UtcNow };
        db.AddRange(first, second, option, response);
        await db.SaveChangesAsync();
        var answer = new SurveyAnswer { SurveyId = survey.Id, ResponseId = response.Id, QuestionId = first.Id };
        db.SurveyAnswers.Add(answer);
        await db.SaveChangesAsync();
        db.SurveyAnswerOptions.Add(new SurveyAnswerOption { AnswerId = answer.Id, QuestionId = first.Id, OptionId = option.Id });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    [Fact]
    public async Task SqlSchemaUsesExpectedMoneyUtcAndHistoricalNoAction()
    {
        await using var sql = new SqlConnection(fixture.ConnectionString);
        await sql.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id
            WHERE (t.name='ProductVariants' AND c.name='Price' AND c.precision=18 AND c.scale=0)
               OR (t.name='Orders' AND c.name='CreatedAt' AND c.system_type_id=42 AND c.scale=3)
               OR (t.name='Orders' AND c.name='UserId' AND c.is_nullable=1)
               OR (t.name='AspNetUserLogins' AND c.name='ProviderKey' AND c.max_length=256);
            """, sql);
        Assert.Equal(4, (int)(await command.ExecuteScalarAsync())!);
        command.CommandText = "SELECT COUNT(*) FROM sys.foreign_keys WHERE delete_referential_action <> 0";
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
    }
}
