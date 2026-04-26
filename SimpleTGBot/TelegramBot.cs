using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace SimpleTGBot;

public class TelegramBot
{
    private const string BotToken = "8762261813:AAHRnE8pVHikTZRDNOH4bnctJ7SA4K0_p50";
    private readonly string _logFile = "fitbot_log.txt";
    private readonly Dictionary<long, string> _userStates = new();

    public async Task Run()
    {
        Log("Бот запущен");
        
        var botClient = new TelegramBotClient(BotToken);
        using CancellationTokenSource cts = new CancellationTokenSource();

        ReceiverOptions receiverOptions = new ReceiverOptions()
        {
            AllowedUpdates = new[] { UpdateType.Message }
        };

        botClient.StartReceiving(
            updateHandler: OnMessageReceived,
            pollingErrorHandler: OnErrorOccured,
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token
        );

        var me = await botClient.GetMeAsync(cancellationToken: cts.Token);
        Console.WriteLine($"Бот @{me.Username} запущен.\nДля остановки нажмите клавишу Esc...");

        while (Console.ReadKey().Key != ConsoleKey.Escape) { }
        cts.Cancel();
    }

    async Task OnMessageReceived(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        var message = update.Message;
        if (message is null) return;

        var chatId = message.Chat.Id;
        var username = message.Chat.FirstName ?? "Пользователь";

        Log($"[{chatId}] {username}: {message.Type}");

        if (message.Photo is not null)
        {
            await HandlePhoto(botClient, chatId, username, cancellationToken);
            return;
        }
        
        if (message.Sticker is not null)
        {
            await HandleSticker(botClient, chatId, cancellationToken);
            return;
        }

        if (message.Text is not { } messageText) return;

        Log($"[{chatId}] Текст: {messageText}");

        if (_userStates.TryGetValue(chatId, out var state))
        {
            await HandleStatefulMessage(botClient, chatId, messageText, state, cancellationToken);
            return;
        }

        await HandleCommand(botClient, chatId, username, messageText, cancellationToken);
    }

    async Task HandleCommand(ITelegramBotClient botClient, long chatId, string username,
        string text, CancellationToken ct)
    {
        switch (text.ToLower().Trim())
        {
            case "/start":
            case "начать":
                await SendWelcome(botClient, chatId, username, ct);
                break;

            case "🏋️ тренировки":
            case "/тренировки":
                await SendWorkoutMenu(botClient, chatId, ct);
                break;

            case "🥗 питание":
            case "/питание":
                await SendNutritionMenu(botClient, chatId, ct);
                break;

            case "📊 рассчитать bmi":
            case "/bmi":
                await AskForBmi(botClient, chatId, ct);
                break;

            case "🌤️ погода для пробежки":
            case "/погода":
                await AskForCity(botClient, chatId, ct);
                break;

            case "💪 грудь":
                await SendWorkout(botClient, chatId, "грудь", ct);
                break;
            case "🦵 ноги":
                await SendWorkout(botClient, chatId, "ноги", ct);
                break;
            case "🔙 назад":
                await SendMainMenu(botClient, chatId, ct);
                break;

            default:
                await HandleUnknown(botClient, chatId, text, ct);
                break;
        }
    }

    async Task HandleStatefulMessage(ITelegramBotClient botClient, long chatId,
        string text, string state, CancellationToken ct)
    {
        switch (state)
        {
            case "awaiting_weight":
                if (double.TryParse(text.Replace(',', '.'), out double weight))
                {
                    _userStates[chatId] = $"awaiting_height:{weight}";
                    await botClient.SendTextMessageAsync(chatId, "Теперь введи свой рост в см:", cancellationToken: ct);
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "Пожалуйста, введи число. Например: 70", cancellationToken: ct);
                }
                break;

            case var s when s.StartsWith("awaiting_height:"):
                var weightStr = s.Split(':')[1];
                if (double.TryParse(weightStr, out double w) && double.TryParse(text.Replace(',', '.'), out double height))
                {
                    _userStates.Remove(chatId);
                    await SendBmiResult(botClient, chatId, w, height, ct);
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "Пожалуйста, введи число. Например: 175", cancellationToken: ct);
                }
                break;

