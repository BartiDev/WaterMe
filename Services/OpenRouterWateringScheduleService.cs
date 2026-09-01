using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace water_me.Services;

public class OpenRouterWateringScheduleService : IWateringScheduleService
{
    private readonly ILogger<OpenRouterWateringScheduleService> _logger;
    private readonly ChatClient _chat;

    private const string SystemPrompt =
        "You are a plant care expert. Respond ONLY with a JSON object in this exact format: " +
        "{\"FrequencyDays\": <int>, \"Amount\": \"<string>\"}. " +
        "FrequencyDays is the number of days between waterings. " +
        "Amount is a concise English description of how much water to give (e.g. '200ml' or 'water until it drains from the pot'). " +
        "Do not include any other text.";

    public OpenRouterWateringScheduleService(IConfiguration configuration, ILogger<OpenRouterWateringScheduleService> logger)
    {
        var apiKey = configuration["OpenRouter:ApiKey"]
            ?? throw new InvalidOperationException("OpenRouter:ApiKey is not configured.");
        var modelId = configuration["OpenRouter:ModelId"] ?? "openai/gpt-4o-mini";
        _logger = logger;

        var options = new OpenAIClientOptions { Endpoint = new Uri("https://openrouter.ai/api/v1") };
        options.AddPolicy(new OpenRouterHeadersPolicy(), PipelinePosition.PerCall);
        var client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        _chat = client.GetChatClient(modelId);
    }

    public async Task<WateringScheduleResult> GetScheduleAsync(string speciesName, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(SystemPrompt),
                new UserChatMessage(speciesName)
            };

            var response = await _chat.CompleteChatAsync(messages, cancellationToken: cts.Token);
            var content = response.Value.Content[0].Text;

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var frequencyDays = root.GetProperty("FrequencyDays").GetInt32();
            var amount = root.GetProperty("Amount").GetString() ?? "";

            if (frequencyDays <= 0 || string.IsNullOrEmpty(amount))
            {
                _logger.LogWarning("OpenRouter returned invalid schedule values for {Species}", speciesName);
                return new WateringScheduleResult(false, 0, "");
            }

            return new WateringScheduleResult(true, frequencyDays, amount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get watering schedule from OpenRouter for {Species}", speciesName);
            return new WateringScheduleResult(false, 0, "");
        }
    }

    private sealed class OpenRouterHeadersPolicy : PipelinePolicy
    {
        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set("HTTP-Referer", "https://waterme.app");
            message.Request.Headers.Set("X-Title", "WaterMe");
            ProcessNext(message, pipeline, currentIndex);
        }

        public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set("HTTP-Referer", "https://waterme.app");
            message.Request.Headers.Set("X-Title", "WaterMe");
            await ProcessNextAsync(message, pipeline, currentIndex);
        }
    }
}
