using System;
using System.Collections.Generic;
using UnityEngine;
using BepInEx;
using RogueLibsCore;

namespace FollowerPlus
{
    [BepInPlugin(pGuid, pName, pVers)]
    public class FollowerPlusMain : BaseUnityPlugin
    {
        public const string pGuid = "cf27n.streetsofrogue.FollowerPlus";
        public const string pName = "FollowerPlus";
        public const string pVers = "0.1.0";

        public static BepInEx.Logging.ManualLogSource mLog;

        public void Awake()
        {
            RoguePatcher patcher = new RoguePatcher(this, typeof(InteractionPatches));
            patcher.Postfix(typeof(AgentInteractions), nameof(AgentInteractions.DetermineButtons));
            patcher.Prefix(typeof(AgentInteractions), nameof(AgentInteractions.PressedButton));
            patcher.Postfix(typeof(InvSlot), nameof(InvSlot.UpdateInvSlot));
            patcher.Prefix(typeof(InvSlot), nameof(InvSlot.BuyItem));
            patcher.Postfix(typeof(AgentInteractions), nameof(AgentInteractions.UseItemOnObject), new Type[] { typeof(Agent), typeof(Agent), typeof(InvItem), typeof(int), typeof(string), typeof(string) });
            
            RogueLibs.LoadFromAssembly();

            mLog = Logger;
            mLog.LogMessage("FollowerPlus");
        }
    }

    public class InteractionPatches
    {
        private static BepInEx.Logging.ManualLogSource mLog = FollowerPlusMain.mLog;

        public static void AgentInteractions_DetermineButtons(AgentInteractions __instance, Agent agent, Agent interactingAgent, List<string> buttons1, List<string> buttonsExtra1, List<int> buttonPrices1)
        {
            if (agent.employer == interactingAgent)
            {
                __instance.AddButton("ShowMeYourInv");
            }
        }

        public static bool AgentInteractions_PressedButton(AgentInteractions __instance, Agent agent, Agent interactingAgent, string buttonText, int buttonPrice)
        {
            mLog.LogMessage(agent.agentName + " : " + interactingAgent.agentName);

            if (buttonText == "ShowMeYourInv")
            {
                FollowerInventoryManagement.SetInvManagementStatus(interactingAgent, true);
                agent.ShowNPCChest(agent.inventory, false);
                return false;
            } else
            {
                FollowerInventoryManagement.SetInvManagementStatus(interactingAgent, false);
            }

            if (buttonText == "GiveItem")
            {

            }
            return true;
        }

        public static void InvSlot_UpdateInvSlot(InvSlot __instance)
        {
            if (__instance.slotType == "NPCChest")
            {
                if (__instance.curItemList.Count == 0)
                    __instance.SetupCurItemList();

                if (__instance.invInterface.chestDatabase.InvItemList[__instance.slotNumber].invItemName != null)
                {
                    if (__instance.agent == null || __instance.agent.interactionHelper.interactionAgent == null || !__instance.agent.worldSpaceGUI.openedNPCChest) { return; }
                    if (!FollowerInventoryManagement.IsManagingInventory(__instance.agent)) { return; }

                    if (!FollowerInventoryManagement.IsBorrowedItem(__instance.item, __instance.agent, __instance.agent.interactionHelper.interactionAgent))
                    {
                        __instance.myImage.color = new Color32(__instance.br, 0, __instance.br, __instance.standardAlpha);
                        __instance.itemImage.color = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, __instance.fadedItemAlpha);
                    }

                    __instance.toolbarNumText.enabled = false;

                    if (__instance.mySelectable == __instance.mainGUI.curSelected && __instance.agent.controllerType == "Gamepad")
                        __instance.overSlot = true;
                    else if (__instance.agent.controllerType == "Gamepad")
                        __instance.overSlot = false;
                    if (__instance.invInterfaceTr.localScale != Vector3.one && __instance.slotType == "Player")
                        __instance.overSlot = false;
                }
            }
        }

        public static bool InvSlot_BuyItem(InvSlot __instance)
        {
            if (__instance.item == null || __instance.agent == null || __instance.agent.interactionHelper.interactionAgent == null) { return true; }
            if (!FollowerInventoryManagement.IsManagingInventory(__instance.agent)) { return true; }
            if (!FollowerInventoryManagement.IsBorrowedItem(__instance.item, __instance.agent, __instance.agent.interactionHelper.interactionAgent)) { return false; }
            if (__instance.item != null && (!__instance.item.questItem || __instance.item.questItemCanBuy) && (__instance.item.invItemName != "Money" && __instance.item.itemValue != 0))
            {
                int num = __instance.MoveFromChestToInventory() ? 1 : 0;
                FollowerInventoryManagement.RemoveBorrowedItem(__instance.item, __instance.agent, __instance.agent.interactionHelper.interactionAgent);
                return false;
            }
            return true;
        }

