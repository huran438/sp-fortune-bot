namespace Fortune.Bot;

class BotSettings
{
    public string ApiKey { get; set; } = "";
    public List<string> Administrators { get; set; } = new() { "" };
    public string ParticipantsFilePath { get; set; } = "./data/participants.json";
    public string HistoryFilePath { get; set; } = "./data/history.json";
    public int NumberOfWinners { get; set; } = 3;
}