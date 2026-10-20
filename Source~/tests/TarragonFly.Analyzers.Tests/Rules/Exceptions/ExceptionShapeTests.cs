using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Exceptions;
using TarragonFly.Analyzers.Rules.SilentFailures;
using TarragonFly.Analyzers.Tests.Harness;

namespace TarragonFly.Analyzers.Tests.Rules.Exceptions
{
    // TF3003 catching NullReferenceException, TF3052 throws from special members, TF3053 iterator checks.
    public class ExceptionShapeTests
    {
        [Test]
        public void CatchingNullReferenceException_IsReportedOnTheType()
        {
            AnalyzerVerifier.Verify(new CatchNullReferenceAnalyzer(), @"
using System;

class Loader
{
    void Load(object o)
    {
        try { o.ToString(); } catch ({|TF3003:NullReferenceException|}) { }
        try { o.ToString(); } catch ({|TF3003:System.NullReferenceException|} e) when (e != null) { throw; }
        try { o.ToString(); } catch (ArgumentNullException) { }
    }
}");
        }

        [Test]
        public void FilterTestingTheCaughtExceptionForNullReference_IsReportedOnTheType()
        {
            AnalyzerVerifier.Verify(new CatchNullReferenceAnalyzer(), @"
using System;

class Loader
{
    void Load(object o)
    {
        try { o.ToString(); } catch (Exception e) when (e is {|TF3003:NullReferenceException|}) { }
        try { o.ToString(); } catch (Exception e) when ((e is {|TF3003:NullReferenceException|} missing)) { }
        try { o.ToString(); } catch (AggregateException e) when (e.InnerException is {|TF3003:NullReferenceException|}) { }
        try { o.ToString(); } catch (Exception e) when (e is NullReferenceException && o != null) { }
        try { o.ToString(); } catch (Exception e) when (o is NullReferenceException) { }
        try { o.ToString(); } catch (Exception) when (o is NullReferenceException) { }
        try { o.ToString(); } catch (Exception e) when (e is ArgumentException) { }
    }
}");
        }

        [Test]
        public void ThrowFromAMemberCallersDontExpectToThrow_IsReported()
        {
            AnalyzerVerifier.Verify(new ThrowInSpecialMemberAnalyzer(), @"
using System;

class Cell : IDisposable, IEquatable<Cell>
{
    static Cell() { {|TF3052:throw new InvalidOperationException();|} }
    public bool Equals(Cell other) { {|TF3052:throw new InvalidOperationException();|} }
    public override int GetHashCode() => {|TF3052:throw new NotSupportedException()|};
    public override string ToString() { {|TF3052:throw new InvalidOperationException();|} }
    public void Dispose() { {|TF3052:throw new InvalidOperationException();|} }
    public static bool operator ==(Cell a, Cell b) { {|TF3052:throw new InvalidOperationException();|} }
    public static bool operator !=(Cell a, Cell b) => {|TF3052:throw new InvalidOperationException()|};
    public static implicit operator int(Cell c) { {|TF3052:throw new InvalidOperationException();|} }
}");
        }

        [Test]
        public void NotImplementedRethrowsCaughtThrowsAndOtherMembers_AreFine()
        {
            AnalyzerVerifier.Verify(new ThrowInSpecialMemberAnalyzer(), @"
using System;

class Cell
{
    public override bool Equals(object obj) => throw new NotImplementedException();

    public override int GetHashCode()
    {
        try { throw new InvalidOperationException(); }
        catch (InvalidOperationException) { return 0; }
    }

    public override string ToString()
    {
        try { return base.ToString(); }
        catch (Exception) { throw; }
    }

    public static explicit operator string(Cell c) { throw new InvalidOperationException(); }

    ~Cell() { throw new InvalidOperationException(); }

    public void Dispose() { throw new InvalidOperationException(); }

    public void Other()
    {
        Action later = () => throw new InvalidOperationException();
        throw new InvalidOperationException();
    }
}");
        }

        [Test]
        public void ArgumentCheckInAnIterator_IsReportedOnTheMethod()
        {
            AnalyzerVerifier.Verify(new LateIteratorArgumentCheckAnalyzer(), @"
using System;
using System.Collections;
using System.Collections.Generic;

class Waves
{
    public IEnumerable<int> {|TF3053:Spawn|}(int[] enemies)
    {
        if (enemies == null) throw new ArgumentNullException(nameof(enemies));
        foreach (int enemy in enemies) yield return enemy;
    }

    public IEnumerator {|TF3053:Wait|}(float seconds)
    {
        if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        yield return null;
    }

    public IEnumerable<int> {|TF3053:Helper|}(int[] enemies)
    {
        ArgumentNullException.ThrowIfNull(enemies);
        yield return 1;
    }

    public IEnumerable<int> Checked(int[] enemies)
    {
        if (enemies == null) throw new ArgumentNullException(nameof(enemies));
        return CheckedIterator(enemies);
    }

    private IEnumerable<int> CheckedIterator(int[] enemies)
    {
        foreach (int enemy in enemies) yield return enemy;
    }

    public IEnumerable<int> OtherException()
    {
        if (DateTime.Now.Year < 2000) throw new InvalidOperationException();
        yield return 1;
    }
}");
        }
    }
}