namespace RevolutionaryWebApp.Shared.Models;

using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<BackupOperation>))]
public enum BackupOperation
{
    PostgreSql = 1,
    Cache = 2,
    Mail = 3,
    Bunny = 4,
    KopiaPrimary = 5,
    KopiaSecondary = 6,
}