            case "awaiting_city":
                _userStates.Remove(chatId);
                await SendWeather(botClient, chatId, text, ct);
                break;
        }
    }

    async Task SendWelcome(ITelegramBotClient botClient, long chatId, string username, CancellationToken ct)
    {
        var greetings = new[]
        {
            $"Привет, {username}! 💪 Готов стать лучше?",
            $"Йоу, {username}! 🔥 Начнём тренировку?",
            $"Здарова, {username}! 🏋️ FitBot на связи!"
        };
        
        var text = greetings[new Random().Next(greetings.Length)] + "\n\nЯ помогу тебе с тренировками, питанием и не только!";
        
        await botClient.SendTextMessageAsync(chatId, text, cancellationToken: ct);
        await SendMainMenu(botClient, chatId, ct);
    }

    async Task SendMainMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
    {
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("🏋️ Тренировки"), new KeyboardButton("🥗 Питание") },
            new[] { new KeyboardButton("📊 Рассчитать BMI"), new KeyboardButton("🌤️ Погода для пробежки") }
        })
        {
            ResizeKeyboard = true
        };

        await botClient.SendTextMessageAsync(
            chatId,
            "Выбери раздел:",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    async Task SendWorkoutMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
    {
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("💪 Грудь"), new KeyboardButton("🦵 Ноги") },
            new[] { new KeyboardButton("🔙 Назад") }
        })
        {
            ResizeKeyboard = true
        };

        await botClient.SendTextMessageAsync(chatId, "Выбери группу мышц:", replyMarkup: keyboard, cancellationToken: ct);
    }

    async Task SendWorkout(ITelegramBotClient botClient, long chatId, string muscle, CancellationToken ct)
    {
        string text = muscle switch
        {
            "грудь" => "💪 *Тренировка на грудь:*\n\n" +
                       "1. Жим лёжа — 4×8\n" +
                       "2. Разводка с гантелями — 3×12\n" +
                       "3. Отжимания на брусьях — 3×10\n" +
                       "4. Кроссовер — 3×15\n\n" +
                       "⏱ Отдых между подходами: 90 сек",
            "ноги" => "🦵 *Тренировка на ноги:*\n\n" +
                      "1. Приседания со штангой — 4×8\n" +
                      "2. Жим ногами — 3×12\n" +
                      "3. Выпады с гантелями — 3×10\n" +
                      "4. Разгибание ног — 3×15\n\n" +
                      "⏱ Отдых между подходами: 2 мин",
            _ => "Упражнение не найдено"
        };

        var photoUrl = muscle switch
        {
            "грудь" => "https://cdn.pixabay.com/photo/2017/08/07/14/02/people-2604149_1280.jpg",
            "ноги" => "https://cdn.pixabay.com/photo/2016/11/19/12/43/barbell-1839086_1280.jpg",
            _ => null
        };

        if (photoUrl != null)
        {
            try
            {
                await botClient.SendPhotoAsync(
                    chatId,
                    new InputFileUrl(photoUrl),
                    caption: text,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: ct);
                return;
            }
            catch
            {
            }
        }

        await botClient.SendTextMessageAsync(chatId, text, parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    async Task SendNutritionMenu(ITelegramBotClient botClient, long chatId, CancellationToken ct)
    {
        var tips = new[]
        {
            "🥗 *Советы по питанию:*\n\n• Пей 2-3 литра воды в день\n• Ешь белок в каждый приём пищи\n• Избегай сахара после 18:00\n• Завтрак — главный приём пищи",
            "🥗 *Питание для роста мышц:*\n\n• Белок: 2г на кг веса\n• Углеводы утром и после тренировки\n• Не пропускай завтрак\n• Казеин на ночь",
            "🥗 *Питание для похудения:*\n\n• Дефицит калорий 300-500 ккал\n• Больше клетчатки и овощей\n• Меньше быстрых углеводов\n• Дробное питание 4-5 раз в день"
        };

        var tip = tips[new Random().Next(tips.Length)];
        await botClient.SendTextMessageAsync(chatId, tip, parseMode: ParseMode.Markdown, cancellationToken: ct);
        await SendMainMenu(botClient, chatId, ct);
    }

    async Task AskForBmi(ITelegramBotClient botClient, long chatId, CancellationToken ct)
    {
        _userStates[chatId] = "awaiting_weight";
        
        var keyboard = new ReplyKeyboardMarkup(new[] { new[] { new KeyboardButton("🔙 Назад") } })
        {
            ResizeKeyboard = true
        };
        
        await botClient.SendTextMessageAsync(chatId, "Введи свой вес в кг (например: 70):", replyMarkup: keyboard, cancellationToken: ct);
    }

    async Task SendBmiResult(ITelegramBotClient botClient, long chatId, double weight, double height, CancellationToken ct)
    {
        double heightM = height / 100;
        double bmi = weight / (heightM * heightM);
        
        string category = bmi switch
        {
            < 18.5 => "😟 Недостаточный вес",
            < 25 => "✅ Норма — отличная форма!",
            < 30 => "⚠️ Избыточный вес",
            _ => "🔴 Ожирение"
        };

        var text = $"📊 *Твой BMI: {bmi:F1}*\n\nКатегория: {category}\n\nBMI = вес / (рост в м)²";
        await botClient.SendTextMessageAsync(chatId, text, parseMode: ParseMode.Markdown, cancellationToken: ct);
        await SendMainMenu(botClient, chatId, ct);
    }

    async Task AskForCity(ITelegramBotClient botClient, long chatId, CancellationToken ct)
    {
        _userStates[chatId] = "awaiting_city";
        await botClient.SendTextMessageAsync(chatId, "🌍 Введи название города:", cancellationToken: ct);
    }

    async Task SendWeather(ITelegramBotClient botClient, long chatId, string city, CancellationToken ct)
    {
        const string apiKey = "220b8fe89099fb7a5c8cae1961c3c2af";
        
        try
        {
            using var http = new HttpClient();
            var url = $"https://api.openweathermap.org/data/2.5/weather?q={Uri.EscapeDataString(city)}&appid={apiKey}&units=metric&lang=ru";
            
            var response = await http.GetStringAsync(url);
            
            var tempMatch = System.Text.RegularExpressions.Regex.Match(response, "\"temp\":([-\\d.]+)");
            var descMatch = System.Text.RegularExpressions.Regex.Match(response, "\"description\":\"([^\"]+)\"");
            var humidMatch = System.Text.RegularExpressions.Regex.Match(response, "\"humidity\":(\\d+)");

            string temp = tempMatch.Success ? tempMatch.Groups[1].Value : "?";
            string desc = descMatch.Success ? descMatch.Groups[1].Value : "?";
            string humid = humidMatch.Success ? humidMatch.Groups[1].Value : "?";

            double tempVal = tempMatch.Success ? double.Parse(temp, System.Globalization.CultureInfo.InvariantCulture) : 0;
            string advice = tempVal switch
            {
                < 0 => "🥶 Холодно — оденься теплее или тренируйся дома",
                < 10 => "🧥 Прохладно — надень куртку для пробежки",
                < 20 => "👍 Отличная погода для пробежки!",
                < 30 => "☀️ Тепло — возьми воду с собой",
                _ => "🔥 Очень жарко — лучше тренируйся вечером"
            };

            var text = $"🌤️ *Погода в {city}:*\n\n🌡 Температура: {temp}°C\n☁️ {desc}\n💧 Влажность: {humid}%\n\n{advice}";
            await botClient.SendTextMessageAsync(chatId, text, parseMode: ParseMode.Markdown, cancellationToken: ct);
        }
        catch (HttpRequestException ex)
        {
            Log($"Ошибка погоды: {ex.Message}");
            await botClient.SendTextMessageAsync(chatId, $"❌ Ошибка: {ex.Message}", cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Log($"Ошибка погоды: {ex.Message}");
            await botClient.SendTextMessageAsync(chatId, $"❌ Что-то пошло не так: {ex.Message}", cancellationToken: ct);
        }

        await SendMainMenu(botClient, chatId, ct);
    }

    async Task HandlePhoto(ITelegramBotClient botClient, long chatId, string username, CancellationToken ct)
    {
        var responses = new[]
        {
            "🔥 Отличная форма! Продолжай в том же духе!",
            "💪 Видно что тренируешься! Молодец!",
            "😎 Хорошо выглядишь! Так держать!"
        };
        var text = responses[new Random().Next(responses.Length)];
        await botClient.SendTextMessageAsync(chatId, text, cancellationToken: ct);
        await SendMainMenu(botClient, chatId, ct);
    }

    async Task HandleSticker(ITelegramBotClient botClient, long chatId, CancellationToken ct)
    {
        await botClient.SendTextMessageAsync(chatId, "😄 Классный стикер! Но давай лучше потренируемся?", cancellationToken: ct);
        await SendMainMenu(botClient, chatId, ct);
    }

    async Task HandleUnknown(ITelegramBotClient botClient, long chatId, string text, CancellationToken ct)
    {
        var responses = new[]
        {
            "🤔 Не понял тебя. Используй кнопки меню!",
            "❓ Такой команды нет. Выбери пункт из меню.",
            "😅 Я не знаю что на это ответить. Вот меню:"
        };
        var response = responses[new Random().Next(responses.Length)];
        await botClient.SendTextMessageAsync(chatId, response, cancellationToken: ct);
        await SendMainMenu(botClient, chatId, ct);
    }

    void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        Console.WriteLine(line);
        System.IO.File.AppendAllText(_logFile, line + "\n");
    }

    Task OnErrorOccured(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };
        Log($"ОШИБКА: {errorMessage}");
        return Task.CompletedTask;
    }
}