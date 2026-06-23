namespace MartialHeroes.Assets.Parsers.DataTables.Models;

public sealed class AutoQuestionRecord
{
    public required uint QuestionId { get; init; }

    public required string QuestionText { get; init; }

    public required string AnswerPrompt { get; init; }
}