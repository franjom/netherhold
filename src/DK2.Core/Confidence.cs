// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

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
