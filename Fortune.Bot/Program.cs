using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using File = System.IO.File;

namespace Fortune.Bot;

class Program
{
    private static TelegramBotClient _botClient;
    private static List<Participant> _participants = new();
    private static CancellationTokenSource _cts;
    private static List<Participant> _manualWinners = new();
    private static BotSettings _settings;
    private static bool _manualSelectionMode;
    private static bool _shutdown;

    static async Task Main()
    {
        await LoadSettings();

        _botClient = new TelegramBotClient(_settings.ApiKey);
        _cts = new CancellationTokenSource();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = []
        };

        _botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            cancellationToken: _cts.Token
        );

        await LoadParticipants();

        Console.WriteLine("Bot is running...");

        while (!_shutdown)
        {
            await Task.Yield();
        }

        Console.WriteLine("Bot is shutting down...");

        await _cts.CancelAsync();
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
        CancellationToken cancellationToken)
    {
        if (update.Type == UpdateType.Message && update.Message != null && update.Message.Type == MessageType.Text)
        {
            var message = update.Message;

            if (message.Text.StartsWith("/start"))
            {
                if (IsAdmin(message.From.Username))
                {
                    await SendAdminCommands(message.Chat.Id);
                }
                else
                {
                    var joinButton =
                        new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Вступить в розыгрыш", "join"));
                    await botClient.SendTextMessageAsync(message.Chat.Id,
                        "Добро пожаловать в розыгрыш! Нажмите кнопку ниже для участия.", replyMarkup: joinButton,
                        cancellationToken: cancellationToken);
                }
            }
            else if (message.Text.StartsWith("/join"))
            {
                await RegisterParticipant(message.From, message.Chat.Id);
            }
            else if (IsAdmin(message.From.Username))
            {
                if (message.Text.StartsWith("/draw"))
                {
                    await DrawWinnersAndNotifyParticipants(message.Chat.Id);
                }
                else if (message.Text.StartsWith("/shutdown"))
                {
                    _shutdown = true;
                }
                else if (message.Text.StartsWith("/reset"))
                {
                    await ResetParticipants();
                    await botClient.SendTextMessageAsync(message.Chat.Id, "Результаты розыгрыша сброшены.",
                        cancellationToken: cancellationToken);
                }
                else if (message.Text.StartsWith("/setwinners"))
                {
                    await SetNumberOfWinners(message);
                }
                else if (message.Text.StartsWith("/participants"))
                {
                    await SendParticipantsList(message.Chat.Id);
                }
                else if (message.Text.StartsWith("/history"))
                {
                    await SendDrawHistory(message.Chat.Id);
                }
                else if (message.Text.StartsWith("/manualstart"))
                {
                    await StartManualSelection(message.Chat.Id);
                }
                else if (message.Text.StartsWith("/manualstop"))
                {
                    await StopManualSelection(message.Chat.Id);
                }
                else if (_manualSelectionMode && message.Text.StartsWith("/addwinner"))
                {
                    await AddManualWinner(message.Chat.Id, message.Text);
                }
                else if (message.Text.StartsWith("/addadmin"))
                {
                    await AddAdmin(message);
                }
                else if (message.Text.StartsWith("/removeadmin"))
                {
                    await RemoveAdmin(message);
                }
                else if (message.Text.StartsWith("/admincommands"))
                {
                    await SendAdminCommands(message.Chat.Id);
                }
            }
        }
        else if (update.Type == UpdateType.CallbackQuery)
        {
            var callbackQuery = update.CallbackQuery;

            if (callbackQuery.Data == "join")
            {
                await RegisterParticipant(callbackQuery.From, callbackQuery.Message.Chat.Id);
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
            }
        }
    }

    private static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("Ошибка: " + exception.Message);
        return Task.CompletedTask;
    }

    private static async Task RegisterParticipant(User user, long chatId)
    {
        if (_participants.Any(p => p.User.Id == user.Id))
        {
            await _botClient.SendTextMessageAsync(chatId, "Вы уже зарегистрированы в розыгрыше.");
        }
        else
        {
            var participant = new Participant
            {
                User = user,
                RegistrationDateTime = DateTime.Now
            };
            _participants.Add(participant);
            await SaveParticipants();
            await _botClient.SendTextMessageAsync(chatId, "Вы успешно зарегистрированы в розыгрыше!");
        }
    }

    private static async Task DrawWinnersAndNotifyParticipants(long adminChatId)
    {
        var winners = SelectWinners(_settings.NumberOfWinners);
        var winnersSet = new HashSet<long>(winners.Select(w => w.User.Id));

        var drawResults = new List<DrawResult>();

        foreach (var participant in _participants)
        {
            bool isWinner = winnersSet.Contains(participant.User.Id);
            var resultMessage =
                isWinner ? "Поздравляем! Вы выиграли в розыгрыше!" : "К сожалению, вы не выиграли в этот раз.";
            await _botClient.SendTextMessageAsync(participant.User.Id, resultMessage);

            drawResults.Add(new DrawResult
            {
                User = participant.User,
                IsWinner = isWinner,
                DrawDateTime = DateTime.Now
            });
        }

        await SaveDrawHistory(drawResults);

        var winnersInfo = string.Join("\n",
            winners.Select(w => $"{w.User.FirstName} {w.User.LastName} (@{w.User.Username})"));
        await _botClient.SendTextMessageAsync(adminChatId, "Победители:\n" + winnersInfo);

        await ResetParticipants();
    }

    private static async Task SaveDrawHistory(List<DrawResult> drawResults)
    {
        var history = new List<DrawResult>();

        if (File.Exists(_settings.HistoryFilePath))
        {
            var json = await File.ReadAllTextAsync(_settings.HistoryFilePath);
            history = JsonSerializer.Deserialize<List<DrawResult>>(json) ?? new List<DrawResult>();
        }

        history.AddRange(drawResults);

        var options = new JsonSerializerOptions { WriteIndented = true };
        var historyJson = JsonSerializer.Serialize(history, options);
        await File.WriteAllTextAsync(_settings.HistoryFilePath, historyJson);
    }

    private static List<Participant> SelectWinners(int count)
    {
        var random = new Random();
        return _participants.OrderBy(x => random.Next()).Take(count).ToList();
    }

    private static async Task SaveParticipants()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(_participants, options);
        await File.WriteAllTextAsync(_settings.ParticipantsFilePath, json);
    }

    private static async Task LoadParticipants()
    {
        if (File.Exists(_settings.ParticipantsFilePath))
        {
            var json = await File.ReadAllTextAsync(_settings.ParticipantsFilePath);
            _participants = JsonSerializer.Deserialize<List<Participant>>(json) ?? new List<Participant>();
        }
    }

    private static async Task ResetParticipants()
    {
        _participants.Clear();
        await SaveParticipants();
    }

    private static async Task SetNumberOfWinners(Message message)
    {
        var parts = message.Text.Split(' ');

        if (parts.Length == 2 && int.TryParse(parts[1], out int newCount))
        {
            _settings.NumberOfWinners = newCount;
            await _botClient.SendTextMessageAsync(message.Chat.Id,
                $"Количество победителей установлено на {_settings.NumberOfWinners}.");
        }
        else
        {
            await _botClient.SendTextMessageAsync(message.Chat.Id,
                "Некорректная команда. Используйте /setwinners [количество].");
        }
    }

    private static async Task SendParticipantsList(long chatId)
    {
        if (_participants.Count == 0)
        {
            await _botClient.SendTextMessageAsync(chatId, "В данный момент нет зарегистрированных участников.");
            return;
        }

        var participantList = string.Join("\n", _participants.Select(p =>
            string.Format("{0} {1} (@{2}) - Зарегистрирован: {3}",
                p.User.FirstName, p.User.LastName, p.User.Username, p.RegistrationDateTime)));

        await _botClient.SendTextMessageAsync(chatId, "Список участников:\n" + participantList);
    }

    private static async Task SendDrawHistory(long chatId)
    {
        if (!File.Exists(_settings.HistoryFilePath))
        {
            await _botClient.SendTextMessageAsync(chatId, "История розыгрышей пуста.");
            return;
        }

        var historyJson = await File.ReadAllTextAsync(_settings.HistoryFilePath);
        var history = JsonSerializer.Deserialize<List<DrawResult>>(historyJson) ?? new List<DrawResult>();

        if (history.Count == 0)
        {
            await _botClient.SendTextMessageAsync(chatId, "История розыгрышей пуста.");
            return;
        }

        var historyMessages = history.Select(dr => string.Format("{0} {1} (@{2}) - {3} - {4}",
            dr.User.FirstName, dr.User.LastName, dr.User.Username,
            dr.IsWinner ? "Победитель" : "Проигравший",
            dr.DrawDateTime.ToString("yyyy-MM-dd HH:mm:ss")));

        await _botClient.SendTextMessageAsync(chatId, "История розыгрышей:\n" + string.Join("\n", historyMessages));
    }

    private static async Task StartManualSelection(long chatId)
    {
        _manualSelectionMode = true;
        _manualWinners.Clear();
        await _botClient.SendTextMessageAsync(chatId,
            "Режим ручного выбора победителей активирован. Используйте команду /addwinner [user_id], чтобы добавить участника в список победителей.");
    }

    private static async Task StopManualSelection(long chatId)
    {
        _manualSelectionMode = false;
        _manualWinners.Clear();
        await _botClient.SendTextMessageAsync(chatId, "Режим ручного выбора победителей остановлен.");
    }

    private static async Task AddManualWinner(long chatId, string command)
    {
        var parts = command.Split(' ');

        if (parts.Length == 2 && long.TryParse(parts[1], out long userId))
        {
            var participant = _participants.FirstOrDefault(p => p.User.Id == userId);
            if (participant != null)
            {
                if (_manualWinners.Any(w => w.User.Id == userId))
                {
                    await _botClient.SendTextMessageAsync(chatId, "Этот участник уже добавлен в список победителей.");
                }
                else
                {
                    _manualWinners.Add(participant);
                    await _botClient.SendTextMessageAsync(chatId,
                        $"{participant.User.FirstName} {participant.User.LastName} (@{participant.User.Username}) добавлен в список победителей.");

                    if (_manualWinners.Count >= _settings.NumberOfWinners)
                    {
                        await _botClient.SendTextMessageAsync(chatId,
                            "Достигнуто максимальное количество победителей. Обработка результатов...");
                        await DrawWinnersAndNotifyParticipants(chatId);
                    }
                }
            }
            else
            {
                await _botClient.SendTextMessageAsync(chatId, "Участник с указанным user_id не найден.");
            }
        }
        else
        {
            await _botClient.SendTextMessageAsync(chatId, "Некорректная команда. Используйте /addwinner [user_id].");
        }
    }

    private static async Task AddAdmin(Message message)
    {
        var parts = message.Text.Split(' ');

        if (parts.Length == 2)
        {
            var username = parts[1];
            if (!_settings.Administrators.Contains(username))
            {
                _settings.Administrators.Add(username);
                await SaveSettings();
                await _botClient.SendTextMessageAsync(message.Chat.Id,
                    $"{username} добавлен в список администраторов.");
            }
            else
            {
                await _botClient.SendTextMessageAsync(message.Chat.Id, $"{username} уже является администратором.");
            }
        }
        else
        {
            await _botClient.SendTextMessageAsync(message.Chat.Id,
                "Некорректная команда. Используйте /addadmin [username].");
        }
    }

    private static async Task RemoveAdmin(Message message)
    {
        var parts = message.Text.Split(' ');

        if (parts.Length == 2)
        {
            var username = parts[1];
            if (_settings.Administrators.Contains(username))
            {
                _settings.Administrators.Remove(username);
                await SaveSettings();
                await _botClient.SendTextMessageAsync(message.Chat.Id, $"{username} удален из списка администраторов.");
            }
            else
            {
                await _botClient.SendTextMessageAsync(message.Chat.Id,
                    $"{username} не найден в списке администраторов.");
            }
        }
        else
        {
            await _botClient.SendTextMessageAsync(message.Chat.Id,
                "Некорректная команда. Используйте /removeadmin [username].");
        }
    }

    private static bool IsAdmin(string username)
    {
        return _settings.Administrators.Contains(username);
    }

    private static async Task LoadSettings()
    {
        if (File.Exists("./data/settings.json"))
        {
            var json = await File.ReadAllTextAsync("./data/settings.json");
            _settings = JsonSerializer.Deserialize<BotSettings>(json) ?? new BotSettings();
        }
        else
        {
            _settings = new BotSettings();
            await SaveSettings();
        }
    }

    private static async Task SaveSettings()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(_settings, options);
        await File.WriteAllTextAsync("./data/settings.json", json);
    }

    private static async Task SendAdminCommands(long chatId)
    {
        var commands = @"
        Доступные команды администратора:
        /join - Участвовать в розыгрыше
        /draw - Провести розыгрыш автоматически.
        /manualstart - Начать ручной выбор победителей.
        /manualstop - Остановить ручной выбор победителей.
        /addwinner [user_id] - Добавить участника в список победителей (вручную).
        /addadmin [username] - Добавить нового администратора.
        /removeadmin [username] - Удалить администратора.
        /reset - Сбросить результаты розыгрыша.
        /setwinners [количество] - Установить количество победителей.
        /participants - Показать список участников.
        /history - Показать историю розыгрышей.
        /admincommands - Показать все доступные команды администратора.";

        await _botClient.SendTextMessageAsync(chatId, commands);
    }
}