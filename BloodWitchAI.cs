using GameNetcodeStuff;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Unity.Netcode;
using Unity.Services.Authentication.Generated;
using UnityEngine;

namespace BloodWitch
{
    public class BloodWitchAI : EnemyAI
    {
        System.Random enemyRandom = null!;

        public GameObject[] outsideNodes;

        public SkinnedMeshRenderer[] skin;

        public Material[] thisMaterial;

        private bool hasLOS;

        private float LOStimer = 0f;

        public float chaseTimer = 0f;

        public bool canTeleport = true;

        public bool hasTeleported;

        private float chosenBloodTimer;

        private float teleportCooldownTimer;

        public float teleportCooldown = 15f;

        private Vector3 lastPosition;

        public Vector3 recentBlood;

        private Vector3 chosenTeleportNode;

        [System.Serializable]
        public class LimbGroup
        {
            public SkinnedMeshRenderer[] renderers;
        }

        public LimbGroup[] removableLimbs;

        public GameObject[] severedLimbPrefabs;

        private float[] limbRegenTimers;

        public float limbRegenDuration = 10f;

        private bool[] isLimbDetached;

        private bool isConsumingBlood = false;

        private float consumeTimer = 0f;

        public float consumeDuration = 10f;

        public Transform currentBloodTarget;

        private List<Transform> consumedBloodTargets = new List<Transform>();

        private float bloodSearchTimer = 0f;

        public GameObject geyserPrefab;
        private GameObject currentActiveGeyser;

        public GameObject playerBloodExplosionPrefab;

        public AudioClip[] laughingClips;
        public AudioClip[] levelUpScreams;
        public AudioClip bloodExplosionSFX;
        public AudioClip boilTarget2DSFX;
        public AudioClip teleportSFX;
        public AudioClip consumeBloodSFX;
        public AudioClip stabSFX;
        public AudioSource daggerAudioSource;
        private AudioSource boil2DAudioSource;

        public GameObject grannyModelContainer;

        public GameObject monsterModelContainer;

        public GameObject Level1Decal;
        public GameObject Level2Decal;
        public GameObject Level3Decal;

        public ParticleSystem BloodSpurtParticleArmL;
        public ParticleSystem BloodSpurtParticleArmR;
        public ParticleSystem BloodSpurtParticleHead;
        public ParticleSystem BloodSpurtParticleBackL;
        public ParticleSystem BloodSpurtParticleBackR;

        private float timeSinceLastAttack;

        public int bloodConsumed = 0;
        
        public int CurrentLevel 
        {
            get 
            {
                if (bloodConsumed >= 9) return 4;
                if (bloodConsumed >= 6) return 3;
                if (bloodConsumed >= 3) return 2;
                return 1;
            }
        }

        private float explosionTimer = 0f;
        public float explosionThreshold = 5f;
        private float geyserCooldown = 0f;
        private bool hasStartedTransformation = false;
        private bool isCurrentlyTransforming = false;
        private bool isPausedAfterAttack = false;

        private static FieldInfo currentBloodIndexField = typeof(PlayerControllerB).GetField("currentBloodIndex", BindingFlags.NonPublic | BindingFlags.Instance);




        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            if (Plugin.Logger != null)
            {
                Plugin.Logger.LogInfo(text);
            }
            else
            {
                UnityEngine.Debug.Log($" {text}");
            }
        }

        public override void Start()
        {
            base.Start();
            bloodSearchTimer = 2f;
            currentBehaviourStateIndex = 1;

            boil2DAudioSource = gameObject.AddComponent<AudioSource>();
            boil2DAudioSource.spatialBlend = 0f; // 2D Audio
            boil2DAudioSource.loop = true;
            boil2DAudioSource.volume = 1f;
            boil2DAudioSource.clip = boilTarget2DSFX;
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + this.thisEnemyIndex);
            List<Material> combinedMaterials = new List<Material>();

            if (skin != null)
            {
                foreach (SkinnedMeshRenderer s in skin)
                {
                    if (s != null)
                    {
                        combinedMaterials.AddRange(s.materials);
                    }
                }
            }
            if (!RoundManager.Instance.hasInitializedLevelRandomSeed)
            {
                RoundManager.Instance.InitializeRandomNumberGenerators();
            }
            base.GetAINodes();
            outsideNodes = GameObject.FindGameObjectsWithTag("OutsideAINode");

