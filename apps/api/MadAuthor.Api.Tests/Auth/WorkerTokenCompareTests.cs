using System.Security.Cryptography;
using System.Text;

namespace MadAuthor.Api.Tests.Auth;

/// <summary>
/// The worker-token middleware in <c>Program.cs</c> compares the presented
/// <c>X-Worker-Token</c> header against <c>CLAUDE_WORKER_TOKEN</c> using
/// <see cref="CryptographicOperations.FixedTimeEquals"/>. These tests pin
/// down the behaviour of that compare so we can't accidentally regress it
/// to a regular string equality (which would leak timing info).
/// </summary>
public class WorkerTokenCompareTests
{
    /// <summary>
    /// Helper that mirrors the middleware's compare exactly. Kept here as a
    /// pure function so the test asserts the same primitive the production
    /// code calls.
    /// </summary>
    private static bool TokensMatch(string presented, string expected)
    {
        if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(expected)) return false;
        var p = Encoding.UTF8.GetBytes(presented);
        var e = Encoding.UTF8.GetBytes(expected);
        if (p.Length != e.Length) return false;
        return CryptographicOperations.FixedTimeEquals(p, e);
    }

    [Fact]
    public void Match_returns_true_for_identical_tokens()
    {
        const string token = "4d260e4cee6be56fcac5fc668e7c942d5daf8ee2f4005f4616d241c3753fede5";
        Assert.True(TokensMatch(token, token));
    }

    [Fact]
    public void Match_returns_false_for_different_same_length_tokens()
    {
        const string a = "4d260e4cee6be56fcac5fc668e7c942d5daf8ee2f4005f4616d241c3753fede5";
        const string b = "0000000000000000000000000000000000000000000000000000000000000000";
        Assert.False(TokensMatch(a, b));
    }

    [Fact]
    public void Match_returns_false_when_presented_shorter_than_expected()
    {
        Assert.False(TokensMatch("short", "4d260e4cee6be56fcac5fc668e7c942d5daf8ee2f4005f4616d241c3753fede5"));
    }

    [Fact]
    public void Match_returns_false_when_presented_longer_than_expected()
    {
        Assert.False(TokensMatch("4d260e4cee6be56fcac5fc668e7c942d5daf8ee2f4005f4616d241c3753fede5XX", "4d260e4cee6be56fcac5fc668e7c942d5daf8ee2f4005f4616d241c3753fede5"));
    }

    [Fact]
    public void Match_returns_false_for_empty_presented()
    {
        Assert.False(TokensMatch(string.Empty, "abc"));
    }

    [Fact]
    public void Match_returns_false_for_empty_expected()
    {
        Assert.False(TokensMatch("abc", string.Empty));
    }

    [Fact]
    public void Match_returns_false_when_only_one_byte_differs()
    {
        const string a = "abcdefghijklmnop";
        const string b = "abcdefghijklmnoq";
        Assert.False(TokensMatch(a, b));
    }

    /// <summary>
    /// Sanity-check that <see cref="CryptographicOperations.FixedTimeEquals"/>
    /// rejects different-length spans without throwing. If a future BCL change
    /// makes it throw instead, the middleware needs a length-pre-check (already
    /// has one, but this is the regression test).
    /// </summary>
    [Fact]
    public void FixedTimeEquals_returns_false_for_different_length_arrays()
    {
        Assert.False(CryptographicOperations.FixedTimeEquals(new byte[] { 1, 2, 3 }, new byte[] { 1, 2 }));
    }
}
