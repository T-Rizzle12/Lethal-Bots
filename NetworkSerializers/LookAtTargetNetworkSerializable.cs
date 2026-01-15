using GameNetcodeStuff;
using LethalBots.AI;
using LethalBots.Constants;
using LethalBots.Enums;
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
    /// <remarks>
    /// This really should be rewritten to use a much better system!
    /// The current is a bit outdated and not very efficient!
    /// This makes it hard to add new look at targets!
    /// </remarks>
    [Serializable]
    public class LookAtTarget : INetworkSerializable, IEquatable<LookAtTarget>
    {
        // Look at stuff
        public Vector3 lookAtPos;
        public EnumLookAtPriority lookAtPriority;
        public CountdownTimer lookAtExpireTimer;
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

        public LookAtTarget()
        {
            lookAtPos = Vector3.zero;
            lookAtPriority = EnumLookAtPriority.LOW_PRIORITY;
            lookAtExpireTimer = new CountdownTimer();
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
            serializer.SerializeValue(ref lookAtPriority);
            serializer.SerializeValue(ref lookAtExpireTimer);
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
        /// Is the bot's head steady?
        /// </summary>
        /// <returns></returns>
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
                lookAtPriority = this.lookAtPriority,
                lookAtExpireTimer = this.lookAtExpireTimer.Clone(),
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
            PlayerControllerB lethalBotController = npcController.Npc;
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

            // FIXME: make this close to rather than equal to!
            if (lethalBotController.gameplayCamera.transform.rotation == cameraRotationToUpdateLookAt)
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
                return;
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
                && lookAtPriority == other.lookAtPriority
                && lookAtExpireTimer == other.lookAtExpireTimer
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
            hash.Add(lookAtPriority);
            hash.Add(lookAtExpireTimer);
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

    // TODO: Move these timers into their own class file!
    [Serializable]
    public class IntervalTimer : INetworkSerializable, IEquatable<IntervalTimer>
    {
        public float timestamp = -1.0f;

        /// <summary>
        /// Restarts the Interval Timer
        /// </summary>
        public void Reset()
        {
            timestamp = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Starts the Interval Timer
        /// </summary>
        public void Start()
        {
            timestamp = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Stops the Interval Timer
        /// </summary>
        public void Invalidate()
        {
            timestamp = -1.0f;
        }

        /// <summary>
        /// Was the Interval Timer started?
        /// </summary>
        /// <returns>true: if we were started; otherwise false</returns>
        public bool HasStarted()
        {
            return timestamp > 0f;
        }

        /// <summary>
        /// How long has this timer been running!
        /// </summary>
        /// <returns></returns>
        public float GetElapsedTime()
        {
            return HasStarted() ? Time.realtimeSinceStartup - timestamp : -1.0f;
        }

        /// <summary>
        /// Has the timer been running longer than the given <paramref name="duration"/>
        /// </summary>
        /// <param name="duration">The duration to test!</param>
        /// <returns>true: if we have been running longer than <paramref name="duration"/>; otherwise false</returns>
        public bool IsGreaterThan(float duration)
        {
            return GetElapsedTime() > duration;
        }

        /// <summary>
        /// Has the timer been running less than the given <paramref name="duration"/>
        /// </summary>
        /// <param name="duration">The duration to test!</param>
        /// <returns>true: if we have been running less than <paramref name="duration"/>; otherwise false</returns>
        public bool IsLessThan(float duration)
        {
            return GetElapsedTime() < duration;
        }

        /// <summary>
        /// Creates a deep copy of this <see cref="IntervalTimer"/> instance
        /// </summary>
        /// <returns></returns>
        public IntervalTimer Clone()
        {
            return new IntervalTimer(){
                timestamp = this.timestamp
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref timestamp);
        }

        public bool Equals(IntervalTimer other)
        {
            return timestamp == other.timestamp;
        }

        public override bool Equals(object? obj)
        {
            return obj is IntervalTimer other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(timestamp);
        }

        public static bool operator ==(IntervalTimer? left, IntervalTimer? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(IntervalTimer? left, IntervalTimer? right)
        {
            return !(left == right);
        }
    }

    [Serializable]
    public class CountdownTimer : INetworkSerializable, IEquatable<CountdownTimer>
    {
        public float startTime = -1.0f;
        public float endTime = -1.0f;

        /// <summary>
        /// Restarts the Interval Timer
        /// </summary>
        public void Reset()
        {
            startTime = -1.0f;
            endTime = -1.0f;
        }

        /// <summary>
        /// Starts the Countdown Timer with the given <paramref name="time"/>
        /// </summary>
        /// <param name="time">How long should this timer run</param>
        public void Start(float time)
        {
            startTime = Time.realtimeSinceStartup;
            endTime = Time.realtimeSinceStartup + (time >= 0 ? time : 0);
        }

        /// <summary>
        /// Was the Countdown Timer started?
        /// </summary>
        /// <returns>true: if we were started; otherwise false</returns>
        public bool HasStarted()
        {
            return endTime > 0f;
        }

        /// <summary>
        /// How long has this timer been running!
        /// </summary>
        /// <returns></returns>
        public float GetElapsedTime()
        {
            return HasStarted() ? Time.realtimeSinceStartup - startTime : -1.0f;
        }

        /// <summary>
        /// Has this Countdown Timer elapsed
        /// </summary>
        /// <returns></returns>
        public bool Elapsed()
        {
            return HasStarted() && endTime <= Time.realtimeSinceStartup;
        }

        /// <summary>
        /// Creates a deep copy of this <see cref="CountdownTimer"/> instance
        /// </summary>
        /// <returns></returns>
        public CountdownTimer Clone()
        {
            return new CountdownTimer()
            {
                startTime = this.startTime,
                endTime = this.endTime
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref startTime);
            serializer.SerializeValue(ref endTime);
        }

        public bool Equals(CountdownTimer other)
        {
            return startTime == other.startTime 
                && endTime == other.endTime;
        }

        public override bool Equals(object? obj)
        {
            return obj is CountdownTimer other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(startTime, endTime);
        }

        public static bool operator ==(CountdownTimer? left, CountdownTimer? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(CountdownTimer? left, CountdownTimer? right)
        {
            return !(left == right);
        }
    }
}
