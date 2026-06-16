using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using GameNetcodeStuff;

namespace BloodWitch
{
    [HarmonyPatch(typeof(EnemyAI))]
    public class EnemyBloodPatch
    {
        public static List<GameObject> enemyBloodDrops = new List<GameObject>();

        [HarmonyPatch("HitEnemy")]
        [HarmonyPostfix]
        public static void HitEnemyPostfix(EnemyAI __instance, int force, PlayerControllerB playerWhoHit, bool playHitSFX, int hitID)
        {
            if (__instance == null || __instance.isEnemyDead) return;

            // Don't spawn blood for the Blood Witch herself if she gets hit (or maybe do? Let's exclude her to be safe)
            if (__instance is BloodWitchAI) return;

            // Create visual blood by cloning a player's blood drop if possible
            GameObject bloodDrop = null;
            if (StartOfRound.Instance != null && StartOfRound.Instance.allPlayerScripts != null && StartOfRound.Instance.allPlayerScripts.Length > 0)
            {
                PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[0];
                if (player != null && player.playerBloodPooledObjects != null && player.playerBloodPooledObjects.Count > 0)
                {
                    GameObject template = player.playerBloodPooledObjects[0];
                    if (template != null)
                    {
                        bloodDrop = UnityEngine.Object.Instantiate(template);
                        bloodDrop.name = "EnemyBloodDrop_" + __instance.enemyType.enemyName;
                        
                        Ray interactRay = new Ray(__instance.transform.position + Vector3.up * 1f, Vector3.down);
                        RaycastHit hit;
                        if (Physics.Raycast(interactRay, out hit, 6f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                        {
                            bloodDrop.transform.position = hit.point + Vector3.up * 0.05f;
                            bloodDrop.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.up);
                            
                            Vector3 localEulerAngles = bloodDrop.transform.localEulerAngles;
                            localEulerAngles.z = UnityEngine.Random.Range(-180f, 180f);
                            bloodDrop.transform.localEulerAngles = localEulerAngles;
                            
                            float num = UnityEngine.Random.Range(0.23f, 0.62f);
                            bloodDrop.transform.localScale = new Vector3(num + UnityEngine.Random.Range(-0.1f, 0.1f), num + UnityEngine.Random.Range(-0.1f, 0.1f), 0.55f);
                            
                            bloodDrop.SetActive(true);
                        }
                        else
                        {
                            UnityEngine.Object.Destroy(bloodDrop);
                            bloodDrop = null;
                        }
                    }
                }
            }

            // Fallback if we couldn't clone
            if (bloodDrop == null)
            {
                bloodDrop = new GameObject("EnemyBloodDrop_" + __instance.enemyType.enemyName);
                bloodDrop.transform.position = __instance.transform.position;
            }
            
            // Add it to our tracking list
            enemyBloodDrops.Add(bloodDrop);
            
            // Auto-destroy after 120 seconds so the list doesn't grow infinitely and stays visible longer
            UnityEngine.Object.Destroy(bloodDrop, 120f);

            // Periodically clean up the list of any null/destroyed references
            enemyBloodDrops.RemoveAll(x => x == null);
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB))]
    public class PlayerBloodPatch
    {
        [HarmonyPatch("DamagePlayer")]
        [HarmonyPostfix]
        public static void DamagePlayerPostfix(PlayerControllerB __instance)
        {
            SpawnPersistentBlood(__instance);
        }

        [HarmonyPatch("DamagePlayerClientRpc")]
        [HarmonyPostfix]
        public static void DamagePlayerClientRpcPostfix(PlayerControllerB __instance)
        {
            SpawnPersistentBlood(__instance);
        }

        private static void SpawnPersistentBlood(PlayerControllerB __instance)
        {
            if (__instance == null || __instance.isPlayerDead) return;

            GameObject bloodDrop = null;
            if (__instance.playerBloodPooledObjects != null && __instance.playerBloodPooledObjects.Count > 0)
            {
                GameObject template = __instance.playerBloodPooledObjects[0];
                if (template != null)
                {
                    bloodDrop = UnityEngine.Object.Instantiate(template);
                    bloodDrop.name = "PlayerPersistentBloodDrop_" + __instance.playerUsername;
                    
                    Ray interactRay = new Ray(__instance.transform.position + Vector3.up * 1f, Vector3.down);
                    RaycastHit hit;
                    if (Physics.Raycast(interactRay, out hit, 6f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                    {
                        bloodDrop.transform.position = hit.point + Vector3.up * 0.05f;
                        bloodDrop.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.up);
                        
                        Vector3 localEulerAngles = bloodDrop.transform.localEulerAngles;
                        localEulerAngles.z = UnityEngine.Random.Range(-180f, 180f);
                        bloodDrop.transform.localEulerAngles = localEulerAngles;
                        
                        float num = UnityEngine.Random.Range(0.23f, 0.62f);
                        bloodDrop.transform.localScale = new Vector3(num + UnityEngine.Random.Range(-0.1f, 0.1f), num + UnityEngine.Random.Range(-0.1f, 0.1f), 0.55f);
                        
                        bloodDrop.SetActive(true);
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(bloodDrop);
                        bloodDrop = null;
                    }
                }
            }

            if (bloodDrop != null)
            {
                BloodWitch.EnemyBloodPatch.enemyBloodDrops.Add(bloodDrop);
                UnityEngine.Object.Destroy(bloodDrop, 60f); // Make sure it stays for 60 seconds
                BloodWitch.EnemyBloodPatch.enemyBloodDrops.RemoveAll(x => x == null);
            }
        }
    }
}
