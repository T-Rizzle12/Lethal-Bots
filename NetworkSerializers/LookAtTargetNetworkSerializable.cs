using GameNetcodeStuff;
using JetBrains.Annotations;
using LethalBots.AI;
using LethalBots.AI.AIStates;
using LethalBots.Constants;
using LethalBots.Enums;
using LethalBots.Utils.Helpers;
using System;
using System.Runtime.CompilerServices;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LethalBots.NetworkSerializers
{
    /// <summary>
    /// Class for serializing the look at target of the bot
    /// </summary>
    [Serializable]
    public class LookAtTarget : INetworkSerializable, IEquatable<LookAtTarget>
    {
        // Look at stuff
        public Vector3 lookAtPos;
        public NetworkObjectReference? lookAtSubject;
        public EnumLookAtPriority lookAtPriority;
        public CountdownTimer lookAtExpireTimer;
        public CountdownTimer lookAtTrackingTimer;
        public IntervalTimer lookAtDurationTimer;
        public bool isSightedIn;
        public bool hasBeenSightedIn;
        public IntervalTimer headSteadyTimer;
        public Vector3 directionToUpdateTurnBodyTowardsTo;
        public float maxBodyFOV;

        // Old stuff I hope to get rid of eventually
        public EnumObjectsLookingAt enumObjectsLookingAt;

        // Not networked stuff
        private Vector3 lastDirectionToLookAt;
        private Quaternion cameraRotationToUpdateLookAt;
        public const float ON_TARGET_TOLERANCE = 0.98f;

        public LookAtTarget()
        {
            lookAtPos = Vector3.zero;
            lookAtSubject = null;
            lookAtPriority = EnumLookAtPriority.LOW_PRIORITY;
            lookAtExpireTimer = new CountdownTimer();
            lookAtTrackingTimer = new CountdownTimer();
            lookAtDurationTimer = new IntervalTimer();
            isSightedIn = false;
            hasBeenSightedIn = false;
            headSteadyTimer = new IntervalTimer();
            maxBodyFOV = Const.LETHAL_BOT_FOV;
            enumObjectsLookingAt = EnumObjectsLookingAt.Forward;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref lookAtPos);
            LethalBotNetworkSerializer.SerializeNullable(serializer, ref lookAtSubject);
            serializer.SerializeValue(ref lookAtPriority);
            serializer.SerializeValue(ref lookAtExpireTimer);
            serializer.SerializeValue(ref lookAtTrackingTimer);
            serializer.SerializeValue(ref lookAtDurationTimer);
            serializer.SerializeValue(ref isSightedIn);
            serializer.SerializeValue(ref hasBeenSightedIn);
            serializer.SerializeValue(ref headSteadyTimer);
            serializer.SerializeValue(ref directionToUpdateTurnBodyTowardsTo);
            serializer.SerializeValue(ref maxBodyFOV);
            serializer.SerializeValue(ref enumObjectsLookingAt);
        }

        /// <summary>
        /// Is the bot's <see cref="enumObjectsLookingAt"/> set to <see cref="EnumObjectsLookingAt.Forward"/>
        /// </summary>
        /// <returns>true: if we are; otherwise false</returns>
        public bool IsLookingForward()
        {
            return enumObjectsLookingAt == EnumObjectsLookingAt.Forward;
        }

        /// <summary>
        /// Checks if the bot is allowed to swap aim states
        /// </summary>
        /// <param name="priority">The <see cref="EnumLookAtPriority"/> of the given <paramref name="lookAtPos"/></param>
        /// <param name="lookAtMustExpire">If we must wait until <see cref="lookAtExpireTimer"/> elapses first!</param>
        /// <param name="bypassSteadyCheck">If the given <paramref name="priority"/> is the same as our current, should we bypass the steady head checks</param>
        /// <returns>true: is the bot able to swap aim states; otherwise false</returns>
        public bool CanSwapAimState(EnumLookAtPriority priority = EnumLookAtPriority.LOW_PRIORITY, bool lookAtMustExpire = false, bool bypassSteadyCheck = false)
        {
            // Don't make us spin around if its the same priority!
            if (this.lookAtPriority == priority)
            {
                if (!bypassSteadyCheck && (!IsHeadSteady() || GetHeadSteadyDuration() < 0.3f))
                {
                    return false;
                }
            }

            // Higher priority targets must finish before we can look at lower priorites!
            if ((lookAtMustExpire || this.lookAtPriority > priority) && !this.lookAtExpireTimer.Elapsed())
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Does the bot consider itself aiming at where it wants to look at?
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsHeadAimingOnTarget()
        {
            return isSightedIn;
        }

        /// <summary>
        /// Is the bot's head steady?
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsHeadSteady()
        {
            return headSteadyTimer.HasStarted();
        }

        /// <summary>
        /// How long has the bot's head been steady.
        /// </summary>
        /// <returns></returns>
        public float GetHeadSteadyDuration()
        {
            if (headSteadyTimer.HasStarted())
            {
                return headSteadyTimer.GetElapsedTime();
            }
            return 0f;
        }

        /// <summary>
        /// Creates a deep copy of this <see cref="LookAtTarget"/> instance
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LookAtTarget Clone()
        {
            return new LookAtTarget() {
                lookAtPos = this.lookAtPos,
                lookAtSubject = this.lookAtSubject,
                lookAtPriority = this.lookAtPriority,
                lookAtExpireTimer = this.lookAtExpireTimer.Clone(),
                lookAtTrackingTimer = this.lookAtTrackingTimer.Clone(),
                lookAtDurationTimer = this.lookAtDurationTimer.Clone(),
                isSightedIn = this.isSightedIn,
                hasBeenSightedIn = this.hasBeenSightedIn,
                headSteadyTimer = this.headSteadyTimer.Clone(),
                directionToUpdateTurnBodyTowardsTo = this.directionToUpdateTurnBodyTowardsTo,
                maxBodyFOV = this.maxBodyFOV
            };
        }

        /// <summary>
        /// Makes the <see cref="LethalBotAI"/> associated with this <see cref="LookAtTarget"/> look at the given <paramref name="lookAtPos"/>
        /// </summary>
        /// <param name="lookAtPos">The postion to look at</param>
        /// <param name="priority">The <see cref="EnumLookAtPriority"/> of the given <paramref name="lookAtPos"/></param>
        /// <param name="duration">How long we should look at <paramref name="lookAtPos"/></param>
        /// <param name="bypassSteadyCheck">If the given <paramref name="priority"/> is the same as our current, should we bypass the steady head checks</param>
        /// <param name="maxBodyFOV">The maximum FOV the bot is allowed to look at before its forced to turn its body and not just its head</param>
        public void AimHeadTowards(Vector3 lookAtPos, EnumLookAtPriority priority = EnumLookAtPriority.LOW_PRIORITY, float duration = 0.0f, bool bypassSteadyCheck = false, float maxBodyFOV = Const.LETHAL_BOT_FOV)
        {
            // Duration can not be negative!
            if (duration <= 0.0f)
            {
                duration = 0.1f;
            }

            // Make sure we can actually change aim states!
            if (!CanSwapAimState(priority: priority, bypassSteadyCheck: bypassSteadyCheck))
            {
                return;
            }

            this.lookAtExpireTimer.Start(duration);

            // If given the same point, just update priority and FOV
            if ((this.lookAtPos - lookAtPos).sqrMagnitude < Const.EPSILON * Const.EPSILON)
            {
                this.lookAtPriority = priority;
                this.maxBodyFOV = maxBodyFOV;
                return;
            }

            this.lookAtPos = lookAtPos;
            this.lookAtSubject = null;
            this.lookAtDurationTimer.Start();
            this.lookAtPriority = priority;
            this.hasBeenSightedIn = false;
            this.maxBodyFOV = maxBodyFOV;
        }

        /// <summary>
        /// Makes the <see cref="LethalBotAI"/> associated with this <see cref="LookAtTarget"/> look at the given <paramref name="lookAtSubject"/>
        /// </summary>
        /// <param name="lookAtSubject">The <see cref="NetworkObject"/> to look at</param>
        /// <param name="priority">The <see cref="EnumLookAtPriority"/> of the given <paramref name="lookAtSubject"/></param>
        /// <param name="duration">How long we should look at <paramref name="lookAtSubject"/></param>
        /// <param name="bypassSteadyCheck">If the given <paramref name="priority"/> is the same as our current, should we bypass the steady head checks</param>
        /// <param name="maxBodyFOV">The maximum FOV the bot is allowed to look at before its forced to turn its body and not just its head</param>
        public void AimHeadTowards(NetworkObjectReference lookAtSubject, EnumLookAtPriority priority = EnumLookAtPriority.LOW_PRIORITY, float duration = 0.0f, bool bypassSteadyCheck = false, float maxBodyFOV = Const.LETHAL_BOT_FOV)
        {
            // Duration can not be negative!
            if (duration <= 0.0f)
            {
                duration = 0.1f;
            }

            // Make sure we can actually change aim states!
            if (!CanSwapAimState(priority: priority, bypassSteadyCheck: bypassSteadyCheck))
            {
                return;
            }

            this.lookAtExpireTimer.Start(duration);

            // If given the same target, just update priority and FOV
            if (this.lookAtSubject.Equals(lookAtSubject))
            {
                this.lookAtPriority = priority;
                this.maxBodyFOV = maxBodyFOV;
                return;
            }

            this.lookAtSubject = lookAtSubject;
            this.lookAtDurationTimer.Start();
            this.lookAtPriority = priority;
            this.hasBeenSightedIn = false;
            this.maxBodyFOV = maxBodyFOV;
        }

        /// <summary>
        /// Updates info about the where the bot is currently looking.
        /// </summary>
        public void Update(NpcController npcController, LethalBotAI lethalBotAI)
        {
            // If we are aiming at a target subject, make sure to update our target lookAtPos!
            PlayerControllerB lethalBotController = npcController.Npc;
            if (!lookAtTrackingTimer.HasStarted() || lookAtTrackingTimer.Elapsed())
            {
                lookAtTrackingTimer.Start(UnityEngine.Random.Range(0.05f, 0.3f));
                if (lookAtSubject.HasValue)
                {
                    Vector3? lookAtSubjectPos = lethalBotAI.State?.SelectSubjectTargetPoint(this, lookAtSubject.Value, lethalBotController);
                    if (lookAtSubjectPos.HasValue)
                    {
                        lookAtPos = lookAtSubjectPos.Value;
                    }
                }
            }

            Vector3 direction = lookAtPos - lethalBotController.gameplayCamera.transform.position;
            if (!npcController.DirectionNotZero(direction.x) && !npcController.DirectionNotZero(direction.y) && !npcController.DirectionNotZero(direction.z))
            {
                return;
            }

            if (direction != lastDirectionToLookAt)
            {
                lastDirectionToLookAt = direction;
                cameraRotationToUpdateLookAt = Quaternion.LookRotation(new Vector3(direction.x, direction.y, direction.z));
            }

            // Check if we are sighted in!
            Vector3 to = direction.normalized;
            Vector3 forward = lethalBotController.gameplayCamera.transform.forward;
            if (Vector3.Dot(forward, to) > ON_TARGET_TOLERANCE)
            {
                // We are sighted in!
                if (!hasBeenSightedIn)
                {
                    hasBeenSightedIn = true;
                }

                // Our head is steady now, start the timer
                if (!headSteadyTimer.HasStarted())
                {
                    headSteadyTimer.Start();
                }

                isSightedIn = true;
            }
            else
            {
                headSteadyTimer.Invalidate();
                isSightedIn = false;
            }

            // Check whether we are set to look forward or at a certain position
            if (enumObjectsLookingAt == EnumObjectsLookingAt.Forward)
            {
                if (lethalBotController.gameplayCamera.transform.rotation == lethalBotController.thisPlayerBody.rotation)
                {
                    return;
                }

                lethalBotController.gameplayCamera.transform.rotation = Quaternion.RotateTowards(lethalBotController.gameplayCamera.transform.rotation, lethalBotController.thisPlayerBody.rotation, Const.CAMERA_TURNSPEED);
                return;
            }

            lethalBotController.gameplayCamera.transform.rotation = Quaternion.RotateTowards(lethalBotController.gameplayCamera.transform.rotation, cameraRotationToUpdateLookAt, Const.CAMERA_TURNSPEED);
            if (Vector3.Angle(lethalBotController.gameplayCamera.transform.forward, lethalBotController.thisPlayerBody.transform.forward) > Mathf.Max(maxBodyFOV - 20f, 0f))
            {
                npcController.SetTurnBodyTowardsDirectionWithPosition(lookAtPos);
            }
        }

        public bool Equals(LookAtTarget? other)
        {
            if (other is null)
            {
                return false;
            }
            return lookAtPos == other.lookAtPos 
                && lookAtSubject.Equals(other.lookAtSubject)
                && lookAtPriority == other.lookAtPriority
                && lookAtExpireTimer == other.lookAtExpireTimer
                && lookAtTrackingTimer == other.lookAtTrackingTimer
                && lookAtDurationTimer == other.lookAtDurationTimer
                && isSightedIn == other.isSightedIn
                && hasBeenSightedIn == other.hasBeenSightedIn
                && headSteadyTimer == other.headSteadyTimer
                && directionToUpdateTurnBodyTowardsTo == other.directionToUpdateTurnBodyTowardsTo 
                && maxBodyFOV == other.maxBodyFOV;
        }

        public override bool Equals(object? obj)
        {
            return obj is LookAtTarget other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(lookAtPos);
            hash.Add(lookAtSubject);
            hash.Add(lookAtPriority);
            hash.Add(lookAtExpireTimer);
            hash.Add(lookAtTrackingTimer);
            hash.Add(lookAtDurationTimer);
            hash.Add(isSightedIn);
            hash.Add(hasBeenSightedIn);
            hash.Add(headSteadyTimer);
            hash.Add(directionToUpdateTurnBodyTowardsTo);
            hash.Add(maxBodyFOV);
            return hash.ToHashCode();
        }

        public static bool operator ==(LookAtTarget? left, LookAtTarget? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(LookAtTarget? left, LookAtTarget? right)
        {
            return !(left == right);
        }
    }
}
