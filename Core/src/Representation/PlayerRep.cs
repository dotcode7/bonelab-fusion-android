﻿using LabFusion.Data;
using LabFusion.Extensions;
using LabFusion.Network;
using LabFusion.Utilities;
using SLZ;
using SLZ.Interaction;
using SLZ.Marrow.Warehouse;
using SLZ.Rig;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace LabFusion.Representation
{
    public class PlayerRep : IDisposable {
        public static readonly Dictionary<byte, PlayerRep> Representations = new Dictionary<byte, PlayerRep>();
        public static readonly Dictionary<RigManager, PlayerRep> Managers = new Dictionary<RigManager, PlayerRep>(new UnityComparer());

        public const float PelvisPinMlp = 0.1f;

        public PlayerId PlayerId { get; private set; }
        public string Username { get; private set; } = "Unknown";

        public RigReferenceCollection RigReferences { get; private set; } = new RigReferenceCollection();

        public static Transform[] syncedPoints = new Transform[PlayerRepUtilities.TransformSyncCount];
        public static Transform syncedPlayspace;
        public static Transform syncedPelvis;
        public static BaseController syncedLeftController;
        public static BaseController syncedRightController;

        public SerializedTransform[] serializedTransforms = new SerializedTransform[PlayerRepUtilities.TransformSyncCount];
        public SerializedTransform serializedPelvis;

        public Vector3 predictVelocity;
        public float timeSincePelvisSent;

        public Transform[] repTransforms = new Transform[PlayerRepUtilities.TransformSyncCount];
        public OpenControllerRig repControllerRig;
        public Transform repPlayspace;
        public Rigidbody repPelvis;
        public BaseController repLeftController;
        public BaseController repRightController;

        public PullCordDevice pullCord;

        public GameObject repCanvas;
        public Canvas repCanvasComponent;
        public Transform repCanvasTransform;
        public TextMeshProUGUI repNameText;

        public SerializedBodyVitals vitals = null;
        public string avatarId = AvatarWarehouseUtilities.INVALID_AVATAR_BARCODE;

        public PlayerRep(PlayerId playerId, string barcode)
        {
            PlayerId = playerId;

            Username = playerId.Username;

            Representations.Add(playerId.SmallId, this);
            avatarId = barcode;

            CreateRep();
        }

        public void AttachObject(Handedness handedness, Grip grip) {
            var hand = RigReferences.GetHand(handedness);
            if (hand == null)
                return;

            if (grip) {
                grip.OnGrabConfirm(hand, true);
                RigReferences.SetSnatch(handedness, grip);
            }
        }

        public void DetachObject(Handedness handedness) {
            var hand = RigReferences.GetHand(handedness);
            if (hand == null)
                return;

            var grip = RigReferences.GetSnatch(handedness);
            if (grip)
                grip.ForceDetach(hand);
            else
                hand.DetachObject();
        }

        public void OnHandFixedUpdate(Hand hand) {
            if (hand.m_CurrentAttachedGO == null || hand.joint == null) {
                RigReferences.SetSerializedAnchor(hand.handedness, null);
            }
            else {
                var anchor = RigReferences.GetSerializedAnchor(hand.handedness);

                if (anchor != null)
                    anchor.CopyTo(hand, Grip.Cache.Get(hand.m_CurrentAttachedGO));
            }
        }

        public void SwapAvatar(string barcode) {
            avatarId = barcode;

            if (RigReferences.RigManager && !string.IsNullOrWhiteSpace(barcode))
                RigReferences.RigManager.SwapAvatarCrate(barcode, false, (Il2CppSystem.Action<bool>)OnSwapAvatar);
        }

        public void OnSwapAvatar(bool success) {
            if (pullCord) {
                pullCord.PlayAvatarParticleEffects();
                pullCord.PlayClip(pullCord.switchAvatar, pullCord.ap3, pullCord.switchVolume, 4f, false);
            }
        }

        public void SetVitals(SerializedBodyVitals vitals) {
            this.vitals = vitals;
            if (RigReferences.RigManager != null && vitals != null) {
                vitals.CopyTo(RigReferences.RigManager.bodyVitals);
                RigReferences.RigManager.bodyVitals.CalibratePlayerBodyScale();
            }
        }

        public void CreateRep() {
            // Make sure we don't have any extra objects
            DestroyRep();

            repCanvas = new GameObject("RepCanvas");
            repCanvasComponent = repCanvas.AddComponent<Canvas>();

            repCanvasComponent.renderMode = RenderMode.WorldSpace;
            repCanvasTransform = repCanvas.transform;
            repCanvasTransform.localScale = Vector3.one / 200.0f;

            repNameText = repCanvas.AddComponent<TextMeshProUGUI>();

            repNameText.alignment = TextAlignmentOptions.Midline;
            repNameText.enableAutoSizing = true;

            repNameText.text = Username;

            var rig = PlayerRepUtilities.CreateNewRig();
            pullCord = rig.GetComponentInChildren<PullCordDevice>(true);

            if (vitals != null) {
                vitals.CopyTo(rig.bodyVitals);
                rig.bodyVitals.CalibratePlayerBodyScale();
            }

            // Lock many of the bones in place to increase stability
            foreach (var found in rig.GetComponentsInChildren<ConfigurableJoint>(true)) {
                found.projectionMode = JointProjectionMode.PositionAndRotation;
                found.projectionDistance = 0.001f;
                found.projectionAngle = 40f;
            }

            if (!string.IsNullOrWhiteSpace(avatarId))
                rig.SwapAvatarCrate(avatarId);

            var leftHaptor = rig.openControllerRig.leftController.haptor;
            rig.openControllerRig.leftController = rig.openControllerRig.leftController.gameObject.AddComponent<Controller>();
            leftHaptor.device_Controller = rig.openControllerRig.leftController;
            rig.openControllerRig.leftController.handedness = Handedness.LEFT;

            var rightHaptor = rig.openControllerRig.rightController.haptor;
            rig.openControllerRig.rightController = rig.openControllerRig.rightController.gameObject.AddComponent<Controller>();
            rightHaptor.device_Controller = rig.openControllerRig.rightController;
            rig.openControllerRig.rightController.handedness = Handedness.RIGHT;

            Managers.Add(rig, this);

            repPelvis = rig.physicsRig.m_pelvis.GetComponent<Rigidbody>();
            repControllerRig = rig.openControllerRig;
            repPlayspace = rig.openControllerRig.vrRoot.transform;

            repLeftController = repControllerRig.leftController;
            repRightController = repControllerRig.rightController;

            RigReferences = new RigReferenceCollection(rig);

            PlayerRepUtilities.FillTransformArray(ref repTransforms, rig);
        }

        public static void OnRecreateReps() {
            foreach (var rep in Representations.Values) {
                rep.CreateRep();
            }
        }

        public void OnUpdateTransforms() {
            try
            {
                if (repTransforms == null)
                    return;

                if (serializedTransforms == null)
                    return;

                for (var i = 0; i < PlayerRepUtilities.TransformSyncCount; i++)
                {
                    if (repTransforms[i] == null)
                        break;

                    repTransforms[i].localPosition = serializedTransforms[i].position;
                    repTransforms[i].localRotation = serializedTransforms[i].rotation.Expand();
                }

                if (repCanvasTransform) {
                    repCanvasTransform.position = repTransforms[0].position + Vector3.up * 0.4f;

                    if (RigData.RigReferences.RigManager)
                        repCanvasTransform.rotation = Quaternion.LookRotation(Vector3.Normalize(repCanvasTransform.position - RigData.RigReferences.RigManager.physicsRig.m_head.position), Vector3.up);
                }
            }
            catch {
                // Literally no reason this should happen but it does
                // Doesn't cause anything soooo
            }
        }

        public void OnUpdateVelocity() {
            try {
                // Stop pelvis
                if (repPelvis == null)
                    return;

                // Move position with prediction
                if (Time.realtimeSinceStartup - timeSincePelvisSent <= 2.5f)
                    serializedPelvis.position += predictVelocity;

                // Apply velocity
                if (Time.timeScale > 0f && Time.deltaTime > 0f && Time.fixedDeltaTime > 0f)
                    repPelvis.velocity = PhysXUtils.GetLinearVelocity(repPelvis.transform.position, serializedPelvis.position) * PelvisPinMlp;

                // Check for stability teleport
                if (RigData.RigReferences.RigManager && RigData.RigReferences.RigManager.avatar)
                {
                    float distSqr = (repPelvis.transform.position - serializedPelvis.position).sqrMagnitude;
                    if (distSqr > (1.2f * RigData.RigReferences.RigManager.avatar.height))
                    {
                        RigReferences.RigManager.Teleport(serializedPelvis.position);
                    }
                }
            }
            catch {
                // I give up idk there was so many null checks and yet still an error when missed
                // Il2?
            }
        }

        private static bool TrySendRep() {
            try {
                if (syncedPoints == null)
                    return false;

                for (var i = 0; i < syncedPoints.Length; i++) {
                    if (syncedPoints[i] == null)
                        return false;
                }

                using (var writer = FusionWriter.Create()) {
                    using (var data = PlayerRepTransformData.Create(PlayerIdManager.LocalSmallId, syncedPoints, syncedPelvis, syncedPlayspace, syncedLeftController, syncedRightController)) {
                        writer.Write(data);

                        using (var message = FusionMessage.Create(NativeMessageTag.PlayerRepTransform, writer)) {
                            MessageSender.BroadcastMessage(NetworkChannel.Unreliable, message);
                        }
                    }
                }

                return true;
            } 
            catch (Exception e) {
#if DEBUG
                FusionLogger.Error($"Failed sending player transforms with reason: {e.Message}\nTrace:{e.StackTrace}");
#endif
            }
            return false;
        }

        public static void OnSyncRep() {
            if (NetworkInfo.HasServer) {
                if (!TrySendRep())
                    OnCachePlayerTransforms();
            }
        }

        public void OnRepFixedUpdate() {
            OnHandFixedUpdate(RigReferences.LeftHand);
            OnHandFixedUpdate(RigReferences.RightHand);
        }

        public static void OnFixedUpdate() {
            foreach (var rep in Representations.Values)
                rep.OnRepFixedUpdate();
        }

        /// <summary>
        /// Destroys anything about the PlayerRep and frees it from memory.
        /// </summary>
        public void Dispose() {
            Representations.Remove(PlayerId.SmallId);

            DestroyRep();

            GC.SuppressFinalize(this);

#if DEBUG
            FusionLogger.Log($"Disposed PlayerRep with small id {PlayerId.SmallId}");
#endif
        }

        /// <summary>
        /// Destroys the GameObjects of the PlayerRep. Does not free it from memory or remove it from its slots. Use Dispose for that.
        /// </summary>
        public void DestroyRep() {
            if (RigReferences.RigManager != null)
                GameObject.Destroy(RigReferences.RigManager.gameObject);

            if (repCanvas != null)
                GameObject.Destroy(repCanvas.gameObject);
        }

        public static void OnCachePlayerTransforms() {
            if (RigData.RigReferences.RigManager == null)
                return;

            syncedPelvis = RigData.RigReferences.RigManager.physicsRig.m_pelvis;
            syncedPlayspace = RigData.RigReferences.RigManager.openControllerRig.vrRoot.transform;
            syncedLeftController = RigData.RigReferences.RigManager.openControllerRig.leftController;
            syncedRightController = RigData.RigReferences.RigManager.openControllerRig.rightController;

            PlayerRepUtilities.FillTransformArray(ref syncedPoints, RigData.RigReferences.RigManager);
        }
    }
}