            if (removableLimbs != null)
            {
                limbRegenTimers = new float[removableLimbs.Length];
                isLimbDetached = new bool[removableLimbs.Length];
            }
        }

        private enum State
        {
            GrannyLevel1,
            GrannyLevel2,
            GrannyLevel3,
            MonsterLevel4
        }

        public void SyncStateSafely(int newStateIndex)
        {
            if (IsServer)
            {
                SyncStateSafelyClientRpc(newStateIndex);
            }
            else
            {
                SyncStateSafelyServerRpc(newStateIndex);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SyncStateSafelyServerRpc(int newStateIndex)
        {
            SyncStateSafelyClientRpc(newStateIndex);
        }

        [ClientRpc]
        private void SyncStateSafelyClientRpc(int newStateIndex)
        {
            LogIfDebugBuild($"State changed from {(State)currentBehaviourStateIndex} to {(State)newStateIndex}");
            currentBehaviourStateIndex = newStateIndex;
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || isCurrentlyTransforming || isPausedAfterAttack || isConsumingBlood)
                return;

            int level = CurrentLevel;
            int desiredState = level - 1;

            if (currentBehaviourStateIndex != desiredState)
            {
                LogIfDebugBuild($"Switching state from {currentBehaviourStateIndex} to {desiredState} (Level {level})");
                SyncStateSafely(desiredState);
                
                if (level == 4 && !hasStartedTransformation)
                {
                    hasStartedTransformation = true;
                    canTeleport = false; // Monster mode loses granny abilities
                    TriggerTransformationClientRpc();
                }
                else if (level < 4)
                {
                    if (grannyModelContainer != null) grannyModelContainer.SetActive(true);
                    if (monsterModelContainer != null) monsterModelContainer.SetActive(false);
                }
            }

            if (IsServer && agent != null)
            {
                switch ((State)currentBehaviourStateIndex)
                {
                    case State.GrannyLevel1:
                    case State.GrannyLevel2:
                        agent.speed = 3f; // Level 2 is same speed as level 1
                        break;
                    case State.GrannyLevel3:
                        agent.speed = 6f; // Faster in level 3
                        break;
                    case State.MonsterLevel4:
                        agent.speed = 8f; // Fast blood beast
                        break;
                }
            }

            if (targetPlayer != null && !isConsumingBlood)
            {

                if (CheckLineOfSightForPosition(targetPlayer.gameplayCamera.transform.position, 120f, 60))
                {
                    hasLOS = true;
                    lastPosition = targetPlayer.transform.position;
                    if (currentSearch != null && currentSearch.inProgress) StopSearch(currentSearch);
                    SetMovingTowardsTargetPlayer(targetPlayer);

                    if (level == 3) 
                    {
                        if (explosionTimer == 0f && creatureAnimator != null)
                        {
                            LogIfDebugBuild($"Started boiling player {targetPlayer.playerUsername}");
                            SyncBoilingAnimationClientRpc(true, false);
                        }

                        explosionTimer += 0.2f;
                        SyncBoilTargetClientRpc((int)targetPlayer.playerClientId, explosionTimer);

                        if (explosionTimer >= explosionThreshold) 
                        {
                            LogIfDebugBuild($"Started boiling player {targetPlayer.playerUsername}");
                            SyncBoilingAnimationClientRpc(true, false);
                            targetPlayer.isSinking = false;
                        }

                        if (explosionTimer >= explosionThreshold + 2f) // Explosion takes 2 seconds
                        {
                            LogIfDebugBuild($"DoAIInterval: Exploding player {targetPlayer.playerUsername}");
                            ExplodePlayer(targetPlayer);
                            targetPlayer = null;
                            explosionTimer = 0f;
                            
                            SyncBoilingAnimationClientRpc(false, true);
                            SyncBoilTargetClientRpc(-1, 0f);
                        }
                    }
                    else if (level == 2)
                    {
                        if (Vector3.Distance(transform.position, targetPlayer.transform.position) < 15f && geyserCooldown <= 0f)
                        {
                            LogIfDebugBuild($"Casting Geyser on player {targetPlayer.playerUsername}");
                            CastBloodGeyser(targetPlayer);
                            geyserCooldown = 10f;
                        }
                    }
                }
                else
                {
                    if (hasLOS) LogIfDebugBuild("Lost Line of Sight to target player.");
                    hasLOS = false;
                    if (explosionTimer > 0f)
                    {
                        explosionTimer = 0f;
                        SyncBoilingAnimationClientRpc(false, false);
                        SyncBoilTargetClientRpc(-1, 0f);
                    }
                    explosionTimer = 0f; // Reset explosion build-up if LOS broken
                    if (chaseTimer <= 0f)
                    {
                        targetPlayer = null;
                        if (currentSearch == null || !currentSearch.inProgress) StartSearch(base.transform.position);
                    }
                    else
                    {
                        SetDestinationToPosition(lastPosition);

                        if (Vector3.Distance(transform.position, lastPosition) <= 2f)
                        {
                            chaseTimer = 0f;
                        }
                    }
                }
            }
            else
            {
                hasLOS = false;
                explosionTimer = 0f;

                if (TargetClosestPlayer(5f, requireLineOfSight: true, 120f))
                {
                    hasLOS = true;
                    chaseTimer = 10f;
                    lastPosition = targetPlayer.transform.position;

                    if (currentSearch != null && currentSearch.inProgress) StopSearch(currentSearch);
                    SetMovingTowardsTargetPlayer(targetPlayer);
                }
                else if (currentSearch == null || !currentSearch.inProgress)
                {
                    StartSearch(base.transform.position);
                }
            }
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;

            if (Level1Decal != null) Level1Decal.SetActive(CurrentLevel >= 1);
            if (Level2Decal != null) Level2Decal.SetActive(CurrentLevel >= 2 && !isConsumingBlood);
            if (Level3Decal != null) Level3Decal.SetActive(CurrentLevel >= 3 && !isConsumingBlood);

            if (currentBehaviourStateIndex == 3)
            {
                if (IsServer && agent != null && agent.isOnNavMesh && !isPausedAfterAttack && !isCurrentlyTransforming)
                {
                    agent.acceleration = Mathf.MoveTowards(agent.acceleration, 15f, Time.deltaTime * 3f);
                }
                
                float speed = Vector3.Distance(transform.position, lastPosition) / Time.deltaTime;
                lastPosition = transform.position;
                
                float speedMult = Mathf.Clamp(speed / 8f, 0f, 1f);
                if (creatureAnimator != null)
                {
                    creatureAnimator.SetFloat("monsterSpeedMult", speedMult);
                }
            }

            timeSinceLastAttack += Time.deltaTime;
            chosenBloodTimer -= Time.deltaTime;
            teleportCooldownTimer -= Time.deltaTime;
            geyserCooldown -= Time.deltaTime;

            if (isConsumingBlood)
            {
                consumeTimer -= Time.deltaTime;
                if (consumeTimer <= 0f)
                {
                    isConsumingBlood = false;
                    ConsumeBlood();
                    if (IsServer && agent != null && agent.isOnNavMesh) agent.isStopped = false;

                    if (currentBloodTarget != null && !consumedBloodTargets.Contains(currentBloodTarget))
                    {
                        consumedBloodTargets.Add(currentBloodTarget);
                        currentBloodTarget = null;
                    }
                }
                else
                {
                    return; // Pause normal logic while consuming
                }
            }

            // Limb Regeneration
            if (removableLimbs != null)
            {
                for (int i = 0; i < removableLimbs.Length; i++)
                {
                    if (isLimbDetached[i])
                    {
                        limbRegenTimers[i] -= Time.deltaTime;
                        if (limbRegenTimers[i] <= 0f)
                        {
                            isLimbDetached[i] = false;
                            if (removableLimbs[i] != null && removableLimbs[i].renderers != null)
                            {
                                foreach (var r in removableLimbs[i].renderers)
                                {
                                    if (r != null && r.materials != null)
                                    {
                                        foreach (Material mat in r.materials)
                                        {
                                            if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", 0f);
                                            
                                            Color c = mat.color;
                                            c.a = 1f;
                                            mat.color = c;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (removableLimbs[i] != null && removableLimbs[i].renderers != null)
                            {
                                float progress = 1f - (limbRegenTimers[i] / limbRegenDuration);
                                foreach (var r in removableLimbs[i].renderers)
                                {
                                    if (r != null && r.materials != null)
                                    {
                                        foreach (Material mat in r.materials)
                                        {
                                            if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", Mathf.Lerp(1f, 0f, progress));
                                            
                                            Color c = mat.color;
                                            c.a = Mathf.Lerp(0f, 1f, progress);
                                            mat.color = c;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            if (canTeleport && teleportCooldownTimer <= 0f && !isConsumingBlood && explosionTimer <= 0f)
            {
                bloodSearchTimer -= Time.deltaTime;
                if (bloodSearchTimer <= 0f)
                {
                    bloodSearchTimer = 2f;
                    if (currentBloodTarget == null || !currentBloodTarget.gameObject.activeInHierarchy)
                    {
                        currentBloodTarget = FindBestBloodSource();
                    }
                    
                    if (currentBloodTarget != null)
                    {
                        FindNodeNearestToBlood(currentBloodTarget.position);
                    }
                }
            }

            if (hasTeleported)
            {
                LogIfDebugBuild($"Processing hasTeleported and starting consumption.");
                hasTeleported = false;
                
                StartCoroutine(TeleportThenConsumeRoutine());
            }

        }

        private System.Collections.IEnumerator TeleportThenConsumeRoutine()
        {
            isConsumingBlood = true; // Pauses AI immediately
            consumeTimer = 999f; // Prevent Update from cancelling it
            if (IsServer && agent != null && agent.isOnNavMesh) 
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }
            
            // Wait for teleport animation to finish
            yield return new WaitForSeconds(1.5f);
            
            if (creatureAnimator != null)
            {
                creatureAnimator.SetTrigger("hasStartedConsume");
            }
            if (consumeBloodSFX != null && creatureVoice != null)
            {
                creatureVoice.PlayOneShot(consumeBloodSFX);
            }
            
            consumeTimer = consumeDuration; // Starts consumption countdown
        }

        private Transform FindBestBloodSource()
        {
            Transform bestSource = null;
            float closestDistance = float.MaxValue;
            Vector3 myPos = transform.position;

            LogIfDebugBuild("FindBestBloodSource: Searching for blood sources...");

            void CheckSource(Transform source)
            {
                if (source == null || !source.gameObject.activeInHierarchy) return;
                if (consumedBloodTargets.Contains(source)) return;
                float dist = Vector3.Distance(myPos, source.position);
                if (dist < closestDistance)
                {
                    closestDistance = dist;
                    bestSource = source;
                }
            }

            if (StartOfRound.Instance != null && StartOfRound.Instance.allPlayerScripts != null)
            {
                foreach (PlayerControllerB p in StartOfRound.Instance.allPlayerScripts)
                {
                    if (p != null && p.playerBloodPooledObjects != null)
                    {
                        foreach (GameObject bloodObj in p.playerBloodPooledObjects)
                        {
                            if (bloodObj != null) CheckSource(bloodObj.transform);
                        }
                    }
                    
                    if (p != null && p.isPlayerDead && p.deadBody != null)
                    {
                        CheckSource(p.deadBody.transform);
                    }
                }
            }

            if (EnemyBloodPatch.enemyBloodDrops != null)
            {
                foreach (GameObject eBlood in EnemyBloodPatch.enemyBloodDrops)
                {
                    if (eBlood != null) CheckSource(eBlood.transform);
                }
            }

            if (RoundManager.Instance != null && RoundManager.Instance.SpawnedEnemies != null)
            {
                foreach (EnemyAI enemy in RoundManager.Instance.SpawnedEnemies)
                {
                    if (enemy != null && enemy != this && enemy.isEnemyDead)
                    {
                        CheckSource(enemy.transform);
                    }
                }
            }

            if (bestSource != null)
            {
                LogIfDebugBuild($"FindBestBloodSource: Found best source at {bestSource.position} with distance {closestDistance}");
            }
            else
            {
                LogIfDebugBuild("FindBestBloodSource: No valid blood sources found.");
            }

            return bestSource;
        }

        private void FindNodeNearestToBlood(Vector3 bloodLocation)
        {
            if (!canTeleport) return;
            List<GameObject> validNodes = new List<GameObject>();
            float teleportRadius = 10f;

            foreach (GameObject node in allAINodes)
            {
                if (node == null) continue;

                if (Vector3.Distance(node.transform.position, bloodLocation) <= teleportRadius)
                {
                    validNodes.Add(node);
                }
            }

            if (validNodes.Count > 0)
            {
                GameObject chosenNode = validNodes[enemyRandom.Next(0, validNodes.Count)];
                teleportCooldownTimer = (CurrentLevel >= 2) ? 30f : 15f; // cooldown for level 2 and 3
                chosenTeleportNode = chosenNode.transform.position;
                if (IsServer)
                {
                    TeleportBWClientRpc(bloodLocation);
                }
                else
                {
                    TeleportBWServerRpc(bloodLocation);
                }

                LogIfDebugBuild($"Found {validNodes.Count} nodes. Teleporting near blood area.");
                hasTeleported = true;

            }
            else
            {
                LogIfDebugBuild("No AI nodes found within 10 units. Teleport cancelled");
            }
        }

        //Local
        private void TeleportBloodWitch(Vector3 bloodLocation)
        {
            if (IsServer && agent != null)
            {
                agent.enabled = false;
                transform.position = bloodLocation;
                agent.enabled = true;
                if (agent.isOnNavMesh)
                {
                    agent.Warp(bloodLocation);
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }
            }
            else
            {
                transform.position = bloodLocation;
            }

            if (creatureAnimator != null)
            {
                creatureAnimator.SetTrigger("hasTeleported");
            }
            if (teleportSFX != null && creatureVoice != null)
            {
                creatureVoice.PlayOneShot(teleportSFX);
            }
            serverPosition = bloodLocation;
        }

        [ServerRpc(RequireOwnership = false)]
        public void TeleportBWServerRpc(Vector3 bloodLocation)
        {
            TeleportBWClientRpc(bloodLocation);
        }

        [ClientRpc]
        public void TeleportBWClientRpc(Vector3 bloodLocation)
        {
            TeleportBloodWitch(bloodLocation);
        }

        private System.Collections.IEnumerator TransformationRoutine()
        {
            isCurrentlyTransforming = true;
            if (IsServer && agent != null)
            {
                agent.speed = 0f;
                if (agent.isOnNavMesh) agent.isStopped = true;
            }
            if (creatureAnimator != null)
            {
                creatureAnimator.SetTrigger("isTransforming");
            }
            yield return new WaitForSeconds(6f);
            
            if (grannyModelContainer != null) grannyModelContainer.SetActive(false);
            if (monsterModelContainer != null) monsterModelContainer.SetActive(true);
            
            if (IsServer && agent != null)
            {
                if (agent.isOnNavMesh) agent.isStopped = false;
                agent.speed = 8f; // Resumes monster speed
                agent.acceleration = 2f; // reset acceleration so it ramps up
            }
            isCurrentlyTransforming = false;
        }

        public void ConsumeBlood()
        {
            if (IsServer) 
            {
                bloodConsumed++;
                ConsumeBloodClientRpc(bloodConsumed);
            }
        }

        [ClientRpc]
        public void ConsumeBloodClientRpc(int newBloodCount)
        {
            int oldLevel = CurrentLevel;
            bloodConsumed = newBloodCount;
            int newLevel = CurrentLevel;
            LogIfDebugBuild($"Consumed blood! Level is now: {CurrentLevel}");

            if (newLevel > oldLevel && levelUpScreams != null && levelUpScreams.Length > 0 && creatureVoice != null)
            {
                int index = UnityEngine.Random.Range(0, levelUpScreams.Length);
                creatureVoice.PlayOneShot(levelUpScreams[index]);
            }
        }

        public void CastBloodGeyser(PlayerControllerB player)
        {
            if (!IsServer) return;
            LogIfDebugBuild("Casting blood geyser on " + player.playerUsername);
            // Casts while continuing to move
            
            SyncGeyserAnimationClientRpc();
            
            Vector3 predictedPos = player.transform.position + (player.transform.forward * 2f);
            SpawnGeyserWarningClientRpc(predictedPos);
            StartCoroutine(GeyserAttackDelay(predictedPos));
        }

        [ClientRpc]
        public void SpawnGeyserWarningClientRpc(Vector3 position)
        {
            if (geyserPrefab != null)
            {
                currentActiveGeyser = Instantiate(geyserPrefab, position, Quaternion.identity);
                Transform warningAudioObj = currentActiveGeyser.transform.Find("Warning");
                if (warningAudioObj != null)
                {
                    AudioSource audio = warningAudioObj.GetComponent<AudioSource>();
                    if (audio != null) audio.Play();
                }
            }
        }

        private IEnumerator GeyserAttackDelay(Vector3 position)
        {
            yield return new WaitForSeconds(1.5f);
            SpawnGeyserAttackClientRpc(position);
            
            // Damage lingers for 2 seconds
            float lingerTimer = 0f;
            while (lingerTimer < 2f)
            {
                Collider[] colliders = Physics.OverlapSphere(position, 3f, StartOfRound.Instance.playersMask);
                foreach (Collider col in colliders)
                {
                    PlayerControllerB player = col.GetComponent<PlayerControllerB>();
                    if (player != null && !player.isPlayerDead)
                    {
                        player.DamagePlayer(15, hasDamageSFX: true, callRPC: true, CauseOfDeath.Unknown);
                    }
                }
                
                yield return new WaitForSeconds(0.25f);
                lingerTimer += 0.25f;
            }
            
            DestroyGeyserClientRpc();
        }

        [ClientRpc]
        public void SpawnGeyserAttackClientRpc(Vector3 position)
        {
            if (currentActiveGeyser != null)
            {
                Animator anim = currentActiveGeyser.GetComponentInChildren<Animator>();
                if (anim != null) anim.SetBool("Toggle Geyser", true);

                var vfx = currentActiveGeyser.GetComponentInChildren<UnityEngine.VFX.VisualEffect>();
                if (vfx != null) vfx.SetBool("Toggle Geyser", true);

                Transform castAudioObj = currentActiveGeyser.transform.Find("Cast");
                if (castAudioObj != null)
                {
                    AudioSource audio = castAudioObj.GetComponent<AudioSource>();
                    if (audio != null) audio.Play();
                    else LogIfDebugBuild("Cast is null!");
                }
            }
        }

        [ClientRpc]
        public void DestroyGeyserClientRpc()
        {
            if (currentActiveGeyser != null)
            {
                Destroy(currentActiveGeyser);
                currentActiveGeyser = null;
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead) return;

            if (removableLimbs != null)
            {
                List<int> attachedIndices = new List<int>();
                for (int i = 0; i < isLimbDetached.Length; i++)
                {
                    if (!isLimbDetached[i]) attachedIndices.Add(i);
                }

                if (attachedIndices.Count > 0)
                {
                    int indexToDetach = attachedIndices[0];
                    DetachLimbServerRpc(indexToDetach);
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void DetachLimbServerRpc(int limbIndex)
        {
            DetachLimbClientRpc(limbIndex);
        }

        [ClientRpc]
        public void DetachLimbClientRpc(int limbIndex)
        {
            if (isLimbDetached[limbIndex]) return;

            isLimbDetached[limbIndex] = true;
            limbRegenTimers[limbIndex] = limbRegenDuration;

            // Play blood spurt particles
            if (limbIndex == 0 && BloodSpurtParticleArmL != null) BloodSpurtParticleArmL.Play();
            else if (limbIndex == 1 && BloodSpurtParticleArmR != null) BloodSpurtParticleArmR.Play();
            else if (limbIndex == 2 && BloodSpurtParticleHead != null) BloodSpurtParticleHead.Play();
            else if (limbIndex == 3)
            {
                if (BloodSpurtParticleBackL != null) BloodSpurtParticleBackL.Play();
                if (BloodSpurtParticleBackR != null) BloodSpurtParticleBackR.Play();
            }

            if (removableLimbs[limbIndex] != null && removableLimbs[limbIndex].renderers != null && removableLimbs[limbIndex].renderers.Length > 0)
            {
                if (severedLimbPrefabs != null && severedLimbPrefabs.Length > limbIndex && severedLimbPrefabs[limbIndex] != null)
                {
                    Transform spawnTrans = removableLimbs[limbIndex].renderers[0].transform;
                    GameObject severedLimb = Instantiate(severedLimbPrefabs[limbIndex], spawnTrans.position, spawnTrans.rotation);
                    
                    MeshCollider mc = severedLimb.GetComponentInChildren<MeshCollider>();
                    if (mc != null) mc.convex = true;
                    
                    Rigidbody rb = severedLimb.GetComponentInChildren<Rigidbody>();
                    if (rb == null) rb = severedLimb.AddComponent<Rigidbody>();
                    rb.isKinematic = false;
                    rb.useGravity = true;
                }

                foreach (var r in removableLimbs[limbIndex].renderers)
                {
                    if (r != null && r.materials != null)
                    {
                        foreach (Material mat in r.materials)
                        {
                            if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", 1f);
                            
                            Color c = mat.color;
                            c.a = 0f;
                            mat.color = c;
                        }
                    }
                }
            }

            bool allDetached = true;
            for (int i = 0; i < isLimbDetached.Length; i++)
            {
                if (!isLimbDetached[i])
                {
                    allDetached = false;
                    break;
                }
            }

            if (allDetached)
            {
                KillEnemyOnOwnerClient();
            }
        }

        public void ExplodePlayer(PlayerControllerB player)
        {
            if (!IsServer) return;
            int numberOfBloodSplatters = 20;
            for (int i = 0; i < numberOfBloodSplatters; i++)
            {
                Vector3 randomDirection = UnityEngine.Random.onUnitSphere;
                randomDirection.y = Mathf.Abs(randomDirection.y); 
                player.DropBlood(randomDirection, leaveBlood: true, leaveFootprint: false);
            }
            LogIfDebugBuild("Exploding player " + player.playerUsername);
            player.DamagePlayer(100, hasDamageSFX: true, callRPC: true, CauseOfDeath.Blast, deathAnimation: -1);
            
            SpawnBloodExplosionClientRpc(player.transform.position);
        }

        [ClientRpc]
        public void SpawnBloodExplosionClientRpc(Vector3 pos)
        {
            if (playerBloodExplosionPrefab != null)
            {
                Instantiate(playerBloodExplosionPrefab, pos, Quaternion.identity);
            }
            if (bloodExplosionSFX != null)
            {
                AudioSource.PlayClipAtPoint(bloodExplosionSFX, pos, 1f);
            }
        }

        public override void OnCollideWithPlayer(UnityEngine.Collider other)
        {
            base.OnCollideWithPlayer(other);
            if (isEnemyDead) return;

            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);
            if (player != null && timeSinceLastAttack >= 1f)
            {
                // Index 1 = right arm
                if (CurrentLevel < 4 && removableLimbs != null && isLimbDetached != null && isLimbDetached.Length > 1 && isLimbDetached[1])
                {
                    return; // Granny cannot attack if right arm is missing
                }

                timeSinceLastAttack = 0f;
                if (creatureAnimator != null)
                {
                    creatureAnimator.SetTrigger("hasStabbed");
                }

                if (CurrentLevel < 4)
                {
                    // Level 1-3 Dagger attack
                    player.DamagePlayer(20, hasDamageSFX: true, callRPC: true, CauseOfDeath.Mauling);
                    
                    PlayStabSFXClientRpc();

                    if (laughingClips != null && laughingClips.Length > 0)
                    {
                        int index = enemyRandom.Next(0, laughingClips.Length);
                        PlayLaughClientRpc(index);
                    }
                }
                else 
                {
                    if (creatureAnimator != null)
                    {
                        creatureAnimator.SetTrigger("MonsterAttack");
                    }
                    if (monsterModelContainer != null)
                    {
                        Animator[] animators = monsterModelContainer.GetComponentsInChildren<Animator>(true);
                        foreach (Animator anim in animators)
                        {
                            anim.SetTrigger("MonsterAttack");
                        }
                    }
                    
                    StartCoroutine(PauseAfterAttack(3f));
                    
                    // Level 4 Monster Mode leapp
                    player.DamagePlayer(100, hasDamageSFX: true, callRPC: true, CauseOfDeath.Mauling);
                }
            }
        }

        private System.Collections.IEnumerator PauseAfterAttack(float duration)
        {
            isPausedAfterAttack = true;
            agent.speed = 0f;
            if (agent.isOnNavMesh) agent.isStopped = true;
            
            yield return new WaitForSeconds(duration);
            
            if (!isEnemyDead)
            {
                isPausedAfterAttack = false;
                if (agent.isOnNavMesh) agent.isStopped = false;
            }
        }

        [ClientRpc]
        public void TriggerTransformationClientRpc()
        {
            hasStartedTransformation = true;
            StartCoroutine(TransformationRoutine());
        }

        [ClientRpc]
        public void SyncBoilingAnimationClientRpc(bool isBoiling, bool exploded)
        {
            if (creatureAnimator != null)
            {
                creatureAnimator.SetBool("isBoiling", isBoiling);
                if (isBoiling)
                {
                    creatureAnimator.SetTrigger("startBoiling");
                }
                
                if (exploded)
                {
                    creatureAnimator.SetBool("explodedPlayer", true);
                }
                else if (!isBoiling)
                {
                    creatureAnimator.SetBool("explodedPlayer", false);
                }
            }
        }

        [ClientRpc]
        public void SyncGeyserAnimationClientRpc()
        {
            if (creatureAnimator != null)
            {
                creatureAnimator.SetTrigger("useGeyser");
            }
        }

        [ClientRpc]
        public void PlayStabSFXClientRpc()
        {
            if (stabSFX != null && daggerAudioSource != null)
            {
                daggerAudioSource.PlayOneShot(stabSFX);
            }
        }

        [ClientRpc]
        public void PlayLaughClientRpc(int clipIndex)
        {
            if (laughingClips != null && clipIndex >= 0 && clipIndex < laughingClips.Length && creatureVoice != null)
            {
                creatureVoice.PlayOneShot(laughingClips[clipIndex]);
            }
        }

        [ClientRpc]
        public void SyncBoilTargetClientRpc(int playerId, float explosionTimerValue)
        {
            if (boil2DAudioSource == null) return;
            
            if (playerId == -1)
            {
                if (boil2DAudioSource.isPlaying) boil2DAudioSource.Stop();
                boil2DAudioSource.pitch = 1f;
                return;
            }
            
            // Only play if the local player is the target
            if (GameNetworkManager.Instance.localPlayerController != null && 
                (int)GameNetworkManager.Instance.localPlayerController.playerClientId == playerId)
            {
                if (!boil2DAudioSource.isPlaying && boilTarget2DSFX != null)
                {
                    boil2DAudioSource.Play();
                }
                
                // Pitch goes up as explosionTimer reaches explosionThreshold + 2f
                float maxTime = explosionThreshold + 2f;
                float progress = Mathf.Clamp01(explosionTimerValue / maxTime);
                boil2DAudioSource.pitch = 1f + (progress * 1.5f); // Pitch from 1.0 to 2.5
            }
        }

        public static Vector3? GetLatestBloodDropLocation(PlayerControllerB player)
        {
            if (player == null) return null;
            List<GameObject> bloodObjects = player.playerBloodPooledObjects;

            if (bloodObjects == null || bloodObjects.Count == 0 || currentBloodIndexField == null)
            {
                return null;
            }
            int nextIndex = (int)currentBloodIndexField.GetValue(player);
            int latestIndex = (nextIndex - 1 + bloodObjects.Count) % bloodObjects.Count;
            GameObject latestBlood = bloodObjects[latestIndex];
            if (latestBlood != null && latestBlood.activeSelf)
            {
                return latestBlood.transform.position;
            }
            return null;
        }
    }
}
