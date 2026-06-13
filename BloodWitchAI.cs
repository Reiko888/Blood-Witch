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
    [System.Serializable]
    public struct LevelMaterialSwap
    {
        public SkinnedMeshRenderer targetRenderer;
        public int materialIndex;
        public Material level1Material;
        public Material level2Material;
        public Material level3Material;
        public Material level4Material;
    }

    public class BloodWitchAI : EnemyAI
    {
        [Header("Material Swaps")]
        public LevelMaterialSwap[] grannyMaterialSwaps;

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
        private Vector3 consumptionLockPosition;

        public float consumeDuration = 10f;

        public Transform currentBloodTarget;

        private List<Transform> consumedBloodTargets = new List<Transform>();

        private float bloodSearchTimer = 0f;
        private float bloodTargetWaitTimer = 0f;
        private float currentMonsterSpeed = 2f;
        
        private Vector3 previousPosition;
        private float averageVelocity;
        private float velocityInterval;
        private float velocityAverageCount;

        public GameObject geyserPrefab;
        private GameObject currentActiveGeyser;

        public GameObject playerBloodExplosionPrefab;

        [Header("Footprints")]
        public GameObject footprintPrefab;
        public int maxFootprints = 15;
        public float footprintDistanceThreshold = 1.5f;
        public float footprintLateralOffset = 0.3f;
        public float footprintBackwardOffset = 1.2f;
        public float footprintLifetime = 4f;
        
        private float accumulatedFootprintDistance = 0f;
        private Vector3 previousFootprintPosition;
        private bool alternateFootprint = false;
        private float activeFootprintTimer = 0f;
        private GameObject[] footprintPool;
        private float[] footprintTimers;
        private int currentFootprintIndex = 0;

        public GameObject bloodOrb;
        public GameObject dagger;

        public PlayAudioAnimationEvent grannyAudioAnimationEvent;
        public PlayAudioAnimationEvent monsterAudioAnimationEvent;

        [Header("Audio")]
        public AudioClip[] hitSFX;
        public AudioClip[] monsterAttackSFX;
        public AudioClip[] monsterSeePlayerSFX;
        public AudioClip[] monsterHitPlayerSFX;
        private float monsterSeePlayerCooldown = 0f;
        public AudioClip[] laughingClips;
        public AudioClip[] level3LaughingClips;
        public AudioClip[] levelUpScreams;
        private float laughCooldown = 0f;
        public AudioClip bloodExplosionSFX;
        public AudioClip boilTarget2DSFX;
        public AudioClip teleportSFX;
        public AudioClip consumeBloodSFX;
        public AudioClip stabSFX;
        public AudioSource daggerAudioSource;
        public AudioSource breathingAudioSource;
        public AudioSource screamAudioSource;
        private AudioSource boil2DAudioSource;

        public GameObject grannyModelContainer;

        public GameObject monsterModelContainer;

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

        private void SpawnFootprint()
        {
            if (footprintPool == null || footprintPool.Length == 0) return;

            GameObject fp = footprintPool[currentFootprintIndex];
            if (fp == null) return;

            Vector3 direction = transform.forward;
            if (direction == Vector3.zero) direction = Vector3.forward;

            Vector3 rightDir = transform.right;
            float offset = alternateFootprint ? footprintLateralOffset : -footprintLateralOffset;

            Vector3 spawnPos = transform.position - (direction * footprintBackwardOffset) + (rightDir * offset) + (Vector3.up * 1.0f);

            int mask = StartOfRound.Instance.collidersAndRoomMaskAndDefault;
            mask &= ~(1 << gameObject.layer);

            RaycastHit hit;
            if (Physics.Raycast(spawnPos, Vector3.down, out hit, 4f, mask, QueryTriggerInteraction.Ignore))
            {
                if (Vector3.Dot(hit.normal, Vector3.up) < 0.7f) return;

                fp.transform.position = hit.point + (Vector3.up * 0.02f); 

                Vector3 projectedForward = Vector3.ProjectOnPlane(direction, hit.normal).normalized;
                if (projectedForward != Vector3.zero)
                {
                    fp.transform.rotation = Quaternion.LookRotation(-hit.normal, projectedForward);
                }

                Vector3 scale = fp.transform.localScale;
                scale.x = alternateFootprint ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
                fp.transform.localScale = scale;

                fp.SetActive(true);
                footprintTimers[currentFootprintIndex] = footprintLifetime;

                alternateFootprint = !alternateFootprint;
                currentFootprintIndex = (currentFootprintIndex + 1) % maxFootprints;
            }
        }

        private void ApplyLevelMaterials(int level)
        {
            if (grannyMaterialSwaps == null || grannyMaterialSwaps.Length == 0) return;
            
            foreach (var swap in grannyMaterialSwaps)
            {
                if (swap.targetRenderer == null) continue;
                
                Material targetMat = null;
                if (level == 1 && swap.level1Material != null) targetMat = swap.level1Material;
                else if (level == 2 && swap.level2Material != null) targetMat = swap.level2Material;
                else if (level == 3 && swap.level3Material != null) targetMat = swap.level3Material;
                else if (level >= 4 && swap.level4Material != null) targetMat = swap.level4Material;
                
                if (targetMat != null)
                {
                    Material[] mats = swap.targetRenderer.sharedMaterials;
                    if (swap.materialIndex >= 0 && swap.materialIndex < mats.Length)
                    {
                        mats[swap.materialIndex] = targetMat;
                        swap.targetRenderer.sharedMaterials = mats;
                    }
                }
            }
        }

        public override void Start()
        {
            base.Start();
            ApplyLevelMaterials(CurrentLevel);
            
            if (footprintPrefab != null)
            {
                footprintPool = new GameObject[maxFootprints];
                footprintTimers = new float[maxFootprints];
                for (int i = 0; i < maxFootprints; i++)
                {
                    GameObject fp = Instantiate(footprintPrefab, Vector3.zero, Quaternion.identity);
                    fp.transform.SetParent(null, true);
                    fp.SetActive(false);
                    footprintPool[i] = fp;
                    footprintTimers[i] = 0f;
                }
            }
            
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
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || isCurrentlyTransforming || isPausedAfterAttack || isConsumingBlood)
                return;
            base.DoAIInterval();

            bool oldLOS = hasLOS;
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
                        agent.speed = 4f; // Faster in level 3
                        break;
                    case State.MonsterLevel4:
                        agent.speed = 2f; // Fast blood beast starts slow
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

                    bool leftArmMissing = (removableLimbs != null && isLimbDetached != null && isLimbDetached.Length > 0 && isLimbDetached[0]);

                    if (level == 3 && !leftArmMissing) 
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
                    else if (level == 2 && !leftArmMissing)
                    {
                        if (Vector3.Distance(transform.position, targetPlayer.transform.position) < 15f && geyserCooldown <= 0f)
                        {
                            LogIfDebugBuild($"Casting Geyser on player {targetPlayer.playerUsername}");
                            CastBloodGeyser(targetPlayer);
                            geyserCooldown = 10f;
                        }
                    }

                    if (leftArmMissing && explosionTimer > 0f)
                    {
                        explosionTimer = 0f;
                        SyncBoilingAnimationClientRpc(false, false);
                        SyncBoilTargetClientRpc(-1, 0f);
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

            if (!oldLOS && hasLOS && CurrentLevel == 4 && monsterSeePlayerCooldown <= 0f)
            {
                monsterSeePlayerCooldown = 5f;
                if (IsServer) PlayMonsterSeePlayerSFXClientRpc();
            }
        }

        private Vector3 targetPlayerLastPos;
        private Vector3 targetPlayerVelocity;

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;

            if (monsterSeePlayerCooldown > 0f) monsterSeePlayerCooldown -= Time.deltaTime;

            if (targetPlayer != null)
            {
                targetPlayerVelocity = (targetPlayer.transform.position - targetPlayerLastPos) / Time.deltaTime;
                targetPlayerLastPos = targetPlayer.transform.position;
            }

            if (hasTeleported)
            {
                LogIfDebugBuild($"Processing hasTeleported and starting consumption.");
                hasTeleported = false;

                StartCoroutine(TeleportThenConsumeRoutine());
            }

            if (currentBehaviourStateIndex == 3)
            {
                if (IsServer && agent != null && agent.isOnNavMesh && !isPausedAfterAttack && !isCurrentlyTransforming)
                {
                    if (targetPlayer != null && hasLOS)
                    {
                        currentMonsterSpeed += Time.deltaTime * 2.5f;
                        agent.acceleration = Mathf.MoveTowards(agent.acceleration, 35f, Time.deltaTime * 5f);
                    }
                    else
                    {
                        currentMonsterSpeed -= Time.deltaTime * 4f; // Slow down if no target
                        agent.acceleration = Mathf.MoveTowards(agent.acceleration, 10f, Time.deltaTime * 8f);
                    }
                    currentMonsterSpeed = Mathf.Clamp(currentMonsterSpeed, 2f, 14f);
                    agent.speed = currentMonsterSpeed;
                }
                
                float rawSpeed = (transform.position - previousPosition).magnitude / (Time.deltaTime / 1.4f);
                
                if (velocityInterval <= 0f)
                {
                    velocityInterval = 0.05f;
                    velocityAverageCount += 1f;
                    if (velocityAverageCount > 5f)
                    {
                        averageVelocity += (rawSpeed - averageVelocity) / 3f;
                    }
                    else
                    {
                        averageVelocity += rawSpeed;
                        if (velocityAverageCount == 2f)
                        {
                            averageVelocity /= velocityAverageCount;
                        }
                    }
                }
                else
                {
                    velocityInterval -= Time.deltaTime;
                }
                
                previousPosition = transform.position;

                float speedMult = Mathf.Clamp(averageVelocity / 12f * 2.5f, 0.1f, 3f);
                if (creatureAnimator != null)
                {
                    creatureAnimator.SetFloat("monsterSpeedMult", speedMult);
                }
            }

            timeSinceLastAttack += Time.deltaTime;
            chosenBloodTimer -= Time.deltaTime;
            teleportCooldownTimer -= Time.deltaTime;
            geyserCooldown -= Time.deltaTime;
            laughCooldown -= Time.deltaTime;

            if (!isEnemyDead && agent != null && agent.isOnNavMesh && currentBehaviourStateIndex != 3)
            {
                agent.speed = 3.5f;
            }

            if (footprintPool != null && footprintTimers != null)
            {
                for (int i = 0; i < footprintPool.Length; i++)
                {
                    if (footprintTimers[i] > 0f)
                    {
                        footprintTimers[i] -= Time.deltaTime;
                        if (footprintTimers[i] <= 0f && footprintPool[i] != null)
                        {
                            footprintPool[i].SetActive(false);
                        }
                    }
                }
            }

            if (activeFootprintTimer > 0f) activeFootprintTimer -= Time.deltaTime;

            if (activeFootprintTimer > 0f && !isEnemyDead && !isCurrentlyTransforming && !isConsumingBlood && footprintPool != null && footprintPool.Length > 0)
            {
                if (previousFootprintPosition == Vector3.zero) previousFootprintPosition = transform.position;
                
                float distMoved = Vector3.Distance(transform.position, previousFootprintPosition);
                accumulatedFootprintDistance += distMoved;
                previousFootprintPosition = transform.position;

                if (accumulatedFootprintDistance >= footprintDistanceThreshold)
                {
                    accumulatedFootprintDistance = 0f;
                    SpawnFootprint();
                }
            }
            else if (!isConsumingBlood)
            {
                previousFootprintPosition = transform.position;
            }

            if (isConsumingBlood)
            {
                movingTowardsTargetPlayer = false;
                targetPlayer = null;
                transform.position = consumptionLockPosition;
                serverPosition = consumptionLockPosition;

                if (IsServer && agent != null && agent.isOnNavMesh)
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }
                
                consumeTimer -= Time.deltaTime;
                if (consumeTimer <= 0f)
                {
                    isConsumingBlood = false;
                    activeFootprintTimer = 20f;
                    if (creatureAnimator != null)
                    {
                        creatureAnimator.SetTrigger("backToWalk");
                    }
                    StartCoroutine(DelayedConsumeBlood(0.5f));
                    if (IsServer && agent != null)
                    {
                        if (agent.isOnNavMesh)
                        {
                            agent.isStopped = false;
                        }
                    }
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

                            if (i == 0 && bloodOrb != null) bloodOrb.SetActive(true);
                            if (i == 1 && dagger != null) dagger.SetActive(true);

                            if (removableLimbs[i] != null && removableLimbs[i].renderers != null)
                            {
                                foreach (var r in removableLimbs[i].renderers)
                                {
                                    if (r != null && r.materials != null)
                                    {
                                        foreach (Material mat in r.materials)
                                        {
                                            if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", 0.011f);
                                            
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
                                            if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", Mathf.Lerp(1f, 0.011f, progress));
                                            
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
            
            bool leftArmSevered = (removableLimbs != null && isLimbDetached != null && isLimbDetached.Length > 0 && isLimbDetached[0]);
            
            if (IsServer && canTeleport && teleportCooldownTimer <= 0f && !isConsumingBlood && explosionTimer <= 0f && CurrentLevel < 4 && !leftArmSevered)
            {
                if (bloodTargetWaitTimer > 0f) bloodTargetWaitTimer -= Time.deltaTime;

                bloodSearchTimer -= Time.deltaTime;
                if (bloodSearchTimer <= 0f)
                {
                    bloodSearchTimer = 2f;
                    if (currentBloodTarget == null || !currentBloodTarget.gameObject.activeInHierarchy)
                    {
                        Transform newTarget = FindBestBloodSource();
                        if (newTarget != null && newTarget != currentBloodTarget)
                        {
                            currentBloodTarget = newTarget;
                            bloodTargetWaitTimer = 3f; // wait 3 seconds before engaging fresh blood
                        }
                    }
                    
                    if (currentBloodTarget != null && bloodTargetWaitTimer <= 0f)
                    {
                        FindNodeNearestToBlood(currentBloodTarget.position);
                    }
                }
            }
        }

        private System.Collections.IEnumerator TeleportThenConsumeRoutine()
        {
            isConsumingBlood = true; // Pauses AI immediately
            movingTowardsTargetPlayer = false;
            consumeTimer = 999f;

            if (IsServer)
            {
                if (currentSearch != null && currentSearch.inProgress)
                {
                    StopSearch(currentSearch);
                }
                SetDestinationToPosition(transform.position);

                if (agent != null)
                {
                    agent.speed = 0f;
                    agent.velocity = Vector3.zero;

                    if (agent.isOnNavMesh)
                    {
                        agent.isStopped = true;
                        agent.ResetPath();
                    }
                }
            }

            // Short delay so teleport state updates across network
            yield return new WaitForSeconds(0.1f);

            if (creatureAnimator != null)
            {
                creatureAnimator.SetTrigger("hasStartedConsume");
            }
            if (consumeBloodSFX != null && creatureVoice != null)
            {
                creatureVoice.PlayOneShot(consumeBloodSFX);
            }

            consumeTimer = 10f;
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
            
            GameObject closestNode = null;
            float minDistance = float.MaxValue;

            foreach (GameObject node in allAINodes)
            {
                if (node == null) continue;

                float dist = Vector3.Distance(node.transform.position, bloodLocation);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestNode = node;
                }
            }

            if (closestNode != null)
            {
                teleportCooldownTimer = (CurrentLevel >= 2) ? 30f : 15f; // cooldown for level 2 and 3
                chosenTeleportNode = closestNode.transform.position;
                LogIfDebugBuild($"Found closest node at {minDistance} units. Teleporting near blood area.");
                if (IsServer)
                {
                    TeleportBWClientRpc(bloodLocation);
                }
                else
                {
                    TeleportBWServerRpc(bloodLocation);
                }
            }
            else
            {
                LogIfDebugBuild("No AI nodes found at all. Teleport cancelled");
            }
        }

        //Local
        private void TeleportBloodWitch(Vector3 bloodLocation)
        {
            movingTowardsTargetPlayer = false;
            if (currentSearch != null && currentSearch.inProgress) StopSearch(currentSearch);

            if (IsServer && agent != null)
            {
                agent.Warp(bloodLocation);

                agent.isStopped = true;
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
            else if (!IsServer)
            {
                transform.position = bloodLocation;
            }

            if (teleportSFX != null && creatureVoice != null)
            {
                creatureVoice.PlayOneShot(teleportSFX);
            }
            serverPosition = bloodLocation;
            consumptionLockPosition = bloodLocation;
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
            hasTeleported = true;
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
                agent.speed = 2f; // Starts slow
                agent.acceleration = 2f; // reset acceleration so it ramps up
            }
            isCurrentlyTransforming = false;
        }

        private System.Collections.IEnumerator DelayedConsumeBlood(float delay)
        {
            yield return new WaitForSeconds(delay);
            ConsumeBlood();
        }

        public void ConsumeBlood()
        {
            if (IsServer) 
            {
                int oldLevel = CurrentLevel;
                bloodConsumed++;
                int newLevel = CurrentLevel;
                ConsumeBloodClientRpc(bloodConsumed, oldLevel, newLevel);
            }
        }

        [ClientRpc]
        public void ConsumeBloodClientRpc(int newBloodCount, int oldLevel, int newLevel)
        {
            bloodConsumed = newBloodCount;
            LogIfDebugBuild($"Consumed blood! Level is now: {CurrentLevel}");

            if (newLevel > oldLevel)
            {
                ApplyLevelMaterials(newLevel);
            }

            // Play granny scream only if not transforming to monster
            if (newLevel > oldLevel && newLevel < 4 && levelUpScreams != null && levelUpScreams.Length > 0 && screamAudioSource != null)
            {
                int index = UnityEngine.Random.Range(0, levelUpScreams.Length);
                screamAudioSource.PlayOneShot(levelUpScreams[index]);
            }
        }

        public void CastBloodGeyser(PlayerControllerB player)
        {
            if (!IsServer) return;
            
            // Cannot use Level 2 Geyser if left arm is severed
            if (CurrentLevel < 4 && removableLimbs != null && isLimbDetached != null && isLimbDetached.Length > 0 && isLimbDetached[0])
            {
                return;
            }

            LogIfDebugBuild("Casting blood geyser on " + player.playerUsername);
            
            SyncGeyserAnimationClientRpc();
            
            Vector3 predictedPos = player.transform.position;
            if (player == targetPlayer)
            {
                Vector3 vel = targetPlayerVelocity;
                if (vel.magnitude > 10f) vel = vel.normalized * 10f; 
                predictedPos += vel * 1.5f; 
            }
            else
            {
                predictedPos += player.playerBodyAnimator.transform.forward * 2f;
            }

            SpawnGeyserWarningClientRpc(predictedPos);
            StartCoroutine(GeyserAttackDelay(predictedPos));
        }

        [ClientRpc]
        public void SyncGeyserAnimationClientRpc()
        {
            if (creatureAnimator != null)
            {
                creatureAnimator.SetTrigger("useGeyser");
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void SpawnGeyserWarningServerRpc(Vector3 position)
        {
            SpawnGeyserWarningClientRpc(position);
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
            
            yield return new WaitForSeconds(2f);
            
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
                }
            }

            StartCoroutine(ClientGeyserDamageRoutine(position));
        }

        private IEnumerator ClientGeyserDamageRoutine(Vector3 position)
        {
            float lingerTimer = 0f;
            while (lingerTimer < 2f)
            {
                if (GameNetworkManager.Instance != null && GameNetworkManager.Instance.localPlayerController != null)
                {
                    PlayerControllerB localPlayer = GameNetworkManager.Instance.localPlayerController;
                    if (!localPlayer.isPlayerDead && Vector3.Distance(localPlayer.transform.position, position) <= 3.5f)
                    {
                        localPlayer.DamagePlayer(15, hasDamageSFX: true, callRPC: true, CauseOfDeath.Unknown);
                    }
                }
                yield return new WaitForSeconds(0.25f);
                lingerTimer += 0.25f;
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

            if (isConsumingBlood)
            {
                isConsumingBlood = false;
                consumeTimer = 0f;
                if (creatureAnimator != null)
                {
                    creatureAnimator.SetTrigger("backToWalk");
                }
                if (IsServer && agent != null && agent.isOnNavMesh)
                {
                    agent.isStopped = false;
                }
            }

            if (playerWhoHit != null && IsServer)
            {
                targetPlayer = playerWhoHit;
                movingTowardsTargetPlayer = true;
                if (currentSearch != null) StopSearch(currentSearch);
            }

            if (!isLimbDetached[2] && !isLimbDetached[3])
            {
                if (IsServer) PlayHitSFXClientRpc();
            }

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

            if (limbIndex == 0 && bloodOrb != null) bloodOrb.SetActive(false);
            if (limbIndex == 1 && dagger != null) dagger.SetActive(false);

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
            LogIfDebugBuild("Exploding player " + player.playerUsername);
            SpawnBloodExplosionClientRpc((int)player.playerClientId, player.transform.position);
        }

        [ServerRpc(RequireOwnership = false)]
        public void SpawnBloodExplosionServerRpc(int playerId, Vector3 pos)
        {
            SpawnBloodExplosionClientRpc(playerId, pos);
        }

        [ClientRpc]
        public void SpawnBloodExplosionClientRpc(int playerId, Vector3 pos)
        {
            if (playerBloodExplosionPrefab != null)
            {
                GameObject explosion = Instantiate(playerBloodExplosionPrefab, pos, Quaternion.identity);
                ParticleSystem[] pSystems = explosion.GetComponentsInChildren<ParticleSystem>();
                foreach (ParticleSystem ps in pSystems)
                {
                    ps.Play(true);
                }
            }
            if (bloodExplosionSFX != null)
            {
                AudioSource.PlayClipAtPoint(bloodExplosionSFX, pos, 1f);
            }

            if (StartOfRound.Instance != null)
            {
                PlayerControllerB target = StartOfRound.Instance.allPlayerScripts[playerId];
                if (target != null)
                {
                    if (target == GameNetworkManager.Instance.localPlayerController && !target.isPlayerDead)
                    {
                        target.DamagePlayer(100, hasDamageSFX: true, callRPC: true, CauseOfDeath.Blast, deathAnimation: -1);
                    }

                    if (!target.isPlayerDead || (target.isPlayerDead && target.deadBody != null))
                    {
                        for (int i = 0; i < 20; i++)
                        {
                            Vector3 randomDirection = UnityEngine.Random.onUnitSphere;
                            randomDirection.y = Mathf.Abs(randomDirection.y); 
                            target.DropBlood(randomDirection, leaveBlood: true, leaveFootprint: false);
                        }
                    }
                }
            }
        }

        public override void OnCollideWithPlayer(UnityEngine.Collider other)
        {
            base.OnCollideWithPlayer(other);
            if (isEnemyDead || isConsumingBlood) return;

            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);
            if (player != null && timeSinceLastAttack >= 1f)
            {
                // Index 1 = right arm
                if (removableLimbs != null && isLimbDetached != null && isLimbDetached.Length > 1 && isLimbDetached[1])
                {
                    return; // Cannot attack if right arm is missing
                }

                timeSinceLastAttack = 0f;

                if (CurrentLevel < 4)
                {
                    // Level 1-3 Dagger attack
                    player.DamagePlayer(20, hasDamageSFX: true, callRPC: true, CauseOfDeath.Stabbing);
                    
                    if (IsServer) PlayStabSFXClientRpc();
                    else PlayStabSFXServerRpc();

                    if (laughCooldown <= 0f)
                    {
                        if (CurrentLevel == 3 && level3LaughingClips != null && level3LaughingClips.Length > 0)
                        {
                            int index = enemyRandom.Next(0, level3LaughingClips.Length);
                            if (IsServer) PlayLaughClientRpc(index, true);
                            else PlayLaughServerRpc(index, true);
                            laughCooldown = 1f;
                        }
                        else if (laughingClips != null && laughingClips.Length > 0)
                        {
                            int index = enemyRandom.Next(0, laughingClips.Length);
                            if (IsServer) PlayLaughClientRpc(index, false);
                            else PlayLaughServerRpc(index, false);
                            laughCooldown = 1f;
                        }
                    }
                }
                else 
                {
                    // Level 4 Monster attack
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
                      
                    bool willDie = player.health - 40 <= 0 || player.isPlayerDead;
                    player.DamagePlayer(40, hasDamageSFX: true, callRPC: true, CauseOfDeath.Mauling);
                      
                    if (IsServer) PlayMonsterHitPlayerSFXClientRpc();
                    else PlayMonsterHitPlayerSFXServerRpc();
                      
                    if (willDie)
                    {
                        if (IsServer) SpawnBloodExplosionClientRpc((int)player.playerClientId, player.transform.position);
                        else SpawnBloodExplosionServerRpc((int)player.playerClientId, player.transform.position);
                    }
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

        [ServerRpc(RequireOwnership = false)]
        public void PlayStabSFXServerRpc()
        {
            PlayStabSFXClientRpc();
        }

        [ClientRpc]
        public void PlayStabSFXClientRpc()
        {
            if (creatureAnimator != null)
            {
                creatureAnimator.SetTrigger("hasStabbed");
            }
            if (stabSFX != null && daggerAudioSource != null)
            {
                daggerAudioSource.PlayOneShot(stabSFX);
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            base.KillEnemy(destroy);
            if (grannyAudioAnimationEvent != null)
            {
                grannyAudioAnimationEvent.enableAudio = false;
            }
            if (monsterAudioAnimationEvent != null)
            {
                monsterAudioAnimationEvent.enableAudio = false;
            }
        }

        [ClientRpc]
        public void PlayMonsterAttackSFXClientRpc()
        {
            if (monsterAttackSFX != null && monsterAttackSFX.Length > 0 && creatureSFX != null)
            {
                creatureSFX.PlayOneShot(monsterAttackSFX[UnityEngine.Random.Range(0, monsterAttackSFX.Length)]);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void PlayMonsterHitPlayerSFXServerRpc()
        {
            PlayMonsterHitPlayerSFXClientRpc();
        }

        [ClientRpc]
        public void PlayMonsterHitPlayerSFXClientRpc()
        {
            if (monsterHitPlayerSFX != null && monsterHitPlayerSFX.Length > 0 && creatureSFX != null)
            {
                creatureSFX.PlayOneShot(monsterHitPlayerSFX[UnityEngine.Random.Range(0, monsterHitPlayerSFX.Length)]);
            }
        }

        [ClientRpc]
        public void PlayMonsterSeePlayerSFXClientRpc()
        {
            if (monsterSeePlayerSFX != null && monsterSeePlayerSFX.Length > 0 && creatureVoice != null)
            {
                creatureVoice.PlayOneShot(monsterSeePlayerSFX[UnityEngine.Random.Range(0, monsterSeePlayerSFX.Length)]);
            }
        }

        [ClientRpc]
        public void PlayHitSFXClientRpc()
        {
            if (hitSFX != null && hitSFX.Length > 0 && creatureVoice != null)
            {
                creatureVoice.PlayOneShot(hitSFX[UnityEngine.Random.Range(0, hitSFX.Length)]);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void PlayLaughServerRpc(int index, bool level3)
        {
            PlayLaughClientRpc(index, level3);
        }

        [ClientRpc]
        public void PlayLaughClientRpc(int clipIndex, bool isLevel3)
        {
            if (isLevel3)
            {
                if (level3LaughingClips != null && clipIndex >= 0 && clipIndex < level3LaughingClips.Length && creatureVoice != null)
                {
                    creatureVoice.PlayOneShot(level3LaughingClips[clipIndex]);
                }
            }
            else
            {
                if (laughingClips != null && clipIndex >= 0 && clipIndex < laughingClips.Length && creatureVoice != null)
                {
                    creatureVoice.PlayOneShot(laughingClips[clipIndex]);
                }
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
