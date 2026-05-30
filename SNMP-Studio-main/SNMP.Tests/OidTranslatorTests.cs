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

    // ── HP FutureSmart OID tests ──────────────────────────────────────────────

    [Theory]
    [InlineData("1.3.6.1.4.1.11.2.3.9.4.2.1.1.8.0",  "hp-fsDeviceSerialNumber.0")]
    [InlineData("1.3.6.1.4.1.11.2.3.9.4.2.1.1.10.0", "hp-fsDeviceFWVersion.0")]
    [InlineData("1.3.6.1.4.1.11.2.3.9.4.2.1.1.7.0",  "hp-fsDeviceModelNumber.0")]
    [InlineData("1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.4","hp-fsMarkerSupplyLevel")]
    [InlineData("1.3.6.1.4.1.11.2.3.9.4.2.2.1.1.1.0","hp-fsNetworkIPv4Address.0")]
    [InlineData("1.3.6.1.4.1.11.2.4.3.5.1.0",        "hp-jdPrintPageCount.0")]
    public void Translate_HpFutureSmart_ReturnsSymbolicName(string oid, string expectedName)
    {
        var t = BuildTranslator();
        var (name, _) = t.Translate(oid);
        Assert.Equal(expectedName, name);
    }

    [Fact]
    public void Translate_HpFutureSmartRoot_ReturnsHp()
    {
        var t = BuildTranslator();
        var (name, desc) = t.Translate("1.3.6.1.4.1.11");
        Assert.Equal("hp", name);
        Assert.NotEmpty(desc);
    }

    [Fact]
    public void InterpretValue_HpTonerLevel_Percent()
    {
        var t = BuildTranslator();
        var result = t.InterpretValue("1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.4", "75", "Integer32");
        Assert.Contains("75%", result);
    }

    [Fact]
    public void InterpretValue_HpTonerLevel_Negative1_Unknown()
    {
        var t = BuildTranslator();
        var result = t.InterpretValue("1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.4", "-1", "Integer32");
        Assert.Contains("Unknown", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterpretValue_HpTonerLevel_Negative2_Exhausted()
    {
        var t = BuildTranslator();
        var result = t.InterpretValue("1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.4", "-2", "Integer32");
        Assert.Contains("Exhausted", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterpretValue_HpMarkerSupplyStatus_Enum()
    {
        var t = BuildTranslator();
        // 2 = lowLevel
        var result = t.InterpretValue("1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.7", "2", "Integer32");
        Assert.Contains("lowLevel", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── Cisco OID tests ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.3.6.1.4.1.9",                       "cisco")]
    [InlineData("1.3.6.1.4.1.9.9.109.1.1.1.1.3.1",    "cpmCPUTotal5sec")]
    [InlineData("1.3.6.1.4.1.9.9.109.1.1.1.1.4.1",    "cpmCPUTotal1min")]
    [InlineData("1.3.6.1.4.1.9.9.109.1.1.1.1.5.1",    "cpmCPUTotal5min")]
    [InlineData("1.3.6.1.4.1.9.9.48.1.1.1.5.1",       "ciscoMemoryPoolUsed")]
    [InlineData("1.3.6.1.4.1.9.9.48.1.1.1.6.1",       "ciscoMemoryPoolFree")]
    [InlineData("1.3.6.1.4.1.9.9.13.1.2.1.3.1",       "ciscoEnvMonTemperatureStatusValue")]
    [InlineData("1.3.6.1.4.1.9.9.23.1.2.1.1.5",       "cdpCacheDeviceId")]
    [InlineData("1.3.6.1.4.1.9.9.23.1.2.1.1.7",       "cdpCachePlatform")]
    [InlineData("1.3.6.1.4.1.9.9.106.1.2.1.1.10",     "cHsrpGrpStandbyState")]
    [InlineData("1.3.6.1.2.1.15.3.1.2",                "bgpPeerState")]
    [InlineData("1.3.6.1.2.1.14.7.1.6",                "ospfNbrState")]
    public void Translate_Cisco_ReturnsSymbolicName(string oid, string expectedName)
    {
        var t = BuildTranslator();
        var (name, _) = t.Translate(oid);
        Assert.Equal(expectedName, name);
    }

    [Fact]
    public void InterpretValue_CiscoCpu_AppendsPct()
    {
        var t = BuildTranslator();
        var result = t.InterpretValue("1.3.6.1.4.1.9.9.109.1.1.1.1.4.1", "42", "Integer32");
        Assert.Equal("42%", result);
    }

    [Fact]
    public void InterpretValue_CiscoMemory_FormatsBytes()
    {
        var t = BuildTranslator();
        // 52428800 bytes = 50 MB
        var result = t.InterpretValue("1.3.6.1.4.1.9.9.48.1.1.1.5.1", "52428800", "Integer32");
        Assert.Contains("MB", result);
    }

    [Fact]
    public void InterpretValue_CiscoTemperature_AppendsDegC()
    {
        var t = BuildTranslator();
        var result = t.InterpretValue("1.3.6.1.4.1.9.9.13.1.2.1.3.1", "45", "Integer32");
        Assert.Contains("45", result);
        Assert.Contains("°C", result);
    }

    [Fact]
    public void InterpretValue_HsrpState_Active_ReturnsEnum()
    {
        var t = BuildTranslator();
        // 6 = active
        var result = t.InterpretValue("1.3.6.1.4.1.9.9.106.1.2.1.1.10", "6", "Integer32");
        Assert.Contains("active", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterpretValue_BgpPeerState_Established_ReturnsEnum()
    {
        var t = BuildTranslator();
        // 6 = established
        var result = t.InterpretValue("1.3.6.1.2.1.15.3.1.2", "6", "Integer32");
        Assert.Contains("established", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterpretValue_OspfNbrState_Full_ReturnsEnum()
    {
        var t = BuildTranslator();
        // 8 = full
        var result = t.InterpretValue("1.3.6.1.2.1.14.7.1.6", "8", "Integer32");
        Assert.Contains("full", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterpretValue_NtpStratum16_ReturnsUnsyncLabel()
    {
        var t = BuildTranslator();
        var result = t.InterpretValue("1.3.6.1.4.1.9.9.168.1.1.8.0", "16", "Integer32");
        Assert.Contains("unsynced", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterpretValue_CiscoMemory_SmallValue_FormatsKb()
    {
        var t = BuildTranslator();
        // 102400 bytes = 100 KB
        var result = t.InterpretValue("1.3.6.1.4.1.9.9.48.1.1.1.6.1", "102400", "Integer32");
        Assert.Contains("KB", result);
    }

    [Fact]
    public void Translate_CiscoLongestPrefix_InstanceSuffix()
    {
        var t = BuildTranslator();
        // cdpCacheDeviceId.1.1 — suffix .1 (ifIndex) .1 (entry)
        var (name, _) = t.Translate("1.3.6.1.4.1.9.9.23.1.2.1.1.5.1.1");
        Assert.StartsWith("cdpCacheDeviceId", name);
    }

}