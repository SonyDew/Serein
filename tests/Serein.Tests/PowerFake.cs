using Serein.Core;

namespace Serein.Tests;
public sealed class FakePower : IPowerPlatform
{
    public readonly Guid Original = new("11111111-1111-1111-1111-111111111111"), Target = new("22222222-2222-2222-2222-222222222222"), External = new("33333333-3333-3333-3333-333333333333");
    public Guid Active = new("11111111-1111-1111-1111-111111111111"); public bool FailSelect, FailRead; public int Writes, Releases;
    public Guid Current() => FailRead ? throw new System.ComponentModel.Win32Exception(5) : Active;
    public IReadOnlyList<PowerScheme> Schemes() => new[] { new PowerScheme(Original, "Original"), new PowerScheme(Target, "Target"), new PowerScheme(External, "External") };
    public void Select(Guid id) { if (FailSelect) throw new System.ComponentModel.Win32Exception(5); Writes++; Active = id; }
    public IDisposable Acquire() => new ActionOnDispose(() => Releases++);
    private sealed class ActionOnDispose(Action action) : IDisposable { public void Dispose() => action(); }
}
