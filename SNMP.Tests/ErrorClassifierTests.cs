using SNMP.Core.Enums;
using SNMP.Engine.Services;
using Xunit;

namespace SNMP.Tests;

public class ErrorClassifierTests
{
    [Fact]
    public void Classify_Timeout_ReturnsTimeoutCategory()
    {
        var ex     = new Exception("Request timed out after 3000ms");
        var result = ErrorClassifier.Classify(ex);
        Assert.Equal(NegotiationResult.Timeout, result.Category);
        Assert.Contains("timed out", result.FriendlyMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_AuthFailure_ReturnsAuthCategory()
    {
        var ex     = new Exception("Authentication failure: wrong digest received");
        var result = ErrorClassifier.Classify(ex);
        Assert.Equal(NegotiationResult.AuthFailure, result.Category);
    }

    [Fact]
    public void Classify_UnknownSecurityName_ReturnsAuthCategory()
    {
        var ex     = new Exception("Unknown security name");
        var result = ErrorClassifier.Classify(ex);
        Assert.Equal(NegotiationResult.AuthFailure, result.Category);
        Assert.Contains("security (user) name", result.FriendlyMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_ContextMismatch_ReturnsContextCategory()
    {
        var ex     = new Exception("No such context found");
        var result = ErrorClassifier.Classify(ex);
        Assert.Equal(NegotiationResult.ContextMismatch, result.Category);
    }

    [Fact]
    public void Classify_ConnectionRefused_ReturnsNoResponseCategory()
    {
        var ex     = new Exception("Connection refused on port 161");
        var result = ErrorClassifier.Classify(ex);
        Assert.Equal(NegotiationResult.NoResponse, result.Category);
    }

    [Fact]
    public void Classify_GenericException_ReturnsUnknown()
    {
        var ex     = new InvalidOperationException("something weird happened");
        var result = ErrorClassifier.Classify(ex);
        Assert.Equal(NegotiationResult.Unknown, result.Category);
    }

    [Fact]
    public void Classify_PrivacyError_ReturnsAuthCategory()
    {
        var ex     = new Exception("Priv decryption error");
        var result = ErrorClassifier.Classify(ex);
        Assert.Equal(NegotiationResult.AuthFailure, result.Category);
        Assert.Contains("privacy", result.FriendlyMessage, StringComparison.OrdinalIgnoreCase);
    }
}
