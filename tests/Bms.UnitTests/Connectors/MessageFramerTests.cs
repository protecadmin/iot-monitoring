using System.Text;
using Bms.Connectors.Core;
using Bms.Domain.Connectors;
using Xunit;

namespace Bms.UnitTests.Connectors;

public class MessageFramerTests
{
    private static string[] Texts(IEnumerable<FramedMessage> messages) => messages.Select(m => Encoding.UTF8.GetString(m.Bytes)).ToArray();

    [Fact]
    public void NewLine_LinesSplitAcrossReads_AreReassembled()
    {
        var framer = new MessageFramer(new Connector { Framing = ConnectorFraming.NewLine });

        var first = framer.Feed("100000005,+022.2"u8);
        var second = framer.Feed("1,7\r\n100000007,+001.0,9\n100"u8);

        Assert.Empty(first);
        Assert.Equal(["100000005,+022.21,7", "100000007,+001.0,9"], Texts(second));
        Assert.Equal("100", Encoding.UTF8.GetString(framer.Flush()!.Bytes)); // a last line without newline still counts
    }

    [Fact]
    public void EndCharacter_SplitsOnIt()
    {
        var framer = new MessageFramer(new Connector { Framing = ConnectorFraming.EndCharacter, EndCharacter = "#", Format = ConnectorFormat.KeyValue });

        Assert.Equal(["ID=1;T=2", "ID=2;T=3"], Texts(framer.Feed("ID=1;T=2#ID=2;T=3#"u8)));
    }

    [Fact]
    public void FixedLength_BinaryWithNewlineBytes_IsNotCutAtThem()
    {
        var framer = new MessageFramer(new Connector { Framing = ConnectorFraming.FixedLength, FixedLength = 4, Format = ConnectorFormat.Binary });

        var frames = framer.Feed([0x01, 0x0A, 0x0D, 0x02, 0x03]);

        Assert.Single(frames);
        Assert.Equal(new byte[] { 0x01, 0x0A, 0x0D, 0x02 }, frames[0].Bytes);
        Assert.True(framer.Flush()!.Incomplete);
    }

    [Fact]
    public void OnePerConnection_IsTheWholeStream()
    {
        var framer = new MessageFramer(new Connector { Framing = ConnectorFraming.OnePerConnection, Format = ConnectorFormat.Json });

        Assert.Empty(framer.Feed("{\"id\":1,\n\"t\":2}"u8));
        Assert.Equal("{\"id\":1,\n\"t\":2}", Encoding.UTF8.GetString(framer.Flush()!.Bytes));
    }

    [Fact]
    public void OverlongMessage_IsFlaggedNotBuffered()
    {
        var framer = new MessageFramer(new Connector { Framing = ConnectorFraming.NewLine, MaxMessageBytes = 16 });

        var frames = framer.Feed(Encoding.UTF8.GetBytes(new string('9', 40)));

        Assert.All(frames, f => Assert.True(f.Oversize));
    }
}
