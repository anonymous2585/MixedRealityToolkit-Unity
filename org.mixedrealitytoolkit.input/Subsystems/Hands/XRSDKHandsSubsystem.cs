// Copyright (c) Mixed Reality Toolkit Contributors
// Licensed under the BSD 3-Clause

using MixedReality.Toolkit.Subsystems;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;
using InputDevice = UnityEngine.XR.InputDevice;

namespace MixedReality.Toolkit.Input
{
    /// <summary>
    /// A Unity subsystem that extends <see cref="MixedReality.Toolkit.Subsystems.HandsSubsystem">HandsSubsystem</see>, and 
    /// obtains hand joint poses from the Unity engine's XR <see href="https://docs.unity3d.com/ScriptReference/XR.Hand.html">Hand</see> class.
    /// </summary>
    [Preserve]
    [MRTKSubsystem(
        Name = "org.mixedrealitytoolkit.xrsdkhands",
        DisplayName = "Unity XR SDK Hand Data",
        Author = "Mixed Reality Toolkit Contributors",
        ProviderType = typeof(HandsProvider<XRSDKHandContainer>),
        SubsystemTypeOverride = typeof(XRSDKHandsSubsystem),
        ConfigType = typeof(BaseSubsystemConfig))]
    public class XRSDKHandsSubsystem : HandsSubsystem
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            // Fetch subsystem metadata from the attribute.
            var cinfo = XRSubsystemHelpers.ConstructCinfo<XRSDKHandsSubsystem, HandsSubsystemCinfo>();

            // Populate remaining cinfo field.
            cinfo.IsPhysicalData = true;

