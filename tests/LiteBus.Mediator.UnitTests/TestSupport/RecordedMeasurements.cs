using System.Diagnostics.Metrics;
using LiteBus.Messaging;

namespace LiteBus.Mediator.UnitTests;

/// <summary>
///     Collects the mediation measurements taken while it is alive, with the tags each one carried.
/// </summary>
/// <remarks>
///     A <see cref="Meter" /> instrument records nothing unless a listener enables it, so a test asserting on metrics
///     has to enable them. Recording the tags matters as much as recording the instrument names: the outcome, the code
///     and the stage are the dimensions the instruments exist for, and an instrument with no dimensions answers none
///     of the questions.
/// </remarks>
internal sealed class RecordedMeasurements : IDisposable
{
    /// <summary>
    ///     The tag keys seen per instrument.
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _tagKeys = new(StringComparer.Ordinal);

    /// <summary>
    ///     The tag values seen per instrument and tag key.
    /// </summary>
    private readonly Dictionary<(string Instrument, string Tag), HashSet<string>> _tagValues = [];

    /// <summary>
    ///     The listener subscribed to the mediation meter.
    /// </summary>
    private readonly MeterListener _listener;

    /// <summary>
    ///     The instrument names that recorded at least one measurement.
    /// </summary>
    private readonly HashSet<string> _instruments = new(StringComparer.Ordinal);

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordedMeasurements" /> class.
    /// </summary>
    public RecordedMeasurements()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == LiteBusMediationTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };

        _listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Record(instrument.Name, tags));
        _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Record(instrument.Name, tags));
        _listener.Start();
    }

    /// <summary>
    ///     Gets the instrument names that recorded at least one measurement.
    /// </summary>
    public IReadOnlyCollection<string> Instruments => _instruments;

    /// <summary>
    ///     Reads the tag keys one instrument carried.
    /// </summary>
    /// <param name="instrument">The instrument name.</param>
    /// <returns>The tag keys, or an empty collection when the instrument recorded nothing.</returns>
    public IReadOnlyCollection<string> TagsFor(string instrument)
    {
        return _tagKeys.TryGetValue(instrument, out var keys) ? keys : [];
    }

    /// <summary>
    ///     Reads the values one instrument carried for one tag key.
    /// </summary>
    /// <param name="instrument">The instrument name.</param>
    /// <param name="tag">The tag key.</param>
    /// <returns>The values, or an empty collection when nothing recorded that pair.</returns>
    public IReadOnlyCollection<string> TagValuesFor(string instrument, string tag)
    {
        return _tagValues.TryGetValue((instrument, tag), out var values) ? values : [];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _listener.Dispose();
    }

    /// <summary>
    ///     Records one measurement and its tags.
    /// </summary>
    /// <param name="instrument">The instrument name.</param>
    /// <param name="tags">The tags the measurement carried.</param>
    private void Record(string instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        _instruments.Add(instrument);

        if (!_tagKeys.TryGetValue(instrument, out var keys))
        {
            keys = new HashSet<string>(StringComparer.Ordinal);
            _tagKeys[instrument] = keys;
        }

        foreach (var tag in tags)
        {
            keys.Add(tag.Key);

            if (!_tagValues.TryGetValue((instrument, tag.Key), out var values))
            {
                values = new HashSet<string>(StringComparer.Ordinal);
                _tagValues[(instrument, tag.Key)] = values;
            }

            values.Add(tag.Value?.ToString() ?? string.Empty);
        }
    }
}
