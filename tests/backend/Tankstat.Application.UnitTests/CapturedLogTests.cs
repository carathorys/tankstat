using Microsoft.Extensions.Logging;
using Tankstat.TestSupport;

namespace Tankstat.Application.UnitTests;

/// <summary>
/// What the privacy canaries rely on: <see cref="CapturedLog.Mentions"/> finds a value however a line writes it, and is not fooled by the
/// random ids every log holds (trace and span ids, GUIDs, request ids), which can spell a short needle such as 24687 by chance.
/// </summary>
public class CapturedLogTests
{
    private const string Id = "1b24687c-9d3e-4f00-8a1b-0c2d3e4f5a6b";
    private const string Trace = "e5055acbbcc9df4bb6aeacc24687fb6a";

    [Theory]
    [InlineData("the receipt says total 24687 HUF")]
    [InlineData("""{"total":24687,"volume":38.52}""")]
    [InlineData("""the model said "24687" """)]
    [InlineData("24687.0")]
    [InlineData("24687")]
    public void AValue_IsMentioned_HoweverTheLineWritesIt(string line) =>
        Assert.True(Logged(line).Mentions("24687"));

    [Theory]
    [InlineData("TraceId", Trace)] // 32 hex, as the host opens every request's scope
    [InlineData("SpanId", "d2bb078f24687b4c")] // 16 hex
    [InlineData("SpanId", "24687b4cd2bb078f")] // at an id's first characters
    [InlineData("SpanId", "d2bb078fb4c24687")] // and at its last
    [InlineData("UserId", Id)]
    [InlineData("RequestId", "0HNFK24687M1A")] // base32, so hex lookarounds alone would not do
    [InlineData("RequestPath", $"/media/vehicles/{Id}/photo-drafts")]
    public void ANeedleAScopesIdSpellsByChance_IsNotMentioned(string key, string value) =>
        Assert.False(Logged("nothing secret here", (key, value)).Mentions("24687"));

    [Fact]
    public void EveryIdOfTheScopeTheHostOpens_IsSkipped_NotOnlyTheFirst() =>
        Assert.False(Logged("nothing secret here",
            ("SpanId", "d2bb078f25dabb4c"), ("TraceId", Trace), ("ParentId", "0000000000000000"), ("RequestId", "0HNP6UN3N2C6J")).Mentions("24687"));

    [Theory]
    [InlineData($"Created the first administrator {Id}")]
    [InlineData($"No XML encryptor configured. Key {{{Id}}} may be persisted to storage in unencrypted form.")]
    [InlineData("Created the first administrator 1B24687C-9D3E-4F00-8A1B-0C2D3E4F5A6B")] // an id in capitals is an id too
    [InlineData("Asking qwen to read photo 1b24687c9d3e4f008a1b0c2d3e4f5a6b")] // a GUID without its hyphens
    public void ANeedleAnIdInTheMessageSpellsByChance_IsNotMentioned(string line) =>
        Assert.False(Logged(line).Mentions("24687"));

    [Theory]
    [InlineData($"photo {Id}: total 24687", "24687")]
    [InlineData($"total 24687 for photo {Id}", "24687")] // a find before the id
    [InlineData($"photo {Id} total F5A6", "f5a6")] // the find after the id is in capitals: one comparison for every find
    [InlineData($"photo {Id}: total", "5a6b: total")] // a find that starts inside the id and runs past it
    public void AFindInsideAnId_DoesNotHideOneElsewhereOnTheLine(string line, string needle) =>
        Assert.True(Logged(line).Mentions(needle));

    [Fact]
    public void AFindInsideAnIdOnOneLine_DoesNotHideOneOnALaterLine() =>
        Assert.True(Logged([$"photo {Id} saved", "total 24687 HUF"]).Mentions("24687"));

    [Fact]
    public void AScope_IsSearched() =>
        Assert.True(Logged("nothing secret here", ("RequestPath", "/imports/24687")).Mentions("24687"));

    [Fact]
    public void AnException_IsSearched()
    {
        var log = new CapturedLog();
        log.For<CapturedLogTests>().LogWarning(new InvalidOperationException("the server said total 24687"), "Reading failed");
        Assert.True(log.Mentions("24687"));
    }

    [Theory]
    [InlineData($"user {Id} signed in", Id)]
    [InlineData($"token {Trace}", Trace)]
    [InlineData("link /reset?t=1b24687c9d3e4f008a1b0c2d3e4f5a6b.Xy-z_Secret43", "1b24687c9d3e4f008a1b0c2d3e4f5a6b.Xy-z_Secret43")] // {id:N}.{secret}
    [InlineData("hash 1b24687c9d3e4f008a1b0c2d3e4f5a6b0123456789abcdef0123456789abcdef", "24687")] // 64 hex: no id, so searched as ever,
    [InlineData("hash 0123456789abcdef0123456789abcdef1b24687c9d3e4f008a1b0c2d3e4f5a6b", "24687")] // in either half
    [InlineData("pw hunter2password1 typed", "hunter2")] // 16 characters that are not all hex: no id
    [InlineData("Set-Cookie: session=abc123", "abc123")]
    [InlineData("Authorization: Bearer Secret-Key", "secret-key")] // case is ignored, as ever
    public void AnIdItself_TextRunningPastOne_AndAnyOtherText_AreMentioned(string line, string needle) =>
        Assert.True(Logged(line).Mentions(needle));

    [Fact]
    public void ALongRunOfANeedleMadeOfHexLetters_IsMentioned()
    {
        // A long run of a needle: every "aaaa" in it has an "a" beside it, which must not hide it.
        Assert.True(Logged($"Query.{new string('a', 5000)} was refused").Mentions("aaaa"));
    }

    [Fact]
    public void ANeedleTheLogDoesNotHold_IsNotMentioned() =>
        Assert.False(Logged("total 24688 HUF", ("TraceId", "e5055acbbcc9df4bb6aeacc2468afb6a")).Mentions("24687"));

    [Fact]
    public void AnEmptyNeedle_IsATestsMistake() =>
        Assert.Throws<ArgumentException>(() => Logged("anything").Mentions(""));

    private static CapturedLog Logged(string line, params (string Key, string Value)[] scope) => Logged([line], scope);

    // One scope as the host opens one (key/value pairs), around every line.
    private static CapturedLog Logged(string[] lines, params (string Key, string Value)[] scope)
    {
        var log = new CapturedLog();
        var logger = log.For<CapturedLogTests>();
        using (scope.Length > 0 ? logger.BeginScope(scope.Select(p => new KeyValuePair<string, object>(p.Key, p.Value)).ToList()) : null)
            foreach (var line in lines) logger.LogInformation("{Line}", line);
        return log;
    }
}
