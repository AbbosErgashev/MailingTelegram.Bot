using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MailingTelegram.Bot.Services;

public class TelegramService
{
    private readonly TelegramBotClient _botClient;
    private readonly IConfiguration _config;
    private Dictionary<long, string> _userStates;
    private readonly string _logFilePath;

    public TelegramService(IConfiguration config)
    {
        _config = config;
        _botClient = new TelegramBotClient(_config["TelegramBot:Token"]);
        _userStates = new Dictionary<long, string>();
        _logFilePath = _config["Logging:TelegramServiceLog"];
        LogMessage("Telegram bot xizmati ishga tushdi.");
    }

    public void StartReceiving()
    {
        LogMessage("📩 Bot xabarlarni qabul qilishni boshladi.");
        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync
        );
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        LogMessage($"Update qabul qilindi: {update?.Message?.Text ?? "No message"}");

        if (update.Message != null)
        {
            long chatId = update.Message.Chat.Id;
            LogMessage($"💬 Xabar kelgan chat ID: {chatId}");

            if (_userStates.ContainsKey(chatId) && _userStates[chatId] == "waiting_for_message")
            {
                string recipient = _userStates[chatId];
                bool sendToChannel = recipient == "channel" || recipient == "both";
                bool sendToGroups = recipient == "group" || recipient == "both";

                LogMessage($"Xabar jo‘natish: '{update.Message.Text}', Kanalga: {sendToChannel}, Guruhga: {sendToGroups}");

                await SendMessageAsync(update.Message.Text, sendToChannel, sendToGroups);

                _userStates.Remove(chatId);
                await botClient.SendTextMessageAsync(chatId, "Xabar yuborildi");

                LogMessage("Xabar muvaffaqiyatli yuborildi.");
            }
            else
            {
                if (update.Message.Chat.Type == ChatType.Private)
                {
                    LogMessage("Shaxsiy chat uchun menyu jo‘natilmoqda...");
                    await SendCustomKeyboardAsync(chatId);
                }
            }
        }
        else if (update.CallbackQuery != null)
        {
            await HandleCallbackQueryAsync(botClient, update.CallbackQuery);
        }
    }

    private async Task SendCustomKeyboardAsync(long chatId)
    {
        LogMessage($"Menyu jo‘natish: Chat ID {chatId}");

        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Guruhga xabar yuborish", "Kanalga xabar yuborish" },
            new KeyboardButton[] { "Guruh va Kanalga xabar yuborish" }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = false
        };

        await _botClient.SendTextMessageAsync(chatId, "Bot menyusi:", replyMarkup: keyboard);
    }

    private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery)
    {
        long chatId = callbackQuery.Message.Chat.Id;
        string selectedOption = callbackQuery.Data;
        LogMessage($"Callback qabul qilindi: {selectedOption}, Chat ID: {chatId}");

        string message = "Tanlov qabul qilindi! ";

        try
        {
            if (selectedOption == "send_to_group")
            {
                message += "Guruhga xabar yuborish tanlandi.";
                _userStates[chatId] = "group";
            }
            else if (selectedOption == "send_to_channel")
            {
                message += "Kanalga xabar yuborish tanlandi.";
                _userStates[chatId] = "channel";
            }
            else if (selectedOption == "send_to_both")
            {
                message += "Guruh va kanalga xabar yuborish tanlandi.";
                _userStates[chatId] = "both";
            }

            LogMessage($"🔄 Foydalanuvchi tanlovi: {_userStates[chatId]}");

            await Task.Delay(1500);
            await botClient.SendTextMessageAsync(chatId, message);
            await botClient.SendTextMessageAsync(chatId, "Iltimos, yuborish uchun xabar matnini kiriting ✍️");

            var backKeyboard = new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "🔙 Orqaga" }
            })
            {
                ResizeKeyboard = true
            };

            await botClient.SendTextMessageAsync(chatId, "Orqaga qaytish uchun tugmani bosing 👇", replyMarkup: backKeyboard);
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id);
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Xatolik callback queryda: {ex.Message}");
        }
    }

    public async Task SendMessageAsync(string message, bool sendToChannel = false, bool sendToGroups = false)
    {
        try
        {
            var channelId = _config["TelegramBot:ChannelId"];
            var groupId = _config["TelegramBot:GroupId"];

            LogMessage($"Xabar jo‘natish boshlanmoqda: \"{message}\"");

            if (sendToChannel)
            {
                LogMessage($"Kanalga yuborilmoqda: {channelId}");
                await _botClient.SendTextMessageAsync(channelId, message);
            }

            if (sendToGroups)
            {
                LogMessage($"Guruhga yuborilmoqda: {groupId}");
                await _botClient.SendTextMessageAsync(groupId, message);
            }

            LogMessage("Xabar jo‘natildi.");
        }
        catch (Exception ex)
        {
            LogMessage($"Xatolik xabar yuborishda: {ex.Message}");
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        LogMessage($"Xatolik yuz berdi: {exception.Message}");
        return Task.CompletedTask;
    }

    private void LogMessage(string logMessage)
    {
        try
        {
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {logMessage}";
            Console.WriteLine(logEntry);

            if (!string.IsNullOrEmpty(_logFilePath))
            {
                using (StreamWriter writer = new StreamWriter(_logFilePath, true))
                {
                    writer.WriteLine(logEntry);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Log yozishda xatolik: {ex.Message}");
        }
    }
}
