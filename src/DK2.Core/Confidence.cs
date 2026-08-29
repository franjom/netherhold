namespace DK2.Core;

/// <summary>
/// Epistemic status of a reverse-engineered behaviour, per AGENTS.md.
/// Used by research annotations and by the harness when reporting divergences.
/// </summary>
public enum Confidence
{
    Unknown = 0,
    Hypothesis = 1,
    StronglyInferred = 2,
    Confirmed = 3,
}

/// <summary>
/// Marks a type or member as implementing a researched DK2 mechanic.
/// Points back at the research note that justifies it.
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
public sealed class Dk2BehaviorAttribute : Attribute
{
    public Dk2BehaviorAttribute(Confidence confidence, string researchNote)
    {
        Confidence = confidence;
        ResearchNote = researchNote;
    }

    public Confidence Confidence { get; }

    /// <summary>Path under docs/research/ documenting the evidence.</summary>
    public string ResearchNote { get; }
}
