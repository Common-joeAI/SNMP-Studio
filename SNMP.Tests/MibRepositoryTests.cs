using SNMP.Engine.Mib;
using Xunit;

namespace SNMP.Tests;

public class MibRepositoryTests
{
    private const string SampleMib = """
        TEST-MIB DEFINITIONS ::= BEGIN

        IMPORTS
            enterprises FROM SNMPv2-SMI;

        testOrg OBJECT IDENTIFIER ::= { enterprises 99999 }
        testObjects OBJECT IDENTIFIER ::= { testOrg 1 }

        testDescr OBJECT-TYPE
            SYNTAX      OCTET STRING
            MAX-ACCESS  read-only
            STATUS      current
            DESCRIPTION "A test description string."
            ::= { testObjects 1 }

        testStatus OBJECT-TYPE
            SYNTAX      INTEGER { active(1) inactive(2) error(3) }
            MAX-ACCESS  read-only
            STATUS      current
            DESCRIPTION "Operational status."
            ::= { testObjects 2 }

        END
        """;

    [Fact]
    public async Task ImportAsync_ValidMibText_LoadsNodes()
    {
        var repo = new MibRepository();
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, SampleMib);

        var (loaded, failed, errors) = await repo.ImportAsync(new[] { path });

        File.Delete(path);

        Assert.True(loaded > 0, $"Expected nodes to be loaded, errors: {string.Join(", ", errors)}");
        Assert.Equal(0, failed);
    }

    [Fact]
    public async Task Lookup_KnownOid_ReturnsNode()
    {
        var repo = new MibRepository();
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, SampleMib);
        await repo.ImportAsync(new[] { path });
        File.Delete(path);

        // enterprises = 1.3.6.1.4.1, testOrg = .99999, testObjects = .1, testDescr = .1
        var node = repo.LongestPrefixMatch("1.3.6.1.4.1.99999.1.1");
        Assert.NotNull(node);
        Assert.Equal("testDescr", node!.Name);
    }

    [Fact]
    public async Task LongestPrefixMatch_InstanceSuffix_ResolvesBaseNode()
    {
        var repo = new MibRepository();
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, SampleMib);
        await repo.ImportAsync(new[] { path });
        File.Delete(path);

        // .0 instance suffix
        var node = repo.LongestPrefixMatch("1.3.6.1.4.1.99999.1.1.0");
        Assert.NotNull(node);
        Assert.Equal("testDescr", node!.Name);
    }

    [Fact]
    public void Lookup_MissingOid_ReturnsNull()
    {
        var repo = new MibRepository();
        Assert.Null(repo.Lookup("9.9.9.9.9"));
    }
}
