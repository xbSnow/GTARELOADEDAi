using System;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace GTAServer
{
    public static class DiscordWebhook
    {
        private static readonly string WebhookUrl =
            "https://discord.com/api/webhooks/1460398692774445096/Q82lmkrDIF34LWvQBz9Fwhkyz6l8S7lKLyDw7KFtdCYVgnFTXdIIAEybt2R526sHgl6k";

        public static void Send(string message)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using var client = new HttpClient();
                    var json = $"{{\"content\":\"{Escape(message)}\"}}";

                    var content = new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json"
                    );

                    client.PostAsync(WebhookUrl, content).Wait();
                }
                catch
                {
                    // never crash auth
                }
            });
        }

        private static string Escape(string s)
        {
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "");
        }
    }
}

