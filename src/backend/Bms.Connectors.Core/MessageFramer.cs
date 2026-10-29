using Bms.Domain.Connectors;

namespace Bms.Connectors.Core;

/// <summary>
/// Cuts one connection's byte stream into messages, the way the connector
/// says — never by guessing from the content. TCP delivers bytes, not
/// messages: one read can hold half a message or several. One framer per
/// connection.
/// </summary>
public sealed class MessageFramer(Connector connector)
{
    private readonly List<byte> _pending = new(512);
    private readonly byte? _end = connector.Framing switch
    {
        ConnectorFraming.NewLine => (byte)'\n',
        ConnectorFraming.EndCharacter when !string.IsNullOrEmpty(connector.EndCharacter) => (byte)connector.EndCharacter[0],
        _ => null
    };

    /// <summary>Messages completed by these bytes. An over-long message is returned as <see cref="FramedMessage.Oversize"/>.</summary>
    public IReadOnlyList<FramedMessage> Feed(ReadOnlySpan<byte> data)
    {
        var output = new List<FramedMessage>();
        foreach (var b in data)
        {
            if (_end is { } end && b == end)
            {
                Emit(output);
                continue;
            }

            _pending.Add(b);
            if (connector.Framing == ConnectorFraming.FixedLength && connector.FixedLength is { } length && _pending.Count == length)
            {
                Emit(output);
            }
            else if (_pending.Count > connector.MaxMessageBytes)
            {
                output.Add(new FramedMessage([.. _pending], Oversize: true));
                _pending.Clear();
            }
        }
        return output;
    }

    /// <summary>
    /// What is left when the connection closes: the message itself for
    /// one-per-connection devices, or a last line sent without its newline.
    /// A partial fixed-length message is returned as incomplete.
    /// </summary>
    public FramedMessage? Flush()
    {
        if (_pending.Count == 0)
            return null;
        var incomplete = connector.Framing == ConnectorFraming.FixedLength;
        var message = new FramedMessage([.. _pending], Incomplete: incomplete);
        _pending.Clear();
        return Clean(message);
    }

    private void Emit(List<FramedMessage> output)
    {
        if (Clean(new FramedMessage([.. _pending])) is { } message)
            output.Add(message);
        _pending.Clear();
    }

    /// <summary>Text messages drop a trailing \r (from \r\n) and blank lines are skipped; binary is kept byte for byte.</summary>
    private FramedMessage? Clean(FramedMessage message)
    {
        if (connector.Format == ConnectorFormat.Binary)
            return message.Bytes.Length == 0 ? null : message;
        var bytes = message.Bytes.AsSpan();
        while (bytes.Length > 0 && bytes[^1] is (byte)'\r' or (byte)'\n')
            bytes = bytes[..^1];
        while (bytes.Length > 0 && bytes[0] is (byte)'\r' or (byte)'\n')
            bytes = bytes[1..];
        return bytes.Length == 0 ? null : message with { Bytes = bytes.ToArray() };
    }
}

public sealed record FramedMessage(byte[] Bytes, bool Oversize = false, bool Incomplete = false);
