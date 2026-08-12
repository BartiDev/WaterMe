using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace water_me.Services;

public class OpenAiWateringScheduleService : IWateringScheduleService
{
    private readonly string _apiKey;
    private readonly string _modelId;
    private readonly ILogger<OpenAiWateringScheduleService> _logger;

    private const string SystemPrompt =
        "You are a plant care expert. Respond ONLY with a JSON object in this exact format: " +
        "{\"FrequencyDays\": <int>, \"Amount\": \"<string>\"}. " +
        "FrequencyDays is the number of days between waterings. " +
        "Amount is a concise English description of how much water to give (e.g. '200ml' or 'water until it drains from the pot'). " +
        "Do not include any other text.";

    public OpenAiWateringScheduleService(IConfiguration configuration, ILogger<OpenAiWateringScheduleService> logger)
    {
        _apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
        _modelId = configuration["OpenAI:ModelId"] ?? "gpt-4o-mini";
        _logger = logger;
    }

    public async Task<WateringScheduleResult> GetScheduleAsync(string speciesName, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var client = new OpenAIClient(_apiKey);
            var chat = client.GetChatClient(_modelId);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(SystemPrompt),
                new UserChatMessage(speciesName)
            };

            var response = await chat.CompleteChatAsync(messages, cancellationToken: cts.Token);
            var content = response.Value.Content[0].Text;

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var frequencyDays = root.GetProperty("FrequencyDays").GetInt32();
            var amount = root.GetProperty("Amount").GetString() ?? "";

            if (frequencyDays <= 0 || string.IsNullOrEmpty(amount))
            {
                _logger.LogWarning("OpenAI returned invalid schedule values for {Species}", speciesName);
                return new WateringScheduleResult(false, 0, "");
            }

            return new WateringScheduleResult(true, frequencyDays, amount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get watering schedule from OpenAI for {Species}", speciesName);
            return new WateringScheduleResult(false, 0, "");
        }
    }
}
