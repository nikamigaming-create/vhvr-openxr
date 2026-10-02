using Nikami.OpenXR;
using UnityEngine;
using Object = UnityEngine.Object;

static class Program
{
    static int checks;
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        checks++;
    }
    static (ItemDrop Item, Rigidbody Body, OpenXRPhysicalGrab Grab, OpenXRPhysicalHands.HandState Hand) Fixture(bool body = true, bool kinematic = false, bool autoPickup = true)
    {
        var gameObject = new GameObject();
        var item = gameObject.AddComponent<ItemDrop>(); item.m_autoPickup = autoPickup;
        Rigidbody rigidbody = body ? gameObject.AddComponent<Rigidbody>() : null;
        if (rigidbody)
        {
            rigidbody.isKinematic = kinematic;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rigidbody.interpolation = RigidbodyInterpolation.Extrapolate;
            rigidbody.solverIterations = 7;
            rigidbody.SettingWrites = 0;
            rigidbody.linearVelocity = new Vector3(1, 2, 3);
        }
        var grab = gameObject.AddComponent<OpenXRPhysicalGrab>();
        var hand = new OpenXRPhysicalHands.HandState { Grab = grab };
        return (item, rigidbody, grab, hand);
    }
    static void Begin((ItemDrop Item, Rigidbody Body, OpenXRPhysicalGrab Grab, OpenXRPhysicalHands.HandState Hand) fixture)
        => fixture.Grab.Begin(new GameObject().AddComponent<OpenXRPhysicalHands>(), fixture.Hand, fixture.Item, default);
    static void OriginalSettings(Rigidbody body, string reason)
    {
        Require(body.collisionDetectionMode == CollisionDetectionMode.ContinuousSpeculative, reason + ": original CCD");
        Require(body.interpolation == RigidbodyInterpolation.Extrapolate, reason + ": original interpolation");
        Require(body.solverIterations == 7, reason + ": original solver iterations");
    }
    static void Main()
    {
        Time.unscaledTime = 0;
        var missing = Fixture(body: false);
        Begin(missing);
        Require(!missing.Grab.Protected, "failed missing-body Begin never protects the item");
        Object.FlushDestroyed();
        Require(missing.Item.m_autoPickup, "failed missing-body Begin preserves auto pickup");
        Require(missing.Hand.Grab == null, "failed missing-body cleanup clears the hand reference");

        var kinematic = Fixture(kinematic: true);
        Begin(kinematic); Object.FlushDestroyed();
        OriginalSettings(kinematic.Body, "failed kinematic-body Begin");
        Require(kinematic.Body.SettingWrites == 0, "failed Begin never writes body settings");
        Require(kinematic.Item.m_autoPickup, "failed kinematic-body Begin preserves auto pickup");

        var released = Fixture();
        Begin(released);
        Require(released.Grab.Protected && !released.Item.m_autoPickup, "successful Begin protects the held item");
        Require(released.Body.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic && released.Body.interpolation == RigidbodyInterpolation.Interpolate && released.Body.solverIterations == 12, "successful Begin enables held-body settings");
        released.Grab.Release();
        Require(released.Hand.Grab == null && released.Grab.Protected, "normal release clears the hand and preserves the grace");
        Time.unscaledTime = 1.1f;
        released.Grab.InvokeMessage("Update"); Object.FlushDestroyed();
        OriginalSettings(released.Body, "grace expiry");
        Require(released.Item.m_autoPickup && !OpenXRPhysicalGrab.ProtectedItems.Contains(released.Grab), "grace expiry restores pickup and unregisters the item");

        var disposed = Fixture();
        Begin(disposed);
        disposed.Grab.Dispose();
        OriginalSettings(disposed.Body, "immediate disposal");
        Require(disposed.Item.m_autoPickup && !disposed.Grab.Protected && disposed.Hand.Grab == null, "disposal immediately restores pickup and removes protection");
        int writes = disposed.Body.SettingWrites;
        disposed.Grab.Dispose(); Object.FlushDestroyed();
        Require(disposed.Body.SettingWrites == writes, "repeated disposal and OnDestroy do not restore twice");
        Require(disposed.Body.linearVelocity.sqrMagnitude == 14, "disposal preserves ordinary measured momentum");

        var grace = Fixture(autoPickup: false);
        Begin(grace); grace.Grab.Release(); grace.Grab.Dispose(); Object.FlushDestroyed();
        OriginalSettings(grace.Body, "disposal during release grace");
        Require(!grace.Item.m_autoPickup, "disposal preserves an originally disabled auto pickup");

        var disabled = Fixture();
        Begin(disabled); disabled.Grab.enabled = false; Object.FlushDestroyed();
        OriginalSettings(disabled.Body, "component disable");
        Require(disabled.Item.m_autoPickup && disabled.Hand.Grab == null, "component disable releases and restores the item");

        var lostBody = Fixture();
        Begin(lostBody); Object.Destroy(lostBody.Body); Object.FlushDestroyed();
        lostBody.Grab.Dispose(); Object.FlushDestroyed();
        Require(lostBody.Item.m_autoPickup, "destroyed-body cleanup still restores the surviving item's pickup");
        Require(OpenXRPhysicalGrab.ProtectedItems.Count == 0, "every completed lifecycle removes its registry entry");
        Console.WriteLine($"PASS: {checks} production grip lifecycle checks (managed boundary; native physics/VR not exercised).");
    }
}
