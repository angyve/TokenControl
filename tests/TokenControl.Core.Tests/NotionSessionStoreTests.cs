using TokenControl.Core.Notion;

namespace TokenControl.Core.Tests;

public sealed class NotionSessionStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"tc-test-{Guid.NewGuid():N}", "session.dat");

    [Fact]
    public void RoundTripsEncrypted()
    {
        var store = new NotionSessionStore(_path);
        Assert.Null(store.Load());

        store.Save(new NotionSession("v03%3Asecret-token", "app.notion.com"));

        Assert.DoesNotContain("secret-token", File.ReadAllText(_path));
        Assert.Equal(new NotionSession("v03%3Asecret-token", "app.notion.com"), store.Load());

        store.Clear();
        Assert.Null(store.Load());
    }

    [Fact]
    public void CorruptFileLoadsAsSignedOut()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "garbage");
        Assert.Null(new NotionSessionStore(_path).Load());
    }

    [Theory]
    [InlineData("app.notion.com", "https://app.notion.com/api/v3/")]
    [InlineData(".app.notion.com", "https://app.notion.com/api/v3/")]
    [InlineData(".www.notion.so", "https://www.notion.so/api/v3/")]
    [InlineData(".notion.so", "https://www.notion.so/api/v3/")]
    public void ApiOriginFollowsCookieHost(string host, string expected) =>
        Assert.Equal(new Uri(expected), new NotionSession("t", host).ApiBase);

    [Fact]
    public void ToStringNeverLeaksToken() =>
        Assert.DoesNotContain("secret", new NotionSession("secret", "app.notion.com").ToString());

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
