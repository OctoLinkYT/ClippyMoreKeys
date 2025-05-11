using Clippy.Core.Classes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Clippy.Core.Services
{
    public class ChatGPTService : IChatService
    {
        private const string ClippyKey = "Hello there! To use this app, you need a valid API key and endpoint. Please provide them in the app settings.";
        private const string ClippyStart = "Hi! I'm Clippy, your Windows assistant. Would you like to get some assistance?";
        private const string Instruction = "You are in an app that revives Microsoft Clippy in Windows. Speak in a Clippy style and try to stay as concise/short as possible and not output long messages.";

        public ObservableCollection<IMessage> Messages { get; } = new ObservableCollection<IMessage>();

        private HttpClient HttpClient;
        private ISettingsService Settings;
        private IKeyService KeyService;

        public ChatGPTService(ISettingsService settings, IKeyService keys)
        {
            Settings = settings;
            KeyService = keys;
            HttpClient = new HttpClient();

            if (SetAPI() || Settings.HasKey)
                Add(new ClippyMessage(ClippyStart, true));
        }

        public void Refresh()
        {
            Messages.Clear();
            if (SetAPI() || Settings.HasKey)
                Add(new ClippyMessage(ClippyStart, true));
        }

        public async Task SendAsync(IMessage message) /// Send a message
        {
            if (!Settings.HasKey || string.IsNullOrEmpty(Settings.ApiEndpoint))
            {
                Add(new ClippyMessage(ClippyKey, false));
                return;
            }

            Add(message); // Send user message to UI
            List<Dictionary<string, string>> APIRequestMessages = new List<Dictionary<string, string>>
            {
                new Dictionary<string, string>
                {
                    { "role", "system" },
                    { "content", Instruction }
                }
            };

            foreach (IMessage m in Messages)
            {
                if (m is ClippyMessage)
                    APIRequestMessages.Add(new Dictionary<string, string> { { "role", "assistant" }, { "content", m.Message } });
                else if (m is UserMessage)
                    APIRequestMessages.Add(new Dictionary<string, string> { { "role", "user" }, { "content", m.Message } });
            }

            await Task.Delay(300);

            ClippyMessage Response = new ClippyMessage(true);
            Add(Response); // Send empty message and update text later to show preview UI

            APIRequestMessages.Add(new Dictionary<string, string> { { "role", "user" }, { "content", message.Message } });

            var requestBody = new
            {
                messages = APIRequestMessages,
                max_tokens = Settings.Tokens
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Settings.ApiEndpoint);
                request.Headers.Add("Authorization", $"Bearer {KeyService.GetKey()}");
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<ShuttleAIResponse>(responseContent);

                    if (result != null && result.Choices.Any())
                    {
                        Response.Message = result.Choices.First().Message.Content;
                    }
                    else
                    {
                        Response.Message = "No response received from the API.";
                        Response.IsLatest = false;
                    }
                }
                else
                {
                    Response.Message = $"API error: {response.ReasonPhrase}";
                    Response.IsLatest = false;
                }
            }
            catch (Exception ex)
            {
                Response.Message = $"An error occurred: {ex.Message}";
                Response.IsLatest = false;
            }
        }

        private void Add(IMessage Message) /// Add a message
        {
            foreach (IMessage message in Messages) /// Remove any editable message
            {
                if (message is ClippyMessage)
                    ((ClippyMessage)message).IsLatest = false;
            }
            Messages.Add(Message);
        }

        /// <summary>
        /// Initialise the HTTP client and refresh API key/endpoint
        /// </summary>
        private bool SetAPI()
        {
            try
            {
                if (string.IsNullOrEmpty(KeyService.GetKey()) || string.IsNullOrEmpty(Settings.ApiEndpoint))
                    throw new Exception("API key or endpoint is missing.");

                HttpClient.DefaultRequestHeaders.Clear();
                HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {KeyService.GetKey()}");
                return true;
            }
            catch
            {
                Add(new AnnouncementMessage(ClippyKey));
                return false;
            }
        }

        /// <summary>
        /// Represents the structure of the ShuttleAI API response.
        /// </summary>
        private class ShuttleAIResponse
        {
            public List<Choice> Choices { get; set; }

            public class Choice
            {
                public MessageContent Message { get; set; }

                public class MessageContent
                {
                    public string Content { get; set; }
                }
            }
        }
    }
}
