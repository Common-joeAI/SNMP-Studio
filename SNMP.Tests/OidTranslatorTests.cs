using SNMP.Engine.Mib;
using Xunit;

namespace SNMP.Tests;

public class OidTranslatorTests
{
    private static OidTranslator BuildTranslator()
    {
        var repo = new MibRepository();
        return new OidTranslator(repo);
    }

    [Theory]
    [InlineData("1.3.6.1.2.1.1.1.0", "sysDescr.0")]
    [InlineData("1.3.6.1.2.1.1.3.0", "sysUpTime.0")]
    [InlineData("1.3.6.1.2.1.1.5.0", "sysName.0")]
    [InlineData("1.3.6.1.2.1.2.2.1.8", "ifOperStatus")]
    public void Translate_StandardOid_ReturnsSymbolicName(string oid, string expectedName)
    {
        var t = BuildTranslator();
        var (name, _) = t.Translate(oid);
        Assert.Equal(expectedName, name);
    }

    [Fact]
    public void Translate_UnknownOid_ReturnsRawOid()
    {
        var t = BuildTranslator();
        var (name, desc) = t.Translate("9.9.9.9.9.9");
        Assert.Equal("9.9.9.9.9.9", name);
        Assert.Empty(desc);
    }

    [Fact]
    public void InterpretValue_Uptime_FormatsCorrectly()
    {
        var t = BuildTranslator();
        // 8640000 ticks = 100 seconds * 100 = 1 day
        // Actually: 1 day = 86400s * 100 ticks/s = 8640000 ticks
        var result = t.InterpretValue("1.3.6.1.2.1.1.3.0", "8640000", "TimeTicks");
        Assert.Contains("1d", result);
    }

    [Fact]
    public void InterpretValue_IfOperStatus_Up_ReturnsEnumLabel()
    {
        var t = BuildTranslator();
        var result = t.InterpretValue("1.3.6.1.2.1.2.2.1.8", "1", "Integer32");
        Assert.Contains("up", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterpretValue_IfOperStatus_Down_ReturnsEnumLabel()
    {
        var t = BuildTranslator();
        var result = t.InterpretValue("1.3.6.1.2.1.2.2.1.8", "2", "Integer32");
        Assert.Contains("down", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterpretValue_IfSpeed_FormatsMbps()
    {
        var t = BuildTranslator();
        var result = t.InterpretValue("1.3.6.1.2.1.2.2.1.5", "1000000000", "Gauge32");
        Assert.Contains("Mbps", result);
    }

    [Fact]
    public void InterpretValue_SupplyLevel_Negative1_ReturnsUnknown()
    {
        var t = BuildTranslator();
        var result = t.InterpretValue("1.3.6.1.2.1.43.11.1.1.8", "-1", "Integer32");
        Assert.Contains("Unknown", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Translate_OidWithLeadingDot_ResolvedCorrectly()
    {
        var t = BuildTranslator();
        var (name, _) = t.Translate(".1.3.6.1.2.1.1.1.0");
        Assert.Equal("sysDescr.0", name);
    }
}
