namespace SharpPersistence.Migrator.Builders;

public sealed class SequenceBuilder
{
    internal SequenceDefinition Definition { get; }

    internal SequenceBuilder(SequenceDefinition definition)
    {
        Definition = definition;
    }

    public SequenceBuilder StartsAt(long startValue)
    {
        Definition.StartValue = startValue;
        return this;
    }

    public SequenceBuilder IncrementsBy(int incrementBy)
    {
        Definition.IncrementBy = incrementBy;
        return this;
    }

    public SequenceBuilder HasMin(long? minValue)
    {
        Definition.MinValue = minValue;
        return this;
    }

    public SequenceBuilder HasMax(long? maxValue)
    {
        Definition.MaxValue = maxValue;
        return this;
    }

    public SequenceBuilder IsCyclic(bool cyclic = true)
    {
        Definition.IsCyclic = cyclic;
        return this;
    }
}