            if (!Register(cinfo))
            {
                Debug.LogError($"Failed to register the {cinfo.Name} subsystem.");
            }
        }

        /// <summary>
        /// A class that extends <see cref="MixedReality.Toolkit.Input.HandDataContainer">HandDataContainer</see>, and 
        /// obtains hand joint poses from the Unity Engine's XR <see href="https://docs.unity3d.com/ScriptReference/XR.Hand.html">Hand</see> class.
        /// </summary>
        private class XRSDKHandContainer : HandDataContainer
        {
            // The cached reference to the XRSDK tracked hand.
            // Is re-queried/TryGetFeatureValue'd each frame,
            // as the presence (or absence) of this reference
            // indicates tracking state.
            private Hand? handDevice;

            public XRSDKHandContainer(XRNode handNode) : base(handNode)
            {
                handDevice = GetTrackedHand();
            }

            private static readonly ProfilerMarker TryGetEntireHandPerfMarker =
                new ProfilerMarker("[MRTK] XRSDKHandContainer.TryGetEntireHand");

            /// <inheritdoc/>
            public override bool TryGetEntireHand(out IReadOnlyList<HandJointPose> result)
            {
                using (TryGetEntireHandPerfMarker.Auto())
                {
                    if (!AlreadyFullQueried)
                    {
                        TryCalculateEntireHand();
                    }

                    result = HandJoints;
                    return FullQueryValid;
                }
            }

            private static readonly ProfilerMarker TryGetJointPerfMarker =
                new ProfilerMarker("[MRTK] XRSDKHandContainer.TryGetJoint");

            /// <inheritdoc/>
            public override bool TryGetJoint(TrackedHandJoint joint, out HandJointPose pose)
            {
                using (TryGetJointPerfMarker.Auto())
                {
                    bool thisQueryValid = false;

                    // If we happened to have already queried the entire
                    // hand data this frame, we don't need to re-query for
                    // just the joint. If we haven't, we do still need to
                    // query for the single joint.
                    if (!AlreadyFullQueried)
                    {
                        handDevice = GetTrackedHand();

                        // If the tracked hand is null, we obviously have no data,
                        // and return immediately.
                        if (!handDevice.HasValue)
                        {
                            pose = HandJoints[HandsUtils.ConvertToIndex(joint)];
                            return false;
                        }

                        // Joints are relative to the camera floor offset object.
                        Transform origin = PlayspaceUtilities.XROrigin.CameraFloorOffsetObject.transform;
                        if (origin == null)
                        {
                            pose = HandJoints[HandsUtils.ConvertToIndex(joint)];
                            return false;
                        }

                        // Otherwise, we need to deal with palm/root vs finger separately
                        if (joint == TrackedHandJoint.Palm)
                        {
                            if (handDevice.Value.TryGetRootBone(out Bone rootBone))
                            {
                                thisQueryValid |= TryUpdateJoint(joint, rootBone, origin);
                            }
                        }
                        else
                        {
                            HandFinger finger = HandsUtils.GetFingerFromJoint(joint);
                            if (handDevice.Value.TryGetFingerBones(finger, fingerBones))
                            {
                                Bone bone = fingerBones[HandsUtils.GetOffsetFromBase(joint)];
                                thisQueryValid |= TryUpdateJoint(joint, bone, origin);
                            }
                        }
                    }
                    else
                    {
                        // If we've already run a full-hand query, this single joint query
                        // is just as valid as the full query.
                        thisQueryValid = FullQueryValid;
                    }

                    pose = HandJoints[HandsUtils.ConvertToIndex(joint)];
                    return thisQueryValid;
                }
            }

            // Scratchpad for reading out devices, to reduce allocs.
            private readonly List<InputDevice> handDevices = new List<InputDevice>(2);

            private static readonly ProfilerMarker GetTrackedHandPerfMarker =
                new ProfilerMarker("[MRTK] XRSDKHandContainer.GetTrackedHand");

            /// <summary>
            /// Obtains a reference to the actual Hand object representing the tracked hand
            /// functionality present on HandNode. Returns null if no Hand reference available.
            /// </summary>
            private Hand? GetTrackedHand()
            {
                using (GetTrackedHandPerfMarker.Auto())
                {
                    InputDevices.GetDevicesWithCharacteristics(HandNode == XRNode.LeftHand ? HandsUtils.LeftHandCharacteristics : HandsUtils.RightHandCharacteristics, handDevices);

                    if (handDevices.Count == 0)
                    {
                        // No hand devices detected at this hand.
                        return null;
                    }
                    else
                    {
                        foreach (InputDevice device in handDevices)
                        {
                            if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked)
                                && isTracked
                                && device.TryGetFeatureValue(CommonUsages.handData, out Hand handRef))
                            {
                                // We've found our device that supports CommonUsages.handData, and
                                // the specific Hand object that we can return.
                                return handRef;
                            }
                        }

                        // None of the devices on this hand are tracked and/or support CommonUsages.handData.
                        // This will happen when the platform doesn't support hand tracking,
                        // or the hand is not visible enough to return a tracking solution.
                        return null;
                    }
                }
            }

            // Scratchpad for reading out finger bones, to reduce allocs.
            private readonly List<Bone> fingerBones = new List<Bone>();

            private static readonly ProfilerMarker TryCalculateEntireHandPerfMarker =
                new ProfilerMarker("[MRTK] XRSDKHandContainer.TryCalculateEntireHand");

            /// <summary>
            /// For a certain hand, query every Bone in the hand, and write all results to the
            /// HandJoints collection.
            /// </summary>
            private void TryCalculateEntireHand()
            {
                using (TryCalculateEntireHandPerfMarker.Auto())
                {
                    handDevice = GetTrackedHand();

                    if (!handDevice.HasValue)
                    {
                        // No articulated hand device available this frame.
                        FullQueryValid = false;
                        AlreadyFullQueried = true;
                        return;
                    }

                    // Null checks against Unity objects can be expensive, especially when you do
                    // it 52 times per frame (26 hand joints across 2 hands). Instead, we manage
                    // the playspace transformation internally for hand joints.
                    // Joints are relative to the camera floor offset object.
                    Transform origin = PlayspaceUtilities.XROrigin.CameraFloorOffsetObject.transform;
                    if (origin == null)
                    {
                        return;
                    }

                    FullQueryValid = true;

                    foreach (HandFinger finger in HandsUtils.HandFingers)
                    {
                        if (handDevice.Value.TryGetFingerBones(finger, fingerBones))
                        {
                            for (int i = 0; i < fingerBones.Count; i++)
                            {
                                FullQueryValid &= TryUpdateJoint(HandsUtils.ConvertToTrackedHandJoint(finger, i), fingerBones[i], origin);
                            }
                        }
                    }

                    // Write root bone into HandJoints as palm joint.
                    FullQueryValid &=
                        handDevice.Value.TryGetRootBone(out Bone rootBone)
                        && TryUpdateJoint(TrackedHandJoint.Palm, rootBone, origin);

                    // Mark this hand as having been fully queried this frame.
                    // If any joint is queried again this frame, we'll reuse the
                    // information to avoid extra work.
                    AlreadyFullQueried = true;
                }
            }

            private static readonly ProfilerMarker TryUpdateJointPerfMarker =
                new ProfilerMarker("[MRTK] XRSDKHandContainer.TryUpdateJoint");

            /// <summary>
            /// Given a destination jointID, apply the Bone info to the correct struct
            /// in the HandJoints collection.
            /// </summary>
            private bool TryUpdateJoint(TrackedHandJoint jointID, Bone bone, Transform playspaceTransform)
            {
                using (TryUpdateJointPerfMarker.Auto())
                {
                    if (!bone.TryGetPosition(out Vector3 position) || !bone.TryGetRotation(out Quaternion rotation))
                    {
                        return false;
                    }

                    // XRSDK does not return joint radius. 0.5cm default.
                    HandJoints[HandsUtils.ConvertToIndex(jointID)] = new HandJointPose(
                        playspaceTransform.TransformPoint(position),
                        playspaceTransform.rotation * rotation,
                        HandsUtils.DefaultHandJointRadius);

                    return true;
                }
            }
        }
    }
}
