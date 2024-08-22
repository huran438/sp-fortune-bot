namespace Fortune.Bot;

class BotSettings
{
    public string ApiKey { get; set; } = "";
    public List<string> Administrators { get; set; } = new() { "" };
    public string ParticipantsFilePath { get; set; } = "participants.json";
    public string HistoryFilePath { get; set; } = "history.json";
    public int NumberOfWinners { get; set; } = 3;
}