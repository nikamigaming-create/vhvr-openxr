using HarmonyLib;
using UnityEngine;

namespace Nikami.OpenXR;

// Restrain a small native actor without replacing its AI, hurtboxes or damage
// model. Large actors win the strength contest; no grab changes player pose.
internal sealed class OpenXRCreatureGrip : MonoBehaviour
{
    internal Character Creature { get; private set; }
    internal Rigidbody Body { get; private set; }
    internal Vector3 GripPoint => transform.TransformPoint(anchor);
    internal bool Held;
    OpenXRPhysicalHands owner;
    OpenXRPhysicalHands.HandState hand;
    Player player;
    ZNetView view;
    Vector3 anchor, previousTarget;
    float until;
    internal static bool CanRestrain(Player player, Character target)
    {
        if (!player || !target || target.IsPlayer() || target.IsDead() || target.IsBoss() || target.IsFlying()
            || target.IsSwimming() || target.HaveRider()) return false;
        var capsule = target.GetComponent<CapsuleCollider>();
        float height = capsule ? capsule.height * Mathf.Abs(capsule.transform.lossyScale.y) : 3;
        // Allow small creatures up to the player's own mass (including boars).
        // Level increases resistance; height also excludes a light modded troll.
        return height <= 2.1f && target.GetMass() * Mathf.Max(1, target.GetLevel()) <= player.GetMass();
    }
    internal static void Repel(Player player, Character target, OpenXRPhysicalHands.HandState hand)
    {
        if (!player || !target || target.IsPlayer() || target.IsDead()) return;
        var direction = Vector3.ProjectOnPlane(player.transform.position - target.transform.position, Vector3.up);
        if (direction.sqrMagnitude < .001f) direction = -player.transform.forward;
        // ApplyPushback divides force by player mass. Scale by the actual
        // mass so modded characters still receive a short native shove.
        player.ApplyPushback(direction.normalized, player.GetMass() * 2);
        hand.Hand.hapticAction.Execute(0, .12f, 80, .6f, hand.Source);
        player.Message(MessageHud.MessageType.Center, "Too strong to hold");
    }
    internal void Begin(OpenXRPhysicalHands owner, OpenXRPhysicalHands.HandState hand, Character target, Vector3 point)
    {
        this.owner = owner; this.hand = hand; player = Player.m_localPlayer;
        Creature = target; Body = target.GetComponent<Rigidbody>(); view = target.GetComponent<ZNetView>();
        anchor = transform.InverseTransformPoint(hand.PhysicalPalm);
        previousTarget = hand.Tracked.position + hand.Tracked.rotation * hand.Palm;
        until = Time.unscaledTime + 8;
        Held = true;
    }
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Character), "UpdateWalking"),
            prefix: new HarmonyMethod(typeof(OpenXRCreatureGrip), nameof(AllowWalking)));
    }
    static bool AllowWalking(Character __instance)
    {
        // This native hook runs for every walking actor. Consult only the two
        // local grip references; do not search every actor's components.
        var current = OpenXRPhysicalHands.Current;
        if (!current) return true;
        var grip = current.Left.CreatureGrip;
        if (!grip || grip.Creature != __instance) grip = current.Right.CreatureGrip;
        if (!grip || !grip.Held || grip.Creature != __instance || !grip.Body || grip.Creature.IsDead()) return true;
        grip.Body.useGravity = true;
        return false;
    }
    void FixedUpdate()
    {
        if (!Held) return;
        if (!owner || !hand.Active || !Creature || Creature.IsDead() || !Body || Body.isKinematic
            || !player || player.IsDead() || (view && view.IsValid() && !view.IsOwner()))
        { Release(); return; }
        var target = hand.Tracked.position + hand.Tracked.rotation * hand.Palm;
        var error = target - GripPoint;
        if (error.sqrMagnitude > OpenXRPhysicalHands.HeldTrackingGap * OpenXRPhysicalHands.HeldTrackingGap || Time.unscaledTime >= until || !player.HaveStamina(1))
        {
            Release();
            // A small creature struggles loose rather than becoming an
            // indefinitely immobilised target. Its native AI keeps running.
            hand.Hand.hapticAction.Execute(0, .08f, 80, .35f, hand.Source);
            return;
        }
        player.UseStamina(Time.fixedDeltaTime * 8);
        var velocity = Vector3.ClampMagnitude((target - previousTarget) / Time.fixedDeltaTime, 5);
        previousTarget = target;
        var force = (error * 100 + (velocity - Body.GetPointVelocity(GripPoint)) * 20) * Body.mass;
        Body.AddForceAtPosition(Vector3.ClampMagnitude(force, 1200), GripPoint, ForceMode.Force);
        Body.AddForce(-Physics.gravity, ForceMode.Acceleration);
    }
    internal void Release()
    {
        Held = false;
        if (hand != null && hand.CreatureGrip == this) hand.CreatureGrip = null;
        Destroy(this);
    }
    void OnDisable()
    {
        Held = false;
        if (hand != null && hand.CreatureGrip == this) hand.CreatureGrip = null;
    }
}
