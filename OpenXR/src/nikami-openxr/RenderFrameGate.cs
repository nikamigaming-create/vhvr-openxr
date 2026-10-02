using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Nikami.OpenXR;

// Each callback publishes once per frame; a derived callback may still call its base.
internal sealed class RenderFrameGate
{
    sealed class Stamp
    {
        internal int Frame = -1;
        internal readonly HashSet<MethodBase> Callbacks = new();
    }
    readonly ConditionalWeakTable<object, Stamp> stamps = new();

    internal bool TryEnter(object owner, MethodBase callback, int frame)
    {
        var stamp = stamps.GetOrCreateValue(owner);
        if (stamp.Frame != frame)
        {
            stamp.Frame = frame;
            stamp.Callbacks.Clear();
        }
        return stamp.Callbacks.Add(callback);
    }

    internal void Clear() => stamps.Clear();
}
