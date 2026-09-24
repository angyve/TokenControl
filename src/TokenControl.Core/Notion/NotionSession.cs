namespace TokenControl.Core.Notion;

/// <summary>A decrypted Notion session. <see cref="Host"/> is the cookie's domain, which decides the API origin.</summary>
public sealed record NotionSession(string TokenV2, string Host)
{
    // Older desktop builds keep the session on www.notion.so; newer ones on app.notion.com.
    public Uri ApiBase => Host.TrimStart('.').EndsWith("app.notion.com", StringComparison.OrdinalIgnoreCase)
        ? new Uri("https://app.notion.com/api/v3/")
        : new Uri("https://www.notion.so/api/v3/");

    public override string ToString() => $"NotionSession {{ Host = {Host}, TokenV2 = <{TokenV2.Length} chars> }}";
}
