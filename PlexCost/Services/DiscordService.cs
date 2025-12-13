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
            LogDebug("Attempting to log in Discord bot with supplied token.");
            await _client.LoginAsync(TokenType.Bot, _token);
            await _client.StartAsync();
            LogInformation("Discord bot logged in and starting up.");
        }

        private async Task OnSlashCommandExecutedAsync(SocketSlashCommand command)
        {
            LogInformation("Received slash command: {CommandName}", command.CommandName);
            LogDebug("Slash command invoked by UserId='{UserId}', GuildId='{GuildId}', OptionsCount={OptionCount}",
                command.User.Id,
                (command.GuildId ?? 0).ToString(),
                command.Data.Options.Count);
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

            var options = command.Data.Options.ToDictionary(o => o.Name, o => o.Value);
            var username = (string)options["username"]!;
            var page = options.TryGetValue("page", out var pageOption) ? Convert.ToInt32(pageOption) : 1;

            LogDebug(
                "Handling savings request for user '{Username}' using path '{SavingsPath}', page {Page}",
                username,
                _savingsPath,
                page);
            Dictionary<int, UserSavingsJson> allSavings;
            try
            {
                var json = File.ReadAllText(_savingsPath);
                allSavings = JsonSerializer.Deserialize<Dictionary<int, UserSavingsJson>>(json, Program.jsonOptions)
                             ?? [];
                LogDebug("Loaded {SavingsCount} savings records from disk", allSavings.Count);
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

            var recentMonthly = match.MonthlySavings
                .OrderByDescending(x => new DateTime(x.Year, x.Month, 1))
                .ToList();

            LogDebug(
                "Using {MonthlyCount} monthly records for '{Username}'",
                recentMonthly.Count,
                username);

            const int PageSize = 6;
            var totalRecords = recentMonthly.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalRecords / (double)PageSize));

            if (page < 1 || page > totalPages)
            {
                await command.RespondAsync($"❌ Page `{page}` is out of range. There are {totalPages} pages.", ephemeral: true);
                return;
            }

            var pageItems = recentMonthly
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            var monthlyEmbed = new EmbedBuilder()
                .WithTitle($"Monthly Savings for {match.UserName} (Page {page}/{totalPages})")
                .WithColor(Color.DarkBlue);

            var totalsEmbed = new EmbedBuilder()
                .WithTitle($"Total Savings for {match.UserName}")
                .WithColor(Color.Gold)
                .AddField("Average", $"${match.Totals.TotalAverageSavings:F2}", true)
                .AddField("Maximum", $"${match.Totals.TotalMaximumSavings:F2}", true)
                .AddField("Subscriptions", $"${match.Totals.TotalSubscriptionCosts:F2}", true);

            if (pageItems.Count == 0)
            {
                monthlyEmbed.WithDescription("No monthly savings data available.");
            }
            else
            {
                foreach (var m in pageItems)
                {
                    monthlyEmbed.AddField(
                        $"**{m.Month}/{m.Year}**",
                        $"Max: ${m.MaximumSavings:F2}\nAvg: ${m.AverageSavings:F2}\nSubscriptions: ${m.SubscriptionCosts:F2}",
                        inline: false
                    );
                }
            }

            await command.RespondAsync(embeds: new[] { monthlyEmbed.Build(), totalsEmbed.Build(), }, ephemeral: false);
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
            LogDebug("Options received for data command: {Options}", string.Join(", ", opts.Keys));

            Dictionary<int, UserDataJson> allData;
            try
            {
                var json = File.ReadAllText(_dataPath);
                allData = JsonSerializer.Deserialize<Dictionary<int, UserDataJson>>(json, Program.jsonOptions)
                          ?? [];
                LogDebug("Loaded {RecordCount} user data entries from disk", allData.Count);
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
            LogDebug("User '{Username}' has {TotalRecords} records; total pages={TotalPages}", username, totalRecords, totalPages);
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

            LogDebug("Building data embed for user '{Username}' with {PageItemCount} records on page {Page}", username, pageItems.Count, page);

            // Build a single embed for this page
            var embed = new EmbedBuilder()
                .WithTitle($"Data for {userBucket.UserName} (Page {page}/{totalPages})")
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
