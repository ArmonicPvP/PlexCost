using Discord;
using Discord.WebSocket;
using PlexCost.Models;
using System.Text;
using System.Text.Json;
using static PlexCost.Services.LoggerService;

namespace PlexCost.Services
{
    public class DiscordService
    {
        private readonly DiscordSocketClient _client;
        private readonly string _token;
        private readonly string _savingsPath;
        private readonly string _dataPath;
        private readonly ulong? _logChannelId;

        public DiscordService(string botToken, string savingsJsonPath, string dataJsonPath, ulong? logChannelId = null)
        {
            _token = botToken;
            _savingsPath = savingsJsonPath;
            _dataPath = dataJsonPath;
            _logChannelId = logChannelId;

            LogInformation("DiscordService initialized with SavingsPath='{SavingsPath}', DataPath='{DataPath}'", _savingsPath, _dataPath);

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds
            });

            if (!string.IsNullOrWhiteSpace(_savingsPath) || !string.IsNullOrWhiteSpace(_dataPath))
                _client.SlashCommandExecuted += OnSlashCommandExecutedAsync;
        }

        public async Task InitializeAsync()
        {
            await _client.LoginAsync(TokenType.Bot, _token);
            await _client.StartAsync();
            LogInformation("Discord bot logged in and starting up.");
        }

        private async Task OnSlashCommandExecutedAsync(SocketSlashCommand command)
        {
            LogInformation("Received slash command: {CommandName}", command.CommandName);
            switch (command.CommandName)
            {
                case "savings":
                    await HandleSavingsAsync(command);
                    break;
                case "data":
                    await HandleDataAsync(command);
                    break;
                default:
                    return;
            }
        }

        private async Task HandleSavingsAsync(SocketSlashCommand command)
        {
            if (string.IsNullOrWhiteSpace(_savingsPath))
                return;

            var opts = command.Data.Options.ToDictionary(o => o.Name, o => o.Value);
            var username = (string)opts["username"]!;
            var page = opts.TryGetValue("page", out object? value) ? Convert.ToInt32(value) : 1;
            LogInformation("Fetching savings for user '{Username}', page {Page}", username, page);
            Dictionary<int, UserSavingsJson> allSavings;
            try
            {
                var json = File.ReadAllText(_savingsPath);
                allSavings = JsonSerializer.Deserialize<Dictionary<int, UserSavingsJson>>(json, Program.jsonOptions)
                             ?? [];
            }
            catch (Exception ex)
            {
                LogError("Could not read savings.json: {Error}", ex.Message);
                await command.RespondAsync("❌ Error reading savings data.", ephemeral: true);
                return;
            }

            var match = allSavings.Values.FirstOrDefault(u =>
                string.Equals(u.UserName, username, StringComparison.OrdinalIgnoreCase)
            );
            if (match == null)
            {
                await command.RespondAsync($"❌ No savings found for user `{username}`.", ephemeral: true);
                return;
            }

            const int MonthlyPageSize = 12;

            var monthlySavings = match.MonthlySavings
                .OrderByDescending(x => (x.Year, x.Month))
                .ToList();

            var totalRecords = monthlySavings.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalRecords / (double)MonthlyPageSize));
            if (page < 1 || page > totalPages)
            {
                await command.RespondAsync($"❌ Page `{page}` is out of range. There are {totalPages} pages.", ephemeral: true);
                return;
            }

            var pageItems = monthlySavings
                .Skip((page - 1) * MonthlyPageSize)
                .Take(MonthlyPageSize)
                .ToList();

            // Single embed summarizing totals and the latest 12 months on the requested page
            var embed = new EmbedBuilder()
                .WithTitle($"# Plex Savings for {match.UserName} (Page {page}/{totalPages})")
                .WithColor(Color.DarkBlue)
                .WithDescription(
                    $"**Totals**\n" +
                    $"Max: ${match.Totals.TotalMaximumSavings:F2}\n" +
                    $"Avg: ${match.Totals.TotalAverageSavings:F2}\n" +
                    $"Subscriptions: ${match.Totals.TotalSubscriptionCosts:F2}");

            foreach (var m in pageItems)
            {
                var subscriptions = m.Subscriptions.Count != 0
                    ? string.Join(", ", m.Subscriptions)
                    : "None";

                var monthLabel = new DateTime(m.Year, m.Month, 1).ToString("MMMM yyyy");
                var fieldValue = new StringBuilder()
                    .AppendLine($"Maximum Savings: ${m.MaximumSavings:F2}")
                    .AppendLine($"Average Savings: ${m.AverageSavings:F2}")
                    .AppendLine($"Subscription Costs: ${m.SubscriptionCosts:F2}")
                    .Append($"Subscriptions: {subscriptions}")
                    .ToString();

                embed.AddField(monthLabel, fieldValue, inline: false);
            }

            await command.RespondAsync(embed: embed.Build(), ephemeral: false);
        }

        private async Task HandleDataAsync(SocketSlashCommand command)
        {
            LogInformation("Entering data handler. DataPath='{DataPath}'", _dataPath);
            if (string.IsNullOrWhiteSpace(_dataPath))
            {
                LogError("data.json path not configured.");
                await command.RespondAsync("❌ data.json path is not configured.", ephemeral: true);
                return;
            }

            if (command.User is not SocketGuildUser guildUser)
            {
                await command.RespondAsync("❌ This command can only be used in a server.", ephemeral: true);
                return;
            }
            if (!guildUser.GuildPermissions.Administrator)
            {
                await command.RespondAsync("❌ You must have Administrator permission.", ephemeral: true);
                return;
            }

            var opts = command.Data.Options.ToDictionary(o => o.Name, o => o.Value);
            var username = (string)opts["username"]!;
            var page = opts.TryGetValue("page", out object? value) ? Convert.ToInt32(value) : 1;
            LogInformation("Fetching data for user '{Username}', page {Page}", username, page);

            Dictionary<int, UserDataJson> allData;
            try
            {
                var json = File.ReadAllText(_dataPath);
                allData = JsonSerializer.Deserialize<Dictionary<int, UserDataJson>>(json, Program.jsonOptions)
                          ?? [];
            }
            catch (Exception ex)
            {
                LogError("Could not read data.json: {Error}", ex.Message);
                await command.RespondAsync("❌ Error reading data file.", ephemeral: true);
                return;
            }

            var userBucket = allData.Values.FirstOrDefault(u =>
                string.Equals(u.UserName, username, StringComparison.OrdinalIgnoreCase)
            );
            if (userBucket == null)
            {
                await command.RespondAsync($"❌ No data found for user `{username}`.", ephemeral: true);
                return;
            }

            const int PageSize = 10;
            var totalRecords = userBucket.Records.Count;
            var totalPages = (int)Math.Ceiling(totalRecords / (double)PageSize);
            if (page < 1 || page > totalPages)
            {
                await command.RespondAsync($"❌ Page `{page}` is out of range. There are {totalPages} pages.", ephemeral: true);
                return;
            }

            // Order records by DateStopped ascending (oldest first), then paginate
            var pageItems = userBucket.Records
                .OrderBy(r => r.DateStopped)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            // Build a single embed for this page
            var embed = new EmbedBuilder()
                .WithTitle($"# Data for {userBucket.UserName} (Page {page}/{totalPages})")
                .WithColor(Color.DarkBlue);

            foreach (var rec in pageItems)
            {
                var ts = DateTimeOffset.FromUnixTimeSeconds(rec.DateStopped)
                             .UtcDateTime
                             .ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                var subs = rec.SubscriptionNames.Count != 0
                    ? string.Join(", ", rec.SubscriptionNames)
                    : "None";

                var fieldValue = new StringBuilder()
                    .AppendLine(ts)
                    .AppendLine($"Maximum Price: ${rec.MaximumPrice:F2}")
                    .AppendLine($"Average Price: ${rec.AveragePrice:F2}")
                    .Append($"Subscription Names: {subs}")
                    .ToString();

                embed.AddField($"**{rec.GUID}**", fieldValue, inline: false);
            }

            await command.RespondAsync(embed: embed.Build(), ephemeral: false);
        }

        public async Task SendLogAsync(string level, string message)
        {
            if (_logChannelId is null || _logChannelId == 0) return;
            var channel = _client.GetChannel(_logChannelId.Value) as IMessageChannel;
            if (channel is not null)
            {
                var prefix = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{level}] ";
                await channel.SendMessageAsync(prefix + message);
            }
        }
    }
}
