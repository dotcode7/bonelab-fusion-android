﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using HarmonyLib;

using LabFusion.Data;
using LabFusion.Network;
using LabFusion.Representation;
using LabFusion.Syncables;
using LabFusion.Utilities;

using SLZ.Interaction;
using SLZ.Player;
using SLZ.Props.Weapons;
using SLZ.Rig;

namespace LabFusion.Patching {

    [HarmonyPatch(typeof(InventorySlotReceiver), nameof(InventorySlotReceiver.OnHandGrab))]
    public class InventorySlotReceiverGrab
    {
        public static bool PreventDropCheck = false;

        public static void Prefix(InventorySlotReceiver __instance, Hand hand) {
            if (PreventDropCheck)
                return;

            try {
                if (NetworkInfo.HasServer && __instance._slottedWeapon && hand.manager == RigData.RigReferences.RigManager) {
                    var rigManager = __instance.GetComponentInParent<RigManager>();

                    if (rigManager == RigData.RigReferences.RigManager) {
                        byte? index = RigData.RigReferences.GetIndex(__instance);

                        if (!index.HasValue) {
                            return;
                        }
                        
                        using (var writer = FusionWriter.Create())
                        {
                            using (var data = InventorySlotDropData.Create(PlayerIdManager.LocalSmallId, index.Value, hand.handedness))
                            {
                                writer.Write(data);

                                using (var message = FusionMessage.Create(NativeMessageTag.InventorySlotDrop, writer))
                                {
                                    MessageSender.SendToServer(NetworkChannel.Reliable, message);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e) {
#if DEBUG
                FusionLogger.LogException("patching InventorySlotReceiver.OnHandGrab", e);
#endif
            }
        }
    }

    [HarmonyPatch(typeof(InventorySlotReceiver), nameof(InventorySlotReceiver.OnHandDrop))]
    public class InventorySlotReceiverDrop
    {
        public static bool PreventInsertCheck = false;

        public static void Postfix(InventorySlotReceiver __instance, IGrippable host)
        {
            if (PreventInsertCheck)
                return;

            try
            {
                if (NetworkInfo.HasServer && __instance._slottedWeapon && PropSyncable.WeaponSlotCache.TryGetValue(__instance._slottedWeapon, out var syncable)) {
                    var rigManager = __instance.GetComponentInParent<RigManager>();

                    if (rigManager == RigData.RigReferences.RigManager) {
                        byte? index = RigData.RigReferences.GetIndex(__instance);

                        if (!index.HasValue) {
                            return;
                        }


                        using (var writer = FusionWriter.Create())
                        {
                            using (var data = InventorySlotInsertData.Create(PlayerIdManager.LocalSmallId, syncable.Id, index.Value))
                            {
                                writer.Write(data);

                                using (var message = FusionMessage.Create(NativeMessageTag.InventorySlotInsert, writer))
                                {
                                    MessageSender.SendToServer(NetworkChannel.Reliable, message);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
#if DEBUG
                FusionLogger.LogException("patching InventorySlotReceiver.OnHandDrop", e);
#endif
            }
        }
    }
}