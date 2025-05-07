using System.Text.Json;
using Discord;
using Discord.WebSocket;
using PlexCost.Models;
using static PlexCost.Services.LoggerService;

namespace PlexCost.Services
{
    public class DiscordService
    {
        private readonly DiscordSocketClient _client;
        private readonly string _token;
        private readonly string _savingsPath;

        public DiscordService(string botToken, string savingsJsonPath)
        {
            _token = botToken;
            _savingsPath = savingsJsonPath;
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds
            });
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
            if (command.CommandName != "savings")
                return;

            var username = (string)command.Data.Options.First().Value!;

            Dictionary<int, UserSavingsJson> allSavings;
            try
            {
                var json = File.ReadAllText(_savingsPath);
                allSavings = JsonSerializer.Deserialize<Dictionary<int, UserSavingsJson>>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                ) ?? new Dictionary<int, UserSavingsJson>();
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

            // Build an embed
            var embed = new EmbedBuilder()
                .WithTitle($"Plex Savings for {match.UserName}")
                .WithColor(Discord.Color.DarkBlue);

            // Monthly breakdown
            foreach (var m in match.MonthlySavings.OrderBy(x => (x.Year, x.Month)))
            {
                embed.AddField(
                    $"{m.Month}/{m.Year}",
                    $"Max: ${m.MaximumSavings:F2}\nAvg: ${m.AverageSavings:F2}\nSubscriptions: ${m.SubscriptionCosts:F2}",
                    inline: false
                );
            }

            // Totals
            embed.AddField("\u200B", "\u200B"); // spacer
            embed.AddField(
                "**Totals**",
                $"Max: ${match.Totals.TotalMaximumSavings:F2}\n" +
                $"Avg: ${match.Totals.TotalAverageSavings:F2}\n" +
                $"Subscriptions: ${match.Totals.TotalSubscriptionCosts:F2}"
            );

            await command.RespondAsync(embed: embed.Build(), ephemeral: false);
        }
    }
}
