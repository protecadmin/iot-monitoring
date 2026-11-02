using Bms.Domain.Connectors;
using System.Text;

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
    private readonly byte[] _marker = Encoding.ASCII.GetBytes(connector.StartMarker ?? string.Empty);
    private bool _inFrame;
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
            if (connector.Framing == ConnectorFraming.StartMarker)
            {
                FeedMarker(b, output);
                continue;
            }
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
        var incomplete = connector.Framing == ConnectorFraming.FixedLength
            || (connector.Framing == ConnectorFraming.StartMarker && (!_inFrame || EndsWithPartialMarker()));
        var message = new FramedMessage([.. _pending], Incomplete: incomplete);
        _pending.Clear();
        return Clean(message);
    }

    private void FeedMarker(byte b, List<FramedMessage> output)
    {
        // Some firmware includes CR/LF and some joins records directly.
        if (b is (byte)'\r' or (byte)'\n')
        {
            if (_pending.Count > 0)
            {
                if (Clean(new FramedMessage([.. _pending], Incomplete: !_inFrame || EndsWithPartialMarker())) is {} line) output.Add(line);
                _pending.Clear();
            }
            _inFrame = false;
            return;
        }
        _pending.Add(b);
        if (_marker.Length > 0 && EndsWith(_marker.Length))
        {
            var preceding = _pending.Count - _marker.Length;
            if (preceding > 0 && Clean(new FramedMessage(_pending.Take(preceding).ToArray(), Incomplete: !_inFrame)) is {} message)
                output.Add(message);
            _pending.Clear(); _pending.AddRange(_marker); _inFrame = true;
        }
        // Allow space for a split marker following a maximum-size record.
        else if (_pending.Count > connector.MaxMessageBytes + _marker.Length)
        {
            output.Add(new FramedMessage([.. _pending], Oversize: true));
            _pending.Clear(); _inFrame = false;
        }
    }

    private bool EndsWith(int length)
    {
        if (_pending.Count < length) return false;
        for (var i = 0; i < length; i++) if (_pending[_pending.Count - length + i] != _marker[i]) return false;
        return true;
    }

    private bool EndsWithPartialMarker()
    {
        for (var n = Math.Min(_marker.Length - 1, _pending.Count); n > 0; n--) if (EndsWith(n)) return true;
        return false;
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