        public static void AgentInteractions_UseItemOnObject(Agent agent, Agent interactingAgent, InvItem item, int slotNum, string combineType, string useOnType)
        {
            if (useOnType == "GiveItem" && (agent.employer == interactingAgent || agent.formerEmployer == interactingAgent || agent.oma.mindControlled))
            {
                if (!(item.invItemName != "") || !item.isWeapon && !item.isArmor && (!item.isArmorHead && !(item.itemType == "Consumable")) && !(item.itemType == "Food") || (item.CharacterExclusiveSpecificCharacter(interactingAgent) || item.CantDrop(interactingAgent) || item.questItem || item.healthChange != 0 && (double)agent.health == (double)agent.healthMax && agent.isPlayer == 0 || (item.itemType == "Food" && agent.statusEffects.hasTrait("BloodRestoresHealth") || item.invItemName == "BloodBag" && !agent.statusEffects.hasTrait("BloodRestoresHealth") || item.Categories.Contains("Food") && agent.statusEffects.hasTrait("CannibalizeRestoresHealth"))) || ((item.Categories.Contains("Food") || item.Categories.Contains("Alcohol") || item.Categories.Contains("Health")) && agent.statusEffects.hasTrait("OilRestoresHealth") || ((item.Categories.Contains("Food") || item.Categories.Contains("Health")) && agent.electronic || !agent.inventory.DetermineIfCanUseWeapon(item))))
                {
                    return;
                }
                if (combineType == "Combine")
                {
                    if (agent.inventory.hasEmptySlotForItem(item))
                    {
                        FollowerInventoryManagement.AddBorrowedItem(item, interactingAgent, agent);
                    }
                }
            }
        }
   }

    public static class FollowerInventoryManagement
    {
        private static BepInEx.Logging.ManualLogSource mLog = FollowerPlusMain.mLog;

        private static List<OwnedItem> leasedItems = new List<OwnedItem> { };
        private static List<Agent> usingInvManagement = new List<Agent> { };

        public static bool IsBorrowedItem(InvItem item, Agent lender, Agent borrower)
        {
            if (item == null || lender == null || borrower == null) { return false; }
            for (int i = 0; i < leasedItems.Count; i++)
            {                if (leasedItems[i].itemName == item.invItemName && leasedItems[i].lenderID == lender.agentID && leasedItems[i].borrowerID == borrower.agentID)//(leasedItems[i].Equals(new OwnedItem(item.invItemName, lender.agentID, borrower.agentID)))
                {
                    return true;
                }
            }
            return false;
        }

        public static void AddBorrowedItem(InvItem item, Agent lender, Agent borrower)
        {
            if (item == null || lender == null || borrower == null) { return; }
            leasedItems.Add(new OwnedItem(item.invItemName, lender.agentID, borrower.agentID));
        }

        public static void RemoveBorrowedItem(InvItem item, Agent lender, Agent borrower)
        {
            if (item == null || lender == null || borrower == null) { return; }
            for (int i = 0; i < leasedItems.Count; i++)
            {
                if (leasedItems[i].Equals(new OwnedItem(item.invItemName, lender.agentID, borrower.agentID)))
                {
                    leasedItems.RemoveAt(i);
                    return;
                }
            }
        }

        public static void SetInvManagementStatus(Agent agent, bool isUsing)
        {
            if (agent == null) { return; }
            
            if (usingInvManagement == null) { return; }
            
            for (int i = 0; i < usingInvManagement.Count; i++)
            {
                if (usingInvManagement[i] == agent)
                {
                    usingInvManagement.Remove(usingInvManagement[i]);
                    break;
                }
            }

            if (isUsing)
            {
                usingInvManagement.Add(agent);
            }
        }

        public static bool IsManagingInventory(Agent agent)
        {
            if (agent == null) { return false; }
            
            if (usingInvManagement == null) { return false; }
            
            for (int i = 0; i < usingInvManagement.Count; i++)
            {
                if (usingInvManagement[i] == agent)
                {
                    return true;
                }
            }
            return false;
        }

    }

    public class OwnedItem
    {
        public int lenderID;
        public int borrowerID;
        public string itemName;

        public OwnedItem(string itemName, int lenderID, int borrowerID)
        {
            this.itemName = itemName;
            this.lenderID = lenderID;
            this.borrowerID = borrowerID;
        }
    }
}
