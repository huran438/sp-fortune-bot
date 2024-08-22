using Telegram.Bot.Types;

class DrawResult
{
    public User User { get; set; }
    public bool IsWinner { get; set; }
    public DateTime DrawDateTime { get; set; }
}